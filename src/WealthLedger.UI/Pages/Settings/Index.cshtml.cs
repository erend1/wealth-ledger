using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Settings;

[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class IndexModel : PageModel
{
    private readonly ReadyHouseholdResolver _householdResolver;
    private readonly ValuePresenter _values;

    public IndexModel(
        ReadyHouseholdResolver householdResolver,
        ValuePresenter values)
    {
        _householdResolver = householdResolver
            ?? throw new ArgumentNullException(nameof(householdResolver));
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public string HouseholdName { get; private set; } = string.Empty;

    public string BaseCurrencyCode { get; private set; } = string.Empty;

    public DisplayValue? HouseholdCreatedAt { get; private set; }

    public string HouseholdStateResourceKey { get; private set; } = string.Empty;

    public bool HasHousehold => HouseholdStateResourceKey.Length == 0;

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        var resolution = await _householdResolver.ResolveAsync(
            cancellationToken);

        if (resolution.State != ReadyHouseholdResolutionState.Single)
        {
            HouseholdStateResourceKey = resolution.State
                == ReadyHouseholdResolutionState.None
                    ? "Ready_Household_None"
                    : "Ready_Household_Multiple";
            return Page();
        }

        var household = resolution.Household!;
        HouseholdName = household.Name;
        BaseCurrencyCode = household.BaseCurrency.Code;
        HouseholdCreatedAt = _values.UtcTimestamp(household.CreatedAtUtc);
        return Page();
    }
}
