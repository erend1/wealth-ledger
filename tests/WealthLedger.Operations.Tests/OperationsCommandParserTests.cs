using WealthLedger.Application.LocalData;
using WealthLedger.Operations;

namespace WealthLedger.Operations.Tests;

public sealed class OperationsCommandParserTests
{
    public static TheoryData<string[], string> ValidCommands
        => new()
        {
            { ["status"], "Status" },
            {
                ["database", "initialize"],
                "DatabaseInitialize"
            },
            {
                ["database", "migrate"],
                "DatabaseMigrate"
            },
            { ["backup", "create"], "BackupCreate" },
            {
                ["backup", "verify", "--file", Path.GetFullPath("a.wlbackup")],
                "BackupVerify"
            },
            {
                [
                    "restore",
                    "stage",
                    "--file",
                    Path.GetFullPath("a.wlbackup"),
                    "--target",
                    Path.GetFullPath("restored.db")
                ],
                "RestoreStage"
            },
            {
                [
                    "restore",
                    "replace",
                    "--file",
                    Path.GetFullPath("a.wlbackup"),
                    "--confirm-replace-active"
                ],
                "RestoreReplace"
            }
        };

    [Theory]
    [MemberData(nameof(ValidCommands))]
    public void Parse_AcceptedSurfaceMapsToFrozenCommandKind(
        string[] arguments,
        string expectedKind)
    {
        var result = OperationsCommandParser.Parse(arguments);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedKind, result.Command!.Kind.ToString());
    }

    [Fact]
    public void Parse_NarrowConfigurationOverridesAreSeparatedFromCommand()
    {
        var databasePath = Path.GetFullPath("synthetic.db");

        var result = OperationsCommandParser.Parse(
            [
                "--Storage:DatabasePath=" + databasePath,
                "status",
                "--Backup:DestinationEncryptionConfirmed=true"
            ]);

        Assert.True(result.Succeeded);
        Assert.Equal(OperationsCommandKind.Status, result.Command!.Kind);
        Assert.Equal(
            databasePath,
            result.Command.ConfigurationOverrides["Storage:DatabasePath"]);
        Assert.Equal(
            "true",
            result.Command.ConfigurationOverrides[
                "Backup:DestinationEncryptionConfirmed"]);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("backup verify")]
    [InlineData("restore stage --file package.wlbackup")]
    [InlineData("status --confirm-replace-active")]
    [InlineData("backup create --Unknown:Setting=true")]
    [InlineData("restore replace --file=a.wlbackup")]
    public void Parse_InvalidShapeReturnsStableInvalidInput(string commandLine)
    {
        var result = OperationsCommandParser.Parse(
            commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocalDataFailureCategory.InvalidInputOrConfiguration,
            result.Failure!.Category);
    }

    [Fact]
    public void Parse_ReplacementConfirmationIsLiteralAndNotImplied()
    {
        var withoutConfirmation = OperationsCommandParser.Parse(
            [
                "restore",
                "replace",
                "--file",
                Path.GetFullPath("a.wlbackup")
            ]);
        var withConfirmation = OperationsCommandParser.Parse(
            [
                "restore",
                "replace",
                "--file",
                Path.GetFullPath("a.wlbackup"),
                "--confirm-replace-active"
            ]);

        Assert.True(withoutConfirmation.Succeeded);
        Assert.False(withoutConfirmation.Command!.ConfirmReplaceActive);
        Assert.True(withConfirmation.Command!.ConfirmReplaceActive);
    }

    [Fact]
    public void FailureCategoryNumericValuesAreFrozenForAutomation()
    {
        Assert.Equal(2, (int)LocalDataFailureCategory.InvalidInputOrConfiguration);
        Assert.Equal(3, (int)LocalDataFailureCategory.UnsafePath);
        Assert.Equal(4, (int)LocalDataFailureCategory.OwnershipBusy);
        Assert.Equal(5, (int)LocalDataFailureCategory.NotFound);
        Assert.Equal(6, (int)LocalDataFailureCategory.AlreadyExists);
        Assert.Equal(7, (int)LocalDataFailureCategory.InvalidBackup);
        Assert.Equal(8, (int)LocalDataFailureCategory.IncompatibleBackup);
        Assert.Equal(9, (int)LocalDataFailureCategory.IntegrityFailure);
        Assert.Equal(10, (int)LocalDataFailureCategory.IoFailure);
        Assert.Equal(11, (int)LocalDataFailureCategory.MigrationFailure);
        Assert.Equal(12, (int)LocalDataFailureCategory.RestoreFailure);
        Assert.Equal(13, (int)LocalDataFailureCategory.Cancelled);
        Assert.Equal(14, (int)LocalDataFailureCategory.DatabaseNotReady);
    }
}
