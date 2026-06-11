using System.Text.Json;
using IronDev.Client;

namespace IronDev.Cli;

public static class IronDevCliAgentRuns
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

    public static bool IsAgentRunsCommand(string[] args) =>
        args.Length >= 1 && StringEquals(args[0], "agent-runs");

    public static async Task<int> HandleAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return WriteUsageError("agent-runs", output, error, IsJsonOutput(args, environment), "Missing agent-runs subcommand.");

        return args[1].ToLowerInvariant() switch
        {
            "list" => await HandleListAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "get" => await HandleGetAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "audit" => await HandleAuditAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            _ => WriteUsageError("agent-runs", output, error, IsJsonOutput(args, environment), $"Unknown agent-runs subcommand: {args[1]}.")
        };
    }

    private static async Task<int> HandleListAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var resolved = ResolveRequiredOptions("agent-runs list", args, output, error, environment);
        if (resolved.ExitCode is not null)
            return resolved.ExitCode.Value;

        if (!TryGetIntOption(args, "--project-id", out var projectId))
            return WriteUsageError("agent-runs list", output, error, IsJson(resolved.Options.Output), "Missing or invalid required option: --project-id <id>.");

        if (!TryGetOptionalIntOption(args, "--take", out var take) || !TryGetOptionalIntOption(args, "--skip", out var skip))
            return WriteUsageError("agent-runs list", output, error, IsJson(resolved.Options.Output), "Invalid paging option. --take and --skip must be positive integers when supplied.");

        var query = new AgentRunListQuery
        {
            ProjectId = projectId,
            AgentId = GetOption(args, "--agent-id"),
            AgentKind = GetOption(args, "--agent-kind"),
            Status = GetOption(args, "--status"),
            TriggerType = GetOption(args, "--trigger-type"),
            CreatedAfterUtc = GetOption(args, "--created-after-utc"),
            CreatedBeforeUtc = GetOption(args, "--created-before-utc"),
            RunId = GetOption(args, "--run-id"),
            CorrelationId = GetOption(args, "--correlation-id"),
            Take = take,
            Skip = skip
        };

        return await CallApiAsync(
            "agent-runs list",
            resolved.Options,
            output,
            error,
            handler,
            client => client.ListAgentRunsAsync(query, cancellationToken),
            root => WriteListText(root, output, projectId),
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
        var resolved = ResolveRequiredOptions("agent-runs get", args, output, error, environment);
        if (resolved.ExitCode is not null)
            return resolved.ExitCode.Value;

        if (!TryGetPathArgument(args, 2, out var agentRunId))
            return WriteUsageError("agent-runs get", output, error, IsJson(resolved.Options.Output), "Missing required argument: <agentRunId>.");

        if (!TryGetIntOption(args, "--project-id", out var projectId))
            return WriteUsageError("agent-runs get", output, error, IsJson(resolved.Options.Output), "Missing or invalid required option: --project-id <id>.");

        return await CallApiAsync(
            "agent-runs get",
            resolved.Options,
            output,
            error,
            handler,
            client => client.GetAgentRunAsync(projectId, agentRunId, cancellationToken),
            root => WriteDetailText(root, output, projectId, agentRunId),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> HandleAuditAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var resolved = ResolveRequiredOptions("agent-runs audit", args, output, error, environment);
        if (resolved.ExitCode is not null)
            return resolved.ExitCode.Value;

        if (!TryGetPathArgument(args, 2, out var agentRunId))
            return WriteUsageError("agent-runs audit", output, error, IsJson(resolved.Options.Output), "Missing required argument: <agentRunId>.");

        if (!TryGetIntOption(args, "--project-id", out var projectId))
            return WriteUsageError("agent-runs audit", output, error, IsJson(resolved.Options.Output), "Missing or invalid required option: --project-id <id>.");

        return await CallApiAsync(
            "agent-runs audit",
            resolved.Options,
            output,
            error,
            handler,
            client => client.GetAgentRunAuditAsync(projectId, agentRunId, cancellationToken),
            root => WriteAuditText(root, output, projectId, agentRunId),
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

    private static void WriteListText(JsonElement root, TextWriter output, int projectId)
    {
        var data = GetObject(root, "data");
        var items = GetArray(data, "items");
        var totalCount = GetInt(data, "totalCount") ?? items.GetArrayLength();

        output.WriteLine("Agent runs");
        output.WriteLine($"Project: {projectId}");
        output.WriteLine($"Count: {totalCount}");
        output.WriteLine();

        foreach (var item in items.EnumerateArray())
        {
            output.WriteLine(string.Join("  ", new[]
            {
                Safe(GetString(item, "agentRunId")),
                Safe(GetString(item, "status")),
                Safe(GetString(item, "agentName")),
                Safe(GetString(item, "createdAtUtc"))
            }));
        }

        WriteWarnings(root, output);
    }

    private static void WriteDetailText(JsonElement root, TextWriter output, int projectId, string fallbackAgentRunId)
    {
        var data = GetObject(root, "data");
        var detail = GetObject(data, "run");
        var run = GetObject(detail, "run");
        var definition = GetObject(detail, "agentDefinition");
        var safety = GetObject(detail, "safetySummary");
        var inputs = GetArray(detail, "inputs");
        var outputs = GetArray(detail, "outputs");

        output.WriteLine("Agent run");
        output.WriteLine($"Agent run id: {Safe(GetString(data, "agentRunId", fallbackAgentRunId))}");
        output.WriteLine($"Project: {projectId}");
        output.WriteLine($"Status: {Safe(GetString(run, "status"))}");
        output.WriteLine($"Agent: {Safe(GetString(run, "agentName", GetString(definition, "name")))}");
        output.WriteLine($"Agent kind: {Safe(GetString(definition, "kind"))}");
        output.WriteLine($"Created: {Safe(GetString(run, "createdAtUtc"))}");
        output.WriteLine($"Completed: {Safe(GetString(run, "completedAtUtc"))}");
        output.WriteLine($"Inputs: {inputs.GetArrayLength()}");
        output.WriteLine($"Outputs: {outputs.GetArrayLength()}");
        output.WriteLine($"Boundary warnings: {HasAnySafetyFlag(safety).ToString().ToLowerInvariant()}");
        output.WriteLine("Audit is evidence, not approval.");
        output.WriteLine("CLI inspection is not execution permission.");
        WriteWarnings(root, output);
    }

    private static void WriteAuditText(JsonElement root, TextWriter output, int projectId, string fallbackAgentRunId)
    {
        var data = GetObject(root, "data");
        var safety = GetObject(data, "safetySummary");
        var evidence = GetArray(data, "evidenceReferences");

        output.WriteLine("Agent run audit");
        output.WriteLine($"Agent run id: {Safe(GetString(data, "agentRunId", fallbackAgentRunId))}");
        output.WriteLine($"Project: {projectId}");
        output.WriteLine($"Inputs: {GetInt(data, "inputCount") ?? 0}");
        output.WriteLine($"Outputs: {GetInt(data, "outputCount") ?? 0}");
        output.WriteLine($"Thought ledger entries: {GetInt(data, "thoughtLedgerCount") ?? 0}");
        output.WriteLine($"Capability uses: {GetInt(data, "capabilityUseCount") ?? 0}");
        output.WriteLine($"Boundary decisions: {GetInt(data, "boundaryDecisionCount") ?? 0}");
        output.WriteLine($"Evidence references: {evidence.GetArrayLength()}");
        output.WriteLine($"Safety boundary warnings: {HasAnySafetyFlag(safety).ToString().ToLowerInvariant()}");
        output.WriteLine("Audit is not approval.");
        output.WriteLine("Evidence is not permission.");
        WriteWarnings(root, output);
    }

    private static void WriteWarnings(JsonElement root, TextWriter output)
    {
        var warnings = GetArray(root, "warnings");
        foreach (var warning in warnings.EnumerateArray())
        {
            if (warning.ValueKind == JsonValueKind.String)
                output.WriteLine($"Warning: {Safe(warning.GetString())}");
        }
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
                error.WriteLine($"{item.Code}: {item.Message}");

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
            errors
        };

        output.WriteLine(RedactPrivateReasoningMarkers(JsonSerializer.Serialize(envelope, JsonOptions)));
    }

    private static bool TryGetPathArgument(string[] args, int index, out string value)
    {
        if (args.Length > index && !args[index].StartsWith("--", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(args[index]))
        {
            value = args[index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetIntOption(string[] args, string name, out int value) =>
        int.TryParse(GetOption(args, name), out value) && value > 0;

    private static bool TryGetOptionalIntOption(string[] args, string name, out int? value)
    {
        var raw = GetOption(args, name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = null;
            return true;
        }

        if (int.TryParse(raw, out var parsed) && parsed >= 0)
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (StringEquals(args[i], name))
                return args[i + 1];
        }

        return null;
    }

    private static JsonElement GetObject(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Object)
        {
            return property;
        }

        return default;
    }

    private static JsonElement GetArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Array)
        {
            return property;
        }

        return JsonDocument.Parse("[]").RootElement.Clone();
    }

    private static string GetString(JsonElement element, string propertyName, string fallback = "")
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? fallback;
        }

        return fallback;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var value))
        {
            return value;
        }

        return null;
    }

    private static bool HasAnySafetyFlag(JsonElement safety)
    {
        if (safety.ValueKind != JsonValueKind.Object)
            return false;

        var flags = new[]
        {
            "containsRawPrivateReasoning",
            "hasAuthorityClaim",
            "hasApprovalClaim",
            "hasMemoryPromotionClaim",
            "hasRuntimeActionOutput",
            "hasAuthorityCreatingOutput",
            "hasBlockedCapabilityAttempt",
            "hasBoundaryBlock"
        };

        return flags.Any(flag =>
            safety.TryGetProperty(flag, out var property) &&
            property.ValueKind is JsonValueKind.True);
    }

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
            result = result.Replace(marker, "[redacted: sensitive audit text]", StringComparison.OrdinalIgnoreCase);

        return result;
    }
}
