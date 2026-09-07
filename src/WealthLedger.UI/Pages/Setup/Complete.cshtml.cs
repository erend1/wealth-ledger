using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Setup;

[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.InitialBackupRequired)]
public sealed class CompleteModel : PageModel
{
    private readonly GetLocalDataStatusUseCase _getStatus;
    private readonly ValuePresenter _valuePresenter;
    private readonly UiText _text;

    public CompleteModel(
        GetLocalDataStatusUseCase getStatus,
        ValuePresenter valuePresenter,
        UiText text)
    {
        _getStatus = getStatus
            ?? throw new ArgumentNullException(nameof(getStatus));
        _valuePresenter = valuePresenter
            ?? throw new ArgumentNullException(nameof(valuePresenter));
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public string BackupFilePath { get; private set; } = string.Empty;

    public string DigestPrefix { get; private set; } = string.Empty;

    public string EncryptionMode { get; private set; } = string.Empty;

    public DisplayValue? CreatedAt { get; private set; }

    public DisplayValue? VerifiedAt { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        var result = await _getStatus.ExecuteAsync(cancellationToken);
        var status = result.Value;

        if (!InitialBackupStatus.IsEligible(result, status))
        {
            ModelState.AddModelError(
                string.Empty,
                _text["Complete_Error_StateUnavailable"]);
            Response.StatusCode = StatusCodes.Status409Conflict;
            return Page();
        }

        if (!InitialBackupStatus.IsComplete(status!))
        {
            return RedirectToPage("/Setup/Backup");
        }

        var backup = status!.LatestVerifiedBackup!;
        BackupFilePath = backup.FilePath;
        DigestPrefix = backup.DigestPrefix;
        EncryptionMode = backup.EncryptionMode;
        CreatedAt = _valuePresenter.UtcTimestamp(backup.CreatedAtUtc);
        VerifiedAt = _valuePresenter.UtcTimestamp(backup.VerifiedAtUtc);

        return Page();
    }
}
