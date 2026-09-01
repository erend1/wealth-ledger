using WealthLedger.Application.LocalData;

namespace WealthLedger.Operations;

internal enum OperationsCommandKind
{
    Help,
    Status,
    DatabaseInitialize,
    DatabaseMigrate,
    BackupCreate,
    BackupVerify,
    RestoreStage,
    RestoreReplace
}

internal sealed record ParsedOperationsCommand(
    OperationsCommandKind Kind,
    string? FilePath,
    string? TargetPath,
    bool ConfirmReplaceActive,
    IReadOnlyDictionary<string, string?> ConfigurationOverrides);

internal sealed record OperationsCommandParseResult(
    ParsedOperationsCommand? Command,
    LocalDataFailure? Failure)
{
    internal bool Succeeded => Failure is null;
}

internal static class OperationsCommandParser
{
    private static readonly HashSet<string> AllowedConfigurationKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Storage:DatabasePath",
            "Backup:Directory",
            "Backup:DestinationSeparationConfirmed",
            "Backup:DestinationEncryptionConfirmed",
            "Environment"
        };

    internal static OperationsCommandParseResult Parse(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var commandTokens = new List<string>();
        var configuration = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var argument in arguments)
        {
            if (TryParseConfigurationOverride(
                    argument,
                    out var key,
                    out var value))
            {
                if (!AllowedConfigurationKeys.Contains(key!))
                {
                    return Invalid(
                        "The command contains an unsupported configuration override.");
                }

                if (!configuration.TryAdd(key!, value))
                {
                    return Invalid(
                        "A configuration override was specified more than once.");
                }

                continue;
            }

            commandTokens.Add(argument);
        }

        if (commandTokens.Count == 0
            || commandTokens is ["help"] or ["--help"] or ["-h"])
        {
            return Success(
                OperationsCommandKind.Help,
                configuration: configuration);
        }

        if (commandTokens is ["status"])
        {
            return Success(
                OperationsCommandKind.Status,
                configuration: configuration);
        }

        if (commandTokens is ["database", "initialize"])
        {
            return Success(
                OperationsCommandKind.DatabaseInitialize,
                configuration: configuration);
        }

        if (commandTokens is ["database", "migrate"])
        {
            return Success(
                OperationsCommandKind.DatabaseMigrate,
                configuration: configuration);
        }

        if (commandTokens is ["backup", "create"])
        {
            return Success(
                OperationsCommandKind.BackupCreate,
                configuration: configuration);
        }

        if (commandTokens.Count >= 2
            && commandTokens[0] == "backup"
            && commandTokens[1] == "verify")
        {
            return ParseOptions(
                OperationsCommandKind.BackupVerify,
                commandTokens.Skip(2).ToArray(),
                configuration,
                requireFile: true,
                requireTarget: false,
                allowConfirmation: false);
        }

        if (commandTokens.Count >= 2
            && commandTokens[0] == "restore"
            && commandTokens[1] == "stage")
        {
            return ParseOptions(
                OperationsCommandKind.RestoreStage,
                commandTokens.Skip(2).ToArray(),
                configuration,
                requireFile: true,
                requireTarget: true,
                allowConfirmation: false);
        }

        if (commandTokens.Count >= 2
            && commandTokens[0] == "restore"
            && commandTokens[1] == "replace")
        {
            return ParseOptions(
                OperationsCommandKind.RestoreReplace,
                commandTokens.Skip(2).ToArray(),
                configuration,
                requireFile: true,
                requireTarget: false,
                allowConfirmation: true);
        }

        return Invalid("The operations command is not recognized.");
    }

    private static OperationsCommandParseResult ParseOptions(
        OperationsCommandKind kind,
        IReadOnlyList<string> optionTokens,
        IReadOnlyDictionary<string, string?> configuration,
        bool requireFile,
        bool requireTarget,
        bool allowConfirmation)
    {
        string? filePath = null;
        string? targetPath = null;
        var confirmed = false;

        for (var index = 0; index < optionTokens.Count; index++)
        {
            var option = optionTokens[index];

            if (option is "--file" or "--target")
            {
                if (index + 1 >= optionTokens.Count
                    || optionTokens[index + 1].StartsWith(
                        "--",
                        StringComparison.Ordinal))
                {
                    return Invalid($"{option} requires a path value.");
                }

                var value = optionTokens[++index];

                if (option == "--file")
                {
                    if (filePath is not null)
                    {
                        return Invalid("--file may be specified only once.");
                    }

                    filePath = value;
                }
                else
                {
                    if (targetPath is not null)
                    {
                        return Invalid("--target may be specified only once.");
                    }

                    targetPath = value;
                }

                continue;
            }

            if (option == "--confirm-replace-active" && allowConfirmation)
            {
                if (confirmed)
                {
                    return Invalid(
                        "--confirm-replace-active may be specified only once.");
                }

                confirmed = true;
                continue;
            }

            return Invalid("The command contains an unsupported option.");
        }

        if (requireFile && string.IsNullOrWhiteSpace(filePath))
        {
            return Invalid("--file is required for this command.");
        }

        if (requireTarget && string.IsNullOrWhiteSpace(targetPath))
        {
            return Invalid("--target is required for this command.");
        }

        if (!requireTarget && targetPath is not null)
        {
            return Invalid("--target is not supported for this command.");
        }

        return Success(
            kind,
            filePath,
            targetPath,
            confirmed,
            configuration);
    }

    private static bool TryParseConfigurationOverride(
        string argument,
        out string? key,
        out string? value)
    {
        key = null;
        value = null;

        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        var separator = argument.IndexOf('=', StringComparison.Ordinal);

        if (separator <= 2)
        {
            return false;
        }

        key = argument[2..separator];
        value = argument[(separator + 1)..];
        return true;
    }

    private static OperationsCommandParseResult Success(
        OperationsCommandKind kind,
        string? filePath = null,
        string? targetPath = null,
        bool confirmed = false,
        IReadOnlyDictionary<string, string?>? configuration = null)
        => new(
            new ParsedOperationsCommand(
                kind,
                filePath,
                targetPath,
                confirmed,
                configuration
                ?? new Dictionary<string, string?>()),
            Failure: null);

    private static OperationsCommandParseResult Invalid(string message)
        => new(
            Command: null,
            new LocalDataFailure(
                LocalDataFailureCategory.InvalidInputOrConfiguration,
                message));
}
