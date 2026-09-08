using WealthLedger.Application.CoreLedger;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Assets;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.Lots;
using WealthLedger.Domain.Portfolios;

namespace WealthLedger.Application.Tests.Navigation;

public sealed class LedgerTransactionExplanationUseCaseTests
{
    [Fact]
    public async Task Explanation_ComposesAllCurrentContextInOneBoundedRequest()
    {
        var fixture = ExplanationFixture.Create();
        var contextStore = new StubContextStore(fixture.Context);
        var useCase = CreateUseCase(fixture.Transaction, contextStore);

        var result = await useCase.ExecuteAsync(
            new GetLedgerTransactionExplanationQuery(
                fixture.HouseholdId,
                fixture.Transaction.TransactionId));

        Assert.NotNull(result);
        Assert.Same(fixture.Transaction, result.Transaction);
        Assert.Same(fixture.Context, result.CurrentContext);

        var query = Assert.IsType<LedgerTransactionCurrentContextQuery>(
            contextStore.Query);
        Assert.Equal(fixture.HouseholdId, query.HouseholdId);
        Assert.Equal(
            fixture.Transaction.Entries.Select(entry => entry.EntryId),
            query.EntryIds);
        Assert.Equal(
            [fixture.LotId],
            query.AssetLotIds);
        Assert.Equal(fixture.MemberId, query.HouseholdMemberId);
        Assert.Equal(["TRY", "USD"], query.CurrencyCodes);
    }

    [Fact]
    public async Task Explanation_OutsideSelectedHouseholdIsNotDisclosed()
    {
        var fixture = ExplanationFixture.Create();
        var contextStore = new StubContextStore(fixture.Context);
        var useCase = CreateUseCase(fixture.Transaction, contextStore);

        var result = await useCase.ExecuteAsync(
            new GetLedgerTransactionExplanationQuery(
                Guid.NewGuid(),
                fixture.Transaction.TransactionId));

        Assert.Null(result);
        Assert.Null(contextStore.Query);
    }

    [Fact]
    public async Task Explanation_IncompleteEntryContextFailsClosed()
    {
        var fixture = ExplanationFixture.Create();
        var incomplete = fixture.Context with
        {
            Entries = fixture.Context.Entries.Take(1).ToArray()
        };
        var useCase = CreateUseCase(
            fixture.Transaction,
            new StubContextStore(incomplete));

        await Assert.ThrowsAsync<NavigationPersistenceException>(
            () => useCase.ExecuteAsync(
                new GetLedgerTransactionExplanationQuery(
                    fixture.HouseholdId,
                    fixture.Transaction.TransactionId)));
    }

    [Fact]
    public async Task Explanation_MismatchedCurrentIdentityFailsClosed()
    {
        var fixture = ExplanationFixture.Create();
        var first = fixture.Context.Entries[0];
        var mismatched = fixture.Context with
        {
            Entries =
            [
                first with
                {
                    Asset = first.Asset with { AssetId = Guid.NewGuid() }
                },
                fixture.Context.Entries[1]
            ]
        };
        var useCase = CreateUseCase(
            fixture.Transaction,
            new StubContextStore(mismatched));

        await Assert.ThrowsAsync<NavigationPersistenceException>(
            () => useCase.ExecuteAsync(
                new GetLedgerTransactionExplanationQuery(
                    fixture.HouseholdId,
                    fixture.Transaction.TransactionId)));
    }

    private static GetLedgerTransactionExplanationUseCase CreateUseCase(
        LedgerTransactionDetail detail,
        ILedgerTransactionCurrentContextReadStore contextStore)
        => new(
            new GetLedgerTransactionUseCase(
                new StubTransactionReadStore(detail)),
            contextStore);

