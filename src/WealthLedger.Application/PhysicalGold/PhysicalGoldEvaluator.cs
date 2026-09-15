using WealthLedger.Application.OpeningBalances;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.PhysicalGold;

internal sealed record ValidatedPhysicalGoldTrade(
    TransactionType TradeType,
    PhysicalGoldTradeScope Scope,
    OpeningBalancePortfolioReference Portfolio,
    OpeningBalanceAccountReference GoldAccount,
    OpeningBalanceAccountReference CashAccount,
    OpeningBalanceAssetReference GoldAsset,
    OpeningBalanceAssetReference CashAsset,
    OpeningBalanceCurrencyReference Currency,
    OpeningBalanceInstitutionReference? Counterparty,
    Quantity GrossWeight,
    int PieceCount,
    Fineness? Fineness,
    UnitPrice? ExecutedUnitPrice,
    Money CashConsideration,
    DateOnly ExecutionDate,
    DateOnly? OrderDate,
    DateOnly? SettlementDate,
    IReadOnlyList<PhysicalGoldCostInput> Costs,
    string? Hallmark,
    string? CertificateReference,
    string? LotNote,
    string? ExternalReference,
    string? Note,
    PhysicalGoldEconomics Economics,
    IReadOnlyList<string> WarningCodes);

internal sealed record ValidatedPhysicalGoldTransfer(
    PhysicalGoldTransferScope Scope,
    OpeningBalancePortfolioReference SourcePortfolio,
    OpeningBalanceAccountReference SourceAccount,
    OpeningBalancePortfolioReference DestinationPortfolio,
    OpeningBalanceAccountReference DestinationAccount,
    OpeningBalanceAssetReference GoldAsset,
    OpeningBalancePortfolioReference? CashPortfolio,
    OpeningBalanceAccountReference? CashAccount,
    OpeningBalanceAssetReference? CashAsset,
    OpeningBalanceCurrencyReference Currency,
    Quantity GrossWeight,
    int PieceCount,
    DateOnly ExecutionDate,
    IReadOnlyList<PhysicalGoldCostInput> Costs,
    string? ExternalReference,
    string? Note,
    PhysicalGoldEconomics Economics,
    IReadOnlyList<string> WarningCodes);

