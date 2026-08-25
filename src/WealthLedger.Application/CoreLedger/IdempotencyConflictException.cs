namespace WealthLedger.Application.CoreLedger
{
    public sealed class IdempotencyConflictException
        : Exception
    {
        public const string ErrorCode =
            "IDEMPOTENCY_KEY_CONFLICT";

        public IdempotencyConflictException()
            : base(
                "The idempotency key has already been used for a different command.")
        {
        }
    }
}
