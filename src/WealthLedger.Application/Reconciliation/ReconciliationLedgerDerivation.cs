using WealthLedger.Application.Inventory;

namespace WealthLedger.Application.Reconciliation;

internal readonly record struct ReconciliationScope(
    Guid PortfolioId,
    Guid AccountId,
    Guid AssetId);

/// <summary>
/// The ledger side of one exact scope on one effective date. Pieces are
/// present only when they were requested.
/// </summary>
internal readonly record struct ReconciliationLedgerSide(
    long QuantityRawE8,
    int? PieceCount);

/// <summary>
/// Derives the ledger side of reconciliation scopes from effective Posted
/// history as of one date, through the M010 inventory derivation.
/// </summary>
/// <remarks>
/// Gross quantity is the as-of position of the exact scope; a valid scope
/// without effective entries on that date is exactly zero. Physical-gold
/// pieces are the scope's as-of lot custody, from signed allocation-level
/// piece movement. An as-of slice of backdated history may be negative; it is
/// reported as it is, so it surfaces as a visible difference.
/// </remarks>
internal sealed class ReconciliationLedgerDerivation
{
    private readonly ListPositionInventoryUseCase _positions;
    private readonly ListLotInventoryUseCase _lots;

    internal ReconciliationLedgerDerivation(IInventoryReadStore inventoryReadStore)
    {
        ArgumentNullException.ThrowIfNull(inventoryReadStore);

        _positions = new ListPositionInventoryUseCase(inventoryReadStore);
        _lots = new ListLotInventoryUseCase(inventoryReadStore);
    }

    internal async Task<IReadOnlyDictionary<ReconciliationScope, ReconciliationLedgerSide>>
        DeriveAsync(
            Guid householdId,
            DateOnly asOf,
            IReadOnlyCollection<(ReconciliationScope Scope, bool WithPieces)> requests,
            CancellationToken cancellationToken)
    {
        var scopes = requests.Select(request => request.Scope).Distinct().ToArray();

        // A statement usually covers one portfolio, account or asset, so a
        // shared dimension narrows the derivation to the scopes it can touch.
        var positions = await _positions.DeriveAsync(
            new ListPositionInventoryQuery(
                householdId,
                PortfolioId: SharedOrNull(scopes, scope => scope.PortfolioId),
                AccountId: SharedOrNull(scopes, scope => scope.AccountId),
                AssetId: SharedOrNull(scopes, scope => scope.AssetId),
                AsOf: asOf,
                IncludeZero: true),
            cancellationToken);
        var quantities = positions.Items.ToDictionary(
            item => new ReconciliationScope(
                item.PortfolioId,
                item.AccountId,
                item.AssetId),
            item => item.QuantityRawE8);

        var pieceScopes = requests
            .Where(request => request.WithPieces)
            .Select(request => request.Scope)
            .ToHashSet();
        var pieces = new Dictionary<ReconciliationScope, int>();

        if (pieceScopes.Count > 0)
        {
            var pieceAssetIds = pieceScopes
                .Select(scope => scope.AssetId)
                .Distinct()
                .ToArray();
            // Unpaged: every piece in custody counts, however many lots.
            var lots = await _lots.DeriveAllAsync(
                new ListLotInventoryQuery(
                    householdId,
                    AssetId: pieceAssetIds.Length == 1 ? pieceAssetIds[0] : null,
                    AsOf: asOf,
                    IncludeExhausted: true),
                cancellationToken);

            foreach (var lot in lots.Items.Where(lot => pieceAssetIds.Contains(lot.AssetId)))
            {
                foreach (var custody in lot.Custody)
                {
                    var scope = new ReconciliationScope(
                        custody.PortfolioId,
                        custody.AccountId,
                        lot.AssetId);
                    var pieceCount = custody.PieceCount
                        ?? throw new ReconciliationPersistenceException(
                            "Physical-gold custody is missing its piece count.");

                    pieces[scope] = checked(pieces.GetValueOrDefault(scope) + pieceCount);
                }
            }
        }

        return scopes.ToDictionary(
            scope => scope,
            scope => new ReconciliationLedgerSide(
                quantities.GetValueOrDefault(scope),
                pieceScopes.Contains(scope)
                    ? pieces.GetValueOrDefault(scope)
                    : null));
    }

    private static Guid? SharedOrNull(
        IReadOnlyCollection<ReconciliationScope> scopes,
        Func<ReconciliationScope, Guid> dimension)
    {
        var values = scopes.Select(dimension).Distinct().Take(2).ToArray();

        return values.Length == 1 ? values[0] : null;
    }
}
