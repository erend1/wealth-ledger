using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.ValueObjects
{
    public sealed class UnitPriceTests
    {
        [Fact]
        public void UnitPrice_PreservesFundPrice()
        {
            var price = UnitPrice.FromDecimal(
                4.678473m,
                CurrencyCode.TRY);

            Assert.Equal(
                4.678473m,
                price.ToDecimal());

            Assert.Equal(
                CurrencyCode.TRY,
                price.Currency);
        }

        [Fact]
        public void UnitPrice_CannotBeNegative()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => UnitPrice.FromDecimal(
                    -1m,
                    CurrencyCode.TRY));
        }
    }
}
