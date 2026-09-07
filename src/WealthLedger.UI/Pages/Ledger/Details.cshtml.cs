using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LocalData;
using WealthLedger.Application.Navigation;
using WealthLedger.Domain.Lots;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Ledger;

[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class DetailsModel : PageModel
{
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly GetLedgerTransactionExplanationUseCase _getExplanation;
    private readonly ValuePresenter _values;

    public DetailsModel(
        ReadyHouseholdResolver householdResolver,
        GetLedgerTransactionExplanationUseCase getExplanation,
        ValuePresenter values)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _getExplanation = getExplanation
            ?? throw new ArgumentNullException(nameof(getExplanation));
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public bool InvalidIdentifier { get; private set; }

    public bool TransactionNotFound { get; private set; }

    public bool LoadUnavailable { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public TransactionExplanationDisplay? Transaction { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        string? transactionId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(transactionId, "D", out var parsedId)
            || parsedId == Guid.Empty)
        {
            InvalidIdentifier = true;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return Page();
        }

        try
        {
            var householdResolution = await _householdResolver.ResolveAsync(
                cancellationToken);

            if (householdResolution.State
                != ReadyHouseholdResolutionState.Single)
            {
                HouseholdStateResourceKey = householdResolution.State
                    == ReadyHouseholdResolutionState.None
                        ? "Ready_Household_None"
                        : "Ready_Household_Multiple";
                Response.StatusCode = StatusCodes.Status409Conflict;
                return Page();
            }

            var explanation = await _getExplanation.ExecuteAsync(
                new GetLedgerTransactionExplanationQuery(
                    householdResolution.Household!.HouseholdId,
                    parsedId),
                cancellationToken);

            if (explanation is null)
            {
                TransactionNotFound = true;
                Response.StatusCode = StatusCodes.Status404NotFound;
                return Page();
            }

            Transaction = Present(explanation);
        }
        catch (Exception exception)
            when (exception is NavigationRequestException
                  or HouseholdNotFoundException
                  or NavigationPersistenceException)
        {
            LoadUnavailable = true;
            Response.StatusCode = StatusCodes.Status409Conflict;
        }

        return Page();
    }

    private TransactionExplanationDisplay Present(
        LedgerTransactionExplanation explanation)
    {
        var transaction = explanation.Transaction;
        var context = explanation.CurrentContext;
        var entries = context.Entries.ToDictionary(item => item.EntryId);
        var lots = context.Lots.ToDictionary(item => item.AssetLotId);
        var currencies = context.Currencies.ToDictionary(
            item => item.Code,
            StringComparer.Ordinal);

        return new TransactionExplanationDisplay(
            transaction.TransactionId,
            context.Household.HouseholdId,
            context.Household.Name,
            _values.StableCode(transaction.Type),
            _values.StableCode(transaction.Status),
            OptionalDate(transaction.OrderDate),
            OptionalDate(transaction.ExecutionDate),
            OptionalDate(transaction.SettlementDate),
            transaction.ExternalReference,
            transaction.Note,
            transaction.ReversalOfTransactionId,
            transaction.ReversedByTransactionId,
            _values.UtcTimestamp(transaction.CreatedAtUtc),
            transaction.PostedAtUtc is null
                ? _values.Unknown()
                : _values.UtcTimestamp(transaction.PostedAtUtc.Value),
            transaction.Entries
                .OrderBy(item => item.EntrySequence)
                .ThenBy(item => item.EntryId)
                .Select(item => PresentEntry(item, entries[item.EntryId]))
                .ToArray(),
            PresentCashFlow(transaction.CashFlow, context.HouseholdMember),
            transaction.Costs
                .Select(item => PresentCost(item, currencies))
                .ToArray(),
            transaction.CreatedLots
                .Select(
                    item => PresentLot(
                        item,
                        lots[item.AssetLotId],
                        currencies))
                .ToArray(),
            transaction.LotAllocations
                .Select(
                    item => PresentAllocation(
                        item,
                        lots[item.AssetLotId]))
                .ToArray());
    }

    private TransactionEntryDisplay PresentEntry(
        Application.CoreLedger.LedgerTransactionEntryDetail item,
        LedgerTransactionEntryCurrentContext context)
        => new(
            item.EntryId,
            item.EntrySequence,
            context.Portfolio.PortfolioId,
            context.Portfolio.Code,
            context.Portfolio.Name,
            _values.StableCode(context.Portfolio.Status),
            context.Portfolio.Status == Domain.Portfolios.PortfolioStatus.Active,
            context.Account.AccountId,
            context.Account.Code,
            context.Account.Name,
            _values.StableCode(context.Account.Type),
            context.Account.IsActive,
            context.Account.Institution?.InstitutionId,
            context.Account.Institution?.Code,
            context.Account.Institution?.Name,
            context.Account.Institution is null
                ? null
                : _values.StableCode(context.Account.Institution.Type),
            context.Account.Institution?.IsActive,
            context.Asset.AssetId,
            context.Asset.Code,
            context.Asset.Name,
            _values.StableCode(context.Asset.Type),
            _values.StableCode(context.Asset.BaseUnit),
            context.Asset.IsActive,
            _values.Quantity(
                item.QuantityDeltaRawE8,
                context.Asset.BaseUnit,
                QuantitySign.SignedDelta),
            _values.StableCode(item.Role),
            item.UnitPriceRawE8 is null
                ? _values.NotApplicable()
                : _values.UnitPrice(
                    item.UnitPriceRawE8.Value,
                    item.PriceCurrencyCode),
            _values.UtcTimestamp(item.CreatedAtUtc));

    private CashFlowDisplay? PresentCashFlow(
        Application.CoreLedger.LedgerTransactionCashFlowDetail? item,
        HouseholdMemberNavigationItem? member)
        => item is null
            ? null
            : new CashFlowDisplay(
                _values.StableCode(item.Category),
                item.HouseholdMemberId,
                member?.DisplayName,
                member?.IsActive,
                member is null ? _values.Unknown() : null);

    private TransactionCostDisplay PresentCost(
        Application.CoreLedger.LedgerTransactionCostDetail item,
        IReadOnlyDictionary<string, CurrencyNavigationItem> currencies)
    {
        currencies.TryGetValue(item.CurrencyCode, out var currency);

        return new TransactionCostDisplay(
            item.CostId,
            _values.StableCode(item.Type),
            _values.StableCode(item.Treatment),
            _values.Money(
                item.AmountMinorUnits,
                item.CurrencyCode,
                currency?.MinorUnitDigits),
            item.Note);
    }

    private CreatedLotDisplay PresentLot(
        Application.CoreLedger.LedgerTransactionCreatedLotDetail item,
        LedgerTransactionLotCurrentContext context,
        IReadOnlyDictionary<string, CurrencyNavigationItem> currencies)
        => new(
            item.AssetLotId,
            item.OpeningTransactionEntryId,
            context.Asset.AssetId,
            context.Asset.Code,
            context.Asset.Name,
            _values.StableCode(context.Asset.Type),
            _values.StableCode(context.Asset.BaseUnit),
            context.Asset.IsActive,
            item.AcquiredOn is null
                ? _values.Unknown()
                : _values.BusinessDate(item.AcquiredOn.Value),
            _values.StableCode(item.CostBasisStatus),
            PresentCostBasis(item, currencies),
            _values.UtcTimestamp(item.CreatedAtUtc));

    private DisplayValue PresentCostBasis(
        Application.CoreLedger.LedgerTransactionCreatedLotDetail item,
        IReadOnlyDictionary<string, CurrencyNavigationItem> currencies)
    {
        if (item.CostBasisStatus == CostBasisStatus.Unknown)
        {
            return _values.Unknown();
        }

        if (item.CostBasisStatus == CostBasisStatus.NotApplicable)
        {
            return _values.NotApplicable();
        }

        if (item.OriginalCostBasisMinorUnits is not long amount
            || string.IsNullOrWhiteSpace(item.CostBasisCurrencyCode))
        {
            return _values.Unavailable(
                PresentationDiagnostics.IncompleteRecordedValue);
        }

        currencies.TryGetValue(
            item.CostBasisCurrencyCode,
            out var currency);

        return _values.Money(
            amount,
            item.CostBasisCurrencyCode,
            currency?.MinorUnitDigits);
    }

    private LotAllocationDisplay PresentAllocation(
        Application.CoreLedger.LedgerTransactionLotAllocationDetail item,
        LedgerTransactionLotCurrentContext context)
        => new(
            item.AllocationId,
            item.AssetLotId,
            item.TransactionEntryId,
            context.Asset.Code,
            context.Asset.Name,
            context.Asset.IsActive,
            _values.Quantity(
                item.QuantityDeltaRawE8,
                context.Asset.BaseUnit,
                QuantitySign.SignedDelta),
            _values.UtcTimestamp(item.CreatedAtUtc));

    private DisplayValue OptionalDate(DateOnly? value)
        => value is null
            ? _values.Unknown()
            : _values.BusinessDate(value.Value);
}

public sealed record TransactionExplanationDisplay(
    Guid TransactionId,
    Guid HouseholdId,
    string HouseholdName,
    DisplayValue Type,
    DisplayValue Status,
    DisplayValue OrderDate,
    DisplayValue ExecutionDate,
    DisplayValue SettlementDate,
    string? ExternalReference,
    string? Note,
    Guid? ReversalOfTransactionId,
    Guid? ReversedByTransactionId,
    DisplayValue CreatedAt,
    DisplayValue PostedAt,
    IReadOnlyList<TransactionEntryDisplay> Entries,
    CashFlowDisplay? CashFlow,
    IReadOnlyList<TransactionCostDisplay> Costs,
    IReadOnlyList<CreatedLotDisplay> CreatedLots,
    IReadOnlyList<LotAllocationDisplay> LotAllocations);

public sealed record TransactionEntryDisplay(
    Guid EntryId,
    int Sequence,
    Guid PortfolioId,
    string PortfolioCode,
    string PortfolioName,
    DisplayValue PortfolioStatus,
    bool PortfolioIsActive,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    DisplayValue AccountType,
    bool AccountIsActive,
    Guid? InstitutionId,
    string? InstitutionCode,
    string? InstitutionName,
    DisplayValue? InstitutionType,
    bool? InstitutionIsActive,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    DisplayValue AssetType,
    DisplayValue AssetUnit,
    bool AssetIsActive,
    DisplayValue Quantity,
    DisplayValue Role,
    DisplayValue UnitPrice,
    DisplayValue CreatedAt);

public sealed record CashFlowDisplay(
    DisplayValue Category,
    Guid? MemberId,
    string? MemberName,
    bool? MemberIsActive,
    DisplayValue? MissingMember);

public sealed record TransactionCostDisplay(
    Guid CostId,
    DisplayValue Type,
    DisplayValue Treatment,
    DisplayValue Amount,
    string? Note);

public sealed record CreatedLotDisplay(
    Guid AssetLotId,
    Guid OpeningTransactionEntryId,
    Guid AssetId,
    string AssetCode,
    string AssetName,
    DisplayValue AssetType,
    DisplayValue AssetUnit,
    bool AssetIsActive,
    DisplayValue AcquiredOn,
    DisplayValue CostBasisStatus,
    DisplayValue CostBasisAmount,
    DisplayValue CreatedAt);

public sealed record LotAllocationDisplay(
    Guid AllocationId,
    Guid AssetLotId,
    Guid TransactionEntryId,
    string AssetCode,
    string AssetName,
    bool AssetIsActive,
    DisplayValue Quantity,
    DisplayValue CreatedAt);
