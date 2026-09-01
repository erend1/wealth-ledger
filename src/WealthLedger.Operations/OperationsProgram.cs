using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Operations;

internal static class OperationsProgram
{
    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        var parseResult = OperationsCommandParser.Parse(arguments);

        if (!parseResult.Succeeded)
        {
            WriteFailure(standardError, parseResult.Failure!);
            WriteUsage(standardError);
            return (int)parseResult.Failure!.Category;
        }

        var command = parseResult.Command!;

        if (command.Kind == OperationsCommandKind.Help)
        {
            WriteUsage(standardOutput);
            return 0;
        }

        try
        {
            var configuration = BuildConfiguration(
                command.ConfigurationOverrides);
            await using var services = BuildServices(configuration);

            return command.Kind switch
            {
                OperationsCommandKind.Status => await RunStatusAsync(
                    services,
                    standardOutput,
                    standardError,
                    cancellationToken),
                OperationsCommandKind.DatabaseInitialize =>
                    await RunInitializeAsync(
                        services,
                        standardOutput,
                        standardError,
                        cancellationToken),
                OperationsCommandKind.DatabaseMigrate => await RunMigrateAsync(
                    services,
                    standardOutput,
                    standardError,
                    cancellationToken),
                OperationsCommandKind.BackupCreate => await RunBackupCreateAsync(
                    services,
                    standardOutput,
                    standardError,
                    cancellationToken),
                OperationsCommandKind.BackupVerify => await RunBackupVerifyAsync(
                    services,
                    command.FilePath!,
                    standardOutput,
                    standardError,
                    cancellationToken),
                OperationsCommandKind.RestoreStage => await RunRestoreStageAsync(
                    services,
                    command.FilePath!,
                    command.TargetPath!,
                    standardOutput,
                    standardError,
                    cancellationToken),
                OperationsCommandKind.RestoreReplace =>
                    await RunRestoreReplaceAsync(
                        services,
                        command.FilePath!,
                        command.ConfirmReplaceActive,
                        standardOutput,
                        standardError,
                        cancellationToken),
                _ => throw new InvalidOperationException(
                    "The parsed operations command is unsupported.")
            };
        }
        catch (OperationCanceledException)
        {
            var failure = new LocalDataFailure(
                LocalDataFailureCategory.Cancelled,
                "The local data operation was cancelled.");
            WriteFailure(standardError, failure);
            return (int)failure.Category;
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException
                  or System.Security.SecurityException)
        {
            var failure = new LocalDataFailure(
                LocalDataFailureCategory.IoFailure,
                "The operations command could not read its local configuration.");
            WriteFailure(standardError, failure);
            return (int)failure.Category;
        }
        catch (Exception)
        {
            var failure = new LocalDataFailure(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                "The operations command could not be configured safely.");
            WriteFailure(standardError, failure);
            return (int)failure.Category;
        }
    }

    private static IConfiguration BuildConfiguration(
        IReadOnlyDictionary<string, string?> overrides)
        => new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables("WEALTHLEDGER_")
            .AddInMemoryCollection(overrides)
            .Build();

    private static ServiceProvider BuildServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddWealthLedgerInfrastructure(
            configuration,
            new LocalDataRuntimeContext(
                configuration["Environment"] ?? "Production",
                AppContext.BaseDirectory));
        services.AddSingleton<GetLocalDataStatusUseCase>();
        services.AddSingleton<InitializeLocalDatabaseUseCase>();
        services.AddSingleton<MigrateLocalDatabaseUseCase>();
        services.AddSingleton<CreateLocalBackupUseCase>();
        services.AddSingleton<VerifyLocalBackupUseCase>();
        services.AddSingleton<StageLocalRestoreUseCase>();
        services.AddSingleton<ReplaceLocalDatabaseUseCase>();

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
    }

    private static async Task<int> RunStatusAsync(
        IServiceProvider services,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await services
            .GetRequiredService<GetLocalDataStatusUseCase>()
            .ExecuteAsync(cancellationToken);

        if (result.Value is not null)
        {
            WriteStatus(output, result.Value);
        }

        if (!result.Succeeded)
        {
            WriteFailure(error, result.Failure!);
            return (int)result.Failure!.Category;
        }

        if (!result.Value!.LocalProtectionReady)
        {
            output.WriteLine(
                "WARNING LOCAL_PROTECTION_NOT_READY: Confirm separated encrypted backup protection and create a verified generation before real-data use.");
        }

        output.WriteLine("SUCCESS STATUS");
        return 0;
    }

    private static async Task<int> RunInitializeAsync(
        IServiceProvider services,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await services
            .GetRequiredService<InitializeLocalDatabaseUseCase>()
            .ExecuteAsync(cancellationToken);

        if (!result.Succeeded)
        {
            WriteFailure(error, result.Failure!);
            return (int)result.Failure!.Category;
        }

        output.WriteLine($"DatabasePath: {result.Value!.DatabasePath}");
        output.WriteLine(
            $"AppliedMigrationCount: {result.Value.AppliedMigrations.Count}");
        output.WriteLine(
            $"LatestMigration: {result.Value.AppliedMigrations.LastOrDefault() ?? "NONE"}");
        output.WriteLine(
            $"CompletedAtUtc: {FormatUtc(result.Value.CompletedAtUtc)}");
        output.WriteLine("SUCCESS DATABASE_INITIALIZE");
        return 0;
    }

    private static async Task<int> RunMigrateAsync(
        IServiceProvider services,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await services
            .GetRequiredService<MigrateLocalDatabaseUseCase>()
            .ExecuteAsync(cancellationToken);

        if (result.Value is not null)
        {
            output.WriteLine($"DatabasePath: {result.Value.DatabasePath}");
            output.WriteLine(
                $"StartingMigration: {result.Value.StartingMigration ?? "NONE"}");
            output.WriteLine(
                $"EndingMigration: {result.Value.EndingMigration ?? "NONE"}");
            output.WriteLine(
                $"PreMigrationBackup: {result.Value.PreMigrationBackupPath ?? "NONE"}");
            output.WriteLine(
                $"WasNoOp: {FormatBoolean(result.Value.WasNoOp)}");
        }

        if (!result.Succeeded)
        {
            WriteFailure(error, result.Failure!);
            return (int)result.Failure!.Category;
        }

        output.WriteLine("SUCCESS DATABASE_MIGRATE");
        return 0;
    }

    private static async Task<int> RunBackupCreateAsync(
        IServiceProvider services,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await services
            .GetRequiredService<CreateLocalBackupUseCase>()
            .ExecuteAsync(cancellationToken);

        if (!result.Succeeded)
        {
            WriteFailure(error, result.Failure!);
            return (int)result.Failure!.Category;
        }

        WriteBackupCreation(output, result.Value!);
        output.WriteLine("SUCCESS BACKUP_CREATE");
        return 0;
    }

    private static async Task<int> RunBackupVerifyAsync(
        IServiceProvider services,
        string filePath,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await services
            .GetRequiredService<VerifyLocalBackupUseCase>()
            .ExecuteAsync(filePath, cancellationToken);

        if (!result.Succeeded)
        {
            WriteFailure(error, result.Failure!);
            return (int)result.Failure!.Category;
        }

        var verification = result.Value!;
        output.WriteLine($"BackupFile: {verification.FilePath}");
        output.WriteLine(
            $"CreatedAtUtc: {FormatUtc(verification.CreatedAtUtc)}");
        output.WriteLine(
            $"VerifiedAtUtc: {FormatUtc(verification.VerifiedAtUtc)}");
        output.WriteLine($"DigestPrefix: {verification.DigestPrefix}");
        output.WriteLine($"LatestMigration: {verification.LatestMigration}");
        output.WriteLine($"Compatibility: {verification.Compatibility}");
        output.WriteLine($"EncryptionMode: {verification.EncryptionMode}");
        output.WriteLine("SUCCESS BACKUP_VERIFY");
        return 0;
    }

    private static async Task<int> RunRestoreStageAsync(
        IServiceProvider services,
        string filePath,
        string targetPath,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await services
            .GetRequiredService<StageLocalRestoreUseCase>()
            .ExecuteAsync(filePath, targetPath, cancellationToken);

        if (!result.Succeeded)
        {
            WriteFailure(error, result.Failure!);
            return (int)result.Failure!.Category;
        }

        output.WriteLine($"BackupFile: {result.Value!.BackupFilePath}");
        output.WriteLine(
            $"TargetDatabasePath: {result.Value.TargetDatabasePath}");
        output.WriteLine($"Compatibility: {result.Value.Compatibility}");
        output.WriteLine($"LatestMigration: {result.Value.LatestMigration}");
        output.WriteLine(
            $"CompletedAtUtc: {FormatUtc(result.Value.CompletedAtUtc)}");
        output.WriteLine("SUCCESS RESTORE_STAGE");
        return 0;
    }

    private static async Task<int> RunRestoreReplaceAsync(
        IServiceProvider services,
        string filePath,
        bool confirmed,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var result = await services
            .GetRequiredService<ReplaceLocalDatabaseUseCase>()
            .ExecuteAsync(filePath, confirmed, cancellationToken);

        if (!result.Succeeded)
        {
            WriteFailure(error, result.Failure!);
            return (int)result.Failure!.Category;
        }

        output.WriteLine($"DatabasePath: {result.Value!.DatabasePath}");
        output.WriteLine(
            $"PreRestoreBackup: {result.Value.PreRestoreBackupPath}");
        output.WriteLine(
            $"SupersededDatabase: {result.Value.SupersededDatabasePath}");
        output.WriteLine($"LatestMigration: {result.Value.LatestMigration}");
        output.WriteLine(
            $"CompletedAtUtc: {FormatUtc(result.Value.CompletedAtUtc)}");
        output.WriteLine("SUCCESS RESTORE_REPLACE");
        return 0;
    }

    private static void WriteStatus(TextWriter output, LocalDataStatus status)
    {
        output.WriteLine($"DatabasePath: {status.DatabasePath}");
        output.WriteLine(
            $"BackupDirectory: {status.BackupDirectory ?? "NOT_CONFIGURED"}");
        output.WriteLine(
            $"ApplicationVersion: {status.ApplicationVersion}");
        output.WriteLine(
            $"DatabasePathSafe: {FormatBoolean(status.DatabasePathSafe)}");
        output.WriteLine(
            $"DatabaseExists: {FormatBoolean(status.DatabaseExists)}");
        output.WriteLine(
            $"BackupDirectoryExists: {FormatBoolean(status.BackupDirectoryExists)}");
        output.WriteLine(
            $"OwnershipAvailable: {FormatBoolean(status.OwnershipAvailable)}");
        output.WriteLine(
            $"AppliedMigrationCount: {status.AppliedMigrations.Count}");
        output.WriteLine(
            $"PendingMigrationCount: {status.PendingMigrations.Count}");
        output.WriteLine($"Compatibility: {status.Compatibility}");
        output.WriteLine($"IntegrityStatus: {status.IntegrityStatus}");
        output.WriteLine(
            $"DestinationSeparationConfirmed: {FormatBoolean(status.DestinationSeparationConfirmed)}");
        output.WriteLine(
            $"DestinationEncryptionConfirmed: {FormatBoolean(status.DestinationEncryptionConfirmed)}");
        output.WriteLine(
            $"LocalProtectionReady: {FormatBoolean(status.LocalProtectionReady)}");
        output.WriteLine($"EncryptionMode: {status.EncryptionMode}");

        if (status.LatestVerifiedBackup is not null)
        {
            output.WriteLine(
                $"LatestBackupFile: {status.LatestVerifiedBackup.FilePath}");
            output.WriteLine(
                $"LatestBackupCreatedAtUtc: {FormatUtc(status.LatestVerifiedBackup.CreatedAtUtc)}");
            output.WriteLine(
                $"LatestBackupDigestPrefix: {status.LatestVerifiedBackup.DigestPrefix}");
        }
        else
        {
            output.WriteLine("LatestBackupFile: NONE");
        }
    }

    private static void WriteBackupCreation(
        TextWriter output,
        LocalBackupCreation backup)
    {
        output.WriteLine($"BackupFile: {backup.FilePath}");
        output.WriteLine($"CreatedAtUtc: {FormatUtc(backup.CreatedAtUtc)}");
        output.WriteLine($"VerifiedAtUtc: {FormatUtc(backup.VerifiedAtUtc)}");
        output.WriteLine($"DigestPrefix: {backup.DigestPrefix}");
        output.WriteLine($"LatestMigration: {backup.LatestMigration}");
        output.WriteLine($"EncryptionMode: {backup.EncryptionMode}");
    }

    private static void WriteFailure(
        TextWriter error,
        LocalDataFailure failure)
        => error.WriteLine(
            $"FAILURE {GetCategoryCode(failure.Category)}: {failure.Message}");

    private static string GetCategoryCode(LocalDataFailureCategory category)
        => category switch
        {
            LocalDataFailureCategory.InvalidInputOrConfiguration =>
                "INVALID_INPUT_OR_CONFIGURATION",
            LocalDataFailureCategory.UnsafePath => "UNSAFE_PATH",
            LocalDataFailureCategory.OwnershipBusy => "OWNERSHIP_BUSY",
            LocalDataFailureCategory.NotFound => "NOT_FOUND",
            LocalDataFailureCategory.AlreadyExists => "ALREADY_EXISTS",
            LocalDataFailureCategory.InvalidBackup => "INVALID_BACKUP",
            LocalDataFailureCategory.IncompatibleBackup =>
                "INCOMPATIBLE_BACKUP",
            LocalDataFailureCategory.IntegrityFailure => "INTEGRITY_FAILURE",
            LocalDataFailureCategory.IoFailure => "IO_FAILURE",
            LocalDataFailureCategory.MigrationFailure => "MIGRATION_FAILURE",
            LocalDataFailureCategory.RestoreFailure => "RESTORE_FAILURE",
            LocalDataFailureCategory.Cancelled => "CANCELLED",
            LocalDataFailureCategory.DatabaseNotReady => "DATABASE_NOT_READY",
            _ => "UNKNOWN_FAILURE"
        };

    private static string FormatUtc(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string FormatBoolean(bool value)
        => value ? "true" : "false";

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("WealthLedger local data operations");
        writer.WriteLine("  status");
        writer.WriteLine("  database initialize");
        writer.WriteLine("  database migrate");
        writer.WriteLine("  backup create");
        writer.WriteLine("  backup verify --file <absolute-path>");
        writer.WriteLine(
            "  restore stage --file <absolute-path> --target <absolute-path>");
        writer.WriteLine(
            "  restore replace --file <absolute-path> --confirm-replace-active");
    }
}
