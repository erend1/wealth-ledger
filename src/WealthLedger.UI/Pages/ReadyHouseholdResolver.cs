using WealthLedger.Application.Navigation;

namespace WealthLedger.UI.Pages;

internal enum ReadyHouseholdResolutionState
{
    None,
    Single,
    Multiple
}

internal sealed record ReadyHouseholdResolution(
    ReadyHouseholdResolutionState State,
    HouseholdNavigationItem? Household)
{
    internal static ReadyHouseholdResolution None { get; } =
        new(ReadyHouseholdResolutionState.None, Household: null);

    internal static ReadyHouseholdResolution Multiple { get; } =
        new(ReadyHouseholdResolutionState.Multiple, Household: null);

    internal static ReadyHouseholdResolution Single(
        HouseholdNavigationItem household)
        => new(ReadyHouseholdResolutionState.Single, household);
}

/// <summary>
/// Resolves the accepted one-household Ready context without silently choosing
/// the first row if the stored reality ever expands beyond that bootstrap.
/// </summary>
public sealed class ReadyHouseholdResolver
{
    private const int AmbiguityDetectionPageSize = 2;
    private readonly ListHouseholdsUseCase _listHouseholds;

    public ReadyHouseholdResolver(ListHouseholdsUseCase listHouseholds)
    {
        _listHouseholds = listHouseholds
            ?? throw new ArgumentNullException(nameof(listHouseholds));
    }

    internal async Task<ReadyHouseholdResolution> ResolveAsync(
        CancellationToken cancellationToken = default)
    {
        var page = await _listHouseholds.ExecuteAsync(
            new ListHouseholdsQuery(
                PageSize: AmbiguityDetectionPageSize),
            cancellationToken);

        return page.Items.Count switch
        {
            0 => ReadyHouseholdResolution.None,
            1 when page.NextCursor is null =>
                ReadyHouseholdResolution.Single(page.Items[0]),
            _ => ReadyHouseholdResolution.Multiple
        };
    }
}
