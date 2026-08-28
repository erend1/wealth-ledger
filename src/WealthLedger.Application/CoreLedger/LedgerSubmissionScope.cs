namespace WealthLedger.Application.CoreLedger
{
    public sealed record LedgerSubmissionScope(
        Guid HouseholdId,
        string OperationCode,
        string IdempotencyKey);
}
