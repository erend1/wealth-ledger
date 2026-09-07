using WealthLedger.Application.LocalData;

namespace WealthLedger.UI.Hosting;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = true)]
public sealed class LocalStartupPageAttribute : Attribute
{
    public LocalStartupPageAttribute(
        bool supportsPost,
        params LocalStartupMode[] allowedModes)
    {
        ArgumentNullException.ThrowIfNull(allowedModes);

        SupportsPost = supportsPost;
        AllowedModes = allowedModes.ToArray();
    }

    public bool SupportsPost { get; }

    public IReadOnlyList<LocalStartupMode> AllowedModes { get; }

    internal bool Allows(LocalStartupMode mode)
        => AllowedModes.Contains(mode);
}
