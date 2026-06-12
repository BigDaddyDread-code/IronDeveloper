using System.Text.Json;
using IronDev.Client;

namespace IronDev.Cli;

public static class IronDevCliDogfoodLoops
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

    private static readonly string[] SensitiveMarkers =
    [
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer "
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "release approved",
        "release approval",
        "ready to ship",
        "ship it",
        "approved for release",
        "approval granted",
        "approved for execution",
        "human approved",
        "policy cleared",
        "policy override",
        "authority granted",
        "authoritative for action",
        "execution permitted",
        "tool executed",
        "tool ran",
        "gate executed",
        "audit approved",
        "approval source audit",
        "source applied",
        "apply source",
        "apply patch",
        "memory promoted",
        "promote memory",
        "accepted memory",
        "collective memory written",
        "vector authority",
        "model authority",
        "autonomous workflow",
        "workflow completed",
        "create pull request",
        "submit github review"
    ];

    private static readonly string[] UnsupportedAuthorityFlags =
    [
        "--approve",
        "--approval",
        "--release-approved",
        "--ready-to-ship",
        "--execute",
        "--run-workflow",
        "--workflow-completed",
        "--gate-execute",
        "--gate-pass",
        "--apply",
        "--source-apply",
        "--promote-memory",
        "--accept-memory",
        "--append-audit",
        "--write-memory",
        "--collective-write",
        "--vector-write",
        "--index-write",
        "--submit-github"
    ];

    public static bool IsDogfoodLoopsCommand(string[] args) =>
        args.Length >= 1 && string.Equals(args[0], "dogfood-loops", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return WriteUsageError("dogfood-loops", output, error, IsJsonOutput(args, environment), "Missing dogfood-loops subcommand.");

        return args[1].ToLowerInvariant() switch
        {
            "create" => await HandleCreateAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "get" => await HandleGetAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            _ => WriteUsageError("dogfood-loops", output, error, IsJsonOutput(args, environment), $"Unknown dogfood-loops subcommand: {args[1]}.")
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
        var resolved = ResolveRequiredOptions("dogfood-loops create", args, output, error, environment);
        if (resolved.ExitCode is not null)
            return resolved.ExitCode.Value;

        if (HasUnsupportedAuthorityFlag(args, out var unsupportedFlag))
            return WriteUsageError("dogfood-loops create", output, error, IsJson(resolved.Options.Output), $"Unsupported release approval, workflow execution, gate, source-apply, memory-promotion, audit, or authority flag for dogfood-loops create: {unsupportedFlag}.");

        if (!TryGetIntOption(args, "--project-id", out var projectId))
            return WriteUsageError("dogfood-loops create", output, error, IsJson(resolved.Options.Output), "Missing or invalid required option: --project-id <id>.");

        var summary = GetOption(args, "--summary");
        if (string.IsNullOrWhiteSpace(summary))
            return WriteUsageError("dogfood-loops create", output, error, IsJson(resolved.Options.Output), "Missing required option: --summary <text>.");

        var goal = GetOption(args, "--goal");
        if (string.IsNullOrWhiteSpace(goal))
            return WriteUsageError("dogfood-loops create", output, error, IsJson(resolved.Options.Output), "Missing required option: --goal <text>.");

        var textValues = new List<string?>
        {
            summary,
            goal,
            GetOption(args, "--correlation-id")
        };
        textValues.AddRange(GetRepeatedOption(args, "--observation"));
        textValues.AddRange(GetRepeatedOption(args, "--blocked-reason"));
        textValues.AddRange(GetRepeatedOption(args, "--agent-run-id"));
        textValues.AddRange(GetRepeatedOption(args, "--critic-review-run-id"));
        textValues.AddRange(GetRepeatedOption(args, "--memory-improvement-run-id"));
        textValues.AddRange(GetRepeatedOption(args, "--tool-request-id"));
        textValues.AddRange(GetRepeatedOption(args, "--tool-gate-decision-id"));
        textValues.AddRange(GetRepeatedOption(args, "--evidence-ref"));

        if (ContainsAny(textValues, PrivateReasoningMarkers))
            return WriteUsageError("dogfood-loops create", output, error, IsJson(resolved.Options.Output), "Dogfood loop CLI does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, or private reasoning.");

        if (ContainsAny(textValues, SensitiveMarkers))
            return WriteUsageError("dogfood-loops create", output, error, IsJson(resolved.Options.Output), "Dogfood loop CLI does not accept secret-bearing request material.");

        if (ContainsAny(textValues, AuthorityMarkers))
            return WriteUsageError("dogfood-loops create", output, error, IsJson(resolved.Options.Output), "Dogfood loop CLI does not accept release approval, workflow execution, source apply, memory promotion, gate execution, audit approval, model authority, vector authority, or external submission claims.");

        var request = new DogfoodLoopCreateRequest
        {
            ProjectId = projectId,
            Summary = summary.Trim(),
            Goal = goal.Trim(),
            Observations = GetRepeatedOption(args, "--observation"),
            BlockedReasons = GetRepeatedOption(args, "--blocked-reason"),
            AgentRunIds = GetRepeatedOption(args, "--agent-run-id"),
            CriticReviewRunIds = GetRepeatedOption(args, "--critic-review-run-id"),
            MemoryImprovementRunIds = GetRepeatedOption(args, "--memory-improvement-run-id"),
            ToolRequestIds = GetRepeatedOption(args, "--tool-request-id"),
            ToolGateDecisionIds = GetRepeatedOption(args, "--tool-gate-decision-id"),
            EvidenceRefs = BuildEvidenceReferences(GetRepeatedOption(args, "--evidence-ref")),
            CorrelationId = GetOption(args, "--correlation-id")
        };

        return await CallApiAsync(
            "dogfood-loops create",
            resolved.Options,
            output,
            error,
            handler,
            client => client.CreateDogfoodLoopAsync(request, cancellationToken),
            root => WriteCreateText(root, output, projectId),
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
        var resolved = ResolveRequiredOptions("dogfood-loops get", args, output, error, environment);
        if (resolved.ExitCode is not null)
            return resolved.ExitCode.Value;

        if (!TryGetPathArgument(args, 2, out var dogfoodLoopId))
            return WriteUsageError("dogfood-loops get", output, error, IsJson(resolved.Options.Output), "Missing required argument: <dogfoodLoopId>.");

        if (!TryGetIntOption(args, "--project-id", out var projectId))
            return WriteUsageError("dogfood-loops get", output, error, IsJson(resolved.Options.Output), "Missing or invalid required option: --project-id <id>.");

        return await CallApiAsync(
            "dogfood-loops get",
            resolved.Options,
            output,
            error,
            handler,
            client => client.GetDogfoodLoopAsync(projectId, dogfoodLoopId, cancellationToken),
            root => WriteGetText(root, output, projectId, dogfoodLoopId),
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

    private static void WriteCreateText(JsonElement root, TextWriter output, int projectId)
    {
        var data = GetObject(root, "data");
        output.WriteLine("Dogfood loop receipt created");
        output.WriteLine($"  Project: {projectId}");
        output.WriteLine($"  Dogfood loop: {Safe(GetString(root, "dogfoodLoopId", GetString(data, "dogfoodLoopId", "<unknown>")))}");
        output.WriteLine($"  Run id: {Safe(GetString(root, "runId", GetString(data, "runId", "<unknown>")))}");
        output.WriteLine($"  Receipt id: {Safe(GetString(root, "receiptId", GetString(data, "receiptId", "<unknown>")))}");
        output.WriteLine($"  Evidence id: {Safe(GetString(root, "evidenceId", GetString(data, "evidenceId", "<unknown>")))}");
        output.WriteLine($"  Summary: {Safe(GetString(data, "summary", "<unknown>"))}");
        output.WriteLine($"  Goal: {Safe(GetString(data, "goal", "<unknown>"))}");
        output.WriteLine($"  Durable: {GetBool(data, "durable").ToString().ToLowerInvariant()}");
        output.WriteLine($"  Contains non-durable references: {GetBool(data, "containsNonDurableReferences").ToString().ToLowerInvariant()}");
        WriteBoundaryText(root, output);
        WriteWarnings(root, output);
    }

    private static void WriteGetText(JsonElement root, TextWriter output, int projectId, string dogfoodLoopId)
    {
        var data = GetObject(root, "data");
        output.WriteLine("Dogfood loop receipt");
        output.WriteLine($"  Project: {projectId}");
        output.WriteLine($"  Dogfood loop: {Safe(GetString(data, "dogfoodLoopId", dogfoodLoopId))}");
        output.WriteLine($"  Run id: {Safe(GetString(data, "runId", GetString(root, "runId", "<unknown>")))}");
        output.WriteLine($"  Receipt id: {Safe(GetString(data, "receiptId", GetString(root, "receiptId", "<unknown>")))}");
        output.WriteLine($"  Evidence id: {Safe(GetString(data, "evidenceId", GetString(root, "evidenceId", "<unknown>")))}");
        output.WriteLine($"  Summary: {Safe(GetString(data, "summary", "<unknown>"))}");
        output.WriteLine($"  Goal: {Safe(GetString(data, "goal", "<unknown>"))}");
        output.WriteLine($"  Observations: {GetArrayLength(data, "observations")}");
        output.WriteLine($"  Blocked reasons: {GetArrayLength(data, "blockedReasons")}");
        output.WriteLine($"  Tool requests: {GetArrayLength(data, "referencedToolRequests")}");
        output.WriteLine($"  Gate decisions: {GetArrayLength(data, "referencedGateDecisions")}");
        output.WriteLine($"  Evidence refs: {GetArrayLength(data, "evidenceRefs")}");
        output.WriteLine($"  Durable: {GetBool(data, "durable").ToString().ToLowerInvariant()}");
        WriteBoundaryText(root, output);
        WriteWarnings(root, output);
    }

    private static void WriteBoundaryText(JsonElement root, TextWriter output)
    {
        var boundary = GetObject(root, "boundary");
        output.WriteLine("  Boundary:");
        output.WriteLine($"    Dogfood receipt is release approval: {GetBool(boundary, "dogfoodReceiptIsReleaseApproval").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Dogfood loop is autonomous workflow: {GetBool(boundary, "dogfoodLoopIsAutonomousWorkflow").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Tool executed: {GetBool(boundary, "toolExecuted").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Gate executed: {GetBool(boundary, "gateExecuted").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Gate is executor: {GetBool(boundary, "gateIsExecutor").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Source applied: {GetBool(boundary, "sourceApplied").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Memory promoted: {GetBool(boundary, "memoryPromoted").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Audit is approval: {GetBool(boundary, "auditIsApproval").ToString().ToLowerInvariant()}");
        output.WriteLine($"    API response status is governance: {GetBool(boundary, "apiResponseStatusIsGovernance").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Durable: {GetBool(boundary, "durable").ToString().ToLowerInvariant()}");
        output.WriteLine("    Dogfood receipt is not release approval.");
        output.WriteLine("    Dogfood loop is not autonomous workflow.");
        output.WriteLine("    Human review remains required for source apply and memory promotion.");
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

    private static IReadOnlyList<DogfoodLoopEvidenceReference> BuildEvidenceReferences(IReadOnlyList<string> refs) =>
        refs.Select(reference => new DogfoodLoopEvidenceReference
        {
            RefType = "cli_evidence",
            RefId = reference.Trim(),
            Summary = "Caller-supplied CLI evidence reference.",
            Source = "cli"
        }).ToArray();

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

        output.WriteLine(RedactUnsafeMarkers(JsonSerializer.Serialize(envelope, JsonOptions)));
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

    private static bool ContainsAny(IEnumerable<string?> values, IEnumerable<string> markers) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                            markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));

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
        RedactUnsafeMarkers(value ?? string.Empty);

    private static string RedactUnsafeMarkers(string value)
    {
        var result = value;
        foreach (var marker in PrivateReasoningMarkers.Concat(SensitiveMarkers))
            result = result.Replace(marker, "[redacted]", StringComparison.OrdinalIgnoreCase);

        return result;
    }
}
