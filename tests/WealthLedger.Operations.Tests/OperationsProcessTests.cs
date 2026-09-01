using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.Persistence;

namespace WealthLedger.Operations.Tests;

public sealed class OperationsProcessTests : IDisposable
{
    private readonly string _allowedRoot;
    private readonly string _testRoot;
    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public OperationsProcessTests()
    {
        _allowedRoot = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Operations.Tests");
        _testRoot = Path.Combine(
            _allowedRoot,
            Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(
            _testRoot,
            "live",
            "wealthledger.db");
        _backupDirectory = Path.Combine(_testRoot, "backups");
    }

    [Fact]
    public async Task OperationsCli_InitializeStatusBackupVerifyStageAndRestart()
    {
        var initialize = await RunAsync("database", "initialize");
        AssertSuccess(initialize, "SUCCESS DATABASE_INITIALIZE");
        Assert.True(File.Exists(_databasePath));

        var initialStatus = await RunAsync("status");
        AssertSuccess(initialStatus, "SUCCESS STATUS");
        Assert.Contains(
            "LatestBackupFile: NONE",
            initialStatus.StandardOutput);
        Assert.Contains(
            "WARNING LOCAL_PROTECTION_NOT_READY",
            initialStatus.StandardOutput);

        var backup = await RunAsync("backup", "create");
        AssertSuccess(backup, "SUCCESS BACKUP_CREATE");
        var backupFile = ReadOutputPath(backup, "BackupFile");
        Assert.True(File.Exists(backupFile));

        var verify = await RunAsync(
            "backup",
            "verify",
            "--file",
            backupFile);
        AssertSuccess(verify, "SUCCESS BACKUP_VERIFY");
        Assert.Contains("Compatibility: Compatible", verify.StandardOutput);

        var restoreTarget = Path.Combine(
            _testRoot,
            "restore-drill",
            "restored.db");
        var stage = await RunAsync(
            "restore",
            "stage",
            "--file",
            backupFile,
            "--target",
            restoreTarget);
        AssertSuccess(stage, "SUCCESS RESTORE_STAGE");
        Assert.True(File.Exists(restoreTarget));

        var restoredStatus = await RunWithPathsAsync(
            restoreTarget,
            _backupDirectory,
            "status");
        AssertSuccess(restoredStatus, "SUCCESS STATUS");
        Assert.Contains(
            "LocalProtectionReady: true",
            restoredStatus.StandardOutput);

        var restarted = await RunAsync("status");
        AssertSuccess(restarted, "SUCCESS STATUS");
        Assert.Contains(
            "LocalProtectionReady: true",
            restarted.StandardOutput);
        Assert.DoesNotContain("Data Source=", AllOutput(restarted));
        Assert.DoesNotContain("SQLite Error", AllOutput(restarted));
    }

    [Fact]
    public async Task OperationsCli_MigrationCreatesOneVerifiedPreMigrationGeneration()
    {
        await CreateOldSchemaAsync(_databasePath);

        var migrated = await RunAsync("database", "migrate");

        AssertSuccess(migrated, "SUCCESS DATABASE_MIGRATE");
        Assert.Contains(
            "StartingMigration: 20260827072019_002_CommandReceipt",
            migrated.StandardOutput);
        Assert.Contains(
            "EndingMigration: 20260831113310_003_ReversalDependencySemantics",
            migrated.StandardOutput);
        var preMigrationBackup = ReadOutputPath(
            migrated,
            "PreMigrationBackup");
        Assert.True(File.Exists(preMigrationBackup));

        var verified = await RunAsync(
            "backup",
            "verify",
            "--file",
            preMigrationBackup);
        AssertSuccess(verified, "SUCCESS BACKUP_VERIFY");
        Assert.Contains(
            "Compatibility: MigrationRequired",
            verified.StandardOutput);

        var noOp = await RunAsync("database", "migrate");
        AssertSuccess(noOp, "SUCCESS DATABASE_MIGRATE");
        Assert.Contains("WasNoOp: true", noOp.StandardOutput);
        Assert.Single(Directory.GetFiles(
            _backupDirectory,
            "*.wlbackup"));
    }

    [Fact]
    public async Task OperationsCli_ReplacementRequiresConfirmationAndPreservesEvidence()
    {
        var sourceDatabase = Path.Combine(
            _testRoot,
            "source",
            "wealthledger.db");
        var sourceBackups = Path.Combine(_testRoot, "source-backups");
        AssertSuccess(
            await RunWithPathsAsync(
                sourceDatabase,
                sourceBackups,
                "database",
                "initialize"),
            "SUCCESS DATABASE_INITIALIZE");
        var sourceBackupResult = await RunWithPathsAsync(
            sourceDatabase,
            sourceBackups,
            "backup",
            "create");
        AssertSuccess(sourceBackupResult, "SUCCESS BACKUP_CREATE");
        var sourceBackup = ReadOutputPath(
            sourceBackupResult,
            "BackupFile");
        AssertSuccess(
            await RunAsync("database", "initialize"),
            "SUCCESS DATABASE_INITIALIZE");

        var refused = await RunAsync(
            "restore",
            "replace",
            "--file",
            sourceBackup);
        Assert.Equal(
            (int)LocalDataFailureCategory.InvalidInputOrConfiguration,
            refused.ExitCode);
        Assert.Contains(
            "FAILURE INVALID_INPUT_OR_CONFIGURATION",
            refused.StandardError);

        var replaced = await RunAsync(
            "restore",
            "replace",
            "--file",
            sourceBackup,
            "--confirm-replace-active");
        AssertSuccess(replaced, "SUCCESS RESTORE_REPLACE");
        var preRestoreBackup = ReadOutputPath(
            replaced,
            "PreRestoreBackup");
        var superseded = ReadOutputPath(
            replaced,
            "SupersededDatabase");
        Assert.True(File.Exists(preRestoreBackup));
        Assert.True(File.Exists(superseded));
        AssertSuccess(await RunAsync("status"), "SUCCESS STATUS");
    }

    [Fact]
    public async Task OperationsCli_InvalidInputsUseStableSanitizedExitCategories()
    {
        var unknown = await RunAsync("unknown-command");
        Assert.Equal(
            (int)LocalDataFailureCategory.InvalidInputOrConfiguration,
            unknown.ExitCode);
        Assert.Contains(
            "FAILURE INVALID_INPUT_OR_CONFIGURATION",
            unknown.StandardError);

        var invalidPackage = Path.Combine(
            _testRoot,
            "SYNTHETIC_PRIVATE_IDENTIFIER.wlbackup");
        Directory.CreateDirectory(_testRoot);
        await File.WriteAllTextAsync(invalidPackage, "not a backup");
        var invalid = await RunAsync(
            "backup",
            "verify",
            "--file",
            invalidPackage);

        Assert.Equal(
            (int)LocalDataFailureCategory.InvalidBackup,
            invalid.ExitCode);
        Assert.Contains("FAILURE INVALID_BACKUP", invalid.StandardError);
        Assert.DoesNotContain(
            "SYNTHETIC_PRIVATE_IDENTIFIER",
            invalid.StandardError);
        Assert.DoesNotContain("Exception", AllOutput(invalid));
        Assert.DoesNotContain(" at ", AllOutput(invalid));
    }

    [Fact]
    public async Task OperationsCli_CrossProcessOwnershipCollisionIsStableAndRetryable()
    {
        AssertSuccess(
            await RunAsync("database", "initialize"),
            "SUCCESS DATABASE_INITIALIZE");
        var lockPath = Path.ChangeExtension(
            _databasePath,
            ".wloperation.lock");
        await using (var ownership = new FileStream(
                         lockPath,
                         FileMode.OpenOrCreate,
                         FileAccess.ReadWrite,
                         FileShare.None,
                         bufferSize: 1,
                         FileOptions.WriteThrough))
        {
            var busy = await RunAsync("backup", "create");

            Assert.Equal(
                (int)LocalDataFailureCategory.OwnershipBusy,
                busy.ExitCode);
            Assert.Contains("FAILURE OWNERSHIP_BUSY", busy.StandardError);
            Assert.False(Directory.Exists(_backupDirectory));
        }

        var retry = await RunAsync("backup", "create");
        AssertSuccess(retry, "SUCCESS BACKUP_CREATE");
        Assert.Single(Directory.GetFiles(
            _backupDirectory,
            "*.wlbackup"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var root = Path.GetFullPath(_testRoot);
        var allowed = Path.GetFullPath(_allowedRoot);

        if (Directory.Exists(root)
            && root.StartsWith(
                allowed + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private Task<ProcessResult> RunAsync(params string[] command)
        => RunWithPathsAsync(
            _databasePath,
            _backupDirectory,
            command);

    private static async Task<ProcessResult> RunWithPathsAsync(
        string databasePath,
        string backupDirectory,
        params string[] command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(databasePath)!
        };
        Directory.CreateDirectory(startInfo.WorkingDirectory);
        startInfo.ArgumentList.Add(FindOperationsAssembly());

        foreach (var token in command)
        {
            startInfo.ArgumentList.Add(token);
        }

        startInfo.ArgumentList.Add(
            "--Storage:DatabasePath=" + databasePath);
        startInfo.ArgumentList.Add("--Backup:Directory=" + backupDirectory);
        startInfo.ArgumentList.Add(
            "--Backup:DestinationSeparationConfirmed=true");
        startInfo.ArgumentList.Add(
            "--Backup:DestinationEncryptionConfirmed=true");
        startInfo.ArgumentList.Add("--Environment=Testing");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "The operations process could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                "The synthetic operations process did not exit in time.");
        }

        return new ProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static string FindOperationsAssembly()
    {
        var path = typeof(OperationsProgram).Assembly.Location;

        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                "The operations assembly was not built.");
    }

    private static async Task CreateOldSchemaAsync(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        var options = new DbContextOptionsBuilder<WealthLedgerDbContext>()
            .UseSqlite(connectionString)
            .Options;
        await using var context = new WealthLedgerDbContext(options);
        await context.Database.MigrateAsync(
            "20260827072019_002_CommandReceipt");
    }

    private static string ReadOutputPath(
        ProcessResult result,
        string label)
    {
        var prefix = label + ": ";
        var line = result.StandardOutput
            .Split(
                ["\r\n", "\n"],
                StringSplitOptions.RemoveEmptyEntries)
            .Single(value => value.StartsWith(
                prefix,
                StringComparison.Ordinal));
        return line[prefix.Length..];
    }

    private static void AssertSuccess(
        ProcessResult result,
        string successMarker)
    {
        Assert.True(
            result.ExitCode == 0,
            $"Exit {result.ExitCode}. OUT: {result.StandardOutput} ERR: {result.StandardError}");
        Assert.Contains(successMarker, result.StandardOutput);
        Assert.True(
            string.IsNullOrWhiteSpace(result.StandardError),
            result.StandardError);
    }

    private static string AllOutput(ProcessResult result)
        => result.StandardOutput + Environment.NewLine + result.StandardError;

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
