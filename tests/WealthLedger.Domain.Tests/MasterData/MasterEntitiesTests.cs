using WealthLedger.Domain.Common;
using WealthLedger.Domain.Households;
using WealthLedger.Domain.Portfolios;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.MasterData
{
    public sealed class MasterEntitiesTests
    {
        private static readonly DateTimeOffset Now =
            new(
                2026,
                8,
                19,
                12,
                0,
                0,
                TimeSpan.Zero);

        [Fact]
        public void Household_NormalizesName()
        {
            var household =
                Household.Create(
                    Guid.NewGuid(),
                    "  Demirtas Household  ",
                    CurrencyCode.TRY,
                    Now);

            Assert.Equal(
                "Demirtas Household",
                household.Name);
        }

        [Fact]
        public void Portfolio_NormalizesCode()
        {
            var portfolio =
                Portfolio.Create(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    " home ",
                    "Home Goal",
                    Now);

            Assert.Equal(
                "HOME",
                portfolio.Code);
        }

        [Fact]
        public void Portfolio_CanBeClosedAndArchived()
        {
            var portfolio =
                Portfolio.Create(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "HOME",
                    "Home Goal",
                    Now);

            portfolio.Close(
                Now.AddYears(3));

            Assert.Equal(
                PortfolioStatus.Closed,
                portfolio.Status);

            portfolio.Archive();

            Assert.Equal(
                PortfolioStatus.Archived,
                portfolio.Status);
        }

        [Fact]
        public void Portfolio_CannotCloseBeforeCreation()
        {
            var portfolio =
                Portfolio.Create(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "HOME",
                    "Home Goal",
                    Now);

            Assert.Throws<DomainRuleViolationException>(
                () => portfolio.Close(
                    Now.AddDays(-1)));
        }

        [Fact]
        public void Account_CanBeClosed()
        {
            var account =
                Account.Create(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "ISBANK_INVESTMENT",
                    "Is Bank Investment",
                    AccountType.Investment,
                    new DateOnly(2026, 8, 1));

            account.Close(
                new DateOnly(2030, 1, 1));

            Assert.False(
                account.IsActive);

            Assert.Equal(
                new DateOnly(2030, 1, 1),
                account.ClosedOn);
        }

        [Fact]
        public void Account_CannotCloseBeforeOpening()
        {
            var account =
                Account.Create(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "ISBANK_INVESTMENT",
                    "Is Bank Investment",
                    AccountType.Investment,
                    new DateOnly(2026, 8, 1));

            Assert.Throws<DomainRuleViolationException>(
                () => account.Close(
                    new DateOnly(2026, 7, 31)));
        }

        [Fact]
        public void Institution_NormalizesCode()
        {
            var institution =
                Institution.Create(
                    Guid.NewGuid(),
                    " isbank ",
                    "Is Bankasi",
                    InstitutionType.Bank);

            Assert.Equal(
                "ISBANK",
                institution.Code);
        }

        [Fact]
        public void HouseholdMember_CanBeDeactivated()
        {
            var member =
                HouseholdMember.Create(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "Huseyin",
                    Now);

            member.Deactivate();

            Assert.False(
                member.IsActive);
        }
    }
}
