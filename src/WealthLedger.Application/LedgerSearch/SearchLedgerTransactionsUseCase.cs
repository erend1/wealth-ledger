namespace WealthLedger.Application.LedgerSearch;

public sealed class SearchLedgerTransactionsUseCase
{
    private readonly ILedgerSearchReadStore _readStore;

    public SearchLedgerTransactionsUseCase(ILedgerSearchReadStore readStore)
    {
        _readStore = readStore
            ?? throw new ArgumentNullException(nameof(readStore));
    }

    public async Task<LedgerSearchPage> ExecuteAsync(
        SearchLedgerTransactionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.PageSize is < 1 or > 100)
        {
            throw new LedgerSearchRequestException(
                LedgerSearchRequestException.PageSizeInvalidCode,
                "Page size must be an integer between 1 and 100.");
        }

        var filters = LedgerSearchFilterNormalizer.Normalize(query);
        var after = LedgerSearchCursorCodec.Decode(
            query.Cursor,
            query.HouseholdId,
            filters.Fingerprint);

        if (!await _readStore.HouseholdExistsAsync(
                query.HouseholdId,
                cancellationToken))
        {
            throw new LedgerSearchHouseholdNotFoundException();
        }

        if (!await _readStore.FilterScopeExistsAsync(
                query.HouseholdId,
                filters.PortfolioId,
                filters.AccountId,
                filters.AssetId,
                filters.InstitutionId,
                cancellationToken))
        {
            throw new LedgerSearchFilterScopeNotFoundException();
        }

        var rows = await _readStore.SearchPostedTransactionsAsync(
            query.HouseholdId,
            filters,
            checked(query.PageSize + 1),
            after,
            cancellationToken);

        if (rows.Count <= query.PageSize)
        {
            return new LedgerSearchPage(rows, NextCursor: null);
        }

        var page = rows.Take(query.PageSize).ToArray();
        var last = page[^1];
        var nextCursor = LedgerSearchCursorCodec.Encode(
            query.HouseholdId,
            filters.Fingerprint,
            new LedgerSearchCursorKey(
                last.ExecutionDate,
                last.PostedAtUtc,
                last.TransactionId));

        return new LedgerSearchPage(page, nextCursor);
    }
}
