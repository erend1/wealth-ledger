using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WealthLedger.Application.LocalData;
using WealthLedger.UI.Hosting;
using WealthLedger.UI.Presentation;

namespace WealthLedger.UI.Pages.Settings;

[LocalStartupPage(
    supportsPost: false,
    LocalStartupMode.Ready)]
public sealed class DataSafetyModel : PageModel
{
    private const int WorkspacePrefixLength = 12;
    private readonly GetLocalDataStatusUseCase _getStatus;
    private readonly ValuePresenter _values;

    public DataSafetyModel(
        GetLocalDataStatusUseCase getStatus,
        ValuePresenter values)
    {
        _getStatus = getStatus
            ?? throw new ArgumentNullException(nameof(getStatus));
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public bool StatusAvailable { get; private set; }

    public string DatabasePath { get; private set; } = string.Empty;

    public string BackupDirectory { get; private set; } = string.Empty;

    public string ApplicationVersion { get; private set; } = string.Empty;

    public IReadOnlyList<string> AppliedMigrations { get; private set; } = [];

    public IReadOnlyList<string> PendingMigrations { get; private set; } = [];

    public DisplayValue? Compatibility { get; private set; }

    public DisplayValue? Integrity { get; private set; }

    public DisplayValue? EncryptionMode { get; private set; }

    public string? WorkspaceIdPrefix { get; private set; }

    public bool DestinationSeparationConfirmed { get; private set; }

    public bool DestinationEncryptionConfirmed { get; private set; }

    public bool LocalProtectionReady { get; private set; }

    public int UnrelatedVerifiedBackupCount { get; private set; }

    public BackupDisplay? LatestBackup { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        var result = await _getStatus.ExecuteAsync(cancellationToken);
        var status = result.Value;

        if (!result.Succeeded || status is null)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            return Page();
        }

        StatusAvailable = true;
        DatabasePath = status.DatabasePath;
        BackupDirectory = status.BackupDirectory ?? string.Empty;
        ApplicationVersion = status.ApplicationVersion;
        AppliedMigrations = status.AppliedMigrations;
        PendingMigrations = status.PendingMigrations;
        Compatibility = _values.StableCode(status.Compatibility);
        Integrity = _values.StableCode(status.IntegrityStatus);
        EncryptionMode = _values.BackupEncryptionMode(status.EncryptionMode);
        WorkspaceIdPrefix = Prefix(status.LiveWorkspaceId);
        DestinationSeparationConfirmed =
            status.DestinationSeparationConfirmed;
        DestinationEncryptionConfirmed =
            status.DestinationEncryptionConfirmed;
        LocalProtectionReady = status.LocalProtectionReady;
        UnrelatedVerifiedBackupCount =
            status.UnrelatedVerifiedBackupCount;

        if (status.LatestVerifiedBackup is { } backup)
        {
            LatestBackup = new BackupDisplay(
                backup.FilePath,
                _values.UtcTimestamp(backup.CreatedAtUtc),
                _values.UtcTimestamp(backup.VerifiedAtUtc),
                backup.DigestPrefix,
                backup.LatestMigration,
                _values.BackupEncryptionMode(backup.EncryptionMode),
                _values.StableCode(backup.WorkspaceBinding));
        }

        return Page();
    }

    private static string? Prefix(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value[..Math.Min(value.Length, WorkspacePrefixLength)];

    public sealed record BackupDisplay(
        string FilePath,
        DisplayValue CreatedAt,
        DisplayValue VerifiedAt,
        string DigestPrefix,
        string LatestMigration,
        DisplayValue EncryptionMode,
        DisplayValue WorkspaceBinding);
}
