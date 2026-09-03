using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

/// <summary>
/// A verified package protects a live database only when it is proved to come
/// from the same workspace. These tests fix that rule against the ways it could
/// silently weaken back into "some valid package exists nearby".
/// </summary>
public sealed class LocalBackupWorkspaceBindingTests : IDisposable
{
    private static readonly DateTimeOffset OperationTime =
        new(2026, 9, 3, 8, 0, 0, TimeSpan.Zero);

    private const string PreWorkspaceIdentityMigration =
        "20260902112549_004_LedgerNavigationQueries";

    private readonly string _testRoot;
    private readonly string _sharedBackupDirectory;

    public LocalBackupWorkspaceBindingTests()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Infrastructure.Tests",
            nameof(LocalBackupWorkspaceBindingTests),
            Guid.NewGuid().ToString("N"));
        _sharedBackupDirectory = Path.Combine(_testRoot, "shared-backups");
    }

    [Fact]
    public async Task Initialize_IndependentWorkspacesReceiveDistinctIdentities()
    {
        var first = CreateWorkspace("workspace-first");
        var second = CreateWorkspace("workspace-second");

        Assert.True((await first.Initializer.InitializeAsync()).Succeeded);
        Assert.True((await second.Initializer.InitializeAsync()).Succeeded);

        var firstIdentity = await ReadWorkspaceIdAsync(first);
        var secondIdentity = await ReadWorkspaceIdAsync(second);

        Assert.True(Guid.TryParseExact(firstIdentity, "D", out var parsedFirst));
        Assert.NotEqual(Guid.Empty, parsedFirst);
        Assert.Equal(firstIdentity!.ToLowerInvariant(), firstIdentity);
        Assert.NotEqual(firstIdentity, secondIdentity);
    }

    [Fact]
    public async Task Status_UnrelatedWorkspacePackageIsNeverProtection()
    {
        var live = CreateWorkspace("workspace-live");
        var other = CreateWorkspace("workspace-other");

        Assert.True((await live.Initializer.InitializeAsync()).Succeeded);
        Assert.True((await other.Initializer.InitializeAsync()).Succeeded);

        var otherBackup = await other.Creator.CreateAsync();
        Assert.True(otherBackup.Succeeded, otherBackup.Failure?.Message);

        var status = await live.Reader.ReadAsync();

        Assert.True(status.Succeeded, status.Failure?.Message);
        Assert.Null(status.Value!.LatestVerifiedBackup);
        Assert.Equal(1, status.Value.UnrelatedVerifiedBackupCount);
        Assert.False(status.Value.LocalProtectionReady);
        Assert.Equal(
            await ReadWorkspaceIdAsync(live),
            status.Value.LiveWorkspaceId);
    }

    [Fact]
    public async Task Status_OwnPackageIsMatchedProtection()
    {
        var live = CreateWorkspace("workspace-live");
        Assert.True((await live.Initializer.InitializeAsync()).Succeeded);

        var created = await live.Creator.CreateAsync();
        Assert.True(created.Succeeded, created.Failure?.Message);

        var status = await live.Reader.ReadAsync();

        Assert.True(status.Succeeded, status.Failure?.Message);
        Assert.NotNull(status.Value!.LatestVerifiedBackup);
        Assert.Equal(
            created.Value!.FilePath,
            status.Value.LatestVerifiedBackup!.FilePath);
        Assert.Equal(
            LocalBackupWorkspaceBinding.Matched,
            status.Value.LatestVerifiedBackup.WorkspaceBinding);
        Assert.Equal(0, status.Value.UnrelatedVerifiedBackupCount);
        Assert.True(status.Value.LocalProtectionReady);
    }

    [Fact]
    public async Task Status_NewerUnrelatedPackageDoesNotDisplaceOlderMatchingOne()
    {
        var live = CreateWorkspace(
            "workspace-live",
            new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero));
        var other = CreateWorkspace(
            "workspace-other",
            new DateTimeOffset(2026, 9, 4, 8, 0, 0, TimeSpan.Zero));

        Assert.True((await live.Initializer.InitializeAsync()).Succeeded);
        Assert.True((await other.Initializer.InitializeAsync()).Succeeded);

        var matching = await live.Creator.CreateAsync();
        Assert.True(matching.Succeeded, matching.Failure?.Message);
        var newerUnrelated = await other.Creator.CreateAsync();
        Assert.True(newerUnrelated.Succeeded, newerUnrelated.Failure?.Message);
        Assert.True(
            newerUnrelated.Value!.CreatedAtUtc > matching.Value!.CreatedAtUtc);

        var status = await live.Reader.ReadAsync();

        Assert.True(status.Succeeded, status.Failure?.Message);
        Assert.Equal(
            matching.Value.FilePath,
            status.Value!.LatestVerifiedBackup!.FilePath);
        Assert.Equal(1, status.Value.UnrelatedVerifiedBackupCount);
        Assert.True(status.Value.LocalProtectionReady);
    }

    [Fact]
    public async Task Verify_ForgedManifestIdentityIsRejected()
    {
        var live = CreateWorkspace("workspace-live");
        var other = CreateWorkspace("workspace-other");

        Assert.True((await live.Initializer.InitializeAsync()).Succeeded);
        Assert.True((await other.Initializer.InitializeAsync()).Succeeded);

        var otherBackup = await other.Creator.CreateAsync();
        Assert.True(otherBackup.Succeeded, otherBackup.Failure?.Message);

        // The manifest is outside the snapshot digest, so a plain text edit
        // can claim any lineage. The snapshot cross-check must catch it.
        var forged = await RewriteManifestWorkspaceIdAsync(
            otherBackup.Value!.FilePath,
            await ReadWorkspaceIdAsync(live));

        var verified = await live.BackupVerifier.VerifyAsync(forged);

        Assert.False(verified.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidBackup,
            verified.Failure!.Category);

        var status = await live.Reader.ReadAsync();

        Assert.True(status.Succeeded, status.Failure?.Message);
        Assert.Null(status.Value!.LatestVerifiedBackup);
        Assert.False(status.Value.LocalProtectionReady);
    }

    [Fact]
    public async Task Verify_StrippedManifestIdentityIsRejected()
    {
        var live = CreateWorkspace("workspace-live");
        Assert.True((await live.Initializer.InitializeAsync()).Succeeded);

        var created = await live.Creator.CreateAsync();
        Assert.True(created.Succeeded, created.Failure?.Message);

        var stripped = await RewriteManifestWorkspaceIdAsync(
            created.Value!.FilePath,
            null);

        var verified = await live.BackupVerifier.VerifyAsync(stripped);

        Assert.False(verified.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidBackup,
            verified.Failure!.Category);
    }

    [Fact]
    public async Task Verify_PackagePredatingLineageRemainsValidButUnknown()
    {
        // A package produced before the identity migration existed carries no
        // lineage on either side. It must still verify, and must still never
        // count as protection.
        //
        // Ordinary `backup create` refuses a database that needs migrating, so
        // such a package can only arise through the pre-migration path.
        var legacy = CreateWorkspace(
            "workspace-legacy",
            targetMigration: PreWorkspaceIdentityMigration);
        var refused = await legacy.Creator.CreateAsync();

        Assert.False(refused.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.DatabaseNotReady,
            refused.Failure!.Category);

        var created = await legacy.BackupService.CreateVerifiedBackupAsync(
            legacy.DatabasePath,
            Path.GetFullPath(_sharedBackupDirectory),
            LocalBackupPurpose.PreMigration,
            allowMigrationRequired: true);

        Assert.True(created.Succeeded, created.Failure?.Message);

        var verified = await legacy.BackupVerifier.VerifyAsync(
            created.Value!.FilePath);

        Assert.True(verified.Succeeded, verified.Failure?.Message);

        var parts = await LocalBackupTestHarness.ReadPackagePartsAsync(
            created.Value.FilePath);
        var manifest = LocalBackupTestHarness.ReadManifest(parts.Manifest);

        Assert.Equal(
            WealthLedgerBackupManifest.CurrentFormatVersion,
            manifest.FormatVersion);
        Assert.Null(manifest.SourceWorkspaceId);
        Assert.Equal(
            LocalBackupWorkspaceBinding.Unknown,
            SqliteLocalDataStatusReader.DetermineBinding(
                liveWorkspaceId: null,
                packageWorkspaceId: null));
    }

    [Fact]
    public async Task Status_MigratedDatabaseDoesNotAcceptItsPreMigrationPackage()
    {
        // Upgrading assigns a fresh identity. The pre-migration package still
        // exists and is still recoverable, but it cannot prove lineage, so the
        // operator must take one new backup.
        var workspace = CreateWorkspace(
            "workspace-upgraded",
            targetMigration: PreWorkspaceIdentityMigration);
        var migration = await new MigrateLocalDatabaseUseCase(
                workspace.CreateMigrationSessionFactory(),
                new FixedTimeProvider(OperationTime))
            .ExecuteAsync();

        Assert.True(migration.Succeeded, migration.Failure?.Message);
        Assert.False(migration.Value!.WasNoOp);
        Assert.True(File.Exists(migration.Value.PreMigrationBackupPath));

        var status = await workspace.Reader.ReadAsync();

        Assert.True(status.Succeeded, status.Failure?.Message);
        Assert.NotNull(status.Value!.LiveWorkspaceId);
        Assert.Null(status.Value.LatestVerifiedBackup);
        Assert.Equal(1, status.Value.UnrelatedVerifiedBackupCount);
        Assert.False(status.Value.LocalProtectionReady);

        var afterNewBackup = await workspace.Creator.CreateAsync();
        Assert.True(afterNewBackup.Succeeded, afterNewBackup.Failure?.Message);

        var recovered = await workspace.Reader.ReadAsync();

        Assert.True(recovered.Value!.LocalProtectionReady);
        Assert.Equal(
            LocalBackupWorkspaceBinding.Matched,
            recovered.Value.LatestVerifiedBackup!.WorkspaceBinding);
    }

    [Fact]
    public async Task Restore_StagedTargetKeepsPackageLineageAcrossRestart()
    {
        var live = CreateWorkspace("workspace-live");
        Assert.True((await live.Initializer.InitializeAsync()).Succeeded);

        var liveIdentity = await ReadWorkspaceIdAsync(live);
        var created = await live.Creator.CreateAsync();
        Assert.True(created.Succeeded, created.Failure?.Message);

        var restoreTarget = Path.Combine(
            _testRoot,
            "restore-drill",
            "restored.db");
        var staged = await live.RestoreStager.StageAsync(
            created.Value!.FilePath,
            restoreTarget);

        Assert.True(staged.Succeeded, staged.Failure?.Message);

        // A fresh verifier stands in for a restarted process.
        var restarted = await new SqliteDatabaseVerifier().VerifyAsync(
            restoreTarget);

        Assert.True(restarted.Succeeded, restarted.Failure?.Message);
        Assert.Equal(liveIdentity, restarted.Value!.WorkspaceId);
    }

    [Fact]
    public async Task Replace_ActivePromotionRebindsToThePromotedLineage()
    {
        var live = CreateWorkspace("workspace-live");
        var donor = CreateWorkspace("workspace-donor");

        Assert.True((await live.Initializer.InitializeAsync()).Succeeded);
        Assert.True((await donor.Initializer.InitializeAsync()).Succeeded);

        var liveIdentity = await ReadWorkspaceIdAsync(live);
        var donorIdentity = await ReadWorkspaceIdAsync(donor);
        var donorBackup = await donor.Creator.CreateAsync();
        Assert.True(donorBackup.Succeeded, donorBackup.Failure?.Message);

        var replacement = await new ReplaceLocalDatabaseUseCase(
                live.CreateReplacementSessionFactory())
            .ExecuteAsync(
                donorBackup.Value!.FilePath,
                confirmReplaceActive: true);

        Assert.True(replacement.Succeeded, replacement.Failure?.Message);

        var status = await live.Reader.ReadAsync();

        Assert.True(status.Succeeded, status.Failure?.Message);
        Assert.Equal(donorIdentity, status.Value!.LiveWorkspaceId);
        Assert.NotEqual(liveIdentity, status.Value.LiveWorkspaceId);

        // The promoted package now matches; the pre-restore package of the
        // superseded lineage correctly stops counting as protection.
        Assert.Equal(
            donorBackup.Value.FilePath,
            status.Value.LatestVerifiedBackup!.FilePath);
        Assert.Equal(
            LocalBackupWorkspaceBinding.Matched,
            status.Value.LatestVerifiedBackup.WorkspaceBinding);
        Assert.Equal(1, status.Value.UnrelatedVerifiedBackupCount);
    }

    [Fact]
    public async Task Identity_SurvivesRepeatedVerificationAndIsSingleRow()
    {
        var workspace = CreateWorkspace("workspace-live");
        Assert.True((await workspace.Initializer.InitializeAsync()).Succeeded);

        var first = await ReadWorkspaceIdAsync(workspace);
        _ = await workspace.Reader.ReadAsync();
        var second = await ReadWorkspaceIdAsync(workspace);

        Assert.Equal(first, second);

        await using var connection =
            SqliteLocalDataConnectionFactory.CreateConnection(
                workspace.DatabasePath,
                SqliteOpenMode.ReadOnly);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM WorkspaceIdentity;";

        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    private Workspace CreateWorkspace(
        string name,
        DateTimeOffset? operationTime = null,
        string? targetMigration = null)
    {
        var databasePath = Path.Combine(
            _testRoot,
            name,
            "live",
            "wealthledger.db");
        var configuration = new Dictionary<string, string?>
        {
            [LocalDataPathResolver.DatabasePathConfigurationKey] =
                databasePath,
            [LocalDataPathResolver.BackupDirectoryConfigurationKey] =
                _sharedBackupDirectory,
            [LocalDataPathResolver
                .DestinationSeparationConfigurationKey] = "true",
            [LocalDataPathResolver
                .DestinationEncryptionConfigurationKey] = "true"
        };
        var resolver = new LocalDataPathResolver(
            configuration,
            new LocalDataPathEnvironment(
                "Testing",
                FindRepositoryRoot(),
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Path.Combine(_testRoot, name, "local-app-data"),
                Path.Combine(_testRoot, name, "profile")));
        var ownershipGuard = new LocalDatabaseOwnershipGuard(resolver);
        var verifier = new SqliteDatabaseVerifier();
        var packageReader = new LocalBackupPackageReader(verifier);
        var timeProvider = new FixedTimeProvider(
            operationTime ?? OperationTime);
        var backupService = new SqliteBackupService(
            resolver,
            verifier,
            packageReader,
            timeProvider);
        var restoreService = new SqliteRestoreService(
            packageReader,
            verifier,
            timeProvider);
        var workspace = new Workspace(
            databasePath,
            resolver,
            ownershipGuard,
            verifier,
            packageReader,
            backupService,
            restoreService,
            timeProvider,
            new SqliteLocalBackupVerifier(resolver, packageReader),
            new SqliteLocalDatabaseInitializer(
                resolver,
                ownershipGuard,
                verifier,
                timeProvider),
            new SqliteLocalBackupCreator(
                resolver,
                ownershipGuard,
                backupService),
            new SqliteLocalDataStatusReader(
                resolver,
                ownershipGuard,
                verifier,
                packageReader),
            new SqliteLocalRestoreStager(resolver, restoreService));

        if (targetMigration is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            using var context =
                SqliteLocalDataConnectionFactory.CreateContext(
                    databasePath,
                    SqliteOpenMode.ReadWriteCreate);
            Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions
                .Migrate(context.Database, targetMigration);
        }

        return workspace;
    }

    private static async Task<string?> ReadWorkspaceIdAsync(
        Workspace workspace)
    {
        var verification = await workspace.Verifier.VerifyAsync(
            workspace.DatabasePath);

        Assert.True(verification.Succeeded, verification.Failure?.Message);

        return verification.Value!.WorkspaceId;
    }

    private static async Task<string> RewriteManifestWorkspaceIdAsync(
        string packagePath,
        string? workspaceId)
    {
        var rewritten = Path.Combine(
            Path.GetDirectoryName(packagePath)!,
            "rewritten-" + Path.GetFileName(packagePath));
        File.Copy(packagePath, rewritten);

        using var archive = ZipFile.Open(rewritten, ZipArchiveMode.Update);
        var manifestEntry = archive.GetEntry(
            LocalBackupPackageReader.ManifestEntryName)!;
        string original;

        await using (var read = manifestEntry.Open())
        using (var reader = new StreamReader(read, Encoding.UTF8))
        {
            original = await reader.ReadToEndAsync();
        }

        var manifest = LocalBackupTestHarness.ReadManifest(
            Encoding.UTF8.GetBytes(original)) with
        {
            SourceWorkspaceId = workspaceId
        };

        await using (var write = manifestEntry.Open())
        {
            write.SetLength(0);
            var payload = LocalBackupTestHarness.SerializeManifest(manifest);
            await write.WriteAsync(payload);
        }

        return rewritten;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(
                    Path.Combine(current.FullName, "WealthLedger.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("The repository root was not found.");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private sealed record Workspace(
        string DatabasePath,
        LocalDataPathResolver Resolver,
        LocalDatabaseOwnershipGuard OwnershipGuard,
        SqliteDatabaseVerifier Verifier,
        LocalBackupPackageReader PackageReader,
        SqliteBackupService BackupService,
        SqliteRestoreService RestoreService,
        TimeProvider TimeProvider,
        SqliteLocalBackupVerifier BackupVerifier,
        SqliteLocalDatabaseInitializer Initializer,
        SqliteLocalBackupCreator Creator,
        SqliteLocalDataStatusReader Reader,
        SqliteLocalRestoreStager RestoreStager)
    {
        internal SqliteLocalDatabaseMigrationSessionFactory
            CreateMigrationSessionFactory()
            => new(
                Resolver,
                OwnershipGuard,
                BackupService,
                PackageReader,
                Verifier,
                TimeProvider,
                NoOpLocalDataOperationHooks.Instance);

        internal SqliteLocalDatabaseReplacementSessionFactory
            CreateReplacementSessionFactory()
            => new(
                Resolver,
                OwnershipGuard,
                RestoreService,
                BackupService,
                PackageReader,
                Verifier,
                TimeProvider,
                NoOpLocalDataOperationHooks.Instance);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        internal FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