internal static class PhysicalGoldEvaluator
{
    internal static async Task<ValidatedPhysicalGoldTrade> EvaluateTradeAsync(
        TransactionType tradeType,
        PhysicalGoldTradeScope scope,
        Quantity grossWeight,
        int pieceCount,
        Fineness? fineness,
        UnitPrice? executedUnitPrice,
        Money cashConsideration,
        DateOnly executionDate,
        DateOnly? orderDate,
        DateOnly? settlementDate,
        IReadOnlyList<PhysicalGoldCostInput>? costs,
        Guid? counterpartyInstitutionId,
        string? hallmark,
        string? certificateReference,
        string? lotNote,
        string? externalReference,
        string? note,
        DateTimeOffset evaluatedAtUtc,
        TimeZoneInfo operatingTimeZone,
        IOpeningBalanceReferenceReadStore referenceStore,
        CancellationToken cancellationToken)
    {
        if (tradeType is not TransactionType.Buy and not TransactionType.Sell)
        {
            throw new ArgumentOutOfRangeException(nameof(tradeType));
        }

        ArgumentNullException.ThrowIfNull(cashConsideration);
        ArgumentNullException.ThrowIfNull(operatingTimeZone);
        ArgumentNullException.ThrowIfNull(referenceStore);

        EnsureTradeScope(scope);
        EnsurePositiveMovement(grossWeight, pieceCount);

        if (tradeType == TransactionType.Buy && fineness is null)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.FinenessInvalid,
                "A physical-gold purchase requires exact fineness evidence.");
        }

        if (tradeType == TransactionType.Sell && fineness is not null)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.FinenessInvalid,
                "A sale preserves each selected lot's fineness rather than accepting a new value.");
        }

        if (executedUnitPrice is { RawE8: <= 0 })
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.PriceInvalid,
                "An entered physical-gold price must be positive.");
        }

        if (cashConsideration.MinorUnits <= 0)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.AmountInvalid,
                "Physical-gold cash consideration must be greater than zero.");
        }

        var normalizedReference =
            PhysicalGoldCanonicalizer.NormalizeExternalReference(
                externalReference);
        var normalizedNote = PhysicalGoldCanonicalizer.NormalizeNote(note);

        EnsureProvenance(normalizedReference, normalizedNote);
        ValidateTradeDates(
            executionDate,
            orderDate,
            settlementDate,
            evaluatedAtUtc,
            operatingTimeZone);

        var portfolio = await RequirePortfolioAsync(
            scope.PortfolioId, referenceStore, cancellationToken);
        var goldAccount = await RequireAccountAsync(
            scope.GoldAccountId, "gold account", referenceStore,
            cancellationToken);
        var cashAccount = scope.CashAccountId == scope.GoldAccountId
            ? goldAccount
            : await RequireAccountAsync(
                scope.CashAccountId, "cash account", referenceStore,
                cancellationToken);
        var goldAsset = await RequireAssetAsync(
            scope.GoldAssetId, "gold asset", referenceStore,
            cancellationToken);
        var cashAsset = await RequireAssetAsync(
            scope.CashAssetId, "cash asset", referenceStore,
            cancellationToken);

        ValidateCommonReferences(
            scope.HouseholdId,
            portfolio,
            [goldAccount, cashAccount],
            [goldAsset, cashAsset]);
        ValidateGoldAsset(goldAsset);
        ValidateCashAsset(cashAsset);
        await ValidateGoldAccountAsync(
            goldAccount, referenceStore, cancellationToken);
        await ValidateCashAccountAsync(
            cashAccount, referenceStore, cancellationToken);
        ValidateAccountOpening(executionDate, goldAccount, cashAccount);

        var currencyCode = cashConsideration.Currency;
        ValidateCurrency(
            goldAsset, cashAsset, currencyCode, executedUnitPrice);
        var currency = await RequireCurrencyAsync(
            currencyCode, referenceStore, cancellationToken);

        OpeningBalanceInstitutionReference? counterparty = null;
        if (counterpartyInstitutionId is Guid counterpartyId)
        {
            if (counterpartyId == Guid.Empty)
            {
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.CounterpartyInvalid,
                    "A counterparty identity cannot be empty.");
            }

            counterparty = await referenceStore.FindInstitutionAsync(
                counterpartyId, cancellationToken)
                ?? throw PhysicalGoldException.Missing(
                    "The requested counterparty does not exist.");
            ValidateCounterparty(counterparty);
        }

        var normalizedCosts = PhysicalGoldCanonicalizer.NormalizeCosts(
            costs, tradeType, currencyCode);
        PhysicalGoldEconomics economics;
        try
        {
            economics = PhysicalGoldEconomicsCalculator.ComputeTrade(
                tradeType,
                grossWeight,
                executedUnitPrice,
                cashConsideration,
                normalizedCosts,
                currency.MinorUnitDigits);
        }
        catch (OverflowException exception)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Validation,
                PhysicalGoldErrorCodes.PrecisionOverflow,
                "Physical-gold economics exceed the supported exact range.",
                innerException: exception);
        }

        var warnings = ValidateEconomics(economics, normalizedNote);

        if (executedUnitPrice is null)
        {
            warnings.Add(PhysicalGoldWarningCodes.ExecutedPriceUnavailable);
        }

        return new ValidatedPhysicalGoldTrade(
            tradeType,
            scope,
            portfolio,
            goldAccount,
            cashAccount,
            goldAsset,
            cashAsset,
            currency,
            counterparty,
            grossWeight,
            pieceCount,
            fineness,
            executedUnitPrice,
            cashConsideration,
            executionDate,
            orderDate,
            settlementDate,
            normalizedCosts,
            PhysicalGoldCanonicalizer.NormalizeHallmark(hallmark),
            PhysicalGoldCanonicalizer.NormalizeCertificateReference(
                certificateReference),
            PhysicalGoldCanonicalizer.NormalizeLotNote(lotNote),
            normalizedReference,
            normalizedNote,
            economics,
            warnings);
    }

    internal static async Task<ValidatedPhysicalGoldTransfer>
        EvaluateTransferAsync(
            PhysicalGoldTransferCommand command,
            DateTimeOffset evaluatedAtUtc,
            TimeZoneInfo operatingTimeZone,
            IOpeningBalanceReferenceReadStore referenceStore,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(operatingTimeZone);
        ArgumentNullException.ThrowIfNull(referenceStore);

        var scope = new PhysicalGoldTransferScope(
            command.HouseholdId,
            command.SourcePortfolioId,
            command.SourceGoldAccountId,
            command.DestinationPortfolioId,
            command.DestinationGoldAccountId,
            command.GoldAssetId,
            command.CashPortfolioId,
            command.CashAccountId,
            command.CashAssetId);

        EnsureTransferScope(scope);
        EnsurePositiveMovement(command.GrossWeight, command.PieceCount);
        ValidateExecutionDate(
            command.ExecutionDate, evaluatedAtUtc, operatingTimeZone);

        var normalizedReference =
            PhysicalGoldCanonicalizer.NormalizeExternalReference(
                command.ExternalReference);
        var normalizedNote =
            PhysicalGoldCanonicalizer.NormalizeNote(command.Note);
        EnsureProvenance(normalizedReference, normalizedNote);

        var sourcePortfolio = await RequirePortfolioAsync(
            scope.SourcePortfolioId, referenceStore, cancellationToken);
        var destinationPortfolio = await RequirePortfolioAsync(
            scope.DestinationPortfolioId, referenceStore, cancellationToken);
        var sourceAccount = await RequireAccountAsync(
            scope.SourceGoldAccountId, "source gold account",
            referenceStore, cancellationToken);
        var destinationAccount = await RequireAccountAsync(
            scope.DestinationGoldAccountId, "destination gold account",
            referenceStore, cancellationToken);
        var goldAsset = await RequireAssetAsync(
            scope.GoldAssetId, "gold asset", referenceStore,
            cancellationToken);

        ValidateCommonReferences(
            scope.HouseholdId,
            sourcePortfolio,
            [sourceAccount, destinationAccount],
            [goldAsset]);

        if (destinationPortfolio.HouseholdId != scope.HouseholdId)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.HouseholdMismatch,
                "Both transfer portfolios must belong to the selected household.");
        }

        if (destinationPortfolio.Status != PortfolioStatus.Active)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceInactive,
                "Both transfer portfolios must be active.");
        }

        ValidateGoldAsset(goldAsset);
        await ValidateGoldAccountAsync(
            sourceAccount, referenceStore, cancellationToken);
        await ValidateGoldAccountAsync(
            destinationAccount, referenceStore, cancellationToken);
        ValidateAccountOpening(
            command.ExecutionDate, sourceAccount, destinationAccount);

        var currencyCode = goldAsset.BaseCurrency
            ?? throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceShapeInvalid,
                "The physical-gold asset must declare a base currency.");
        var normalizedCosts = PhysicalGoldCanonicalizer.NormalizeCosts(
            command.Costs, TransactionType.Transfer, currencyCode);

        var needsCash = normalizedCosts.Any(
            x => x.Treatment == CostTreatment.AdditionalCashOutflow);
        var anyCash = scope.CashPortfolioId is not null
            || scope.CashAccountId is not null
            || scope.CashAssetId is not null;

        if (needsCash && (scope.CashPortfolioId is null
                          || scope.CashAccountId is null
                          || scope.CashAssetId is null))
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.CashScopeRequired,
                "A transfer with a cash outflow requires a complete cash scope.");
        }

        if (!needsCash && anyCash)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.CashScopeForbidden,
                "A transfer without a cash outflow cannot carry a cash scope.");
        }

        OpeningBalancePortfolioReference? cashPortfolio = null;
        OpeningBalanceAccountReference? cashAccount = null;
        OpeningBalanceAssetReference? cashAsset = null;

        if (needsCash)
        {
            cashPortfolio = await RequirePortfolioAsync(
                scope.CashPortfolioId!.Value,
                referenceStore,
                cancellationToken);
            cashAccount = await RequireAccountAsync(
                scope.CashAccountId!.Value,
                "cash account",
                referenceStore,
                cancellationToken);
            cashAsset = await RequireAssetAsync(
                scope.CashAssetId!.Value,
                "cash asset",
                referenceStore,
                cancellationToken);

            ValidateCommonReferences(
                scope.HouseholdId,
                cashPortfolio,
                [cashAccount],
                [cashAsset]);
            ValidateCashAsset(cashAsset);
            await ValidateCashAccountAsync(
                cashAccount, referenceStore, cancellationToken);
            ValidateAccountOpening(command.ExecutionDate, cashAccount);

            if (cashAsset.BaseCurrency != currencyCode)
            {
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.CurrencyMismatch,
                    "Transfer costs and the cash asset must use the gold asset's currency.");
            }
        }

        var currency = await RequireCurrencyAsync(
            currencyCode, referenceStore, cancellationToken);
        PhysicalGoldEconomics economics;
        try
        {
            economics = PhysicalGoldEconomicsCalculator.ComputeTransfer(
                currencyCode, normalizedCosts);
        }
        catch (OverflowException exception)
        {
            throw new PhysicalGoldException(
                PhysicalGoldErrorCategory.Validation,
                PhysicalGoldErrorCodes.PrecisionOverflow,
                "Physical-gold transfer costs exceed the supported exact range.",
                innerException: exception);
        }

        return new ValidatedPhysicalGoldTransfer(
            scope,
            sourcePortfolio,
            sourceAccount,
            destinationPortfolio,
            destinationAccount,
            goldAsset,
            cashPortfolio,
            cashAccount,
            cashAsset,
            currency,
            command.GrossWeight,
            command.PieceCount,
            command.ExecutionDate,
            normalizedCosts,
            normalizedReference,
            normalizedNote,
            economics,
            []);
    }

    private static List<string> ValidateEconomics(
        PhysicalGoldEconomics economics,
        string? note)
    {
        if (economics.ExpectedConsideration is { MinorUnits: < 0 })
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.NegativeExpectedProceeds,
                "Entered deductions cannot exceed price-implied sale proceeds.");
        }

        var warnings = new List<string>();
        if (economics.Discrepancy
            == PhysicalGoldDiscrepancyClassification.Material)
        {
            if (string.IsNullOrEmpty(note))
            {
                throw PhysicalGoldException.Invalid(
                    PhysicalGoldErrorCodes.UnexplainedDiscrepancy,
                    "Explain the difference between consideration and the entered gross-gram price.");
            }

            warnings.Add(PhysicalGoldWarningCodes.ExplainedDiscrepancy);
        }
        else if (economics.Discrepancy
                 == PhysicalGoldDiscrepancyClassification.RoundingConsistent)
        {
            warnings.Add(PhysicalGoldWarningCodes.RoundingConsistent);
        }

        return warnings;
    }

    private static void EnsureTradeScope(PhysicalGoldTradeScope scope)
    {
        EnsureNonEmpty(scope.HouseholdId, "household");
        EnsureNonEmpty(scope.PortfolioId, "portfolio");
        EnsureNonEmpty(scope.GoldAccountId, "gold account");
        EnsureNonEmpty(scope.CashAccountId, "cash account");
        EnsureNonEmpty(scope.GoldAssetId, "gold asset");
        EnsureNonEmpty(scope.CashAssetId, "cash asset");

        if (scope.GoldAssetId == scope.CashAssetId)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceShapeInvalid,
                "Gold and cash assets must differ.");
        }
    }

    private static void EnsureTransferScope(PhysicalGoldTransferScope scope)
    {
        EnsureNonEmpty(scope.HouseholdId, "household");
        EnsureNonEmpty(scope.SourcePortfolioId, "source portfolio");
        EnsureNonEmpty(scope.SourceGoldAccountId, "source gold account");
        EnsureNonEmpty(scope.DestinationPortfolioId, "destination portfolio");
        EnsureNonEmpty(scope.DestinationGoldAccountId, "destination gold account");
        EnsureNonEmpty(scope.GoldAssetId, "gold asset");

        if (scope.SourcePortfolioId == scope.DestinationPortfolioId
            && scope.SourceGoldAccountId == scope.DestinationGoldAccountId)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.TransferScopeInvalid,
                "Source and destination custody scopes must differ.");
        }
    }

    private static void EnsurePositiveMovement(
        Quantity grossWeight,
        int pieceCount)
    {
        if (grossWeight.RawE8 <= 0)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.QuantityInvalid,
                "Gross physical-gold weight must be greater than zero.");
        }

        if (pieceCount <= 0)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.PieceCountInvalid,
                "Physical-gold piece count must be greater than zero.");
        }
    }

    private static void EnsureProvenance(
        string? externalReference,
        string? note)
    {
        if (externalReference is null && note is null)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ProvenanceRequired,
                "A physical-gold activity requires an external reference or explanatory note.");
        }
    }

    private static void ValidateTradeDates(
        DateOnly executionDate,
        DateOnly? orderDate,
        DateOnly? settlementDate,
        DateTimeOffset evaluatedAtUtc,
        TimeZoneInfo operatingTimeZone)
    {
        ValidateExecutionDate(
            executionDate, evaluatedAtUtc, operatingTimeZone);
        var currentDate = GetLocalDate(evaluatedAtUtc, operatingTimeZone);
        if (orderDate > currentDate || settlementDate > currentDate)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.DateInFuture,
                "A completed physical-gold activity cannot carry a future date.");
        }

        if (orderDate > executionDate || settlementDate < executionDate)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.DateOrderInvalid,
                "Physical-gold dates must satisfy order, execution, then settlement order.");
        }
    }

    private static void ValidateExecutionDate(
        DateOnly executionDate,
        DateTimeOffset evaluatedAtUtc,
        TimeZoneInfo operatingTimeZone)
    {
        if (executionDate > GetLocalDate(evaluatedAtUtc, operatingTimeZone))
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.DateInFuture,
                "A completed physical-gold activity cannot carry a future date.");
        }
    }

    private static DateOnly GetLocalDate(
        DateTimeOffset evaluatedAtUtc,
        TimeZoneInfo operatingTimeZone)
        => DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(evaluatedAtUtc, operatingTimeZone).DateTime);

    private static void ValidateCommonReferences(
        Guid householdId,
        OpeningBalancePortfolioReference portfolio,
        IReadOnlyCollection<OpeningBalanceAccountReference> accounts,
        IReadOnlyCollection<OpeningBalanceAssetReference> assets)
    {
        if (portfolio.HouseholdId != householdId
            || accounts.Any(x => x.HouseholdId != householdId))
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.HouseholdMismatch,
                "The portfolio and accounts must belong to the selected household.");
        }

        if (portfolio.Status != PortfolioStatus.Active
            || accounts.Any(x => !x.IsActive || x.ClosedOn is not null)
            || assets.Any(x => !x.IsActive))
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceInactive,
                "Every selected physical-gold reference must be active.");
        }
    }

    private static void ValidateGoldAsset(OpeningBalanceAssetReference asset)
    {
        if (asset.Type != AssetType.PhysicalGold
            || asset.BaseUnit != AssetUnit.GrossGram
            || asset.LotTrackingMode != LotTrackingMode.Required
            || asset.BaseCurrency is null)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceShapeInvalid,
                "Gold must be an active PhysicalGold asset measured in gross grams with required lot tracking and a base currency.");
        }
    }

    private static void ValidateCashAsset(OpeningBalanceAssetReference asset)
    {
        if (asset.Type is not AssetType.Cash and not AssetType.Currency
            || asset.BaseUnit != AssetUnit.CurrencyUnit
            || asset.LotTrackingMode != LotTrackingMode.None
            || asset.BaseCurrency is null)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceShapeInvalid,
                "Cash settlement requires a cash or currency asset measured in currency units without lot tracking.");
        }
    }

    private static async Task ValidateGoldAccountAsync(
        OpeningBalanceAccountReference account,
        IOpeningBalanceReferenceReadStore referenceStore,
        CancellationToken cancellationToken)
    {
        if (account.Type != AccountType.PhysicalVault)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceShapeInvalid,
                "Physical gold requires a physical-vault account.");
        }

        await ValidateOptionalAccountInstitutionAsync(
            account, referenceStore, cancellationToken);
    }

    private static async Task ValidateCashAccountAsync(
        OpeningBalanceAccountReference account,
        IOpeningBalanceReferenceReadStore referenceStore,
        CancellationToken cancellationToken)
    {
        if (account.Type is not AccountType.Cash
            and not AccountType.Investment
            and not AccountType.Pension)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceShapeInvalid,
                "The selected account cannot hold cash settlement.");
        }

        if (account.Type is AccountType.Investment or AccountType.Pension
            && account.InstitutionId is null)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceShapeInvalid,
                "An investment or pension account requires an institution.");
        }

        await ValidateOptionalAccountInstitutionAsync(
            account, referenceStore, cancellationToken);
    }

    private static async Task ValidateOptionalAccountInstitutionAsync(
        OpeningBalanceAccountReference account,
        IOpeningBalanceReferenceReadStore referenceStore,
        CancellationToken cancellationToken)
    {
        if (account.InstitutionId is not Guid institutionId)
        {
            return;
        }

        var institution = await referenceStore.FindInstitutionAsync(
            institutionId, cancellationToken);
        if (institution is null || !institution.IsActive)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceInactive,
                "An account's institution must exist and be active.");
        }
    }

    private static void ValidateCounterparty(
        OpeningBalanceInstitutionReference counterparty)
    {
        if (!counterparty.IsActive
            || counterparty.Type is not InstitutionType.Jeweler
                and not InstitutionType.Bank
                and not InstitutionType.Other)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.CounterpartyInvalid,
                "A counterparty must be an active jeweler, bank, or other institution.");
        }
    }

    private static void ValidateCurrency(
        OpeningBalanceAssetReference goldAsset,
        OpeningBalanceAssetReference cashAsset,
        CurrencyCode considerationCurrency,
        UnitPrice? price)
    {
        if (goldAsset.BaseCurrency != cashAsset.BaseCurrency
            || cashAsset.BaseCurrency != considerationCurrency
            || price is not null && price.Currency != considerationCurrency)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.CurrencyMismatch,
                "Gold, cash, consideration, costs, and any entered price must use one currency.");
        }
    }

    private static void ValidateAccountOpening(
        DateOnly executionDate,
        params OpeningBalanceAccountReference[] accounts)
    {
        if (accounts.Any(
                x => x.OpenedOn is DateOnly openedOn
                     && executionDate < openedOn))
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.DateBeforeAccountOpening,
                "The execution date precedes a selected account's opening date.");
        }
    }

    private static async Task<OpeningBalancePortfolioReference>
        RequirePortfolioAsync(
            Guid id,
            IOpeningBalanceReferenceReadStore store,
            CancellationToken cancellationToken)
        => await store.FindPortfolioAsync(id, cancellationToken)
           ?? throw PhysicalGoldException.Missing(
               "The requested portfolio does not exist.");

    private static async Task<OpeningBalanceAccountReference>
        RequireAccountAsync(
            Guid id,
            string label,
            IOpeningBalanceReferenceReadStore store,
            CancellationToken cancellationToken)
        => await store.FindAccountAsync(id, cancellationToken)
           ?? throw PhysicalGoldException.Missing(
               $"The requested {label} does not exist.");

    private static async Task<OpeningBalanceAssetReference> RequireAssetAsync(
        Guid id,
        string label,
        IOpeningBalanceReferenceReadStore store,
        CancellationToken cancellationToken)
        => await store.FindAssetAsync(id, cancellationToken)
           ?? throw PhysicalGoldException.Missing(
               $"The requested {label} does not exist.");

    private static async Task<OpeningBalanceCurrencyReference>
        RequireCurrencyAsync(
            CurrencyCode code,
            IOpeningBalanceReferenceReadStore store,
            CancellationToken cancellationToken)
        => await store.FindCurrencyAsync(code, cancellationToken)
           ?? throw PhysicalGoldException.Missing(
               "The requested currency does not exist.");

    private static void EnsureNonEmpty(Guid id, string label)
    {
        if (id == Guid.Empty)
        {
            throw PhysicalGoldException.Invalid(
                PhysicalGoldErrorCodes.ReferenceShapeInvalid,
                $"A physical-gold activity requires a {label}.");
        }
    }
}
