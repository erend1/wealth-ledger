using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.ValueObjects
{
    public sealed class CurrencyCodeTests
    {
        [Fact]
        public void Constructor_NormalizesCurrencyCode()
        {
            var currency = new CurrencyCode("try");

            Assert.Equal("TRY", currency.Value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("TR")]
        [InlineData("TRYX")]
        [InlineData("T1Y")]
        public void Constructor_RejectsInvalidCurrencyCode(
            string value)
        {
            Assert.ThrowsAny<ArgumentException>(
                () => new CurrencyCode(value));
        }
    }
}
