using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using WealthLedger.Application.LocalData;
using WealthLedger.Infrastructure.LocalData;

namespace WealthLedger.Infrastructure.Tests.LocalData;

public sealed class LocalBackupPackageSecurityTests
{
    [Fact]
    public async Task BackupPackage_AdditiveManifestFieldIsExplicitlyCompatible()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var created = await harness.CreateBackupAsync();
        var parts = await LocalBackupTestHarness.ReadPackagePartsAsync(
            created.Value!.FilePath);
        var manifest = JsonNode.Parse(parts.Manifest)!.AsObject();
        manifest["futureAdditiveField"] = "ignored-by-format-1";
        var packagePath = Path.Combine(
            harness.RootPath,
            "additive.wlbackup");
        await WriteTwoEntryPackageAsync(
            packagePath,
            parts.Snapshot,
            Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        var hashBefore = await HashFileAsync(packagePath);

        var result = await harness.Verifier.VerifyAsync(packagePath);
        var hashAfter = await HashFileAsync(packagePath);

        Assert.True(result.Succeeded);
        Assert.Equal(hashBefore, hashAfter);
        Assert.Equal(
            LocalDatabaseCompatibility.Compatible,
            result.Value!.Compatibility);
    }

    [Theory]
    [InlineData("traversal", "../database.sqlite")]
    [InlineData("absolute", "C:/outside/database.sqlite")]
    [InlineData("unknown", "unexpected.bin")]
    [InlineData("directory", "database.sqlite/")]
    public async Task BackupPackage_UnsafeOrUnknownEntryIsRejected(
        string fileName,
        string unsafeEntryName)
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var parts = await CreateValidPartsAsync(harness);
        var packagePath = Path.Combine(
            harness.RootPath,
            $"{fileName}.wlbackup");
        await LocalBackupTestHarness.WritePackageAsync(
            packagePath,
            [
                new SyntheticArchiveEntry(
                    LocalBackupPackageReader.ManifestEntryName,
                    parts.Manifest),
                new SyntheticArchiveEntry(
                    unsafeEntryName,
                    parts.Snapshot)
            ]);

