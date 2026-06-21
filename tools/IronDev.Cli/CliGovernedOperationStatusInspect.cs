using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

public static class IronDevCliGovernedOperationStatusInspect
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "approve",
        "satisfy-policy",
        "execute",
        "run-next-action",
        "next",
        "retry",
        "source-apply",
        "rollback",
        "commit",
        "push",
        "create-pr",
        "merge",
        "release",
        "deploy",
        "publish",
        "publish-package",
        "promote-memory",
        "continue",
        "continue-workflow",
        "dispatch",
        "trigger-pipeline",
        "mutate",
        "mutate-source",
        "mutate-environment",
        "create-approval"
    ];

    public static bool IsOperationStatusCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "operation-status", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "operation-status requires a subcommand: inspect.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"operation-status {args[1]} is intentionally unsupported; status inspection is read-only.");

        return subcommand switch
        {
            "inspect" => await HandleInspectAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            _ => Usage(error, $"unsupported operation-status subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleInspectAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var parsed = ParseInspect(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var read = await ReadStatusAsync(parsed.StatusPath!, cancellationToken).ConfigureAwait(false);
        if (read.Error is not null)
            return Failure(output, error, parsed.Json, "operation-status inspect", read.Error, usageFailure: true);

        var result = GovernedOperationStatusInspector.Inspect(new GovernedOperationStatusInspectRequest
        {
            Status = read.Status!,
            IncludeRefs = true,
            IncludeValidation = true
        });

        if (parsed.Json)
        {
            WriteJson(
                output,
                "operation-status inspect",
                result.IsValid ? "valid" : "invalid",
                new { result, boundary = GovernedOperationStatusInspectBoundary.ReadModel },
                result.IsValid ? [] : result.Validation.Issues.Concat(result.Validation.RedFlags).ToArray());
        }
        else
        {
            output.WriteLine(GovernedOperationStatusInspector.FormatText(result));
        }

        return result.IsValid ? 0 : 1;
    }

    private static async Task<StatusReadResult> ReadStatusAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return StatusReadResult.Fail("Missing required option: --status <operation-status.json>.");

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return StatusReadResult.Fail($"Status file not found: {fullPath}");

        try
        {
            var json = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var status = JsonSerializer.Deserialize<GovernedOperationStatus>(json, JsonOptions);
            return status is null
                ? StatusReadResult.Fail("Status file is empty or invalid.")
                : StatusReadResult.Success(status);
        }
        catch (Exception exception) when (exception is IOException or JsonException or NotSupportedException or UnauthorizedAccessException)
        {
            return StatusReadResult.Fail($"Status file could not be read as canonical GovernedOperationStatus JSON: {exception.Message}");
        }
    }

    private static ParsedInspect ParseInspect(string[] args)
    {
        string? status = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--status":
                case "--file":
                    var option = args[index];
                    if (!TryRead(args, ref index, out status))
                        return ParsedInspect.Fail(json, $"{option} requires a value.");
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return ParsedInspect.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(status)
            ? ParsedInspect.Fail(json, "Missing required option: --status <operation-status.json>.")
            : new ParsedInspect(status, json, null);
    }

    private static bool TryRead(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
            return false;
        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev operation-status inspect --status <operation-status.json> [--json]");
        return 2;
    }

    private static int Failure(
        TextWriter output,
        TextWriter error,
        bool json,
        string command,
        string message,
        bool usageFailure)
    {
        if (json)
            WriteJson(output, command, "failed", null, [message]);
        else
            error.WriteLine(message);

        return usageFailure ? 2 : 1;
    }

    private static void WriteJson(
        TextWriter output,
        string command,
        string status,
        object? data,
        string[] errors)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = errors.Length == 0 && !string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase),
            command,
            status,
            data,
            errors,
            boundary = GovernedOperationStatusInspectBoundary.ReadModel
        }, JsonOptions));
    }

    private sealed record ParsedInspect(string? StatusPath, bool Json, string? Error)
    {
        public static ParsedInspect Fail(bool json, string error) => new(null, json, error);
    }

    private sealed record StatusReadResult(GovernedOperationStatus? Status, string? Error)
    {
        public static StatusReadResult Success(GovernedOperationStatus status) => new(status, null);
        public static StatusReadResult Fail(string error) => new(null, error);
    }
}
