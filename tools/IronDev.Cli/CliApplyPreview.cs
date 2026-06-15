using System.Net.Http;
using System.Text.Json;
using IronDev.Client;

namespace IronDev.Cli;

public static class IronDevCliApplyPreview
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] SensitiveMarkers =
    [
        "PRIVATE_MARKER",
        "chainOfThought",
        "chain-of-thought",
        "chain of thought",
        "rawPrompt",
        "raw prompt",
        "rawCompletion",
        "raw completion",
        "rawToolOutput",
        "raw tool output",
        "entirePatch",
        "entire patch",
        "patchPayload",
        "patch payload",
        "private reasoning",
        "hidden reasoning",
        "scratchpad"
    ];

    public static bool IsApplyPreviewCommand(string[] args) =>
        args.Length >= 2 &&
        string.Equals(args[0], "workflow", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(args[1], "apply-preview", StringComparison.OrdinalIgnoreCase);

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
            return WriteUsageError(output, error, parsed.IsJson, parsed.Error);

        var options = IronDevCliFoundation.ResolveOptions(args, environment);
        if (options.Errors.Count > 0)
            return WriteFailure("workflow apply-preview", options.Output, output, error, IronDevCliFoundation.ConfigError, options.Errors);

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
            return WriteFailure(
                "workflow apply-preview",
                options.Output,
                output,
                error,
                IronDevCliFoundation.ConfigError,
                [new IronDevCliError("IRONDEV_CLI_API_BASE_URL_REQUIRED", "API base URL is required. Set IRONDEV_API_BASE_URL or pass --api-base-url.")]);

        if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out _))
            return WriteFailure(
                "workflow apply-preview",
                options.Output,
                output,
                error,
                IronDevCliFoundation.ConfigError,
                [new IronDevCliError("IRONDEV_CLI_API_BASE_URL_INVALID", "API base URL must be an absolute URL.")]);

        var outputMode = parsed.IsJson ? "json" : options.Output;

        try
        {
            var client = IronDevApiClientFactory.Create(options.ApiBaseUrl, options.Token, handler);
            var response = await client.GetApplyPreviewAsync(
                parsed.WorkflowRunId!,
                parsed.WorkflowStepId!,
                parsed.ControlledApplyPlanReferenceId,
                parsed.TakeDryRuns,
                parsed.IncludeDryRunSummaries,
                cancellationToken).ConfigureAwait(false);

            var root = response.Data is JsonElement element
                ? SanitizeElement(element)
                : null;

            if (response.IsSuccess && root is not null)
            {
                if (IsJson(outputMode))
                {
                    WriteEnvelope(output, "workflow apply-preview", "succeeded", root, response.Warnings, []);
                }
                else
                {
                    WriteText(output, root, response.Warnings, parsed.IncludeDryRunSummaries);
                }

                return IronDevCliFoundation.Success;
            }

            var errors = response.Errors.Count == 0
                ? [new IronDevCliError("IRONDEV_CLI_API_ERROR", $"IronDev API returned status {(response.StatusCode == 0 ? response.Status : response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture))}.")]
                : response.Errors.Select(item => new IronDevCliError(item.Code, item.Message)).ToArray();

            return WriteFailure("workflow apply-preview", outputMode, output, error, IronDevCliFoundation.ApiFailure, errors, response.Warnings, root);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return WriteFailure(
                "workflow apply-preview",
                outputMode,
                output,
                error,
                IronDevCliFoundation.ConnectionFailure,
                [new IronDevCliError("IRONDEV_CLI_API_CONNECTION_FAILED", $"Could not reach the configured IronDev API: {ex.Message}")]);
        }
    }

    private static ParsedApplyPreviewCommand Parse(string[] args)
    {
        var isJson = IsJsonOutput(args);
        string? workflowRunId = null;
        string? workflowStepId = null;
        string? controlledApplyPlanReferenceId = null;
        var takeDryRuns = 10;
        var includeDryRunSummaries = true;

        for (var index = 2; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--workflow-run":
                    if (!TryReadValue(args, ref index, out workflowRunId))
                        return ParsedApplyPreviewCommand.Fail(isJson, "--workflow-run requires a value.");
                    break;
                case "--workflow-step":
                    if (!TryReadValue(args, ref index, out workflowStepId))
                        return ParsedApplyPreviewCommand.Fail(isJson, "--workflow-step requires a value.");
                    break;
                case "--controlled-apply-plan":
                    if (!TryReadValue(args, ref index, out controlledApplyPlanReferenceId))
                        return ParsedApplyPreviewCommand.Fail(isJson, "--controlled-apply-plan requires a value.");
                    break;
                case "--take-dry-runs":
                    if (!TryReadValue(args, ref index, out var takeValue))
                        return ParsedApplyPreviewCommand.Fail(isJson, "--take-dry-runs requires a value.");
                    if (!int.TryParse(takeValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedTake))
                        return ParsedApplyPreviewCommand.Fail(isJson, "--take-dry-runs must be an integer.");
                    takeDryRuns = Math.Clamp(parsedTake, 1, 50);
                    break;
                case "--no-dry-runs":
                    includeDryRunSummaries = false;
                    break;
                case "--api-base-url":
                case "--token":
                case "--output":
                    if (!TrySkipValue(args, ref index))
                        return ParsedApplyPreviewCommand.Fail(isJson, $"{arg} requires a value.");
                    break;
                case "--json":
                case "--verbose":
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                        return ParsedApplyPreviewCommand.Fail(isJson, $"Unsupported apply preview option: {arg}");
                    return ParsedApplyPreviewCommand.Fail(isJson, $"Unexpected apply preview argument: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(workflowRunId))
            return ParsedApplyPreviewCommand.Fail(isJson, "--workflow-run is required.");

        if (string.IsNullOrWhiteSpace(workflowStepId))
            return ParsedApplyPreviewCommand.Fail(isJson, "--workflow-step is required.");

        return new ParsedApplyPreviewCommand(
            workflowRunId.Trim(),
            workflowStepId.Trim(),
            string.IsNullOrWhiteSpace(controlledApplyPlanReferenceId) ? null : controlledApplyPlanReferenceId.Trim(),
            takeDryRuns,
            includeDryRunSummaries,
            isJson,
            null);
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            return false;

        value = args[++index];
        return true;
    }

    private static bool TrySkipValue(string[] args, ref int index) =>
        TryReadValue(args, ref index, out _);

    private static void WriteText(TextWriter output, object root, IReadOnlyList<string> warnings, bool includeDryRunSummaries)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(root, JsonOptions));
        var envelope = document.RootElement;
        var data = envelope.TryGetProperty("data", out var dataProperty) && dataProperty.ValueKind == JsonValueKind.Object
            ? dataProperty
            : envelope;

        output.WriteLine("Apply Preview");
        WriteLineIfPresent(output, "Status", GetString(data, "status") ?? GetString(envelope, "previewStatus") ?? GetString(envelope, "status"));
        WriteLineIfPresent(output, "Workflow Run", GetString(data, "workflowRunId"));
        WriteLineIfPresent(output, "Workflow Step", GetString(data, "workflowStepId"));
        WriteLineIfPresent(output, "Controlled Apply Plan", GetString(data, "controlledApplyPlanReferenceId"));
        WriteLineIfPresent(output, "Preview Reference", GetString(data, "previewReferenceId"));

        output.WriteLine();
        output.WriteLine("Boundary");
        output.WriteLine("  Preview only.");
        output.WriteLine("  Source apply remains unimplemented.");
        output.WriteLine("  Patch apply remains unimplemented.");
        output.WriteLine("  Apply dry-run execution remains unimplemented.");
        output.WriteLine("  Approval was not satisfied.");
        output.WriteLine("  Policy was not satisfied.");
        output.WriteLine("  Workflow was not transitioned.");

        WriteStringArray(output, "Safe summary", data, "safeSummaryLines");
        WriteObjectArray(output, includeDryRunSummaries ? "Dry-run receipt summaries" : "Dry-run receipt summaries: not requested", data, "dryRunSummaries");
        WriteObjectArray(output, "Missing evidence", data, "missingEvidence");
        WriteObjectArray(output, "Gates", data, "gates");
        WriteObjectArray(output, "Risks", data, "risks");
        WriteObjectArray(output, "Issues", data, "issues");

        if (warnings.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Warnings");
            foreach (var warning in warnings)
                output.WriteLine($"  - {SafeText(warning)}");
        }

        output.WriteLine();
        output.WriteLine("Review line: apply preview is evidence, not permission. Human review remains required before source apply.");
    }

    private static void WriteLineIfPresent(TextWriter output, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            output.WriteLine($"{label}: {SafeText(value)}");
    }

    private static void WriteStringArray(TextWriter output, string title, JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return;

        var values = property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => SafeText(item.GetString()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (values.Length == 0)
            return;

        output.WriteLine();
        output.WriteLine(title);
        foreach (var value in values)
            output.WriteLine($"  - {value}");
    }

    private static void WriteObjectArray(TextWriter output, string title, JsonElement data, string propertyName)
    {
        if (!data.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return;

        var items = property.EnumerateArray().ToArray();
        if (items.Length == 0)
            return;

        output.WriteLine();
        output.WriteLine(title);
        foreach (var item in items)
            output.WriteLine($"  - {Summarize(item)}");
    }

    private static string Summarize(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
            return SafeText(item.GetString());

        if (item.ValueKind != JsonValueKind.Object)
            return SafeText(item.ToString());

        var parts = new List<string>();
        foreach (var property in item.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                parts.Add($"{property.Name}: {SafeText(property.Value.ToString())}");
            }
        }

        return parts.Count == 0
            ? SafeText(item.ToString())
            : string.Join("; ", parts);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static object? SanitizeElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(property => property.Name, property => SanitizeElement(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(SanitizeElement).ToArray(),
            JsonValueKind.String => SafeText(element.GetString()),
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return SensitiveMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? "[redacted: sensitive apply preview text]"
            : value.Trim();
    }

    private static int WriteUsageError(TextWriter output, TextWriter error, bool json, string message)
    {
        return WriteFailure(
            "workflow apply-preview",
            json ? "json" : "text",
            output,
            error,
            IronDevCliFoundation.UsageError,
            [new IronDevCliError("IRONDEV_CLI_USAGE_ERROR", message)]);
    }

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
                error.WriteLine($"warning: {SafeText(warning)}");
        }

        return exitCode;
    }

    private static void WriteEnvelope<T>(
        TextWriter output,
        string command,
        string status,
        T data,
        IReadOnlyList<string> warnings,
        IReadOnlyList<IronDevCliError> errors)
    {
        var envelope = new
        {
            ok = errors.Count == 0,
            command,
            status,
            data,
            warnings = warnings.Select(SafeText).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(),
            errors,
            boundary = new
            {
                previewOnly = true,
                canApplySource = false,
                dryRunExecuted = false,
                sourceMutated = false,
                patchApplied = false,
                filesRead = false,
                filesWritten = false,
                commandRun = false,
                toolInvoked = false,
                validationRun = false,
                rollbackRun = false,
                approvalSatisfied = false,
                policySatisfied = false,
                workflowTransitioned = false,
                memoryPromoted = false,
                retrievalActivated = false,
                agentDispatched = false,
                modelCalled = false
            }
        };

        output.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    private static bool IsJsonOutput(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--json", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(args[index], "--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Length &&
                string.Equals(args[index + 1], "json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJson(string? output) =>
        string.Equals(output, "json", StringComparison.OrdinalIgnoreCase);

    private sealed record ParsedApplyPreviewCommand(
        string? WorkflowRunId,
        string? WorkflowStepId,
        string? ControlledApplyPlanReferenceId,
        int TakeDryRuns,
        bool IncludeDryRunSummaries,
        bool IsJson,
        string? Error)
    {
        public static ParsedApplyPreviewCommand Fail(bool isJson, string error) =>
            new(null, null, null, 10, true, isJson, error);
    }
}
