using WealthLedger.Application.LocalData;
using WealthLedger.Operations;

namespace WealthLedger.Operations.Tests;

public sealed class OperationsProgramTests
{
    [Fact]
    public async Task InvalidCommandWritesStableCategoryWithoutExceptionDetail()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await OperationsProgram.RunAsync(
            ["not-a-command"],
            output,
            error);

        Assert.Equal(
            (int)LocalDataFailureCategory.InvalidInputOrConfiguration,
            exitCode);
        Assert.StartsWith(
            "FAILURE INVALID_INPUT_OR_CONFIGURATION:",
            error.ToString());
        Assert.DoesNotContain("Exception", error.ToString());
        Assert.DoesNotContain(" at ", error.ToString());
    }

    [Fact]
    public async Task ReplacementWithoutLiteralConfirmationFailsBeforeFileAccess()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "WealthLedger.Operations.Tests",
            nameof(ReplacementWithoutLiteralConfirmationFailsBeforeFileAccess),
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(testRoot, "live", "wealthledger.db");
        var backupDirectory = Path.Combine(testRoot, "backups");
        var missingPackage = Path.Combine(testRoot, "missing.wlbackup");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await OperationsProgram.RunAsync(
            [
                "restore",
                "replace",
                "--file",
                missingPackage,
                "--Storage:DatabasePath=" + databasePath,
                "--Backup:Directory=" + backupDirectory,
                "--Environment=Testing"
            ],
            output,
            error);

        Assert.Equal(
            (int)LocalDataFailureCategory.InvalidInputOrConfiguration,
            exitCode);
        Assert.Contains(
            "--confirm-replace-active",
            error.ToString());
        Assert.False(Directory.Exists(testRoot));
    }
}
