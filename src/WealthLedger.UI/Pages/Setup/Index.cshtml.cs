using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Hosting;

namespace WealthLedger.UI.Pages.Setup;

[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.StorageUninitialized,
    LocalStartupMode.WorkspaceUninitialized,
    LocalStartupMode.InitialBackupRequired)]
public sealed class IndexModel : PageModel
{
    private readonly LocalUiStartupContext _startupContext;

    public IndexModel(LocalUiStartupContext startupContext)
    {
        _startupContext = startupContext
            ?? throw new ArgumentNullException(nameof(startupContext));
    }

    public IActionResult OnGet()
        => _startupContext.Mode switch
        {
            LocalStartupMode.StorageUninitialized =>
                RedirectToPage("/Setup/Storage"),

            LocalStartupMode.WorkspaceUninitialized =>
                RedirectToPage("/Setup/Workspace"),

            LocalStartupMode.InitialBackupRequired =>
                RedirectToPage("/Setup/Backup"),

            _ => NotFound()
        };
}
