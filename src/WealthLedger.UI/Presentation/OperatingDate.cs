namespace WealthLedger.UI.Presentation;

/// <summary>
/// The current business date that forms offer by default and pre-check
/// against.
/// </summary>
/// <remarks>
/// The Application refuses business dates later than the host's current
/// local operating date from <see cref="TimeProvider"/> (M007, M008), so the
/// UI uses exactly that date. The presentation time zone stays for rendering
/// recorded timestamps only: a default taken from it would, on a host in
/// another zone, offer a date the Application refuses as future.
/// </remarks>
internal static class OperatingDate
{
    internal static DateOnly Today(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
    }
}
