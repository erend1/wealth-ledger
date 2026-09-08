using WealthLedger.Application.LocalData;

namespace WealthLedger.UI.Hosting;

/// <summary>
/// Holds the final process-start selection used by the static UI route policy.
/// It is immutable after composition and is not setup progress or wizard state.
/// </summary>
public sealed class LocalUiStartupContext
{
    private LocalStartupSelection? _selection;

    public LocalStartupSelection Selection
        => Volatile.Read(ref _selection)
           ?? throw new InvalidOperationException(
               "The local UI startup context has not been initialized.");

    public LocalStartupMode Mode => Selection.Mode;

    public void Initialize(LocalStartupSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (Interlocked.CompareExchange(
                ref _selection,
                selection,
                comparand: null) is not null)
        {
            throw new InvalidOperationException(
                "The local UI startup context is already initialized.");
        }
    }
}
