using WealthLedger.Application.CoreLedger;

namespace WealthLedger.Application.Navigation;

/// <summary>
/// Requests one transaction explanation in the selected household scope.
/// </summary>
public sealed record GetLedgerTransactionExplanationQuery(
    Guid HouseholdId,
    Guid TransactionId);

/// <summary>
/// Final M003 transaction facts accompanied by current M005 display context.
/// </summary>
/// <remarks>
/// Current labels are deliberately separate from the immutable transaction.
/// They are not source-time snapshots and never replace stored identities.
/// </remarks>
public sealed record LedgerTransactionExplanation(
    LedgerTransactionDetail Transaction,
    LedgerTransactionCurrentContext CurrentContext);

/// <summary>
/// The complete bounded key set needed to resolve current labels for one
/// transaction without per-entry or per-lot lookups.
/// </summary>
public sealed record LedgerTransactionCurrentContextQuery(
    Guid HouseholdId,
    IReadOnlyList<Guid> EntryIds,
    IReadOnlyList<Guid> AssetLotIds,
    Guid? HouseholdMemberId,
    IReadOnlyList<string> CurrencyCodes);

public sealed record LedgerTransactionCurrentContext(
    HouseholdNavigationItem Household,
    IReadOnlyList<LedgerTransactionEntryCurrentContext> Entries,
    IReadOnlyList<LedgerTransactionLotCurrentContext> Lots,
    HouseholdMemberNavigationItem? HouseholdMember,
    IReadOnlyList<CurrencyNavigationItem> Currencies);

public sealed record LedgerTransactionEntryCurrentContext(
    Guid EntryId,
    PortfolioNavigationItem Portfolio,
    AccountNavigationItem Account,
    AssetNavigationItem Asset);

public sealed record LedgerTransactionLotCurrentContext(
    Guid AssetLotId,
    AssetNavigationItem Asset);

/// <summary>
/// Resolves all current labels needed by one transaction in bounded batches.
/// </summary>
public interface ILedgerTransactionCurrentContextReadStore
{
    Task<LedgerTransactionCurrentContext?> ReadAsync(
        LedgerTransactionCurrentContextQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Composes immutable M003 detail with current M005 labels for a direct URL.
/// </summary>
public sealed class GetLedgerTransactionExplanationUseCase
{
    private readonly GetLedgerTransactionUseCase _getTransaction;
    private readonly ILedgerTransactionCurrentContextReadStore _contextStore;

    public GetLedgerTransactionExplanationUseCase(
        GetLedgerTransactionUseCase getTransaction,
        ILedgerTransactionCurrentContextReadStore contextStore)
    {
        _getTransaction = getTransaction
            ?? throw new ArgumentNullException(nameof(getTransaction));
        _contextStore = contextStore
            ?? throw new ArgumentNullException(nameof(contextStore));
    }

    public async Task<LedgerTransactionExplanation?> ExecuteAsync(
        GetLedgerTransactionExplanationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.HouseholdId == Guid.Empty)
        {
            throw new ArgumentException(
                "Household ID cannot be empty.",
                nameof(query));
        }

        if (query.TransactionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Transaction ID cannot be empty.",
                nameof(query));
        }

        var transaction = await _getTransaction.ExecuteAsync(
            query.TransactionId,
            cancellationToken);

        if (transaction is null
            || transaction.HouseholdId != query.HouseholdId)
        {
            return null;
        }

        var contextQuery = CreateContextQuery(transaction);
        var context = await _contextStore.ReadAsync(
            contextQuery,
            cancellationToken);

        EnsureCompleteContext(transaction, contextQuery, context);

        return new LedgerTransactionExplanation(
            transaction,
            context!);
    }

    private static LedgerTransactionCurrentContextQuery CreateContextQuery(
        LedgerTransactionDetail transaction)
    {
        var entryIds = transaction.Entries
            .Select(entry => entry.EntryId)
            .Distinct()
            .ToArray();
        var lotIds = transaction.LotAllocations
            .Select(allocation => allocation.AssetLotId)
            .Concat(
                transaction.CreatedLots.Select(lot => lot.AssetLotId))
            .Distinct()
            .ToArray();
        var currencyCodes = transaction.Entries
            .Select(entry => entry.PriceCurrencyCode)
            .Concat(transaction.Costs.Select(cost => cost.CurrencyCode))
            .Concat(
                transaction.CreatedLots.Select(
                    lot => lot.CostBasisCurrencyCode))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new LedgerTransactionCurrentContextQuery(
            transaction.HouseholdId,
            entryIds,
            lotIds,
            transaction.CashFlow?.HouseholdMemberId,
            currencyCodes);
    }

    private static void EnsureCompleteContext(
        LedgerTransactionDetail transaction,
        LedgerTransactionCurrentContextQuery query,
        LedgerTransactionCurrentContext? context)
    {
        if (context is null
            || context.Household.HouseholdId != transaction.HouseholdId)
        {
            throw IncompleteContext();
        }

        var expectedEntryIds = query.EntryIds.ToHashSet();
        var actualEntryIds = context.Entries
            .Select(entry => entry.EntryId)
            .ToHashSet();

        if (expectedEntryIds.Count != context.Entries.Count
            || !expectedEntryIds.SetEquals(actualEntryIds)
            || context.Entries.Any(
                entry => entry.Portfolio.HouseholdId
                         != transaction.HouseholdId
                         || entry.Account.HouseholdId
                         != transaction.HouseholdId))
        {
            throw IncompleteContext();
        }

        var currentEntries = context.Entries.ToDictionary(
            entry => entry.EntryId);

        if (transaction.Entries.Any(
                entry =>
                    currentEntries[entry.EntryId].Portfolio.PortfolioId
                        != entry.PortfolioId
                    || currentEntries[entry.EntryId].Account.AccountId
                        != entry.AccountId
                    || currentEntries[entry.EntryId].Asset.AssetId
                        != entry.AssetId))
        {
            throw IncompleteContext();
        }

        var expectedLotIds = query.AssetLotIds.ToHashSet();
        var actualLotIds = context.Lots
            .Select(lot => lot.AssetLotId)
            .ToHashSet();

        if (expectedLotIds.Count != context.Lots.Count
            || !expectedLotIds.SetEquals(actualLotIds))
        {
            throw IncompleteContext();
        }

        var currentLots = context.Lots.ToDictionary(
            lot => lot.AssetLotId);

        if (transaction.CreatedLots.Any(
                lot => currentLots[lot.AssetLotId].Asset.AssetId
                       != lot.AssetId))
        {
            throw IncompleteContext();
        }

        if (query.HouseholdMemberId is Guid memberId)
        {
            if (context.HouseholdMember?.HouseholdMemberId != memberId
                || context.HouseholdMember.HouseholdId
                != transaction.HouseholdId)
            {
                throw IncompleteContext();
            }
        }
        else if (context.HouseholdMember is not null)
        {
            throw IncompleteContext();
        }
    }

    private static NavigationPersistenceException IncompleteContext()
        => new(
            "The transaction current-display context could not be resolved completely.");
}
