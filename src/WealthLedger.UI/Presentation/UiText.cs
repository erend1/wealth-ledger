namespace WealthLedger.UI.Presentation;

/// <summary>
/// Provides deterministic Turkish-first UI text from the same resources used
/// by exact value presentation.
/// </summary>
public sealed class UiText
{
    private readonly PresentationCulture _presentationCulture;

    public UiText(PresentationCulture presentationCulture)
    {
        _presentationCulture = presentationCulture
            ?? throw new ArgumentNullException(nameof(presentationCulture));
    }

    public string this[string key]
        => PresentationText.Require(
            key,
            _presentationCulture.Culture);
}
