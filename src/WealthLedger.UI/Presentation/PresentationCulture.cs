using System.Globalization;

namespace WealthLedger.UI.Presentation;

/// <summary>
/// The culture and time zone a value is rendered for.
/// </summary>
/// <remarks>
/// This is passed explicitly rather than read from thread state so the same
/// stored value always produces the same visible text, before and after a
/// restart and regardless of which thread renders it.
/// </remarks>
public sealed class PresentationCulture
{
    /// <summary>The initial user-facing culture.</summary>
    public const string DefaultCultureName = "tr-TR";

    /// <summary>
    /// The initial display time zone, as an IANA identifier. Windows
    /// identifiers are accepted as a fallback for hosts without IANA data.
    /// </summary>
    public const string DefaultTimeZoneId = "Europe/Istanbul";

    private const string DefaultWindowsTimeZoneId = "Turkey Standard Time";

    public PresentationCulture(CultureInfo culture, TimeZoneInfo displayTimeZone)
    {
        Culture = culture ?? throw new ArgumentNullException(nameof(culture));
        DisplayTimeZone = displayTimeZone
            ?? throw new ArgumentNullException(nameof(displayTimeZone));
    }

    public CultureInfo Culture { get; }

    public TimeZoneInfo DisplayTimeZone { get; }

    /// <summary>
    /// Builds the default presentation culture.
    /// </summary>
    /// <remarks>
    /// An unresolvable time zone throws here, at composition, rather than
    /// silently substituting UTC. Quietly shifting every displayed timestamp
    /// by the host's offset would be a wrong number, not a missing one.
    /// </remarks>
    public static PresentationCulture CreateDefault()
        => new(
            CultureInfo.GetCultureInfo(DefaultCultureName),
            ResolveDefaultTimeZone());

    private static TimeZoneInfo ResolveDefaultTimeZone()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(
                DefaultTimeZoneId,
                out var ianaZone))
        {
            return ianaZone;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(
                DefaultWindowsTimeZoneId,
                out var windowsZone))
        {
            return windowsZone;
        }

        throw new TimeZoneNotFoundException(
            $"The display time zone '{DefaultTimeZoneId}' is not available on this host.");
    }
}
