namespace WealthLedger.Api.Contracts
{
    public sealed record ReversePostedTransactionRequest(
        string Reason);

    public sealed record ReversePostedTransactionResponse(
        Guid ReversalTransactionId,
        Guid ReversalOfTransactionId);

    public sealed record ReversalPreviewResponse(
        Guid OriginalTransactionId,
        bool CanReverse,
        string EligibilityCode,
        Guid? ExistingReversalTransactionId,
        IReadOnlyList<Guid> BlockingTransactionIds,
        IReadOnlyList<ReversalPreviewEntryResponse> InverseEntries,
        IReadOnlyList<ReversalPreviewLotAllocationResponse>
            InverseLotAllocations);

    public sealed record ReversalPreviewEntryResponse(
        int Sequence,
        Guid PortfolioId,
        Guid AccountId,
        Guid AssetId,
        long QuantityDeltaRawE8,
        string EntryRoleCode,
        long? UnitPriceRawE8,
        string? PriceCurrencyCode);

    public sealed record ReversalPreviewLotAllocationResponse(
        Guid AssetLotId,
        Guid OriginalTransactionEntryId,
        int EntrySequence,
        long QuantityDeltaRawE8);
}
