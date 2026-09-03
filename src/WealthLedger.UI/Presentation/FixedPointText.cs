using System.Globalization;

namespace WealthLedger.UI.Presentation;

/// <summary>
/// Turns persisted signed integers into exact decimal text.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here converts through <see cref="double"/> or <see cref="float"/>.
/// A persisted value is decomposed with integer division and reassembled as
/// text, so the rendered digits always reproduce the stored value exactly.
/// </para>
/// <para>
/// The magnitude is carried as <see cref="ulong"/> rather than negated as a
/// <see cref="long"/>, because negating <see cref="long.MinValue"/> overflows.
/// That value is reachable: these are 64-bit persisted amounts, and a display
/// layer must not throw on the extremes of its own storage range.
/// </para>
/// </remarks>
internal static class FixedPointText
{
    internal const int MaximumScale = 8;

    private static readonly ulong[] PowersOfTen =
    [
        1UL,
        10UL,
        100UL,
        1_000UL,
        10_000UL,
        100_000UL,
        1_000_000UL,
        10_000_000UL,
        100_000_000UL
    ];

    internal static bool IsSupportedScale(int scale)
        => scale is >= 0 and <= MaximumScale;

    /// <summary>
    /// Formats <paramref name="value"/> interpreted as a fixed-point number
    /// with <paramref name="scale"/> decimal digits.
    /// </summary>
    /// <param name="trimTrailingZeros">
    /// Drops trailing fractional zeros that carry no recorded scale. The
    /// remaining digits still reproduce the stored value exactly.
    /// </param>
    /// <param name="alwaysSigned">
    /// Emits a leading <c>+</c> for positive values, for signed effects where
    /// direction is part of the meaning. Zero is never signed.
    /// </param>
    internal static string Format(
        long value,
        int scale,
        CultureInfo culture,
        bool trimTrailingZeros,
        bool alwaysSigned)
    {
        if (!IsSupportedScale(scale))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale),
                "Fixed-point scale must be between 0 and 8.");
        }

        ArgumentNullException.ThrowIfNull(culture);

        var (isNegative, magnitude) = SplitSign(value);
        var divisor = PowersOfTen[scale];
        var integerPart = magnitude / divisor;
        var fractionPart = magnitude % divisor;
        var text = integerPart.ToString("N0", culture);

        if (scale > 0)
        {
            var fractionText = fractionPart
                .ToString(CultureInfo.InvariantCulture)
                .PadLeft(scale, '0');

            if (trimTrailingZeros)
            {
                fractionText = fractionText.TrimEnd('0');
            }

            if (fractionText.Length > 0)
            {
                text = text
                       + culture.NumberFormat.NumberDecimalSeparator
                       + fractionText;
            }
        }

        var sign = isNegative
            ? culture.NumberFormat.NegativeSign
            : alwaysSigned && value != 0
                ? culture.NumberFormat.PositiveSign
                : string.Empty;

        return sign + text;
    }

    /// <summary>
    /// Splits a signed value into its sign and its magnitude without ever
    /// negating a <see cref="long"/>.
    /// </summary>
    internal static (bool IsNegative, ulong Magnitude) SplitSign(long value)
        => value < 0
            ? (true, (ulong)(-(value + 1)) + 1)
            : (false, (ulong)value);
}
