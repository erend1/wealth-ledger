using WealthLedger.Application.CoreLedger;
using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Application.Tests.CoreLedger
{
    public sealed class ContributionFingerprintTests
    {

        private static readonly Guid HouseholdId =
            Guid.Parse(
                "10000000-0000-0000-0000-000000000001");

        private static readonly Guid PortfolioId =
            Guid.Parse(
                "20000000-0000-0000-0000-000000000001");

        private static readonly Guid AccountId =
            Guid.Parse(
                "30000000-0000-0000-0000-000000000001");

        private static readonly Guid CashAssetId =
            Guid.Parse(
                "40000000-0000-0000-0000-000000000001");

        private static readonly Guid MemberId =
            Guid.Parse(
                "50000000-0000-0000-0000-000000000001");

        private static RecordContributionCommand CreateCommand(
            long amountMinorUnits = 12_345,
            CurrencyCode? currency = null,
            CashFlowCategory category = CashFlowCategory.Other,
            DateOnly? executionDate = null,
            Guid? householdMemberId = null,
            string? externalReference = "REF-123",
            string? note = "Salary")
        {
            return new RecordContributionCommand(
                HouseholdId,
                PortfolioId,
                AccountId,
                CashAssetId,
                Money.FromMinorUnits(
                    amountMinorUnits,
                    currency ?? CurrencyCode.TRY),
                category,
                executionDate
                    ?? new DateOnly(2026, 8, 24),
                householdMemberId,
                externalReference,
                note);
        }

        [Fact]
        public void ComputeCurrent_SameCommand_ReturnsSameFingerprint()
        {
            var command = CreateCommand();

            var first =
                RecordContributionCommandFingerprint.ComputeCurrent(command);

            var second =
                RecordContributionCommandFingerprint.ComputeCurrent(command);

            Assert.Equal(first, second);
        }

        [Fact]
        public void ComputeCurrent_KnownCommand_MatchesGoldenFingerprint()
        {
            var command =
                new RecordContributionCommand(
                    HouseholdId,
                    PortfolioId,
                    AccountId,
                    CashAssetId,
                    Money.FromMinorUnits(
                        12_345,
                        CurrencyCode.TRY),
                    CashFlowCategory.Other,
                    new DateOnly(2026, 8, 24),
                    MemberId,
                    " salary-aug ",
                    " August salary ");

            var fingerprint =
                RecordContributionCommandFingerprint
                    .ComputeCurrent(command);

            Assert.Equal(
                "SHA256",
                fingerprint.AlgorithmCode);

            Assert.Equal(
                1,
                fingerprint.Version);

            Assert.Equal(
                "42e2bba7d77b0616799b581f904658584357b9cf417eba6bfb25edf7eec1cc81",
                fingerprint.Value);
        }

        [Fact]
        public void ComputeCurrent_TrimsOptionalSemanticText()
        {
            var first =
                CreateCommand(
                    externalReference: " REF-123 ",
                    note: " Salary ");

            var second =
                CreateCommand(
                    externalReference: "REF-123",
                    note: "Salary");

            Assert.Equal(
                RecordContributionCommandFingerprint
                    .ComputeCurrent(first),
                RecordContributionCommandFingerprint
                    .ComputeCurrent(second));
        }

        [Fact]
        public void ComputeCurrent_WhitespaceOptionalText_EqualsNull()
        {
            var first =
                CreateCommand(
                    externalReference: null,
                    note: null);

            var second =
                CreateCommand(
                    externalReference: "   ",
                    note: "\t");

            Assert.Equal(
                RecordContributionCommandFingerprint
                    .ComputeCurrent(first),
                RecordContributionCommandFingerprint
                    .ComputeCurrent(second));
        }

        [Fact]
        public void ComputeCurrent_ChangedAccount_ChangesFingerprint()
        {
            var first =
                CreateCommand();

            var second =
                first with
                {
                    AccountId = Guid.Parse(
                        "30000000-0000-0000-0000-000000000002")
                };

            Assert.NotEqual(
                RecordContributionCommandFingerprint
                    .ComputeCurrent(first),
                RecordContributionCommandFingerprint
                    .ComputeCurrent(second));
        }
    }
}
