using System.Globalization;
using WealthLedger.Domain.Assets;

namespace WealthLedger.UI.Presentation;

/// <summary>
/// Whether a quantity carries a direction.
/// </summary>
public enum QuantitySign
{
    /// <summary>A held amount. Rendered without a leading sign or direction.</summary>
    Absolute,

    /// <summary>
    /// A signed effect on a holding. The direction is part of the meaning and
    /// is stated in words as well as by the sign.
    /// </summary>
    SignedDelta
}

/// <summary>
/// Stable, privacy-safe reasons a recorded value could not be rendered.
/// </summary>
/// <remarks>
/// These name the defect, never the value. They are safe to log and to show in
/// a technical-details disclosure.
/// </remarks>
public static class PresentationDiagnostics
{
    public const string CurrencyMetadataMissing = "CURRENCY_METADATA_MISSING";
    public const string CurrencyCodeInvalid = "CURRENCY_CODE_INVALID";
    public const string MinorUnitDigitsUnsupported = "MINOR_UNIT_DIGITS_UNSUPPORTED";
    public const string UnitCodeUnknown = "UNIT_CODE_UNKNOWN";
    public const string StableCodeUnknown = "STABLE_CODE_UNKNOWN";
    public const string TimestampNotUtc = "TIMESTAMP_NOT_UTC";
    public const string TimeZoneConversionFailed = "TIME_ZONE_CONVERSION_FAILED";
}

/// <summary>
/// Renders persisted financial facts as exact, human-readable text.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here calculates. A presenter formats one already-recorded value; it
/// never sums, converts between assets, derives a position, or infers a
/// missing fact. Those belong to Application and Domain.
/// </para>
/// <para>
/// A value that cannot be rendered becomes an explicit unavailable state with
/// a diagnostic category. It never becomes zero, and never becomes a plausible
/// but wrong number.
/// </para>
/// </remarks>
public sealed class ValuePresenter
{
    private const string BusinessDatePattern = "dd.MM.yyyy";
    private const string ClockPattern = "HH:mm:ss";
    private const string EffectSeparator = " — ";
    private const int CurrencyCodeLength = 3;

    private readonly PresentationCulture _presentationCulture;

    public ValuePresenter(PresentationCulture presentationCulture)
    {
        _presentationCulture = presentationCulture
            ?? throw new ArgumentNullException(nameof(presentationCulture));
    }

    private CultureInfo Culture => _presentationCulture.Culture;

    /// <summary>
    /// A fact that exists as a concept but was never recorded.
    /// </summary>
    /// <remarks>
    /// Callers choose between this and <see cref="NotApplicable"/> from the
    /// contract they are rendering. A presenter must never guess which of the
    /// two a null means.
    /// </remarks>
    public DisplayValue Unknown()
    {
        var text = PresentationText.Require(
            PresentationText.StateUnknown,
            Culture);

        return new DisplayValue(text, text, DisplayState.Unknown);
    }

    /// <summary>A fact that does not apply to this record.</summary>
    public DisplayValue NotApplicable()
    {
        var text = PresentationText.Require(
            PresentationText.StateNotApplicable,
            Culture);

        return new DisplayValue(text, text, DisplayState.NotApplicable);
    }

    /// <summary>
    /// Renders an amount held in signed minor units of a currency.
    /// </summary>
    /// <remarks>
    /// The currency code is always shown. A symbol alone would be ambiguous
    /// across the currencies a household actually holds.
    /// </remarks>
    public DisplayValue Money(
        long amountMinorUnits,
        string? currencyCode,
        int minorUnitDigits)
    {
        var currency = ValidateCurrency(currencyCode);

        if (currency.Failure is not null)
        {
            return currency.Failure;
        }

        if (!FixedPointText.IsSupportedScale(minorUnitDigits))
        {
            return Unavailable(
                PresentationDiagnostics.MinorUnitDigitsUnsupported);
        }

        // Minor-unit digits are the recorded scale of the currency, so
        // trailing zeros carry meaning and are kept.
        var number = FixedPointText.Format(
            amountMinorUnits,
            minorUnitDigits,
            Culture,
            trimTrailingZeros: false,
            alwaysSigned: false);
        var text = number + " " + currency.Code;

        return new DisplayValue(
            text,
            text,
            amountMinorUnits == 0 ? DisplayState.Zero : DisplayState.Known);
    }

