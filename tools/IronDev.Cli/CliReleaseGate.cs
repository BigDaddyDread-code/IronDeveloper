using System.Net.Http;
using System.Text.Json;
using IronDev.Client;

namespace IronDev.Cli;

public static class IronDevCliReleaseGate
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] ForbiddenOptions =
    [
        "--approve-release",
        "--release-approved",
        "--deploy",
        "--merge",
        "--execute-release",
        "--tag",
        "--git-push",
        "--source-apply",
        "--rollback",
        "--continue-workflow",
        "--promote-memory",
        "--activate-retrieval",
        "--dispatch-agent",
        "--run-tool",
        "--call-model"
    ];

    private static readonly string[] UnsafeMarkers =
    [
        "rawPrompt",
        "raw prompt",
        "rawCompletion",
        "raw completion",
        "rawToolOutput",
        "raw tool output",
        "chainOfThought",
        "chain-of-thought",
        "chain of thought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "entirePatch",
        "entire patch",
        "patchPayload",
        "patch payload",
        "password",
        "api_key",
        "secret",
        "private key",
        "bearer",
        "release approved",
        "approved for release",
        "deployment approved",
        "merge approved",
        "safe to deploy",
        "safe to merge",
        "green to ship",
        "release executed",
        "source applied by decision",
        "rollback executed by decision",
        "workflow continued by decision",
        "memory promoted",
        "retrieval activated",
        "agent dispatched",
        "tool executed",
        "model called"
    ];

    public static bool IsReleaseGateCommand(string[] args) =>
        args.Length >= 3 &&
        string.Equals(args[0], "release", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(args[1], "gate", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(args[2], "governed", StringComparison.OrdinalIgnoreCase)
        ||
        args.Length >= 4 &&
        string.Equals(args[0], "release", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(args[1], "readiness", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(args[2], "gate", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(args[3], "governed", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var parsed = Parse(args);
        if (parsed.Error is not null)
            return WriteFailure(parsed.IsJson ? "json" : "text", output, error, IronDevCliFoundation.UsageError, [new IronDevCliError("IRONDEV_CLI_USAGE", parsed.Error)]);

        var options = IronDevCliFoundation.ResolveOptions(args, environment);
        if (options.Errors.Count > 0)
            return WriteFailure(options.Output, output, error, IronDevCliFoundation.ConfigError, options.Errors);

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
            return WriteFailure(options.Output, output, error, IronDevCliFoundation.ConfigError, [new IronDevCliError("IRONDEV_CLI_API_BASE_URL_REQUIRED", "API base URL is required. Set IRONDEV_API_BASE_URL or pass --api-base-url.")]);

        var outputMode = parsed.IsJson ? "json" : options.Output;

        try
        {
            var requestJson = await File.ReadAllTextAsync(parsed.RequestFile!, cancellationToken).ConfigureAwait(false);
            if (ContainsUnsafeMarker(requestJson))
                return WriteFailure(outputMode, output, error, IronDevCliFoundation.UsageError, [new IronDevCliError("IRONDEV_CLI_UNSAFE_REQUEST_FILE", "Request file contains unsafe release gate markers.")]);

            using var document = JsonDocument.Parse(requestJson);
            var body = document.RootElement.Clone();
            var projectId = parsed.ProjectId ?? ExtractProjectId(body);
            if (string.IsNullOrWhiteSpace(projectId))
                return WriteFailure(outputMode, output, error, IronDevCliFoundation.UsageError, [new IronDevCliError("IRONDEV_CLI_PROJECT_ID_REQUIRED", "ProjectId is required in the request file or via --project-id.")]);

            var client = IronDevApiClientFactory.Create(options.ApiBaseUrl, options.Token, handler);
            var response = await client.CreateGovernedReleaseGateAsync(projectId, body, cancellationToken).ConfigureAwait(false);

            var root = response.Data is JsonElement element ? SanitizeElement(element) : null;
            if (response.IsSuccess && root is not null)
            {
                if (IsJson(outputMode))
                {
                    WriteEnvelope(output, "release gate governed", "succeeded", root, response.Warnings, []);
                }
                else
                {
                    output.WriteLine("Governed Release Gate");
                    output.WriteLine("Status: succeeded");
                    WritePropertyIfPresent(output, root, "status", "Result");
                    WriteDecisionPropertyIfPresent(output, root, "decisionStatus", "Decision status");
                    WriteDecisionPropertyIfPresent(output, root, "releaseReadinessDecisionRecordId", "Release readiness decision record");
                    WriteDecisionPropertyIfPresent(output, root, "releaseReadinessDecisionRecordHash", "Release readiness decision record hash");
                    WritePropertyIfPresent(output, root, "releaseReadinessEvidenceSatisfied", "Release readiness evidence satisfied");
                    WritePropertyIfPresent(output, root, "humanReviewRequiredForReleaseApproval", "Human review for release approval required");
                    WritePropertyIfPresent(output, root, "humanReviewRequiredForDeployment", "Human review for deployment required");
                    WritePropertyIfPresent(output, root, "humanReviewRequiredForMerge", "Human review for merge required");
                    WritePropertyIfPresent(output, root, "releaseApproved", "Release approval");
                    WritePropertyIfPresent(output, root, "deploymentApproved", "Deployment approval");
                    WritePropertyIfPresent(output, root, "mergeApproved", "Merge approval");
                    output.WriteLine("Release gate result is evidence only. It does not release the product.");
                    foreach (var warning in response.Warnings)
                        output.WriteLine($"Warning: {warning}");
                }

                return IronDevCliFoundation.Success;
            }

            var errors = response.Errors.Count == 0
                ? [new IronDevCliError("IRONDEV_CLI_API_ERROR", $"IronDev API returned status {(response.StatusCode == 0 ? response.Status : response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture))}.")]
                : response.Errors.Select(item => new IronDevCliError(item.Code, item.Message)).ToArray();

            return WriteFailure(outputMode, output, error, IronDevCliFoundation.ApiFailure, errors, response.Warnings, root);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return WriteFailure(outputMode, output, error, IronDevCliFoundation.ConnectionFailure, [new IronDevCliError("IRONDEV_CLI_API_CONNECTION_FAILED", $"Could not reach the configured IronDev API: {ex.Message}")]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return WriteFailure(outputMode, output, error, IronDevCliFoundation.UsageError, [new IronDevCliError("IRONDEV_CLI_REQUEST_FILE_INVALID", ex.Message)]);
        }
    }

    private static ParsedCommand Parse(string[] args)
    {
        var isJson = args.Any(arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));
        string? projectId = null;
        string? requestFile = null;
        var start = args.Length >= 4 && string.Equals(args[1], "readiness", StringComparison.OrdinalIgnoreCase) ? 4 : 3;

        for (var index = start; index < args.Length; index++)
        {
            var arg = args[index];
            if (ForbiddenOptions.Contains(arg, StringComparer.OrdinalIgnoreCase))
                return ParsedCommand.Fail(isJson, $"Unsupported governed release gate option: {arg}");

            switch (arg)
            {
                case "--project-id":
                    if (!TryReadValue(args, ref index, out projectId))
                        return ParsedCommand.Fail(isJson, "--project-id requires a value.");
                    break;
                case "--request-file":
                    if (!TryReadValue(args, ref index, out requestFile))
                        return ParsedCommand.Fail(isJson, "--request-file requires a value.");
                    break;
                case "--api-base-url":
                case "--token":
                case "--output":
                    if (!TrySkipValue(args, ref index))
                        return ParsedCommand.Fail(isJson, $"{arg} requires a value.");
                    break;
                case "--json":
                case "--verbose":
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                        return ParsedCommand.Fail(isJson, $"Unsupported governed release gate option: {arg}");
                    return ParsedCommand.Fail(isJson, $"Unexpected governed release gate argument: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(requestFile))
            return ParsedCommand.Fail(isJson, "--request-file is required.");
        if (ContainsUnsafeMarker(projectId) || ContainsUnsafeMarker(requestFile))
            return ParsedCommand.Fail(isJson, "Command arguments contain unsafe release gate markers.");

        return new ParsedCommand(projectId?.Trim(), requestFile.Trim(), isJson, null);
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            return false;
        value = args[++index];
        return true;
    }

    private static bool TrySkipValue(string[] args, ref int index) => TryReadValue(args, ref index, out _);

    private static string? ExtractProjectId(JsonElement root)
    {
        if (!root.TryGetProperty("projectId", out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool ContainsUnsafeMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static object SanitizeElement(JsonElement element)
    {
        var serialized = JsonSerializer.Serialize(element, JsonOptions);
        foreach (var marker in UnsafeMarkers)
            serialized = serialized.Replace(marker, "[redacted]", StringComparison.OrdinalIgnoreCase);
        return JsonSerializer.Deserialize<object>(serialized, JsonOptions) ?? new { };
    }

    private static void WriteDecisionPropertyIfPresent(TextWriter output, object root, string property, string label)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(root, JsonOptions));
        var element = document.RootElement;
        if (element.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            element = data;
        if (element.TryGetProperty("decisionRecord", out var record) && record.ValueKind == JsonValueKind.Object)
            element = record;
        if (element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null)
            output.WriteLine($"{label}: {value}");
    }

    private static void WritePropertyIfPresent(TextWriter output, object root, string property, string label)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(root, JsonOptions));
        var element = document.RootElement;
        if (element.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            element = data;
        if (element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null)
            output.WriteLine($"{label}: {value}");
    }

    private static int WriteFailure(
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
            WriteEnvelope(output, "release gate governed", "failed", data, warnings ?? [], errors);
        }
        else
        {
            foreach (var item in errors)
                error.WriteLine($"{item.Code}: {item.Message}");
            foreach (var warning in warnings ?? [])
                output.WriteLine($"Warning: {warning}");
        }

        return exitCode;
    }

    private static void WriteEnvelope<T>(TextWriter output, string command, string status, T? data, IReadOnlyList<string> warnings, IReadOnlyList<IronDevCliError> errors)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            command,
            status,
            data,
            warnings,
            errors
        }, JsonOptions));
    }

    private static bool IsJson(string? outputMode) =>
        string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase);

    private sealed record ParsedCommand(string? ProjectId, string? RequestFile, bool IsJson, string? Error)
    {
        public static ParsedCommand Fail(bool isJson, string error) => new(null, null, isJson, error);
    }
}
