using WealthLedger.Domain.Ledger;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.Ledger
{
    public sealed class TransactionEntryTests
    {
        [Fact]
        public void Constructor_AcceptsPositiveQuantityDelta()
        {
            var entry = new TransactionEntry(
                Guid.NewGuid(),
                0,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                QuantityDelta.FromDecimal(100m),
                EntryRole.Principal,
                UnitPrice.FromDecimal(
                    4.5m,
                    CurrencyCode.TRY));

            Assert.Equal(
                100m,
                entry.QuantityDelta.ToDecimal());
        }

        [Fact]
        public void Constructor_AcceptsNegativeQuantityDelta()
        {
            var entry = new TransactionEntry(
                Guid.NewGuid(),
                0,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                QuantityDelta.FromDecimal(-100m),
                EntryRole.Principal,
                null);

            Assert.True(
                entry.QuantityDelta.IsNegative);
        }

        [Fact]
        public void Constructor_RejectsZeroQuantityDelta()
        {
            Assert.Throws<ArgumentException>(() =>
                new TransactionEntry(
                    Guid.NewGuid(),
                    0,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    QuantityDelta.Zero,
                    EntryRole.Principal,
                    null));
        }

        [Fact]
        public void Constructor_RejectsEmptyAssetId()
        {
            Assert.Throws<ArgumentException>(() =>
                new TransactionEntry(
                    Guid.NewGuid(),
                    0,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.Empty,
                    QuantityDelta.FromDecimal(100m),
                    EntryRole.Principal,
                    null));
        }
    }
}
