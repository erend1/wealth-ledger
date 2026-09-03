namespace WealthLedger.UI.Presentation;

/// <summary>
/// What a rendered value actually means. These states are distinct on purpose:
/// a recorded zero, an absent fact, a fact that does not apply, and a value
/// that could not be rendered are different things and must never collapse
/// into each other.
/// </summary>
public enum DisplayState
{
    /// <summary>A recorded value that is not zero.</summary>
    Known,

    /// <summary>A recorded value that is exactly zero.</summary>
    Zero,

    /// <summary>The fact exists as a concept but its value was never recorded.</summary>
    Unknown,

    /// <summary>The fact does not apply to this record.</summary>
    NotApplicable,

    /// <summary>
    /// A recorded value exists but could not be rendered, because required
    /// metadata was missing or unsupported. Never rendered as a number.
    /// </summary>
    Unavailable
}

/// <summary>
/// One formatted value ready for a page to render.
/// </summary>
/// <param name="Text">The visible text.</param>
/// <param name="AssistiveText">
/// Text for assistive technology. It repeats anything the visible form conveys
/// through symbols or layout alone, such as a leading sign.
/// </param>
/// <param name="State">What the value means.</param>
/// <param name="TechnicalDetail">
/// A stable identifier safe to reveal in an explicit technical-details
/// disclosure, such as the underlying stable code. Never the primary value.
/// </param>
/// <param name="DiagnosticCategory">
/// A stable, privacy-safe reason when <see cref="State"/> is
/// <see cref="DisplayState.Unavailable"/>. It names the defect, never the
/// value, the path, or the exception.
/// </param>
public sealed record DisplayValue(
    string Text,
    string AssistiveText,
    DisplayState State,
    string? TechnicalDetail = null,
    string? DiagnosticCategory = null)
{
    /// <summary>
    /// True when the value carries a recorded amount, whether or not it is
    /// zero. Pages use this to decide emphasis, never to decide arithmetic.
    /// </summary>
    public bool HasRecordedValue
        => State is DisplayState.Known or DisplayState.Zero;
}
