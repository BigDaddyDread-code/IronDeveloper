using System.Text.Json;
using IronDev.Client;

namespace IronDev.Cli;

public static class IronDevCliManualCritic
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] PrivateReasoningMarkers =
    [
        "PRIVATE_MARKER",
        "chain-of-thought",
        "hidden reasoning",
        "private reasoning",
        "raw prompt",
        "raw completion",
        "scratchpad",
        "system prompt",
        "developer prompt"
    ];

    private static readonly string[] UnsupportedAuthorityFlags =
    [
        "--approve",
        "--approval",
        "--apply",
        "--source-apply",
        "--promote-memory",
        "--execute-tool",
        "--submit-github-review",
        "--block-execution"
    ];

    public static bool IsCriticCommand(string[] args) =>
        args.Length >= 1 && StringEquals(args[0], "critic");

    public static async Task<int> HandleAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        if (args.Length < 3 || !StringEquals(args[1], "review"))
            return WriteUsageError("critic review", output, error, IsJsonOutput(args, environment), "Expected critic review create or critic review get.");

        return args[2].ToLowerInvariant() switch
        {
            "create" => await HandleCreateAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "get" => await HandleGetAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            _ => WriteUsageError("critic review", output, error, IsJsonOutput(args, environment), $"Unknown critic review subcommand: {args[2]}.")
        };
    }

    private static async Task<int> HandleCreateAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var resolved = ResolveRequiredOptions("critic review create", args, output, error, environment);
        if (resolved.ExitCode is not null)
            return resolved.ExitCode.Value;

        if (HasUnsupportedAuthorityFlag(args, out var unsupportedFlag))
            return WriteUsageError("critic review create", output, error, IsJson(resolved.Options.Output), $"Unsupported authority or execution flag for critic review create: {unsupportedFlag}.");

        if (!TryGetIntOption(args, "--project-id", out var projectId))
            return WriteUsageError("critic review create", output, error, IsJson(resolved.Options.Output), "Missing or invalid required option: --project-id <id>.");

        var targetAgentRunId = GetOption(args, "--target-agent-run-id");
        if (string.IsNullOrWhiteSpace(targetAgentRunId))
            return WriteUsageError("critic review create", output, error, IsJson(resolved.Options.Output), "Missing required option: --target-agent-run-id <id>.");

        var request = new ManualCriticReviewCreateRequest
        {
            ProjectId = projectId,
            TargetAgentRunId = targetAgentRunId.Trim(),
            ReviewKind = GetOption(args, "--review-kind"),
            Focus = GetOption(args, "--focus"),
            EvidenceRefs = GetRepeatedOption(args, "--evidence-ref"),
            CorrelationId = GetOption(args, "--correlation-id"),
            Reason = GetOption(args, "--reason")
        };

        return await CallApiAsync(
            "critic review create",
            resolved.Options,
            output,
            error,
            handler,
            client => client.CreateManualCriticReviewAsync(request, cancellationToken),
            root => WriteCreateText(root, output, projectId, targetAgentRunId),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> HandleGetAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var resolved = ResolveRequiredOptions("critic review get", args, output, error, environment);
        if (resolved.ExitCode is not null)
            return resolved.ExitCode.Value;

        if (!TryGetPathArgument(args, 3, out var agentRunId))
            return WriteUsageError("critic review get", output, error, IsJson(resolved.Options.Output), "Missing required argument: <agentRunId>.");

        if (!TryGetIntOption(args, "--project-id", out var projectId))
            return WriteUsageError("critic review get", output, error, IsJson(resolved.Options.Output), "Missing or invalid required option: --project-id <id>.");

        return await CallApiAsync(
            "critic review get",
            resolved.Options,
            output,
            error,
            handler,
            client => client.GetManualCriticReviewAsync(projectId, agentRunId, cancellationToken),
            root => WriteGetText(root, output, projectId, agentRunId),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> CallApiAsync(
        string command,
        IronDevApiOptions options,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        Func<IIronDevApiClient, Task<IronDevApiResponse<JsonElement?>>> call,
        Action<JsonElement> writeText,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = IronDevApiClientFactory.Create(options.ApiBaseUrl!, options.Token, handler);
            var response = await call(client).ConfigureAwait(false);
            if (response.Data is not { } root)
            {
                return WriteFailure(
                    command,
                    options.Output,
                    output,
                    error,
                    IronDevCliFoundation.ApiFailure,
                    [new IronDevCliError("IRONDEV_CLI_API_EMPTY_RESPONSE", "IronDev API returned an empty response.")],
                    response.Warnings);
            }

            if (response.IsSuccess)
            {
                if (IsJson(options.Output))
                    WriteEnvelope(output, command, "succeeded", root, response.Warnings, []);
                else
                    writeText(root);

                return IronDevCliFoundation.Success;
            }

            var errors = response.Errors.Select(item => new IronDevCliError(item.Code, item.Message)).ToArray();
            return WriteFailure(command, options.Output, output, error, IronDevCliFoundation.ApiFailure, errors, response.Warnings, root);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return WriteFailure(
                command,
                options.Output,
                output,
                error,
                IronDevCliFoundation.ConnectionFailure,
                [new IronDevCliError("IRONDEV_CLI_API_CONNECTION_FAILED", $"Could not reach the configured IronDev API: {ex.Message}")]);
        }
    }

    private static void WriteCreateText(JsonElement root, TextWriter output, int projectId, string targetAgentRunId)
    {
        var data = GetObject(root, "data");
        output.WriteLine("Manual critic review requested");
        output.WriteLine($"  Project: {projectId}");
        output.WriteLine($"  Target agent run: {Safe(targetAgentRunId)}");
        output.WriteLine($"  Review run: {Safe(GetString(root, "runId", GetString(data, "agentRunId", "<unknown>")))}");
        output.WriteLine($"  Review id: {Safe(GetString(root, "reviewId", GetString(data, "reviewId", "<unknown>")))}");
        output.WriteLine($"  Status: {Safe(GetString(root, "status", "<unknown>"))}");
        output.WriteLine($"  Finding count: {GetInt(data, "findingCount")}");
        WriteBoundaryText(root, output);
        WriteWarnings(root, output);
    }

    private static void WriteGetText(JsonElement root, TextWriter output, int projectId, string agentRunId)
    {
        var data = GetObject(root, "data");
        output.WriteLine("Manual critic review");
        output.WriteLine($"  Project: {projectId}");
        output.WriteLine($"  Agent run: {Safe(agentRunId)}");
        output.WriteLine($"  Review id: {Safe(GetString(root, "reviewId", GetString(data, "reviewId", "<unknown>")))}");
        output.WriteLine($"  Status: {Safe(GetString(root, "status", GetString(data, "status", "<unknown>")))}");
        output.WriteLine($"  Agent: {Safe(GetString(data, "agentName", "<unknown>"))}");
        output.WriteLine($"  Evidence refs: {GetArrayLength(data, "evidenceRefs")}");
        output.WriteLine($"  Review-only output: {GetBool(data, "reviewOnlyOutput").ToString().ToLowerInvariant()}");
        output.WriteLine($"  Creates authority: {GetBool(data, "createsAuthority").ToString().ToLowerInvariant()}");
        WriteBoundaryText(root, output);
        WriteWarnings(root, output);
    }

    private static void WriteBoundaryText(JsonElement root, TextWriter output)
    {
        var boundary = GetObject(root, "boundary");
        output.WriteLine("  Boundary:");
        output.WriteLine($"    Critic is governance: {GetBool(boundary, "criticIsGovernance").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Critic review is approval: {GetBool(boundary, "criticIsApproval").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Audit is approval: {GetBool(boundary, "auditIsApproval").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Source applied: {GetBool(boundary, "sourceApplied").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Memory promoted: {GetBool(boundary, "memoryPromoted").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Tool executed: {GetBool(boundary, "toolExecuted").ToString().ToLowerInvariant()}");
        output.WriteLine("    Critic is not governance.");
        output.WriteLine("    Critic review is not approval.");
    }

    private static void WriteWarnings(JsonElement root, TextWriter output)
    {
        if (!root.TryGetProperty("warnings", out var warnings) || warnings.ValueKind != JsonValueKind.Array)
            return;

        foreach (var warning in warnings.EnumerateArray())
        {
            if (warning.ValueKind == JsonValueKind.String)
                output.WriteLine($"  Warning: {Safe(warning.GetString())}");
        }
    }

    private static (IronDevApiOptions Options, int? ExitCode) ResolveRequiredOptions(
        string command,
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment)
    {
        var options = IronDevCliFoundation.ResolveOptions(args, environment);
        if (options.Errors.Count > 0)
            return (options, WriteFailure(command, options.Output, output, error, IronDevCliFoundation.ConfigError, options.Errors));

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            return (options, WriteFailure(
                command,
                options.Output,
                output,
                error,
                IronDevCliFoundation.ConfigError,
                [new IronDevCliError("IRONDEV_CLI_API_BASE_URL_REQUIRED", "API base URL is required. Set IRONDEV_API_BASE_URL or pass --api-base-url.")]));
        }

        if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out _))
        {
            return (options, WriteFailure(
                command,
                options.Output,
                output,
                error,
                IronDevCliFoundation.ConfigError,
                [new IronDevCliError("IRONDEV_CLI_API_BASE_URL_INVALID", "API base URL must be an absolute URL.")]));
        }

        return (options, null);
    }

    private static int WriteUsageError(string command, TextWriter output, TextWriter error, bool json, string message) =>
        WriteFailure(
            command,
            json ? "json" : "text",
            output,
            error,
            IronDevCliFoundation.UsageError,
            [new IronDevCliError("IRONDEV_CLI_USAGE_ERROR", message)]);

    private static int WriteFailure(
        string command,
        string outputMode,
        TextWriter output,
        TextWriter error,
        int exitCode,
        IReadOnlyList<IronDevCliError> errors,
        IReadOnlyList<string>? warnings = null,
        object? data = null)
    {
        if (IsJson(outputMode))
        {
            WriteEnvelope(output, command, "failed", data, warnings ?? [], errors);
        }
        else
        {
            foreach (var item in errors)
                error.WriteLine($"{item.Code}: {Safe(item.Message)}");

            foreach (var warning in warnings ?? [])
                error.WriteLine($"Warning: {Safe(warning)}");
        }

        return exitCode;
    }

    private static void WriteEnvelope<T>(
        TextWriter output,
        string command,
        string status,
        T? data,
        IReadOnlyList<string> warnings,
        IReadOnlyList<IronDevCliError> errors)
    {
        var envelope = new
        {
            ok = errors.Count == 0,
            command,
            status,
            data,
            warnings = warnings.Select(Safe).ToArray(),
            errors = errors.Select(error => new
            {
                error.Code,
                Message = Safe(error.Message)
            }).ToArray()
        };

        output.WriteLine(RedactPrivateReasoningMarkers(JsonSerializer.Serialize(envelope, JsonOptions)));
    }

    private static bool TryGetIntOption(string[] args, string name, out int value) =>
        int.TryParse(GetOption(args, name), out value) && value > 0;

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (StringEquals(args[i], name))
                return args[i + 1];
        }

        return null;
    }

    private static IReadOnlyList<string> GetRepeatedOption(string[] args, string name)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (StringEquals(args[i], name) && !string.IsNullOrWhiteSpace(args[i + 1]))
                values.Add(args[i + 1].Trim());
        }

        return values;
    }

    private static bool TryGetPathArgument(string[] args, int index, out string value)
    {
        value = string.Empty;
        if (args.Length <= index || args[index].StartsWith("--", StringComparison.Ordinal))
            return false;

        value = args[index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool HasUnsupportedAuthorityFlag(string[] args, out string unsupportedFlag)
    {
        unsupportedFlag = UnsupportedAuthorityFlags.FirstOrDefault(flag => args.Any(arg => StringEquals(arg, flag))) ?? string.Empty;
        return unsupportedFlag.Length > 0;
    }

    private static JsonElement GetObject(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static string GetString(JsonElement root, string property, string fallback = "")
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }

        return value.GetString() ?? fallback;
    }

    private static int GetInt(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
        {
            return 0;
        }

        return result;
    }

    private static bool GetBool(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static int GetArrayLength(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;

    private static bool IsJsonOutput(string[] args, IReadOnlyDictionary<string, string?> environment) =>
        IsJson(IronDevCliFoundation.ResolveOptions(args, environment).Output);

    private static bool IsJson(string? output) =>
        string.Equals(output, "json", StringComparison.OrdinalIgnoreCase);

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Safe(string? value) =>
        RedactPrivateReasoningMarkers(value ?? string.Empty);

    private static string RedactPrivateReasoningMarkers(string value)
    {
        var result = value;
        foreach (var marker in PrivateReasoningMarkers)
            result = result.Replace(marker, "[redacted]", StringComparison.OrdinalIgnoreCase);

        return result;
    }
}
