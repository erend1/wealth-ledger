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
    LocalStartupMode.StorageUninitialized)]
public sealed class StorageModel : PageModel
{
    private readonly GetLocalDataStatusUseCase _getStatus;
    private readonly InitializeLocalDatabaseUseCase _initializeDatabase;
    private readonly UiText _text;

    public StorageModel(
        GetLocalDataStatusUseCase getStatus,
        InitializeLocalDatabaseUseCase initializeDatabase,
        UiText text)
    {
        _getStatus = getStatus
            ?? throw new ArgumentNullException(nameof(getStatus));
        _initializeDatabase = initializeDatabase
            ?? throw new ArgumentNullException(nameof(initializeDatabase));
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    [BindProperty]
    public bool ConfirmInitialization { get; set; }

    public string DatabasePath { get; private set; } = string.Empty;

    public string BackupDirectory { get; private set; } = string.Empty;

    public bool ConfigurationAvailable { get; private set; }

    public bool StorageCreated { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        await LoadStatusAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        var status = await LoadStatusAsync(cancellationToken);

        if (StorageCreated)
        {
            return RedirectToPage();
        }

        if (!ConfigurationAvailable || status is null)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            return Page();
        }

        if (status.DatabaseExists)
        {
            AddOperationError("Storage_Error_ExistingNotReady");
            return Page();
        }

        if (!ConfirmInitialization)
        {
            ModelState.AddModelError(
                nameof(ConfirmInitialization),
                _text["Storage_Validation_ConfirmationRequired"]);
            return Page();
        }

        var result = await _initializeDatabase.ExecuteAsync(
            cancellationToken);

        if (result.Succeeded)
        {
            return RedirectToPage();
        }

        if (result.Failure!.Category
            == LocalDataFailureCategory.AlreadyExists)
        {
            await LoadStatusAsync(cancellationToken);

            if (StorageCreated)
            {
                return RedirectToPage();
            }
        }

        AddOperationError(
            result.Failure.Category
                == LocalDataFailureCategory.OwnershipBusy
                    ? "Storage_Error_OwnershipBusy"
                    : "Storage_Error_InitializationFailed");

        return Page();
    }

    private async Task<LocalDataStatus?> LoadStatusAsync(
        CancellationToken cancellationToken)
    {
        var result = await _getStatus.ExecuteAsync(
            cancellationToken);
        var status = result.Value;

        if (status is null
            || !status.DatabasePathSafe
            || !status.BackupDirectoryConfigured
            || string.IsNullOrWhiteSpace(status.BackupDirectory))
        {
            DatabasePath = string.Empty;
            BackupDirectory = string.Empty;
            ConfigurationAvailable = false;
            StorageCreated = false;

            ModelState.AddModelError(
                string.Empty,
                _text["Storage_Error_ConfigurationUnavailable"]);

            return null;
        }

        DatabasePath = status.DatabasePath;
        BackupDirectory = status.BackupDirectory;
        ConfigurationAvailable = true;
        StorageCreated = IsCurrentCompatibleStorage(result, status);

        return status;
    }

    private void AddOperationError(string resourceKey)
    {
        ModelState.AddModelError(
            string.Empty,
            _text[resourceKey]);
        Response.StatusCode = StatusCodes.Status409Conflict;
    }

    private static bool IsCurrentCompatibleStorage(
        LocalDataOperationResult<LocalDataStatus> result,
        LocalDataStatus status)
        => result.Succeeded
           && status.DatabaseExists
           && status.Compatibility
               == LocalDatabaseCompatibility.Compatible
           && status.PendingMigrations.Count == 0
           && status.IntegrityStatus
               == LocalDataIntegrityStatus.Passed
           && !string.IsNullOrWhiteSpace(status.LiveWorkspaceId);
}
