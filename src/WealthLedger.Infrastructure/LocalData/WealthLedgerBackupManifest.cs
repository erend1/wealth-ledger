using System.Text.Json;
using System.Text.Json.Serialization;

namespace WealthLedger.Infrastructure.LocalData;

internal sealed record WealthLedgerBackupManifest
{
    internal const int CurrentFormatVersion = 1;
    internal const string PlaintextEncryptionMode = "PLAINTEXT";
    internal const string PassedOutcome = "PASSED";
    internal const string CompatibleOutcome = "COMPATIBLE";
    internal const string MigrationRequiredOutcome = "MIGRATION_REQUIRED";
    internal const string VerifiedStatus = "VERIFIED";

    public required int FormatVersion { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required string ApplicationVersion { get; init; }

    public required string[] AppliedMigrations { get; init; }

    public required string LatestSchemaVersion { get; init; }

    public required string SnapshotSha256 { get; init; }

    public required string IntegrityCheckOutcome { get; init; }

    public required string CompatibilityCheckOutcome { get; init; }

    public required DateTimeOffset VerifiedAtUtc { get; init; }

    public required string VerificationStatus { get; init; }

    public required string EncryptionMode { get; init; }

    /*
     * Additive optional member inside format version 1.
     *
     * The format version is deliberately not raised. The reader compares it
     * for equality, so a version bump would make every existing package
     * unreadable by this build and every new package unreadable by an
     * earlier one. Because the serializer skips unmapped members, a package
     * carrying this member still verifies on a build that predates it, and a
     * package without it is simply of unknown lineage here.
     *
     * The manifest is outside the snapshot digest, so this value is a
     * convenience copy only. Verification requires it to agree with the
     * identity read from the snapshot itself, exactly as it already does for
     * the recorded migration history.
     */
    public string? SourceWorkspaceId { get; init; }

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };
}
