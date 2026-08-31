using WealthLedger.Application.CoreLedger;

namespace WealthLedger.Application.Tests.CoreLedger
{
    public sealed class ReversalFingerprintTests
    {
        private static readonly Guid OriginalTransactionId =
            Guid.Parse(
                "60000000-0000-0000-0000-000000000001");

        [Fact]
        public void ComputeCurrent_KnownCommand_MatchesGoldenFingerprint()
        {
            var command =
                new ReversePostedTransactionCommand(
                    OriginalTransactionId,
                    "  Correcting persisted history.  ");

            var fingerprint =
                ReversePostedTransactionCommandFingerprint
                    .ComputeCurrent(command);

            Assert.Equal(
                "SHA256",
                fingerprint.AlgorithmCode);

            Assert.Equal(
                1,
                fingerprint.Version);

            Assert.Equal(
                "fe574f3cc2dbcfc12f3f3056588e920b5586865e291e21cf8a127ae4211213dc",
                fingerprint.Value);
        }

        [Fact]
        public void SurroundingReasonWhitespace_ProducesSameFingerprint()
        {
            var first =
                ReversePostedTransactionCommandFingerprint
                    .ComputeCurrent(
                        new ReversePostedTransactionCommand(
                            OriginalTransactionId,
                            "Incorrect quantity."));

            var second =
                ReversePostedTransactionCommandFingerprint
                    .ComputeCurrent(
                        new ReversePostedTransactionCommand(
                            OriginalTransactionId,
                            "   Incorrect quantity.   "));

            Assert.Equal(
                first,
                second);
        }

        [Fact]
        public void DifferentReason_ProducesDifferentFingerprint()
        {
            var first =
                ReversePostedTransactionCommandFingerprint
                    .ComputeCurrent(
                        new ReversePostedTransactionCommand(
                            OriginalTransactionId,
                            "Incorrect quantity."));

            var second =
                ReversePostedTransactionCommandFingerprint
                    .ComputeCurrent(
                        new ReversePostedTransactionCommand(
                            OriginalTransactionId,
                            "Incorrect price."));

            Assert.NotEqual(
                first,
                second);
        }

        [Fact]
        public void DifferentOriginalTransaction_ProducesDifferentFingerprint()
        {
            var first =
                ReversePostedTransactionCommandFingerprint
                    .ComputeCurrent(
                        new ReversePostedTransactionCommand(
                            OriginalTransactionId,
                            "Incorrect quantity."));

            var second =
                ReversePostedTransactionCommandFingerprint
                    .ComputeCurrent(
                        new ReversePostedTransactionCommand(
                            Guid.Parse(
                                "60000000-0000-0000-0000-000000000002"),
                            "Incorrect quantity."));

            Assert.NotEqual(
                first,
                second);
        }

        [Fact]
        public void Compute_UnsupportedStoredFingerprintVersion_Throws()
        {
            var command =
                new ReversePostedTransactionCommand(
                    OriginalTransactionId,
                    "Incorrect quantity.");

            Assert.Throws<NotSupportedException>(
                () =>
                    ReversePostedTransactionCommandFingerprint
                        .Compute(
                            command,
                            "SHA256",
                            999));
        }

        [Fact]
        public void Normalize_BlankReason_ThrowsRequired()
        {
            var exception =
                Assert.Throws<ReversalReasonValidationException>(
                    () =>
                        ReversePostedTransactionCommandCanonicalizer
                            .Normalize(
                                new ReversePostedTransactionCommand(
                                    OriginalTransactionId,
                                    "   ")));

            Assert.Equal(
                "REVERSAL_REASON_REQUIRED",
                exception.ErrorCode);
        }

        [Fact]
        public void Normalize_ControlCharacter_ThrowsInvalid()
        {
            var exception =
                Assert.Throws<ReversalReasonValidationException>(
                    () =>
                        ReversePostedTransactionCommandCanonicalizer
                            .Normalize(
                                new ReversePostedTransactionCommand(
                                    OriginalTransactionId,
                                    "Incorrect\nquantity.")));

            Assert.Equal(
                "REVERSAL_REASON_INVALID",
                exception.ErrorCode);
        }

        [Fact]
        public void Normalize_OverTwoThousandCharacters_ThrowsInvalid()
        {
            var exception =
                Assert.Throws<ReversalReasonValidationException>(
                    () =>
                        ReversePostedTransactionCommandCanonicalizer
                            .Normalize(
                                new ReversePostedTransactionCommand(
                                    OriginalTransactionId,
                                    new string('x', 2_001))));

            Assert.Equal(
                "REVERSAL_REASON_INVALID",
                exception.ErrorCode);
        }

        [Fact]
        public void Normalize_ExactlyTwoThousandCharacters_Succeeds()
        {
            var reason =
                new string('x', 2_000);

            var normalized =
                ReversePostedTransactionCommandCanonicalizer
                    .Normalize(
                        new ReversePostedTransactionCommand(
                            OriginalTransactionId,
                            reason));

            Assert.Equal(
                reason,
                normalized.Reason);
        }

        [Fact]
        public void Normalize_TrimsReason()
        {
            var normalized =
                ReversePostedTransactionCommandCanonicalizer
                    .Normalize(
                        new ReversePostedTransactionCommand(
                            OriginalTransactionId,
                            "   Incorrect quantity.   "));

            Assert.Equal(
                "Incorrect quantity.",
                normalized.Reason);
        }

        [Fact]
        public void Normalize_EmptyOriginalTransactionId_Throws()
        {
            Assert.Throws<ArgumentException>(
                () =>
                    ReversePostedTransactionCommandCanonicalizer
                        .Normalize(
                            new ReversePostedTransactionCommand(
                                Guid.Empty,
                                "Incorrect quantity.")));
        }
    }
}