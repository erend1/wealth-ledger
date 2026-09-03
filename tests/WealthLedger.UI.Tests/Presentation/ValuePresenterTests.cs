using System.Globalization;
using WealthLedger.Domain.Assets;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Tests.Presentation;

/// <summary>
/// Formatting is the last place a correct stored value can become a wrong
/// visible one. These tests fix the exact text for ordinary values, for both
/// extremes of the 64-bit storage range, and for every way a value can fail to
/// render.
/// </summary>
public sealed class ValuePresenterTests
{
    private static readonly ValuePresenter Presenter =
        new(PresentationCulture.CreateDefault());

    [Theory]
    [InlineData(123_456L, 2, "1.234,56 TRY")]
    [InlineData(-123_456L, 2, "-1.234,56 TRY")]
    [InlineData(0L, 2, "0,00 TRY")]
    [InlineData(5L, 2, "0,05 TRY")]
    [InlineData(1_000_000_00L, 2, "1.000.000,00 TRY")]
    [InlineData(1_234L, 0, "1.234 TRY")]
    [InlineData(1L, 8, "0,00000001 TRY")]
    public void Money_RendersExactRecordedScale(
        long amountMinorUnits,
        int minorUnitDigits,
        string expected)
    {
        var rendered = Presenter.Money(
            amountMinorUnits,
            "TRY",
            minorUnitDigits);

        Assert.Equal(expected, rendered.Text);
        Assert.Equal(
            amountMinorUnits == 0 ? DisplayState.Zero : DisplayState.Known,
            rendered.State);
    }

    [Fact]
    public void Money_KeepsTrailingZerosBecauseScaleIsRecorded()
    {
        Assert.Equal("2,50 TRY", Presenter.Money(250, "TRY", 2).Text);
        Assert.Equal("2,00 TRY", Presenter.Money(200, "TRY", 2).Text);
    }

    [Fact]
    public void Money_HandlesBothEndsOfTheStorageRange()
    {
        // long.MinValue has no positive counterpart. Negating it would
        // overflow, so the magnitude is carried unsigned instead.
        Assert.Equal(
            "-92.233.720.368.547.758,08 TRY",
            Presenter.Money(long.MinValue, "TRY", 2).Text);
        Assert.Equal(
            "92.233.720.368.547.758,07 TRY",
            Presenter.Money(long.MaxValue, "TRY", 2).Text);
    }

