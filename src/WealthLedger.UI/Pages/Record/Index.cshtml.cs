using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Hosting;

namespace WealthLedger.UI.Pages.Record;

/// <summary>
/// The entry point to the reviewed recording workflows.
/// </summary>
/// <remarks>
/// Each workflow is a separate, named choice rather than one generic
/// transaction form. A household operator recording a monthly contribution
/// and one liquidating a holding are doing different things, and the ledger
/// records them differently.
/// </remarks>
[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class IndexModel : PageModel
{
}
