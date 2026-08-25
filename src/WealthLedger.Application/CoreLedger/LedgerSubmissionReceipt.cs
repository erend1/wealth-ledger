namespace WealthLedger.Application.CoreLedger
{
    public sealed record LedgerSubmissionReceipt(
        LedgerSubmissionScope Scope,
        CommandFingerprint Fingerprint,
        Guid TransactionId,
        Guid? AssetLotId,
        DateTimeOffset CreatedAtUtc);
}
