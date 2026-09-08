using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Setup;

[AutoValidateAntiforgeryToken]
[LocalStartupPage(
    supportsPost: true,
    LocalStartupMode.InitialBackupRequired)]
public sealed class BackupModel : PageModel
{
    private readonly GetLocalDataStatusUseCase _getStatus;
    private readonly CreateLocalBackupUseCase _createBackup;
    private readonly UiText _text;

    public BackupModel(
        GetLocalDataStatusUseCase getStatus,
        CreateLocalBackupUseCase createBackup,
        UiText text)
    {
        _getStatus = getStatus
            ?? throw new ArgumentNullException(nameof(getStatus));
        _createBackup = createBackup
            ?? throw new ArgumentNullException(nameof(createBackup));
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    [BindProperty]
    public bool ConfirmBackupCreation { get; set; }

    public string BackupDirectory { get; private set; } = string.Empty;

    public string EncryptionMode { get; private set; } = string.Empty;

    public bool ConfigurationAvailable { get; private set; }

    public bool BackupDirectoryExists { get; private set; }

    public bool DestinationSeparationConfirmed { get; private set; }

    public bool DestinationEncryptionConfirmed { get; private set; }

    public int UnrelatedVerifiedBackupCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        var status = await LoadStatusAsync(cancellationToken);

        return status is not null
               && InitialBackupStatus.IsComplete(status)
            ? RedirectToPage("/Setup/Complete")
            : Page();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        var status = await LoadStatusAsync(cancellationToken);

        if (!ConfigurationAvailable || status is null)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            return Page();
        }

        if (!ConfirmBackupCreation)
        {
            ModelState.AddModelError(
                nameof(ConfirmBackupCreation),
                _text["Backup_Validation_ConfirmationRequired"]);
            return Page();
        }

        /*
         * Do not short-circuit when a stale form is replayed after success.
         * M004 creates a new collision-safe immutable generation for every
         * successful request, so a lost response or deliberate retry never
         * overwrites the earlier package.
         */
        var creationResult = await _createBackup.ExecuteAsync(
            cancellationToken);

        if (!creationResult.Succeeded)
        {
            AddOperationError(
                FailureResourceKey(creationResult.Failure!.Category));
            return Page();
        }

        var refreshedStatus = await LoadStatusAsync(cancellationToken);

        if (refreshedStatus is not null
            && InitialBackupStatus.IsComplete(refreshedStatus))
        {
            return RedirectToPage("/Setup/Complete");
        }

        AddOperationError("Backup_Error_VerificationFailed");
        return Page();
    }

    private async Task<LocalDataStatus?> LoadStatusAsync(
        CancellationToken cancellationToken)
    {
        var result = await _getStatus.ExecuteAsync(cancellationToken);
        var status = result.Value;

        if (!InitialBackupStatus.IsEligible(result, status))
        {
            ClearStatus();
            ModelState.AddModelError(
                string.Empty,
                _text["Backup_Error_StateUnavailable"]);
            return null;
        }

        BackupDirectory = status!.BackupDirectory!;
        EncryptionMode = status.EncryptionMode;
        ConfigurationAvailable = true;
        BackupDirectoryExists = status.BackupDirectoryExists;
        DestinationSeparationConfirmed =
            status.DestinationSeparationConfirmed;
        DestinationEncryptionConfirmed =
            status.DestinationEncryptionConfirmed;
        UnrelatedVerifiedBackupCount =
            status.UnrelatedVerifiedBackupCount;

        return status;
    }

    private void ClearStatus()
    {
        BackupDirectory = string.Empty;
        EncryptionMode = string.Empty;
        ConfigurationAvailable = false;
        BackupDirectoryExists = false;
        DestinationSeparationConfirmed = false;
        DestinationEncryptionConfirmed = false;
        UnrelatedVerifiedBackupCount = 0;
    }

    private void AddOperationError(string resourceKey)
    {
        ModelState.AddModelError(
            string.Empty,
            _text[resourceKey]);
        Response.StatusCode = StatusCodes.Status409Conflict;
    }

    private static string FailureResourceKey(
        LocalDataFailureCategory category)
        => category switch
        {
            LocalDataFailureCategory.OwnershipBusy =>
                "Backup_Error_OwnershipBusy",

            LocalDataFailureCategory.InvalidBackup
                or LocalDataFailureCategory.IncompatibleBackup
                or LocalDataFailureCategory.IntegrityFailure =>
                "Backup_Error_VerificationFailed",

            LocalDataFailureCategory.DatabaseNotReady =>
                "Backup_Error_DatabaseNotReady",

            LocalDataFailureCategory.Cancelled =>
                "Backup_Error_Cancelled",

            _ => "Backup_Error_CreationFailed"
        };
}

internal static class InitialBackupStatus
{
    internal static bool IsEligible(
        LocalDataOperationResult<LocalDataStatus> result,
        LocalDataStatus? status)
        => result.Succeeded
           && status is not null
           && status.DatabasePathSafe
           && status.DatabaseExists
           && status.BackupDirectoryConfigured
           && !string.IsNullOrWhiteSpace(status.BackupDirectory)
           && status.Compatibility
               == LocalDatabaseCompatibility.Compatible
           && status.PendingMigrations.Count == 0
           && status.IntegrityStatus
               == LocalDataIntegrityStatus.Passed
           && !string.IsNullOrWhiteSpace(status.LiveWorkspaceId);

    internal static bool IsComplete(LocalDataStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return status.LatestVerifiedBackup is not null
               && status.LatestVerifiedBackup.WorkspaceBinding
                   == LocalBackupWorkspaceBinding.Matched;
    }
}
