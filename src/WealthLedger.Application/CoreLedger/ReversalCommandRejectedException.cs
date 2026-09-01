namespace WealthLedger.Application.CoreLedger
{
    public sealed class ReversalCommandRejectedException
        : Exception
    {
        public ReversalEligibilityCode EligibilityCode
        {
            get;
        }

        public Guid? ExistingReversalTransactionId
        {
            get;
        }

        public IReadOnlyList<Guid> BlockingTransactionIds
        {
            get;
        }

        internal ReversalCommandRejectedException(
            ReversalEvaluation evaluation)
            : base(CreateMessage(evaluation.Code))
        {
            if (evaluation.CanReverse)
            {
                throw new ArgumentException(
                    "An eligible reversal cannot be represented as a rejection.",
                    nameof(evaluation));
            }

            EligibilityCode = evaluation.Code;
            ExistingReversalTransactionId =
                evaluation.ExistingReversalTransactionId;

            BlockingTransactionIds =
                evaluation.BlockingTransactionIds;
        }

        private static string CreateMessage(
            ReversalEligibilityCode code)
        {
            return code switch
            {
                ReversalEligibilityCode.NotPosted =>
                    "Only a posted transaction may be reversed.",

                ReversalEligibilityCode.TargetIsReversal =>
                    "A reversal transaction cannot itself be reversed directly.",

                ReversalEligibilityCode.AlreadyReversed =>
                    "The transaction has already been reversed.",

                ReversalEligibilityCode.BlockedByDependencies =>
                    "Outstanding downstream lot activity blocks this reversal.",

                ReversalEligibilityCode
                    .UnsupportedPersistedShape =>
                    "The persisted transaction cannot be safely reversed under the current Domain rules.",

                _ =>
                    "The reversal command was rejected."
            };
        }
    }
}