        var result = await harness.Verifier.VerifyAsync(packagePath);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidBackup,
            result.Failure!.Category);
        Assert.DoesNotContain(harness.RootPath, result.Failure.Message);
    }

    [Fact]
    public async Task BackupPackage_LinkEntryIsRejectedBeforeExtraction()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var parts = await CreateValidPartsAsync(harness);
        var packagePath = Path.Combine(harness.RootPath, "link.wlbackup");
        var unixSymbolicLinkAttributes = unchecked((int)0xA1FF0000);
        await LocalBackupTestHarness.WritePackageAsync(
            packagePath,
            [
                new SyntheticArchiveEntry(
                    LocalBackupPackageReader.ManifestEntryName,
                    parts.Manifest),
                new SyntheticArchiveEntry(
                    LocalBackupPackageReader.SnapshotEntryName,
                    Encoding.UTF8.GetBytes("outside.sqlite"),
                    ExternalAttributes: unixSymbolicLinkAttributes)
            ]);

        var result = await harness.Verifier.VerifyAsync(packagePath);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidBackup,
            result.Failure!.Category);
    }

    [Fact]
    public async Task BackupPackage_DuplicateAndEntryCountViolationsAreRejected()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var parts = await CreateValidPartsAsync(harness);
        var duplicatePath = Path.Combine(
            harness.RootPath,
            "duplicate.wlbackup");
        await LocalBackupTestHarness.WritePackageAsync(
            duplicatePath,
            [
                new SyntheticArchiveEntry(
                    LocalBackupPackageReader.ManifestEntryName,
                    parts.Manifest),
                new SyntheticArchiveEntry(
                    LocalBackupPackageReader.SnapshotEntryName,
                    parts.Snapshot),
                new SyntheticArchiveEntry(
                    LocalBackupPackageReader.SnapshotEntryName,
                    parts.Snapshot)
            ]);
        var incompletePath = Path.Combine(
            harness.RootPath,
            "incomplete.wlbackup");
        await LocalBackupTestHarness.WritePackageAsync(
            incompletePath,
            [
                new SyntheticArchiveEntry(
                    LocalBackupPackageReader.ManifestEntryName,
                    parts.Manifest)
            ]);

        var duplicate = await harness.Verifier.VerifyAsync(duplicatePath);
        var incomplete = await harness.Verifier.VerifyAsync(incompletePath);

        Assert.Equal(
            LocalDataFailureCategory.InvalidBackup,
            duplicate.Failure!.Category);
        Assert.Equal(
            LocalDataFailureCategory.InvalidBackup,
            incomplete.Failure!.Category);
    }

    [Fact]
    public async Task BackupPackage_SizeAndCompressionLimitsAreEnforced()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var parts = await CreateValidPartsAsync(harness);
        var packagePath = Path.Combine(harness.RootPath, "bounded.wlbackup");
        await WriteTwoEntryPackageAsync(
            packagePath,
            parts.Snapshot,
            parts.Manifest,
            CompressionLevel.SmallestSize);
        var limits = new BackupPackageLimits(
            MaxPackageBytes: new FileInfo(packagePath).Length + 1,
            MaxManifestBytes: parts.Manifest.Length + 1,
            MaxSnapshotBytes: parts.Snapshot.Length - 1,
            MaxEntryCount: 2,
            MaxMigrationCount: 128,
            MaxCompressionRatio: 1);
        var reader = new LocalBackupPackageReader(
            new SqliteDatabaseVerifier(),
            limits);

        var result = await reader.OpenVerifiedAsync(packagePath);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidBackup,
            result.Failure!.Category);
    }

    [Theory]
    [InlineData("unknown-format", LocalDataFailureCategory.IncompatibleBackup)]
    [InlineData("missing-required", LocalDataFailureCategory.InvalidBackup)]
    [InlineData("null-required", LocalDataFailureCategory.InvalidBackup)]
    [InlineData("non-utc", LocalDataFailureCategory.InvalidBackup)]
    [InlineData("migration-order", LocalDataFailureCategory.InvalidBackup)]
    [InlineData("digest-mismatch", LocalDataFailureCategory.InvalidBackup)]
    [InlineData("compatibility-lie", LocalDataFailureCategory.InvalidBackup)]
    public async Task BackupPackage_InvalidManifestIsRejectedWithStableCategory(
        string mutation,
        LocalDataFailureCategory expectedCategory)
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var parts = await CreateValidPartsAsync(harness);
        var manifest = JsonNode.Parse(parts.Manifest)!.AsObject();

        switch (mutation)
        {
            case "unknown-format":
                manifest["formatVersion"] = 2;
                break;
            case "missing-required":
                _ = manifest.Remove("snapshotSha256");
                break;
            case "null-required":
                manifest["snapshotSha256"] = null;
                break;
            case "non-utc":
                manifest["createdAtUtc"] = "2026-09-01T13:15:30+03:00";
                break;
            case "migration-order":
                var migrations = manifest["appliedMigrations"]!
                    .AsArray()
                    .Select(node => node!.GetValue<string>())
                    .Reverse()
                    .ToArray();
                manifest["appliedMigrations"] = new JsonArray(
                    migrations
                        .Select(value => (JsonNode?)JsonValue.Create(value))
                        .ToArray());
                manifest["latestSchemaVersion"] = migrations[^1];
                break;
            case "digest-mismatch":
                manifest["snapshotSha256"] = new string('0', 64);
                break;
            case "compatibility-lie":
                manifest["compatibilityCheckOutcome"] =
                    WealthLedgerBackupManifest.MigrationRequiredOutcome;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        var packagePath = Path.Combine(
            harness.RootPath,
            $"{mutation}.wlbackup");
        await WriteTwoEntryPackageAsync(
            packagePath,
            parts.Snapshot,
            Encoding.UTF8.GetBytes(manifest.ToJsonString()));

        var result = await harness.Verifier.VerifyAsync(packagePath);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCategory, result.Failure!.Category);
        Assert.DoesNotContain("SQLite Error", result.Failure.Message);
    }

    [Fact]
    public async Task BackupPackage_TruncatedSnapshotWithMatchingDigestFailsIntegrity()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var parts = await CreateValidPartsAsync(harness);
        var truncated = Encoding.UTF8.GetBytes(
            "synthetic bytes that are not a sqlite database");
        var manifest = LocalBackupTestHarness.ReadManifest(parts.Manifest)
            with
        {
            SnapshotSha256 =
                    LocalBackupTestHarness.ComputeSha256(truncated)
        };
        var packagePath = Path.Combine(
            harness.RootPath,
            "truncated.wlbackup");
        await WriteTwoEntryPackageAsync(
            packagePath,
            truncated,
            LocalBackupTestHarness.SerializeManifest(manifest));

        var result = await harness.Verifier.VerifyAsync(packagePath);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.IntegrityFailure,
            result.Failure!.Category);
    }

    [Fact]
    public async Task BackupPackage_FutureSchemaIsRejectedWithoutMutation()
    {
        await using var harness = await LocalBackupTestHarness.CreateAsync();
        var parts = await CreateValidPartsAsync(harness);
        var futureDatabasePath = Path.Combine(
            harness.RootPath,
            "future-source.wlrestore");
        await File.WriteAllBytesAsync(futureDatabasePath, parts.Snapshot);
        await using (var connection =
                     SqliteLocalDataConnectionFactory.CreateConnection(
                         futureDatabasePath,
                         SqliteOpenMode.ReadWrite))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
                VALUES ('99999999999999_999_FutureSchema', '99.0.0');
                """;
            _ = await command.ExecuteNonQueryAsync();
        }

        var futureSnapshot = await File.ReadAllBytesAsync(futureDatabasePath);
        var manifest = LocalBackupTestHarness.ReadManifest(parts.Manifest)
            with
        {
            AppliedMigrations =
                [
                    .. LocalBackupTestHarness.ReadManifest(parts.Manifest)
                        .AppliedMigrations,
                    "99999999999999_999_FutureSchema"
                ],
            LatestSchemaVersion = "99999999999999_999_FutureSchema",
            SnapshotSha256 =
                    LocalBackupTestHarness.ComputeSha256(futureSnapshot)
        };
        var packagePath = Path.Combine(
            harness.RootPath,
            "future.wlbackup");
        await WriteTwoEntryPackageAsync(
            packagePath,
            futureSnapshot,
            LocalBackupTestHarness.SerializeManifest(manifest));
        var hashBefore = await HashFileAsync(packagePath);

        var result = await harness.Verifier.VerifyAsync(packagePath);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.IncompatibleBackup,
            result.Failure!.Category);
        Assert.Equal(hashBefore, await HashFileAsync(packagePath));
    }

    private static async Task<PackageParts> CreateValidPartsAsync(
        LocalBackupTestHarness harness)
    {
        var created = await harness.CreateBackupAsync();
        Assert.True(created.Succeeded);
        return await LocalBackupTestHarness.ReadPackagePartsAsync(
            created.Value!.FilePath);
    }

    private static Task WriteTwoEntryPackageAsync(
        string packagePath,
        byte[] snapshot,
        byte[] manifest,
        CompressionLevel snapshotCompression = CompressionLevel.NoCompression)
        => LocalBackupTestHarness.WritePackageAsync(
            packagePath,
            [
                new SyntheticArchiveEntry(
                    LocalBackupPackageReader.SnapshotEntryName,
                    snapshot,
                    snapshotCompression),
                new SyntheticArchiveEntry(
                    LocalBackupPackageReader.ManifestEntryName,
                    manifest,
                    CompressionLevel.Optimal)
            ]);

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
}
