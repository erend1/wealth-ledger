namespace WealthLedger.Application.CoreLedger
{
    public sealed class ReversalReasonValidationException
        : ArgumentException
    {
        public string ErrorCode { get; }

        private ReversalReasonValidationException(
            string errorCode,
            string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        internal static ReversalReasonValidationException Required()
        {
            return new ReversalReasonValidationException(
                "REVERSAL_REASON_REQUIRED",
                "A reversal reason is required.");
        }

        internal static ReversalReasonValidationException Invalid()
        {
            return new ReversalReasonValidationException(
                "REVERSAL_REASON_INVALID",
                "A reversal reason must contain at most 2,000 non-control characters.");
        }
    }
}
