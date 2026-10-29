using WealthLedger.Api.Contracts;
using WealthLedger.Application.LedgerSearch;
using WealthLedger.Domain.Ledger;

namespace WealthLedger.Api.Mapping;

internal static class LedgerSearchContractMapper
{
    internal static readonly IReadOnlyDictionary<string, TransactionType> TypeCodes =
        Enum.GetValues<TransactionType>()
            .ToDictionary(NavigationContractMapper.ToCode, StringComparer.Ordinal);

    internal static readonly IReadOnlyDictionary<string, TransactionStatus> StatusCodes =
        Enum.GetValues<TransactionStatus>()
            .ToDictionary(NavigationContractMapper.ToCode, StringComparer.Ordinal);

    internal static readonly IReadOnlyDictionary<string, LedgerSearchReversalRelationship>
        ReversalCodes = new Dictionary<string, LedgerSearchReversalRelationship>(
            StringComparer.Ordinal)
        {
            ["ANY"] = LedgerSearchReversalRelationship.Any,
            ["ORIGINAL_ONLY"] = LedgerSearchReversalRelationship.OriginalOnly,
            ["REVERSAL_ONLY"] = LedgerSearchReversalRelationship.ReversalOnly,
            ["REVERSED_ORIGINAL"] = LedgerSearchReversalRelationship.ReversedOriginal
        };

    internal static LedgerSearchTransactionResponse ToResponse(
        this LedgerSearchTransactionItem item)
        => new(
            item.TransactionId,
            item.HouseholdId,
            NavigationContractMapper.ToCode(item.Type),
            NavigationContractMapper.ToCode(item.Status),
            item.OrderDate,
            item.ExecutionDate,
            item.SettlementDate,
            item.ExternalReference,
            item.ReversalOfTransactionId,
            item.ReversedByTransactionId,
            item.CreatedAtUtc,
            item.PostedAtUtc,
            item.EntryEffects
                .Select(
                    effect => new RecentLedgerEntryEffectNavigationResponse(
                        effect.EntryId,
                        effect.EntrySequence,
                        effect.PortfolioId,
                        effect.PortfolioCode,
                        effect.PortfolioName,
                        NavigationContractMapper.ToCode(effect.PortfolioStatus),
                        effect.AccountId,
                        effect.AccountCode,
                        effect.AccountName,
                        NavigationContractMapper.ToCode(effect.AccountType),
                        effect.AccountIsActive,
                        effect.InstitutionId,
                        effect.InstitutionCode,
                        effect.InstitutionName,
                        effect.InstitutionType is null
                            ? null
                            : NavigationContractMapper.ToCode(effect.InstitutionType.Value),
                        effect.InstitutionIsActive,
                        effect.AssetId,
                        effect.AssetCode,
                        effect.AssetName,
                        NavigationContractMapper.ToCode(effect.AssetType),
                        NavigationContractMapper.ToCode(effect.AssetBaseUnit),
                        effect.AssetBaseCurrencyCode,
                        NavigationContractMapper.ToCode(effect.AssetLotTrackingMode),
                        effect.AssetIsActive,
                        effect.QuantityDeltaRawE8,
                        NavigationContractMapper.ToCode(effect.Role)))
                .ToArray());
}