    /// <summary>
    /// Renders a quantity held in an asset's own unit, from raw E8.
    /// </summary>
    public DisplayValue Quantity(
        long quantityRawE8,
        AssetUnit unit,
        QuantitySign sign)
    {
        var unitCode = StableCodes.ToCode(unit);

        if (!PresentationText.TryRead(
                PresentationText.UnitKey(unitCode),
                Culture,
                out var unitText))
        {
            return Unavailable(
                PresentationDiagnostics.UnitCodeUnknown,
                unitCode);
        }

        var signedDelta = sign == QuantitySign.SignedDelta;
        var number = FixedPointText.Format(
            quantityRawE8,
            FixedPointText.MaximumScale,
            Culture,
            trimTrailingZeros: true,
            alwaysSigned: signedDelta);
        var text = number + " " + unitText;
        var assistiveText = text;

        if (signedDelta)
        {
            var effect = PresentationText.Require(
                quantityRawE8 switch
                {
                    > 0 => PresentationText.EffectIncrease,
                    < 0 => PresentationText.EffectDecrease,
                    _ => PresentationText.EffectNoChange
                },
                Culture);
            text += EffectSeparator + effect;

            // Assistive technology should hear the direction before the
            // amount, because a leading "+" or "-" is easily lost.
            assistiveText = effect + " " + number + " " + unitText;
        }

        return new DisplayValue(
            text,
            assistiveText,
            quantityRawE8 == 0 ? DisplayState.Zero : DisplayState.Known,
            unitCode);
    }

    /// <summary>
    /// Renders an executed unit price from raw E8 in an explicit currency.
    /// </summary>
    public DisplayValue UnitPrice(long unitPriceRawE8, string? currencyCode)
    {
        var currency = ValidateCurrency(currencyCode);

        if (currency.Failure is not null)
        {
            return currency.Failure;
        }

        var number = FixedPointText.Format(
            unitPriceRawE8,
            FixedPointText.MaximumScale,
            Culture,
            trimTrailingZeros: true,
            alwaysSigned: false);
        var text = number + " " + currency.Code;

        return new DisplayValue(
            text,
            text,
            unitPriceRawE8 == 0 ? DisplayState.Zero : DisplayState.Known);
    }

    /// <summary>Renders a business date.</summary>
    /// <remarks>
    /// The pattern is fixed rather than taken from the culture's short-date
    /// pattern, which varies by host data and would render the same stored
    /// date differently on two machines.
    /// </remarks>
    public DisplayValue BusinessDate(DateOnly date)
    {
        var text = date.ToString(BusinessDatePattern, Culture);

        return new DisplayValue(text, text, DisplayState.Known);
    }

    /// <summary>
    /// Renders an audit timestamp in the display time zone, alongside the
    /// recorded UTC time.
    /// </summary>
    /// <remarks>
    /// Both are shown because the stored fact is the UTC instant. Showing only
    /// local time would hide what was recorded; showing only UTC would be hard
    /// to reconcile with a household's own memory of the day.
    /// </remarks>
    public DisplayValue UtcTimestamp(DateTimeOffset timestampUtc)
    {
        if (timestampUtc.Offset != TimeSpan.Zero)
        {
            return Unavailable(PresentationDiagnostics.TimestampNotUtc);
        }

        DateTimeOffset local;

        try
        {
            local = TimeZoneInfo.ConvertTime(
                timestampUtc,
                _presentationCulture.DisplayTimeZone);
        }
        catch (Exception exception)
            when (exception is InvalidTimeZoneException
                  or TimeZoneNotFoundException
                  or ArgumentException)
        {
            return Unavailable(
                PresentationDiagnostics.TimeZoneConversionFailed);
        }

        var localText = local.ToString(
            BusinessDatePattern + " " + ClockPattern,
            Culture);
        var utcClock = timestampUtc.ToString(ClockPattern, Culture);
        var zoneId = _presentationCulture.DisplayTimeZone.Id;
        var text = $"{localText} {zoneId} ({utcClock}Z)";
        var assistiveText = $"{localText} {zoneId} ({utcClock} UTC)";

        return new DisplayValue(text, assistiveText, DisplayState.Known);
    }

