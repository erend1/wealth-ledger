using WealthLedger.Domain.Assets;
using WealthLedger.Domain.ValueObjects;

namespace WealthLedger.Domain.Tests.Assets
{
    public sealed class AssetTests
    {
        [Fact]
        public void Create_NormalizesCodeAndName()
        {
            var asset = Asset.Create(
                Guid.NewGuid(),
                " gold_bracelet_22k ",
                "  22K Synthetic Bracelet  ",
                AssetType.PhysicalGold,
                AssetUnit.GrossGram,
                CurrencyCode.TRY,
                LotTrackingMode.Required);

            Assert.Equal(
                "GOLD_BRACELET_22K",
                asset.Code);

            Assert.Equal(
                "22K Synthetic Bracelet",
                asset.Name);
        }

        [Theory]
        [InlineData("AIS!")]
        [InlineData("AIS TRY")]
        [InlineData("A/B")]
        public void Create_RejectsInvalidAssetCode(
            string code)
        {
            Assert.Throws<ArgumentException>(() =>
                Asset.Create(
                    Guid.NewGuid(),
                    code,
                    "Asset",
                    AssetType.Fund,
                    AssetUnit.FundUnit,
                    CurrencyCode.TRY,
                    LotTrackingMode.Required));
        }

        [Fact]
        public void Deactivate_MarksAssetInactive()
        {
            var asset = Asset.Create(
                Guid.NewGuid(),
                "AIS",
                "AIS",
                AssetType.Fund,
                AssetUnit.FundUnit,
                CurrencyCode.TRY,
                LotTrackingMode.Required);

            asset.Deactivate();

            Assert.False(asset.IsActive);
        }
    }
}
