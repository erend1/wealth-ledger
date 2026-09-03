using System.Globalization;
using System.Resources;

namespace WealthLedger.UI.Presentation;

/// <summary>
/// Resolves user-visible text from the presentation resources.
/// </summary>
/// <remarks>
/// Every lookup takes an explicit culture rather than reading
/// <see cref="CultureInfo.CurrentUICulture"/>. Rendering must be deterministic
/// for a supplied culture, and ambient thread state is not a supplied culture.
/// </remarks>
internal static class PresentationText
{
    private static readonly ResourceManager Resources = new(
        "WealthLedger.UI.Resources.PresentationText",
        typeof(PresentationText).Assembly);

    internal const string StateUnknown = "State_Unknown";
    internal const string StateNotApplicable = "State_NotApplicable";
    internal const string StateUnavailable = "State_Unavailable";
    internal const string EffectIncrease = "Effect_Increase";
    internal const string EffectDecrease = "Effect_Decrease";
    internal const string EffectNoChange = "Effect_NoChange";

    /// <summary>
    /// Reads a key that the resources are required to contain. A missing key
    /// is a defect in this assembly, not a data condition.
    /// </summary>
    internal static string Require(string key, CultureInfo culture)
        => Resources.GetString(key, culture)
           ?? throw new MissingManifestResourceException(
               $"Presentation resources are missing the required key '{key}'.");

    /// <summary>
    /// Reads a key that may legitimately be absent, because the underlying
    /// value came from stored data rather than from this assembly.
    /// </summary>
    internal static bool TryRead(
        string key,
        CultureInfo culture,
        out string value)
    {
        var resolved = Resources.GetString(key, culture);
        value = resolved ?? string.Empty;

        return resolved is not null;
    }

    internal static string UnitKey(string unitCode) => "Unit_" + unitCode;

    internal static string CodeKey(StableCodeFamily family, string code)
        => "Code_" + family + "_" + code;
}