    private sealed class StubTransactionReadStore
        : ILedgerTransactionReadStore
    {
        private readonly LedgerTransactionDetail _detail;

        internal StubTransactionReadStore(LedgerTransactionDetail detail)
        {
            _detail = detail;
        }

        public Task<LedgerTransactionDetail?> FindByIdAsync(
            Guid transactionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<LedgerTransactionDetail?>(
                transactionId == _detail.TransactionId
                    ? _detail
                    : null);
    }

    private sealed class StubContextStore
        : ILedgerTransactionCurrentContextReadStore
    {
        private readonly LedgerTransactionCurrentContext? _context;

        internal StubContextStore(LedgerTransactionCurrentContext? context)
        {
            _context = context;
        }

        internal LedgerTransactionCurrentContextQuery? Query { get; private set; }

        public Task<LedgerTransactionCurrentContext?> ReadAsync(
            LedgerTransactionCurrentContextQuery query,
            CancellationToken cancellationToken = default)
        {
            Query = query;
            return Task.FromResult(_context);
        }
    }

    private sealed record ExplanationFixture(
        Guid HouseholdId,
        Guid MemberId,
        Guid LotId,
        LedgerTransactionDetail Transaction,
        LedgerTransactionCurrentContext Context)
    {
        internal static ExplanationFixture Create()
        {
            var householdId = Guid.NewGuid();
            var memberId = Guid.NewGuid();
            var transactionId = Guid.NewGuid();
            var firstEntryId = Guid.NewGuid();
            var secondEntryId = Guid.NewGuid();
            var lotId = Guid.NewGuid();
            var portfolio = new PortfolioNavigationItem(
                Guid.NewGuid(),
                householdId,
                "CORE",
                "Synthetic portfolio",
                PortfolioStatus.Active,
                DateTimeOffset.UnixEpoch,
                ClosedAtUtc: null);
            var institution = new AccountInstitutionNavigationItem(
                Guid.NewGuid(),
                "SYNTHETIC_INSTITUTION",
                "Synthetic institution",
                InstitutionType.Broker,
                IsActive: true);
            var account = new AccountNavigationItem(
                Guid.NewGuid(),
                householdId,
                institution,
                "PRIMARY",
                "Synthetic account",
                AccountType.Investment,
                IsActive: true,
                OpenedOn: null,
                ClosedOn: null);
            var fund = new AssetNavigationItem(
                Guid.NewGuid(),
                "SYNTHETIC_FUND",
                "Synthetic fund",
                AssetType.Fund,
                AssetUnit.FundUnit,
                "TRY",
                LotTrackingMode.Required,
                IsActive: true,
                DateTimeOffset.UnixEpoch);
            var cash = new AssetNavigationItem(
                Guid.NewGuid(),
                "SYNTHETIC_CASH",
                "Synthetic cash",
                AssetType.Cash,
                AssetUnit.CurrencyUnit,
                "TRY",
                LotTrackingMode.None,
                IsActive: true,
                DateTimeOffset.UnixEpoch);
            var transaction = new LedgerTransactionDetail(
                transactionId,
                householdId,
                TransactionType.Buy,
                TransactionStatus.Posted,
                OrderDate: null,
                new DateOnly(2026, 9, 7),
                SettlementDate: null,
                ExternalReference: null,
                Note: null,
                ReversalOfTransactionId: null,
                ReversedByTransactionId: null,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                [
                    new LedgerTransactionEntryDetail(
                        firstEntryId,
                        0,
                        portfolio.PortfolioId,
                        account.AccountId,
                        fund.AssetId,
                        125_000_000,
                        EntryRole.Principal,
                        10_000_000_000,
                        "TRY",
                        DateTimeOffset.UnixEpoch),
                    new LedgerTransactionEntryDetail(
                        secondEntryId,
                        1,
                        portfolio.PortfolioId,
                        account.AccountId,
                        cash.AssetId,
                        -12_500_000_000,
                        EntryRole.Consideration,
                        UnitPriceRawE8: null,
                        PriceCurrencyCode: null,
                        DateTimeOffset.UnixEpoch)
                ],
                new LedgerTransactionCashFlowDetail(
                    CashFlowCategory.AcademicIncome,
                    memberId),
                [
                    new LedgerTransactionCostDetail(
                        Guid.NewGuid(),
                        CostType.Commission,
                        CostTreatment.AdditionalCashOutflow,
                        250,
                        "USD",
                        Note: null)
                ],
                [
                    new LedgerTransactionCreatedLotDetail(
                        lotId,
                        fund.AssetId,
                        firstEntryId,
                        new DateOnly(2026, 9, 7),
                        1_250,
                        "TRY",
                        CostBasisStatus.Known,
                        DateTimeOffset.UnixEpoch)
                ],
                [
                    new LedgerTransactionLotAllocationDetail(
                        Guid.NewGuid(),
                        lotId,
                        firstEntryId,
                        125_000_000,
                        DateTimeOffset.UnixEpoch)
                ]);
            var context = new LedgerTransactionCurrentContext(
                new HouseholdNavigationItem(
                    householdId,
                    "Synthetic household",
                    new CurrencyNavigationItem("TRY", "Turkish lira", 2),
                    DateTimeOffset.UnixEpoch),
                [
                    new LedgerTransactionEntryCurrentContext(
                        firstEntryId,
                        portfolio,
                        account,
                        fund),
                    new LedgerTransactionEntryCurrentContext(
                        secondEntryId,
                        portfolio,
                        account,
                        cash)
                ],
                [new LedgerTransactionLotCurrentContext(lotId, fund)],
                new HouseholdMemberNavigationItem(
                    memberId,
                    householdId,
                    "Synthetic member",
                    IsActive: true,
                    DateTimeOffset.UnixEpoch),
                [
                    new CurrencyNavigationItem("TRY", "Turkish lira", 2),
                    new CurrencyNavigationItem("USD", "US dollar", 2)
                ]);

            return new ExplanationFixture(
                householdId,
                memberId,
                lotId,
                transaction,
                context);
        }
    }
}
