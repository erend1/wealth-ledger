using System.Globalization;

namespace WealthLedger.UI.Presentation;

/// <summary>
/// Formats an already-derived decimal without introducing binary floating
/// point or presentation rounding.
/// </summary>
internal static class ExactDecimalText
{
    internal static string Format(decimal value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return value.ToString(
            "0.############################",
            culture);
    }
}
