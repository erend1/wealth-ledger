using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Hosting;

namespace WealthLedger.UI.Pages;

[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Blocked)]
public sealed class BlockedModel : PageModel
{
    private const string OperationsPrefix =
        "dotnet run --project "
        + "src/WealthLedger.Operations/WealthLedger.Operations.csproj -- ";

    private readonly LocalUiStartupContext _startupContext;

    public BlockedModel(LocalUiStartupContext startupContext)
    {
        _startupContext = startupContext
            ?? throw new ArgumentNullException(nameof(startupContext));
    }

    public string FailureCategory { get; private set; } = string.Empty;

    public string GuidanceResourceKey { get; private set; } = string.Empty;

    public string? OperationsCommand { get; private set; }

    public void OnGet()
    {
        var selection = _startupContext.Selection;
        var category = selection.Failure?.Category
                       ?? LocalDataFailureCategory.DatabaseNotReady;

        FailureCategory = category.ToString();

        if (selection.Status?.PendingMigrations.Count > 0)
        {
            GuidanceResourceKey = "Blocked_Guidance_MigrationRequired";
            OperationsCommand = OperationsPrefix + "database migrate";
            return;
        }

        switch (category)
        {
            case LocalDataFailureCategory.OwnershipBusy:
                GuidanceResourceKey = "Blocked_Guidance_OwnershipBusy";
                break;

            case LocalDataFailureCategory.InvalidInputOrConfiguration:
            case LocalDataFailureCategory.UnsafePath:
                GuidanceResourceKey = "Blocked_Guidance_Configuration";
                OperationsCommand = OperationsPrefix + "status";
                break;

            case LocalDataFailureCategory.IntegrityFailure:
            case LocalDataFailureCategory.DatabaseNotReady:
            case LocalDataFailureCategory.MigrationFailure:
                GuidanceResourceKey = "Blocked_Guidance_Database";
                OperationsCommand = OperationsPrefix + "status";
                break;

            default:
                GuidanceResourceKey = "Blocked_Guidance_General";
                OperationsCommand = OperationsPrefix + "status";
                break;
        }
    }
}