    [Theory]
    [InlineData(null, PresentationDiagnostics.CurrencyMetadataMissing)]
    [InlineData("", PresentationDiagnostics.CurrencyMetadataMissing)]
    [InlineData("   ", PresentationDiagnostics.CurrencyMetadataMissing)]
    [InlineData("TR", PresentationDiagnostics.CurrencyCodeInvalid)]
    [InlineData("TRYX", PresentationDiagnostics.CurrencyCodeInvalid)]
    [InlineData("try", PresentationDiagnostics.CurrencyCodeInvalid)]
    [InlineData("T1Y", PresentationDiagnostics.CurrencyCodeInvalid)]
    public void Money_WithoutUsableCurrencyMetadata_IsUnavailableNotZero(
        string? currencyCode,
        string expectedDiagnostic)
    {
        var rendered = Presenter.Money(123_456, currencyCode, 2);

        Assert.Equal(DisplayState.Unavailable, rendered.State);
        Assert.Equal(expectedDiagnostic, rendered.DiagnosticCategory);
        Assert.DoesNotContain("0", rendered.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "1.234",
            rendered.Text,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void Money_WithUnsupportedScale_IsUnavailable(int minorUnitDigits)
    {
        var rendered = Presenter.Money(123_456, "TRY", minorUnitDigits);

        Assert.Equal(DisplayState.Unavailable, rendered.State);
        Assert.Equal(
            PresentationDiagnostics.MinorUnitDigitsUnsupported,
            rendered.DiagnosticCategory);
    }

    [Theory]
    [InlineData(1_234_567_890L, "12,3456789 fon birimi")]
    [InlineData(2_500_000_000L, "25 fon birimi")]
    [InlineData(25_000_000L, "0,25 fon birimi")]
    [InlineData(1L, "0,00000001 fon birimi")]
    [InlineData(0L, "0 fon birimi")]
    public void Quantity_TrimsOnlyZerosThatCarryNoRecordedScale(
        long rawE8,
        string expected)
    {
        var rendered = Presenter.Quantity(
            rawE8,
            AssetUnit.FundUnit,
            QuantitySign.Absolute);

        Assert.Equal(expected, rendered.Text);
    }

    [Fact]
    public void Quantity_SignedDelta_StatesDirectionInWordsAsWellAsSign()
    {
        var increase = Presenter.Quantity(
            1_234_567_890,
            AssetUnit.FundUnit,
            QuantitySign.SignedDelta);
        var decrease = Presenter.Quantity(
            -25_000_000,
            AssetUnit.GrossGram,
            QuantitySign.SignedDelta);

        Assert.Equal("+12,3456789 fon birimi — artış", increase.Text);
        Assert.Equal("-0,25 gram — azalış", decrease.Text);

        // The leading sign is easy to miss when read aloud, so assistive text
        // leads with the direction instead.
        Assert.StartsWith(
            "artış",
            increase.AssistiveText,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "azalış",
            decrease.AssistiveText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Quantity_ZeroDelta_IsNeitherAnIncreaseNorADecrease()
    {
        var rendered = Presenter.Quantity(
            0,
            AssetUnit.FundUnit,
            QuantitySign.SignedDelta);

        Assert.Equal(DisplayState.Zero, rendered.State);
        Assert.Equal("0 fon birimi — değişim yok", rendered.Text);
        Assert.DoesNotContain("+", rendered.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("-", rendered.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Quantity_HandlesBothEndsOfTheStorageRange()
    {
        Assert.Equal(
            "-92.233.720.368,54775808 fon birimi",
            Presenter.Quantity(
                long.MinValue,
                AssetUnit.FundUnit,
                QuantitySign.Absolute).Text);
        Assert.Equal(
            "+92.233.720.368,54775807 fon birimi — artış",
            Presenter.Quantity(
                long.MaxValue,
                AssetUnit.FundUnit,
                QuantitySign.SignedDelta).Text);
    }

    [Fact]
    public void UnitPrice_RendersExactValueWithItsCurrency()
    {
        Assert.Equal(
            "12,3456789 TRY",
            Presenter.UnitPrice(1_234_567_890, "TRY").Text);
        Assert.Equal("0 TRY", Presenter.UnitPrice(0, "TRY").Text);
        Assert.Equal(
            DisplayState.Zero,
            Presenter.UnitPrice(0, "TRY").State);
        Assert.Equal(
            DisplayState.Unavailable,
            Presenter.UnitPrice(1, null).State);
    }

    [Fact]
    public void BusinessDate_UsesAFixedUnambiguousPattern()
    {
        Assert.Equal(
            "31.08.2026",
            Presenter.BusinessDate(new DateOnly(2026, 8, 31)).Text);
        Assert.Equal(
            "01.09.2026",
            Presenter.BusinessDate(new DateOnly(2026, 9, 1)).Text);
    }

    [Fact]
    public void UtcTimestamp_ShowsLocalTimeAndTheRecordedUtcInstant()
    {
        var rendered = Presenter.UtcTimestamp(
            new DateTimeOffset(2026, 8, 31, 11, 30, 0, TimeSpan.Zero));

        Assert.Equal(
            "31.08.2026 14:30:00 Europe/Istanbul (11:30:00Z)",
            rendered.Text);
        Assert.Equal(
            "31.08.2026 14:30:00 Europe/Istanbul (11:30:00 UTC)",
            rendered.AssistiveText);
    }

    [Fact]
    public void UtcTimestamp_RejectsAnInstantThatIsNotUtc()
    {
        var rendered = Presenter.UtcTimestamp(
            new DateTimeOffset(2026, 8, 31, 14, 30, 0, TimeSpan.FromHours(3)));

        Assert.Equal(DisplayState.Unavailable, rendered.State);
        Assert.Equal(
            PresentationDiagnostics.TimestampNotUtc,
            rendered.DiagnosticCategory);
    }

    [Fact]
    public void StableCode_KeepsTheCodeBesideItsDescription()
    {
        var rendered = Presenter.StableCode(
            Domain.Ledger.TransactionStatus.Posted);

        Assert.Equal(DisplayState.Known, rendered.State);
        Assert.Equal("Kaydedildi", rendered.Text);
        Assert.Equal("POSTED", rendered.TechnicalDetail);
    }

    [Theory]
    [InlineData("NOT_A_REAL_CODE")]
    [InlineData("")]
    [InlineData("   ")]
    public void StableCode_UnknownCode_IsUnavailableRatherThanGuessed(
        string code)
    {
        var rendered = Presenter.StableCode(
            StableCodeFamily.TransactionStatus,
            code);

        Assert.Equal(DisplayState.Unavailable, rendered.State);
        Assert.Equal(
            PresentationDiagnostics.StableCodeUnknown,
            rendered.DiagnosticCategory);
    }

    [Fact]
    public void StableCode_FamilyDisambiguatesASharedCode()
    {
        // CASH is a valid code in two families and means different things.
        var assetType = Presenter.StableCode(AssetType.Cash);
        var accountType = Presenter.StableCode(
            Domain.Portfolios.AccountType.Cash);

        Assert.Equal("CASH", assetType.TechnicalDetail);
        Assert.Equal("CASH", accountType.TechnicalDetail);
        Assert.NotEqual(assetType.Text, accountType.Text);
    }

    [Fact]
    public void UnknownNotApplicableAndZero_AreThreeDistinctOutputs()
    {
        var unknown = Presenter.Unknown();
        var notApplicable = Presenter.NotApplicable();
        var zero = Presenter.Money(0, "TRY", 2);

        Assert.Equal(DisplayState.Unknown, unknown.State);
        Assert.Equal(DisplayState.NotApplicable, notApplicable.State);
        Assert.Equal(DisplayState.Zero, zero.State);

        Assert.NotEqual(unknown.Text, notApplicable.Text);
        Assert.NotEqual(unknown.Text, zero.Text);
        Assert.NotEqual(notApplicable.Text, zero.Text);

        Assert.False(unknown.HasRecordedValue);
        Assert.False(notApplicable.HasRecordedValue);
        Assert.True(zero.HasRecordedValue);
    }

    [Fact]
    public void Rendering_IsDeterministicForASuppliedCultureNotThreadState()
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;

        try
        {
            // A host thread set to an unrelated culture must not change what a
            // stored value looks like.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            Assert.Equal("1.234,56 TRY", Presenter.Money(123_456, "TRY", 2).Text);
            Assert.Equal(
                "31.08.2026",
                Presenter.BusinessDate(new DateOnly(2026, 8, 31)).Text);
            Assert.Equal(
                "Kaydedildi",
                Presenter.StableCode(
                    Domain.Ledger.TransactionStatus.Posted).Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact]
    public void CurrencyCode_IsAlwaysVisibleAndNeverReplacedByASymbol()
    {
        var rendered = Presenter.Money(123_456, "USD", 2);

        Assert.EndsWith("USD", rendered.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("$", rendered.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("₺", rendered.Text, StringComparison.Ordinal);
    }
}
