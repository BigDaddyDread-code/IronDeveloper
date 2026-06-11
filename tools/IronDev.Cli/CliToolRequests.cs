using System.Text.Json;
using IronDev.Client;

namespace IronDev.Cli;

public static class IronDevCliToolRequests
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
        "source applied",
        "apply source",
        "apply patch",
        "memory promoted",
        "promote memory",
        "accepted memory",
        "model authority",
        "create pull request",
        "submit github review"
    ];

    private static readonly string[] UnsupportedAuthorityFlags =
    [
        "--approve",
        "--approval",
        "--execute",
        "--run",
        "--apply",
        "--source-apply",
        "--promote-memory",
        "--accept-memory",
        "--gate-pass",
        "--human-approved",
        "--policy-cleared",
        "--submit-github",
        "--append-audit",
        "--write-memory",
        "--collective-write",
        "--vector-write",
        "--index-write"
    ];

    public static bool IsToolRequestsCommand(string[] args) =>
        args.Length >= 1 && string.Equals(args[0], "tool-requests", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return WriteUsageError("tool-requests", output, error, IsJsonOutput(args, environment), "Missing tool-requests subcommand.");

        return args[1].ToLowerInvariant() switch
        {
            "create" => await HandleCreateAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "get" => await HandleGetAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            _ => WriteUsageError("tool-requests", output, error, IsJsonOutput(args, environment), $"Unknown tool-requests subcommand: {args[1]}.")
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
        var resolved = ResolveRequiredOptions("tool-requests create", args, output, error, environment);
        if (resolved.ExitCode is not null)
            return resolved.ExitCode.Value;

        if (HasUnsupportedAuthorityFlag(args, out var unsupportedFlag))
            return WriteUsageError("tool-requests create", output, error, IsJson(resolved.Options.Output), $"Unsupported approval, execution, gate, source-apply, memory-promotion, or audit flag for tool-requests create: {unsupportedFlag}.");

        if (!TryGetIntOption(args, "--project-id", out var projectId))
            return WriteUsageError("tool-requests create", output, error, IsJson(resolved.Options.Output), "Missing or invalid required option: --project-id <id>.");

        var requestKind = GetOption(args, "--request-kind");
        if (string.IsNullOrWhiteSpace(requestKind))
            return WriteUsageError("tool-requests create", output, error, IsJson(resolved.Options.Output), "Missing required option: --request-kind <kind>.");

        var toolKind = GetOption(args, "--tool-kind");
        if (string.IsNullOrWhiteSpace(toolKind))
            return WriteUsageError("tool-requests create", output, error, IsJson(resolved.Options.Output), "Missing required option: --tool-kind <kind>.");

        var runId = GetOption(args, "--run-id");
        if (string.IsNullOrWhiteSpace(runId))
            return WriteUsageError("tool-requests create", output, error, IsJson(resolved.Options.Output), "Missing required option: --run-id <id>.");

        var reason = GetOption(args, "--reason");
        if (string.IsNullOrWhiteSpace(reason))
            return WriteUsageError("tool-requests create", output, error, IsJson(resolved.Options.Output), "Missing required option: --reason <text>.");

        var textValues = new List<string?>
        {
            requestKind,
            toolKind,
            runId,
            reason,
            GetOption(args, "--summary"),
            GetOption(args, "--risk-level"),
            GetOption(args, "--correlation-id")
        };
        textValues.AddRange(GetRepeatedOption(args, "--evidence-ref"));
        textValues.AddRange(GetRepeatedOption(args, "--input-ref"));
        textValues.AddRange(GetRepeatedOption(args, "--policy-ref"));

        if (ContainsAny(textValues, PrivateReasoningMarkers))
            return WriteUsageError("tool-requests create", output, error, IsJson(resolved.Options.Output), "Tool request CLI does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, or private reasoning.");

        if (ContainsAny(textValues, SensitiveMarkers))
            return WriteUsageError("tool-requests create", output, error, IsJson(resolved.Options.Output), "Tool request CLI does not accept secret-bearing request material.");

        if (ContainsAny(textValues, AuthorityMarkers))
            return WriteUsageError("tool-requests create", output, error, IsJson(resolved.Options.Output), "Tool request CLI does not accept approval, execution, source apply, memory promotion, gate execution, audit approval, model authority, or external submission claims.");

        if (!TryGetOptionalBoolOption(args, "--dry-run-required", out var dryRunRequired))
            return WriteUsageError("tool-requests create", output, error, IsJson(resolved.Options.Output), "Invalid optional value: --dry-run-required true|false.");

        var request = new ToolRequestCreateRequest
        {
            ProjectId = projectId,
            RequestKind = requestKind.Trim(),
            ToolKind = toolKind.Trim(),
            RunId = runId.Trim(),
            Summary = GetOption(args, "--summary"),
            Reason = reason.Trim(),
            EvidenceRefs = GetRepeatedOption(args, "--evidence-ref"),
            InputRefs = GetRepeatedOption(args, "--input-ref"),
            PolicyRefs = GetRepeatedOption(args, "--policy-ref"),
            RiskLevel = GetOption(args, "--risk-level"),
            DryRunRequired = dryRunRequired,
            CorrelationId = GetOption(args, "--correlation-id")
        };

        return await CallApiAsync(
            "tool-requests create",
            resolved.Options,
            output,
            error,
            handler,
            client => client.CreateToolRequestAsync(request, cancellationToken),
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
        var resolved = ResolveRequiredOptions("tool-requests get", args, output, error, environment);
        if (resolved.ExitCode is not null)
            return resolved.ExitCode.Value;

        if (!TryGetPathArgument(args, 2, out var toolRequestId))
            return WriteUsageError("tool-requests get", output, error, IsJson(resolved.Options.Output), "Missing required argument: <toolRequestId>.");

        if (!TryGetIntOption(args, "--project-id", out var projectId))
            return WriteUsageError("tool-requests get", output, error, IsJson(resolved.Options.Output), "Missing or invalid required option: --project-id <id>.");

        return await CallApiAsync(
            "tool-requests get",
            resolved.Options,
            output,
            error,
            handler,
            client => client.GetToolRequestAsync(projectId, toolRequestId, cancellationToken),
            root => WriteGetText(root, output, projectId, toolRequestId),
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
        output.WriteLine("Tool request created");
        output.WriteLine($"  Project: {projectId}");
        output.WriteLine($"  Tool request: {Safe(GetString(root, "toolRequestId", GetString(data, "toolRequestId", "<unknown>")))}");
        output.WriteLine($"  Request kind: {Safe(GetString(data, "requestKind", "<unknown>"))}");
        output.WriteLine($"  Tool kind: {Safe(GetString(data, "requestedTool", "<unknown>"))}");
        output.WriteLine($"  Risk level: {Safe(GetString(data, "riskLevel", "<unknown>"))}");
        output.WriteLine($"  Run id: {Safe(GetString(root, "runId", GetString(data, "runId", "<unknown>")))}");
        output.WriteLine($"  Status: {Safe(GetString(root, "status", GetString(data, "status", "<unknown>")))}");
        output.WriteLine($"  Evidence refs: {GetArrayLength(data, "evidenceRefs")}");
        WriteBoundaryText(root, output);
        WriteWarnings(root, output);
    }

    private static void WriteGetText(JsonElement root, TextWriter output, int projectId, string toolRequestId)
    {
        var data = GetObject(root, "data");
        output.WriteLine("Tool request");
        output.WriteLine($"  Project: {projectId}");
        output.WriteLine($"  Tool request: {Safe(GetString(data, "toolRequestId", toolRequestId))}");
        output.WriteLine($"  Request kind: {Safe(GetString(data, "requestKind", "<unknown>"))}");
        output.WriteLine($"  Tool kind: {Safe(GetString(data, "requestedTool", "<unknown>"))}");
        output.WriteLine($"  Risk level: {Safe(GetString(data, "riskLevel", "<unknown>"))}");
        output.WriteLine($"  Run id: {Safe(GetString(data, "runId", GetString(root, "runId", "<unknown>")))}");
        output.WriteLine($"  Status: {Safe(GetString(root, "status", GetString(data, "status", "<unknown>")))}");
        output.WriteLine($"  Inputs: {GetArrayLength(data, "inputs")}");
        output.WriteLine($"  Evidence: {GetArrayLength(data, "evidence")}");
        WriteBoundaryText(root, output);
        WriteWarnings(root, output);
    }

    private static void WriteBoundaryText(JsonElement root, TextWriter output)
    {
        var boundary = GetObject(root, "boundary");
        output.WriteLine("  Boundary:");
        output.WriteLine($"    Tool request is execution permission: {GetBool(boundary, "toolRequestIsExecutionPermission").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Durable: {GetBool(boundary, "durable").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Request approved: {GetBool(boundary, "requestApproved").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Tool executed: {GetBool(boundary, "toolExecuted").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Gate is executor: {GetBool(boundary, "gateIsExecutor").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Source applied: {GetBool(boundary, "sourceApplied").ToString().ToLowerInvariant()}");
        output.WriteLine($"    Memory promoted: {GetBool(boundary, "memoryPromoted").ToString().ToLowerInvariant()}");
        output.WriteLine($"    API response status is governance: {GetBool(boundary, "apiResponseStatusIsGovernance").ToString().ToLowerInvariant()}");
        output.WriteLine("    Tool request is request form, not execution permission.");
        output.WriteLine("    Request approval is separate.");
        output.WriteLine("    Tool execution is separate.");
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

        output.WriteLine(RedactUnsafeMarkers(JsonSerializer.Serialize(envelope, JsonOptions)));
    }

    private static bool TryGetIntOption(string[] args, string name, out int value) =>
        int.TryParse(GetOption(args, name), out value) && value > 0;

    private static bool TryGetOptionalBoolOption(string[] args, string name, out bool? value)
    {
        value = null;
        var raw = GetOption(args, name);
        if (raw is null)
            return true;

        if (bool.TryParse(raw, out var parsed))
        {
            value = parsed;
            return true;
        }

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
