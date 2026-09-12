using System;
using System.Collections.Generic;
using System.Text;
using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.CoreLedger
{
    public sealed class FundPurchaseFingerprintTests
    {
        [Fact]
        public void ComputeCurrent_KnownCommand_MatchesGoldenFingerprint()
        {
            var command =
                CreateCommand(
                    externalReference:
                        " fund-purchase-aug ",
                    note:
                        " August fund purchase ");

            var fingerprint =
                RecordFundPurchaseCommandFingerprint
                    .ComputeCurrent(command);

            Assert.Equal(
                "SHA256",
                fingerprint.AlgorithmCode);

            Assert.Equal(
                1,
                fingerprint.Version);

            Assert.Equal(
                "31d0a2f1c449387a3aeec86b0848acbff2051213ccbab15a545fad394d0aaeb0",
                fingerprint.Value);
        }

        /*
         * Pins version 1 through the explicit version path rather than
         * through ComputeCurrent.
         *
         * ComputeCurrent follows the newest version. This test must keep
         * passing unchanged after the fund-purchase fingerprint advances to
         * version 2, because an existing version-1 receipt is replayed by
         * recomputing version 1 from the same command. If version 1 ever
         * hashed the moving CurrentVersion constant, every legitimate legacy
         * retry would turn into an idempotency conflict instead.
         */
        [Fact]
        public void ComputeV1_KnownCommand_IsIndependentOfCurrentVersion()
        {
            var command =
                CreateCommand(
                    externalReference:
                        " fund-purchase-aug ",
                    note:
                        " August fund purchase ");

            var fingerprint =
                RecordFundPurchaseCommandFingerprint
                    .Compute(
                        command,
                        "SHA256",
                        version: 1);

            Assert.Equal(
                "SHA256",
                fingerprint.AlgorithmCode);

            Assert.Equal(
                1,
                fingerprint.Version);

            Assert.Equal(
                "31d0a2f1c449387a3aeec86b0848acbff2051213ccbab15a545fad394d0aaeb0",
                fingerprint.Value);
        }

        [Fact]
        public void ComputeCurrent_WhitespaceText_EqualsNormalizedText()
        {
            var first =
                CreateCommand(
                    " REF-123 ",
                    " Purchase ");

            var second =
                CreateCommand(
                    "REF-123",
                    "Purchase");

            Assert.Equal(
                RecordFundPurchaseCommandFingerprint
                    .ComputeCurrent(first),

                RecordFundPurchaseCommandFingerprint
                    .ComputeCurrent(second));
        }

        [Fact]
        public void ComputeCurrent_WhitespaceText_EqualsNull()
        {
            var first =
                CreateCommand(
                    null,
                    null);

            var second =
                CreateCommand(
                    "   ",
                    "\t");

            Assert.Equal(
                RecordFundPurchaseCommandFingerprint
                    .ComputeCurrent(first),

                RecordFundPurchaseCommandFingerprint
                    .ComputeCurrent(second));
        }

        [Fact]
        public void ComputeCurrent_ChangedSemanticFields_ChangeFingerprint()
        {
            var original =
                CreateCommand();

            var variants =
                new[]
                {
            original with
            {
                FundQuantity =
                    Quantity.FromRaw(
                        original.FundQuantity.RawE8
                        + 1)
            },

            original with
            {
                ExecutedUnitPrice =
                    UnitPrice.FromRaw(
                        original.ExecutedUnitPrice.RawE8
                        + 1,
                        CurrencyCode.TRY)
            },

            original with
            {
                CashConsideration =
                    Money.FromMinorUnits(
                        original.CashConsideration
                            .MinorUnits + 1,
                        CurrencyCode.TRY)
            },

            original with
            {
                ExecutionDate =
                    new DateOnly(
                        2026,
                        8,
                        25)
            },

            original with
            {
                ExternalReference =
                    "DIFFERENT"
            },

            original with
            {
                Note =
                    "Different note"
            }
                };

            var expected =
                RecordFundPurchaseCommandFingerprint
                    .ComputeCurrent(original);

            foreach (var variant in variants)
            {
                Assert.NotEqual(
                    expected,
                    RecordFundPurchaseCommandFingerprint
                        .ComputeCurrent(variant));
            }
        }

        private static RecordFundPurchaseCommand CreateCommand(
            string? externalReference = "fund-purchase-aug",
            string? note = "August fund purchase")
        {
            return new RecordFundPurchaseCommand(
                Guid.Parse(
                    "10000000-0000-0000-0000-000000000001"),

                Guid.Parse(
                    "20000000-0000-0000-0000-000000000001"),

                Guid.Parse(
                    "30000000-0000-0000-0000-000000000001"),

                Guid.Parse(
                    "40000000-0000-0000-0000-000000000002"),

                Guid.Parse(
                    "40000000-0000-0000-0000-000000000001"),

                Quantity.FromDecimal(
                    6_412.34918m),

                UnitPrice.FromDecimal(
                    4.678473m,
                    CurrencyCode.TRY),

                Money.FromMinorUnits(
                    3_000_000,
                    CurrencyCode.TRY),

                new DateOnly(
                    2026,
                    8,
                    24),

                externalReference,
                note);
        }
    }
}