    /// <summary>
    /// Renders a stable enum-like code as human text, keeping the code itself
    /// available as technical detail.
    /// </summary>
    /// <remarks>
    /// The code is a contract value and is never translated. An unrecognised
    /// code is reported as unavailable rather than guessed at, but the code is
    /// still surfaced because it is safe to show and is what an operator needs
    /// in order to report the gap.
    /// </remarks>
    public DisplayValue StableCode(StableCodeFamily family, string code)
    {
        if (string.IsNullOrWhiteSpace(code)
            || !PresentationText.TryRead(
                PresentationText.CodeKey(family, code),
                Culture,
                out var description))
        {
            return Unavailable(
                PresentationDiagnostics.StableCodeUnknown,
                string.IsNullOrWhiteSpace(code) ? null : code);
        }

        return new DisplayValue(
            description,
            description,
            DisplayState.Known,
            code);
    }

    public DisplayValue StableCode(AssetType value)
        => StableCode(StableCodeFamily.AssetType, StableCodes.ToCode(value));

    public DisplayValue StableCode(AssetUnit value)
        => StableCode(StableCodeFamily.AssetUnit, StableCodes.ToCode(value));

    public DisplayValue StableCode(LotTrackingMode value)
        => StableCode(
            StableCodeFamily.LotTrackingMode,
            StableCodes.ToCode(value));

    public DisplayValue StableCode(Domain.Portfolios.InstitutionType value)
        => StableCode(
            StableCodeFamily.InstitutionType,
            StableCodes.ToCode(value));

    public DisplayValue StableCode(Domain.Portfolios.AccountType value)
        => StableCode(StableCodeFamily.AccountType, StableCodes.ToCode(value));

    public DisplayValue StableCode(Domain.Portfolios.PortfolioStatus value)
        => StableCode(
            StableCodeFamily.PortfolioStatus,
            StableCodes.ToCode(value));

    public DisplayValue StableCode(Domain.Ledger.TransactionType value)
        => StableCode(
            StableCodeFamily.TransactionType,
            StableCodes.ToCode(value));

    public DisplayValue StableCode(Domain.Ledger.TransactionStatus value)
        => StableCode(
            StableCodeFamily.TransactionStatus,
            StableCodes.ToCode(value));

    public DisplayValue StableCode(Domain.Ledger.EntryRole value)
        => StableCode(StableCodeFamily.EntryRole, StableCodes.ToCode(value));

    public DisplayValue StableCode(Domain.Ledger.CashFlowCategory value)
        => StableCode(
            StableCodeFamily.CashFlowCategory,
            StableCodes.ToCode(value));

    public DisplayValue StableCode(Domain.Ledger.CostType value)
        => StableCode(StableCodeFamily.CostType, StableCodes.ToCode(value));

    public DisplayValue StableCode(Domain.Ledger.CostTreatment value)
        => StableCode(
            StableCodeFamily.CostTreatment,
            StableCodes.ToCode(value));

    public DisplayValue StableCode(Domain.Lots.CostBasisStatus value)
        => StableCode(
            StableCodeFamily.CostBasisStatus,
            StableCodes.ToCode(value));

    private DisplayValue Unavailable(
        string diagnosticCategory,
        string? technicalDetail = null)
    {
        var text = PresentationText.Require(
            PresentationText.StateUnavailable,
            Culture);

        return new DisplayValue(
            text,
            text,
            DisplayState.Unavailable,
            technicalDetail,
            diagnosticCategory);
    }

    private (string Code, DisplayValue? Failure) ValidateCurrency(
        string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return (
                string.Empty,
                Unavailable(PresentationDiagnostics.CurrencyMetadataMissing));
        }

        var candidate = currencyCode.Trim();

        if (candidate.Length != CurrencyCodeLength
            || !candidate.All(character => character is >= 'A' and <= 'Z'))
        {
            return (
                string.Empty,
                Unavailable(PresentationDiagnostics.CurrencyCodeInvalid));
        }

        return (candidate, null);
    }
}
