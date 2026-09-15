using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.PhysicalGold;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;
using WealthLedger.Infrastructure.Persistence;
using WealthLedger.Infrastructure.Persistence.Rows;

namespace WealthLedger.Infrastructure.Tests.Persistence;

public sealed class PhysicalGoldLifecycleTests
{
    private static readonly Guid SourceVaultId =
        Guid.Parse("50000000-0000-0000-0000-000000000091");
    private static readonly Guid DestinationVaultId =
        Guid.Parse("50000000-0000-0000-0000-000000000092");
    private static readonly Guid CounterpartyId =
        Guid.Parse("30000000-0000-0000-0000-000000000091");
    private static readonly DateTimeOffset Now =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PurchaseSaleTransfer_Restart_ReconstructsExactCustody()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);
        Guid purchaseId;
        Guid lotId;

        await using (var context = database.CreateContext())
        {
            var preview = await PurchasePreview(context).ExecuteAsync(
                PurchaseCommand());
            Assert.Equal(18.32m, preview.FineWeightGrams);
            Assert.Equal(
                201_000,
                preview.Economics.AcquisitionLotCost!.MinorUnits);

            var posted = await PurchaseRecorder(context).ExecuteAsync(
                "gold-purchase-1", PurchaseCommand());
            purchaseId = posted.TransactionId;
            lotId = posted.AssetLotId;
        }

        var selection = new PhysicalGoldSelectedLot(
            lotId, Quantity.FromDecimal(8m), 1);
        Guid saleId;
        await using (var context = database.CreateContext())
        {
            var command = SaleCommand([selection]);
            var preview = await SalePreview(context).ExecuteAsync(command);
            Assert.Single(preview.Plan);
            Assert.Equal(7.328m, preview.Plan[0].MovedFineWeightGrams);
            Assert.Equal(80_400, preview.RealizedCost.KnownAmounts.Single().MinorUnits);

            var posted = await SaleRecorder(context).ExecuteAsync(
                "gold-sale-1",
                command with
                {
                    ReviewedPlanFingerprint = preview.PlanFingerprint
                });
            saleId = posted.TransactionId;
        }

        var transferSelection = new PhysicalGoldSelectedLot(
            lotId, Quantity.FromDecimal(12m), 1);
        Guid transferId;
        await using (var context = database.CreateContext())
        {
            var command = TransferCommand([transferSelection]);
            var preview = await TransferPreview(context).ExecuteAsync(command);
            Assert.Equal(10.992m, preview.FineWeightGrams);

            var posted = await TransferRecorder(context).ExecuteAsync(
                "gold-transfer-1",
                command with
                {
                    ReviewedPlanFingerprint = preview.PlanFingerprint
                });
            transferId = posted.TransactionId;
        }

        await using (var reopened = database.CreateContext())
        {
            var verification = Verification(reopened);
            var purchase = await verification.ExecuteAsync(
                CoreLedgerTestData.HouseholdId, purchaseId);
            var sale = await verification.ExecuteAsync(
                CoreLedgerTestData.HouseholdId, saleId);
            var transfer = await verification.ExecuteAsync(
                CoreLedgerTestData.HouseholdId, transferId);

            Assert.Equal(2, purchase.Facts.PieceCount);
            Assert.Equal(1, sale.Facts.PieceCount);
            Assert.Equal(2, transfer.Facts.Allocations.Count);
            Assert.Equal(
                [-12_00000000L, 12_00000000L],
                transfer.Facts.Allocations
                    .Select(x => x.GrossWeightDeltaRawE8)
                    .Order()
                    .ToArray());
            Assert.Equal(
                [-1, 1],
                transfer.Facts.Allocations
                    .Select(x => x.PieceDelta)
                    .Order()
                    .ToArray());

            var inventory = await new GetPhysicalGoldCustodyInventoryUseCase(
                new EfCorePhysicalGoldVerificationReadStore(reopened))
                .ExecuteAsync(CoreLedgerTestData.HouseholdId);
            var position = Assert.Single(inventory.Items);
            Assert.Equal(DestinationVaultId, position.AccountId);
            Assert.Equal(12_00000000L, position.GrossWeightRawE8);
            Assert.Equal(1, position.PieceCount);
            Assert.Equal(10.992m, position.FineWeightGrams);
        }
    }

    [Fact]
    public async Task SalePost_WithChangedPieces_RejectsReviewedPlanAtomically()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);
        Guid lotId;
        await using (var context = database.CreateContext())
        {
            lotId = (await PurchaseRecorder(context).ExecuteAsync(
                "gold-purchase-stale", PurchaseCommand())).AssetLotId;
        }

        var selection = new PhysicalGoldSelectedLot(
            lotId, Quantity.FromDecimal(9m), 1);
        PhysicalGoldSaleCommand reviewed;
        await using (var context = database.CreateContext())
        {
            var command = SaleCommand([selection]);
            var preview = await SalePreview(context).ExecuteAsync(command);
            reviewed = command with
            {
                ReviewedPlanFingerprint = preview.PlanFingerprint
            };
        }

        await using (var context = database.CreateContext())
        {
            var transferSelection = new PhysicalGoldSelectedLot(
                lotId, Quantity.FromDecimal(12m), 1);
            var transfer = TransferCommand([transferSelection]);
            var preview = await TransferPreview(context).ExecuteAsync(transfer);
            await TransferRecorder(context).ExecuteAsync(
                "gold-transfer-race",
                transfer with
                {
                    ReviewedPlanFingerprint = preview.PlanFingerprint
                });
        }

        await using (var context = database.CreateContext())
        {
            var exception = await Assert.ThrowsAsync<PhysicalGoldException>(
                () => SaleRecorder(context).ExecuteAsync(
                    "gold-sale-stale", reviewed));
            Assert.Equal(
                PhysicalGoldErrorCodes.InsufficientGrossWeight,
                exception.ErrorCode);
            Assert.False(await context.CommandReceipts.AnyAsync(
                x => x.IdempotencyKey == "gold-sale-stale"));
        }
    }

    [Fact]
    public async Task SaleReversal_RestoresExactGrossPiecesAndCashWithoutCopyingEvidence()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);
        Guid lotId;
        Guid saleId;

        await using (var context = database.CreateContext())
        {
            lotId = (await PurchaseRecorder(context).ExecuteAsync(
                "gold-purchase-sale-reversal",
                PurchaseCommand())).AssetLotId;
        }

        await using (var context = database.CreateContext())
        {
            var command = SaleCommand(
            [
                new PhysicalGoldSelectedLot(
                    lotId,
                    Quantity.FromDecimal(8m),
                    1)
            ]) with
            {
                Costs =
                [
                    new PhysicalGoldCostInput(
                        CostType.Commission,
                        CostTreatment.AdditionalCashOutflow,
                        Money.FromMinorUnits(1_000, CurrencyCode.TRY)),
                    new PhysicalGoldCostInput(
                        CostType.OtherTax,
                        CostTreatment.WithheldFromProceeds,
                        Money.FromMinorUnits(500, CurrencyCode.TRY))
                ]
            };
            var preview = await SalePreview(context).ExecuteAsync(command);
            saleId = (await SaleRecorder(context).ExecuteAsync(
                "gold-sale-to-reverse",
                command with
                {
                    ReviewedPlanFingerprint = preview.PlanFingerprint
                })).TransactionId;
        }

        Guid reversalId;
        await using (var context = database.CreateContext())
        {
            var reversalStore = new EfCoreLedgerReversalStore(context);
            var preview = await new PreviewPostedTransactionReversalUseCase(
                    reversalStore)
                .ExecuteAsync(saleId);

            Assert.NotNull(preview);
            Assert.True(preview!.CanReverse);
            var inverse = Assert.Single(preview.InverseLotAllocations);
            Assert.Equal(8_00000000L, inverse.QuantityDelta.RawE8);
            Assert.Equal(1, inverse.PhysicalGoldPieceDelta);

            reversalId = (await new ReversePostedTransactionUseCase(
                    reversalStore,
                    new FixedTimeProvider(Now.AddMinutes(1)))
                .ExecuteAsync(
                    "gold-sale-reversal",
                    new ReversePostedTransactionCommand(
                        saleId,
                        "Synthetic incorrect sale.")))!
                .ReversalTransactionId;
        }

        await using (var reopened = database.CreateContext())
        {
            var inventory = await new GetPhysicalGoldCustodyInventoryUseCase(
                    new EfCorePhysicalGoldVerificationReadStore(reopened))
                .ExecuteAsync(CoreLedgerTestData.HouseholdId);
            var position = Assert.Single(inventory.Items);
            Assert.Equal(SourceVaultId, position.AccountId);
            Assert.Equal(20_00000000L, position.GrossWeightRawE8);
            Assert.Equal(2, position.PieceCount);

            Assert.Equal(2, await reopened.TransactionCostComponents
                .CountAsync(x => x.TransactionId == saleId
                    || x.TransactionId == reversalId));
            Assert.Equal(1, await reopened.PhysicalGoldTradeDetails
                .CountAsync(x => x.LedgerTransactionId == saleId
                    || x.LedgerTransactionId == reversalId));
            Assert.False(await reopened.TransactionCostComponents
                .AnyAsync(x => x.TransactionId == reversalId));
            Assert.False(await reopened.PhysicalGoldTradeDetails
                .AnyAsync(x => x.LedgerTransactionId == reversalId));
        }
    }

    [Fact]
    public async Task TransferReversal_MovesExactCustodyBackWithoutCreatingLot()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);
        Guid lotId;
        Guid transferId;

        await using (var context = database.CreateContext())
        {
            lotId = (await PurchaseRecorder(context).ExecuteAsync(
                "gold-purchase-transfer-reversal",
                PurchaseCommand())).AssetLotId;
        }

        await using (var context = database.CreateContext())
        {
            var command = TransferCommand(
            [
                new PhysicalGoldSelectedLot(
                    lotId,
                    Quantity.FromDecimal(12m),
                    1)
            ]);
            var preview = await TransferPreview(context).ExecuteAsync(command);
            transferId = (await TransferRecorder(context).ExecuteAsync(
                "gold-transfer-to-reverse",
                command with
                {
                    ReviewedPlanFingerprint = preview.PlanFingerprint
                })).TransactionId;
        }

        await using (var context = database.CreateContext())
        {
            var reversalStore = new EfCoreLedgerReversalStore(context);
            var preview = await new PreviewPostedTransactionReversalUseCase(
                    reversalStore)
                .ExecuteAsync(transferId);

            Assert.NotNull(preview);
            Assert.True(preview!.CanReverse);
            Assert.Equal(
                [-1, 1],
                preview.InverseLotAllocations
                    .Select(x => x.PhysicalGoldPieceDelta!.Value)
                    .Order()
                    .ToArray());

            await new ReversePostedTransactionUseCase(
                    reversalStore,
                    new FixedTimeProvider(Now.AddMinutes(1)))
                .ExecuteAsync(
                    "gold-transfer-reversal",
                    new ReversePostedTransactionCommand(
                        transferId,
                        "Synthetic incorrect transfer."));
        }

        await using (var reopened = database.CreateContext())
        {
            var inventory = await new GetPhysicalGoldCustodyInventoryUseCase(
                    new EfCorePhysicalGoldVerificationReadStore(reopened))
                .ExecuteAsync(CoreLedgerTestData.HouseholdId);
            var position = Assert.Single(inventory.Items);
            Assert.Equal(SourceVaultId, position.AccountId);
            Assert.Equal(20_00000000L, position.GrossWeightRawE8);
            Assert.Equal(2, position.PieceCount);
            Assert.Equal(1, await reopened.AssetLots.CountAsync());
        }
    }

    [Fact]
    public async Task TransferReversal_AfterDestinationSale_IsBlockedByExactCustody()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);
        Guid lotId;
        Guid transferId;

        await using (var context = database.CreateContext())
        {
            lotId = (await PurchaseRecorder(context).ExecuteAsync(
                "gold-purchase-blocked-transfer-reversal",
                PurchaseCommand())).AssetLotId;
        }

        await using (var context = database.CreateContext())
        {
            var command = TransferCommand(
            [
                new PhysicalGoldSelectedLot(
                    lotId,
                    Quantity.FromDecimal(12m),
                    1)
            ]);
            var preview = await TransferPreview(context).ExecuteAsync(command);
            transferId = (await TransferRecorder(context).ExecuteAsync(
                "gold-transfer-before-sale",
                command with
                {
                    ReviewedPlanFingerprint = preview.PlanFingerprint
                })).TransactionId;
        }

        Guid blockingSaleId;
        await using (var context = database.CreateContext())
        {
            var command = SaleCommand(
                [new PhysicalGoldSelectedLot(
                    lotId,
                    Quantity.FromDecimal(8m),
                    1)],
                sourceVaultId: DestinationVaultId);
            var preview = await SalePreview(context).ExecuteAsync(command);
            blockingSaleId = (await SaleRecorder(context).ExecuteAsync(
                "gold-destination-sale",
                command with
                {
                    ReviewedPlanFingerprint = preview.PlanFingerprint
                })).TransactionId;
        }

        await using (var context = database.CreateContext())
        {
            var store = new EfCoreLedgerReversalStore(context);
            var preview = await new PreviewPostedTransactionReversalUseCase(
                    store)
                .ExecuteAsync(transferId);

            Assert.NotNull(preview);
            Assert.False(preview!.CanReverse);
            Assert.Equal(
                ReversalEligibilityCode.BlockedByDependencies,
                preview.EligibilityCode);
            Assert.Equal([blockingSaleId], preview.BlockingTransactionIds);

            var exception = await Assert.ThrowsAsync<
                ReversalCommandRejectedException>(
                () => new ReversePostedTransactionUseCase(
                        store,
                        new FixedTimeProvider(Now.AddMinutes(1)))
                    .ExecuteAsync(
                        "blocked-gold-transfer-reversal",
                        new ReversePostedTransactionCommand(
                            transferId,
                            "Synthetic transfer correction.")));
            Assert.Equal(
                ReversalEligibilityCode.BlockedByDependencies,
                exception.EligibilityCode);
            Assert.Equal([blockingSaleId], exception.BlockingTransactionIds);
        }
    }

    [Fact]
    public async Task PurchaseReversal_AfterSale_IsBlockedByLotDependency()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await SeedAsync(database);
        Guid purchaseId;
        Guid lotId;

        await using (var context = database.CreateContext())
        {
            var purchase = await PurchaseRecorder(context).ExecuteAsync(
                "gold-purchase-blocked-reversal",
                PurchaseCommand());
            purchaseId = purchase.TransactionId;
            lotId = purchase.AssetLotId;
        }

        Guid saleId;
        await using (var context = database.CreateContext())
        {
            var command = SaleCommand(
            [
                new PhysicalGoldSelectedLot(
                    lotId,
                    Quantity.FromDecimal(8m),
                    1)
            ]);
            var preview = await SalePreview(context).ExecuteAsync(command);
            saleId = (await SaleRecorder(context).ExecuteAsync(
                "gold-sale-blocking-purchase-reversal",
                command with
                {
                    ReviewedPlanFingerprint = preview.PlanFingerprint
                })).TransactionId;
        }

        await using var reader = database.CreateContext();
        var previewResult = await new PreviewPostedTransactionReversalUseCase(
                new EfCoreLedgerReversalStore(reader))
            .ExecuteAsync(purchaseId);

        Assert.NotNull(previewResult);
        Assert.False(previewResult!.CanReverse);
        Assert.Equal([saleId], previewResult.BlockingTransactionIds);
    }

    private static PhysicalGoldPurchaseCommand PurchaseCommand()
        => new(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            SourceVaultId,
            CoreLedgerTestData.DestinationAccountId,
            CoreLedgerTestData.GoldAssetId,
            CoreLedgerTestData.CashAssetId,
            Quantity.FromDecimal(20m),
            new Fineness(916_000),
            2,
            Money.FromMinorUnits(200_000, CurrencyCode.TRY),
            CoreLedgerTestData.ExecutionDate,
            CounterpartyInstitutionId: CounterpartyId,
            Costs:
            [
                new PhysicalGoldCostInput(
                    CostType.MakingCharge,
                    CostTreatment.IncludedInConsideration,
                    Money.FromMinorUnits(5_000, CurrencyCode.TRY)),
                new PhysicalGoldCostInput(
                    CostType.Commission,
                    CostTreatment.AdditionalCashOutflow,
                    Money.FromMinorUnits(1_000, CurrencyCode.TRY))
            ],
            Hallmark: "916",
            CertificateReference: "SYNTHETIC-CERT",
            ExternalReference: "SYNTHETIC-GOLD-PURCHASE",
            Note: "Synthetic test purchase and cash explanation.");

    private static PhysicalGoldSaleCommand SaleCommand(
        IReadOnlyList<PhysicalGoldSelectedLot> selections,
        Guid? sourceVaultId = null)
        => new(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            sourceVaultId ?? SourceVaultId,
            CoreLedgerTestData.DestinationAccountId,
            CoreLedgerTestData.GoldAssetId,
            CoreLedgerTestData.CashAssetId,
            Quantity.FromRaw(
                selections.Sum(x => x.GrossWeight.RawE8)),
            selections.Sum(x => x.PieceCount),
            selections,
            Money.FromMinorUnits(100_000, CurrencyCode.TRY),
            CoreLedgerTestData.ExecutionDate,
            CounterpartyInstitutionId: CounterpartyId,
            ExternalReference: "SYNTHETIC-GOLD-SALE",
            Note: "Synthetic test sale.");

    private static PhysicalGoldTransferCommand TransferCommand(
        IReadOnlyList<PhysicalGoldSelectedLot> selections)
        => new(
            CoreLedgerTestData.HouseholdId,
            CoreLedgerTestData.PortfolioId,
            SourceVaultId,
            CoreLedgerTestData.PortfolioId,
            DestinationVaultId,
            CoreLedgerTestData.GoldAssetId,
            Quantity.FromRaw(
                selections.Sum(x => x.GrossWeight.RawE8)),
            selections.Sum(x => x.PieceCount),
            selections,
            CoreLedgerTestData.ExecutionDate,
            ExternalReference: "SYNTHETIC-GOLD-TRANSFER",
            Note: "Synthetic custody transfer.");

    private static PreviewPhysicalGoldPurchaseUseCase PurchasePreview(
        WealthLedgerDbContext context)
    {
        var posting = new EfCoreLedgerPostingStore(context);
        return new PreviewPhysicalGoldPurchaseUseCase(
            new EfCoreOpeningBalanceReferenceStore(context),
            new EfCorePhysicalGoldCustodyReadStore(posting),
            new FixedTimeProvider(Now));
    }

    private static RecordPhysicalGoldPurchaseUseCase PurchaseRecorder(
        WealthLedgerDbContext context)
    {
        var posting = new EfCoreLedgerPostingStore(context);
        return new RecordPhysicalGoldPurchaseUseCase(
            new EfCoreOpeningBalanceReferenceStore(context),
            posting,
            posting,
            new FixedTimeProvider(Now));
    }

    private static PreviewPhysicalGoldSaleUseCase SalePreview(
        WealthLedgerDbContext context)
    {
        var posting = new EfCoreLedgerPostingStore(context);
        return new PreviewPhysicalGoldSaleUseCase(
            new EfCoreOpeningBalanceReferenceStore(context),
            new EfCorePhysicalGoldCustodyReadStore(posting),
            new EfCorePhysicalGoldRealizedCostReadStore(context),
            new FixedTimeProvider(Now));
    }

    private static RecordPhysicalGoldSaleUseCase SaleRecorder(
        WealthLedgerDbContext context)
    {
        var posting = new EfCoreLedgerPostingStore(context);
        return new RecordPhysicalGoldSaleUseCase(
            new EfCoreOpeningBalanceReferenceStore(context),
            posting,
            posting,
            new FixedTimeProvider(Now));
    }

    private static PreviewPhysicalGoldTransferUseCase TransferPreview(
        WealthLedgerDbContext context)
    {
        var posting = new EfCoreLedgerPostingStore(context);
        return new PreviewPhysicalGoldTransferUseCase(
            new EfCoreOpeningBalanceReferenceStore(context),
            new EfCorePhysicalGoldCustodyReadStore(posting),
            new FixedTimeProvider(Now));
    }

    private static RecordPhysicalGoldTransferUseCase TransferRecorder(
        WealthLedgerDbContext context)
    {
        var posting = new EfCoreLedgerPostingStore(context);
        return new RecordPhysicalGoldTransferUseCase(
            new EfCoreOpeningBalanceReferenceStore(context),
            posting,
            posting,
            new FixedTimeProvider(Now));
    }

    private static GetPhysicalGoldActivityVerificationUseCase Verification(
        WealthLedgerDbContext context)
        => new(
            new EfCorePhysicalGoldVerificationReadStore(context),
            new EfCorePhysicalGoldRealizedCostReadStore(context),
            new EfCoreOpeningBalanceReferenceStore(context),
            new FixedTimeProvider(Now));

    private static async Task SeedAsync(SqliteTestDatabase database)
    {
        await using var context = database.CreateContext();
        await CoreLedgerTestData.SeedMasterDataAsync(context);
        context.Accounts.AddRange(
            new AccountRow
            {
                Id = SourceVaultId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = "GOLD_VAULT_A",
                Name = "Synthetic Vault A",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            },
            new AccountRow
            {
                Id = DestinationVaultId,
                HouseholdId = CoreLedgerTestData.HouseholdId,
                Code = "GOLD_VAULT_B",
                Name = "Synthetic Vault B",
                Type = AccountType.PhysicalVault,
                IsActive = true,
                OpenedOn = new DateOnly(2026, 1, 1)
            });
        context.Institutions.Add(new InstitutionRow
        {
            Id = CounterpartyId,
            Code = "SYNTHETIC_JEWELER",
            Name = "Synthetic Jeweler",
            Type = InstitutionType.Jeweler,
            IsActive = true
        });
        await context.SaveChangesAsync();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        internal FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
