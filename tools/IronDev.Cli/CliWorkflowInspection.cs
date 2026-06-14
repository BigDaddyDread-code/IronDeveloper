using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using IronDev.Client;

namespace IronDev.Cli;

public static class IronDevCliWorkflowInspection
{
    private const int DefaultTake = 100;
    private const int MaxTake = 500;
    private const string Redaction = "[redacted: sensitive workflow text]";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> CommonOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--api-base-url",
        "--token",
        "--output",
        "--json",
        "--verbose",
        "--project",
        "--project-id"
    };

    private static readonly string[] SensitiveMarkers =
    [
        "PRIVATE_MARKER",
        "private reasoning",
        "hidden reasoning",
        "chainOfThought",
        "chain of thought",
        "chain-of-thought",
        "scratchpad",
        "rawPrompt",
        "raw prompt",
        "rawCompletion",
        "raw completion",
        "rawToolOutput",
        "raw tool output",
        "entirePatch",
        "entire patch",
        "system prompt",
        "developer prompt"
    ];

    public static bool IsWorkflowInspectCommand(string[] args) =>
        args.Length >= 2 &&
        EqualsToken(args[0], "workflow") &&
        EqualsToken(args[1], "inspect");

    public static async Task<int> HandleAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        if (args.Length < 3)
            return WriteUsageError("workflow inspect", output, error, IsJsonOutput(args, environment), "Missing workflow inspect subcommand.");

        var subcommand = args[2].Trim().ToLowerInvariant();
        return subcommand switch
        {
            "runs" => await HandleRunsAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "run" => await HandleRunAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "runs-by-correlation" => await HandleRunsByCorrelationAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "runs-by-subject" => await HandleRunsBySubjectAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "steps" => await HandleStepsAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "step" => await HandleStepAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "checkpoints" => await HandleCheckpointsAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "step-checkpoints" => await HandleStepCheckpointsAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            "checkpoint" => await HandleCheckpointAsync(args, output, error, environment, handler, cancellationToken).ConfigureAwait(false),
            _ => WriteUsageError("workflow inspect", output, error, IsJsonOutput(args, environment), $"Unknown workflow inspect subcommand: {args[2]}.")
        };
    }

    private static Task<int> HandleRunsAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyDictionary<string, string?> environment, HttpMessageHandler? handler, CancellationToken cancellationToken) =>
        HandleListAsync(
            "workflow inspect runs",
            args,
            output,
            error,
            environment,
            handler,
            KnownOptions("--take"),
            RequireProject(args, "workflow inspect runs"),
            async (client, projectId, take, token) => await client.ListWorkflowRunsAsync(projectId, take, token).ConfigureAwait(false),
            root => WriteRunsText(root, output, "Workflow runs"),
            cancellationToken);

    private static Task<int> HandleRunAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyDictionary<string, string?> environment, HttpMessageHandler? handler, CancellationToken cancellationToken) =>
        HandleSingleAsync(
            "workflow inspect run",
            args,
            output,
            error,
            environment,
            handler,
            KnownOptions("--run"),
            RequireProject(args, "workflow inspect run"),
            RequireOption(args, "--run", "workflow inspect run"),
            async (client, projectId, id, token) => await client.GetWorkflowRunAsync(projectId, id, token).ConfigureAwait(false),
            root => WriteSingleText(root, output, "Workflow run", "workflowRunId", "runId"),
            cancellationToken);

    private static Task<int> HandleRunsByCorrelationAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyDictionary<string, string?> environment, HttpMessageHandler? handler, CancellationToken cancellationToken) =>
        HandleListWithOneIdAsync(
            "workflow inspect runs-by-correlation",
            args,
            output,
            error,
            environment,
            handler,
            KnownOptions("--correlation", "--take"),
            RequireProject(args, "workflow inspect runs-by-correlation"),
            RequireOption(args, "--correlation", "workflow inspect runs-by-correlation"),
            async (client, projectId, correlationId, take, token) => await client.ListWorkflowRunsByCorrelationAsync(projectId, correlationId, take, token).ConfigureAwait(false),
            root => WriteRunsText(root, output, "Workflow runs by correlation"),
            cancellationToken);

    private static Task<int> HandleRunsBySubjectAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyDictionary<string, string?> environment, HttpMessageHandler? handler, CancellationToken cancellationToken)
    {
        var project = RequireProject(args, "workflow inspect runs-by-subject");
        var subjectType = RequireOption(args, "--subject-type", "workflow inspect runs-by-subject");
        var subjectId = RequireOption(args, "--subject-id", "workflow inspect runs-by-subject");

        return HandleListAsync(
            "workflow inspect runs-by-subject",
            args,
            output,
            error,
            environment,
            handler,
            KnownOptions("--subject-type", "--subject-id", "--take"),
            project,
            async (client, projectId, take, token) => await client.ListWorkflowRunsBySubjectAsync(projectId, subjectType.Value ?? string.Empty, subjectId.Value ?? string.Empty, take, token).ConfigureAwait(false),
            root => WriteRunsText(root, output, "Workflow runs by subject"),
            cancellationToken,
            subjectType,
            subjectId);
    }

    private static Task<int> HandleStepsAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyDictionary<string, string?> environment, HttpMessageHandler? handler, CancellationToken cancellationToken) =>
        HandleListWithOneIdAsync(
            "workflow inspect steps",
            args,
            output,
            error,
            environment,
            handler,
            KnownOptions("--run", "--take"),
            RequireProject(args, "workflow inspect steps"),
            RequireOption(args, "--run", "workflow inspect steps"),
            async (client, projectId, runId, take, token) => await client.ListWorkflowStepsAsync(projectId, runId, take, token).ConfigureAwait(false),
            root => WriteRunsText(root, output, "Workflow steps"),
            cancellationToken);

    private static Task<int> HandleStepAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyDictionary<string, string?> environment, HttpMessageHandler? handler, CancellationToken cancellationToken)
    {
        var run = RequireOption(args, "--run", "workflow inspect step");
        var step = RequireOption(args, "--step", "workflow inspect step");

        return HandleSingleAsync(
            "workflow inspect step",
            args,
            output,
            error,
            environment,
            handler,
            KnownOptions("--run", "--step"),
            RequireProject(args, "workflow inspect step"),
            step,
            async (client, projectId, stepId, token) => await client.GetWorkflowStepAsync(projectId, run.Value ?? string.Empty, stepId, token).ConfigureAwait(false),
            root => WriteSingleText(root, output, "Workflow step", "workflowRunStepId", "stepId"),
            cancellationToken,
            run);
    }

    private static Task<int> HandleCheckpointsAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyDictionary<string, string?> environment, HttpMessageHandler? handler, CancellationToken cancellationToken) =>
        HandleListWithOneIdAsync(
            "workflow inspect checkpoints",
            args,
            output,
            error,
            environment,
            handler,
            KnownOptions("--run", "--take"),
            RequireProject(args, "workflow inspect checkpoints"),
            RequireOption(args, "--run", "workflow inspect checkpoints"),
            async (client, projectId, runId, take, token) => await client.ListWorkflowCheckpointsAsync(projectId, runId, take, token).ConfigureAwait(false),
            root => WriteRunsText(root, output, "Workflow checkpoints"),
            cancellationToken);

    private static Task<int> HandleStepCheckpointsAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyDictionary<string, string?> environment, HttpMessageHandler? handler, CancellationToken cancellationToken)
    {
        var run = RequireOption(args, "--run", "workflow inspect step-checkpoints");
        var step = RequireOption(args, "--step", "workflow inspect step-checkpoints");

        return HandleListAsync(
            "workflow inspect step-checkpoints",
            args,
            output,
            error,
            environment,
            handler,
            KnownOptions("--run", "--step", "--take"),
            RequireProject(args, "workflow inspect step-checkpoints"),
            async (client, projectId, take, token) => await client.ListWorkflowStepCheckpointsAsync(projectId, run.Value ?? string.Empty, step.Value ?? string.Empty, take, token).ConfigureAwait(false),
            root => WriteRunsText(root, output, "Workflow step checkpoints"),
            cancellationToken,
            run,
            step);
    }

    private static Task<int> HandleCheckpointAsync(string[] args, TextWriter output, TextWriter error, IReadOnlyDictionary<string, string?> environment, HttpMessageHandler? handler, CancellationToken cancellationToken)
    {
        var run = RequireOption(args, "--run", "workflow inspect checkpoint");
        var checkpoint = RequireOption(args, "--checkpoint", "workflow inspect checkpoint");

        return HandleSingleAsync(
            "workflow inspect checkpoint",
            args,
            output,
            error,
            environment,
            handler,
            KnownOptions("--run", "--checkpoint"),
            RequireProject(args, "workflow inspect checkpoint"),
            checkpoint,
            async (client, projectId, checkpointId, token) => await client.GetWorkflowCheckpointAsync(projectId, run.Value ?? string.Empty, checkpointId, token).ConfigureAwait(false),
            root => WriteSingleText(root, output, "Workflow checkpoint", "workflowCheckpointId", "checkpointId"),
            cancellationToken,
            run);
    }

    private static async Task<int> HandleListAsync(
        string command,
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        HashSet<string> knownOptions,
        RequiredValue project,
        Func<IIronDevApiClient, string, int?, CancellationToken, Task<IronDevApiResponse<JsonElement?>>> call,
        Action<JsonElement> writeText,
        CancellationToken cancellationToken,
        params RequiredValue[] additionalRequired)
    {
        if (project.Error is not null)
            return WriteUsageError(command, output, error, IsJsonOutput(args, environment), project.Error);

        foreach (var required in additionalRequired)
        {
            if (required.Error is not null)
                return WriteUsageError(command, output, error, IsJsonOutput(args, environment), required.Error);
        }

        if (!ValidateOptions(args, knownOptions, out var optionError))
            return WriteUsageError(command, output, error, IsJsonOutput(args, environment), optionError);

        if (!TryGetTake(args, out var take, out var takeError))
            return WriteUsageError(command, output, error, IsJsonOutput(args, environment), takeError);

        return await CallApiAsync(
            command,
            args,
            output,
            error,
            environment,
            handler,
            client => call(client, project.Value!, take, cancellationToken),
            writeText,
            cancellationToken).ConfigureAwait(false);
    }

    private static Task<int> HandleListWithOneIdAsync(
        string command,
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        HashSet<string> knownOptions,
        RequiredValue project,
        RequiredValue id,
        Func<IIronDevApiClient, string, string, int?, CancellationToken, Task<IronDevApiResponse<JsonElement?>>> call,
        Action<JsonElement> writeText,
        CancellationToken cancellationToken)
    {
        if (id.Error is not null)
            return Task.FromResult(WriteUsageError(command, output, error, IsJsonOutput(args, environment), id.Error));

        return HandleListAsync(
            command,
            args,
            output,
            error,
            environment,
            handler,
            knownOptions,
            project,
            (client, projectId, take, token) => call(client, projectId, id.Value!, take, token),
            writeText,
            cancellationToken);
    }

    private static async Task<int> HandleSingleAsync(
        string command,
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        HashSet<string> knownOptions,
        RequiredValue project,
        RequiredValue id,
        Func<IIronDevApiClient, string, string, CancellationToken, Task<IronDevApiResponse<JsonElement?>>> call,
        Action<JsonElement> writeText,
        CancellationToken cancellationToken,
        params RequiredValue[] additionalRequired)
    {
        if (project.Error is not null)
            return WriteUsageError(command, output, error, IsJsonOutput(args, environment), project.Error);
        if (id.Error is not null)
            return WriteUsageError(command, output, error, IsJsonOutput(args, environment), id.Error);

        foreach (var required in additionalRequired)
        {
            if (required.Error is not null)
                return WriteUsageError(command, output, error, IsJsonOutput(args, environment), required.Error);
        }

        if (!ValidateOptions(args, knownOptions, out var optionError))
            return WriteUsageError(command, output, error, IsJsonOutput(args, environment), optionError);

        return await CallApiAsync(
            command,
            args,
            output,
            error,
            environment,
            handler,
            client => call(client, project.Value!, id.Value!, cancellationToken),
            writeText,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> CallApiAsync(
        string command,
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        HttpMessageHandler? handler,
        Func<IIronDevApiClient, Task<IronDevApiResponse<JsonElement?>>> call,
        Action<JsonElement> writeText,
        CancellationToken cancellationToken)
    {
        var options = IronDevCliFoundation.ResolveOptions(args, environment);
        if (options.Errors.Count > 0)
            return WriteFailure(command, options.Output, output, error, IronDevCliFoundation.ConfigError, options.Errors);

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            return WriteFailure(command, options.Output, output, error, IronDevCliFoundation.ConfigError,
            [
                new IronDevCliError("IRONDEV_CLI_API_BASE_URL_REQUIRED", "API base URL is required. Pass --api-base-url or set IRONDEV_API_BASE_URL.")
            ]);
        }

        try
        {
            var client = IronDevApiClientFactory.Create(options.ApiBaseUrl!, options.Token, handler);
            var response = await call(client).ConfigureAwait(false);

            if (response.IsSuccess && response.Data is JsonElement root)
            {
                if (IsJson(options.Output))
                    WriteEnvelope(output, command, "succeeded", root, response.Warnings, []);
                else
                    writeText(root);

                return IronDevCliFoundation.Success;
            }

            var errors = response.Errors.Count == 0
                ? [new IronDevCliError("IRONDEV_CLI_API_ERROR", $"IronDev API returned status {(response.StatusCode == 0 ? response.Status : response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture))}.")]
                : response.Errors.Select(item => new IronDevCliError(
                    string.IsNullOrWhiteSpace(item.Code) ? "IRONDEV_CLI_API_ERROR" : item.Code,
                    string.IsNullOrWhiteSpace(item.Message) ? "IronDev API returned an error." : item.Message)).ToArray();

            return WriteFailure(command, options.Output, output, error, IronDevCliFoundation.ApiFailure, errors, response.Warnings, response.Data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return WriteFailure(command, options.Output, output, error, IronDevCliFoundation.ConnectionFailure,
            [
                new IronDevCliError("IRONDEV_CLI_API_CONNECTION_FAILED", $"Could not reach the IronDev API: {ex.Message}")
            ]);
        }
    }

    private static RequiredValue RequireProject(string[] args, string command)
    {
        if (TryGetOption(args, "--project", out var project) || TryGetOption(args, "--project-id", out project))
            return string.IsNullOrWhiteSpace(project)
                ? new RequiredValue(null, "Missing or invalid required option: --project <projectId>.")
                : new RequiredValue(project.Trim(), null);

        return new RequiredValue(null, "Missing required option: --project <projectId>.");
    }

    private static RequiredValue RequireOption(string[] args, string name, string command)
    {
        _ = command;
        if (TryGetOption(args, name, out var value) && !string.IsNullOrWhiteSpace(value))
            return new RequiredValue(value.Trim(), null);

        return new RequiredValue(null, $"Missing required option: {name} <value>.");
    }

    private static bool TryGetTake(string[] args, out int? take, out string error)
    {
        take = DefaultTake;
        error = string.Empty;
        if (!TryGetOption(args, "--take", out var value))
            return true;

        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            error = "Invalid paging option. --take must be a positive integer when supplied.";
            return false;
        }

        take = Math.Min(parsed, MaxTake);
        return true;
    }

    private static HashSet<string> KnownOptions(params string[] specific)
    {
        var options = new HashSet<string>(CommonOptions, StringComparer.OrdinalIgnoreCase);
        foreach (var item in specific)
            options.Add(item);
        return options;
    }

    private static bool ValidateOptions(string[] args, HashSet<string> knownOptions, out string error)
    {
        error = string.Empty;
        for (var i = 3; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (!knownOptions.Contains(arg))
            {
                error = $"Unsupported workflow inspection option: {arg}.";
                return false;
            }

            if (!IsFlag(arg) && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                i++;
        }

        return true;
    }

    private static bool IsFlag(string option) =>
        EqualsToken(option, "--json") || EqualsToken(option, "--verbose");

    private static bool TryGetOption(string[] args, string name, out string? value)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!EqualsToken(args[i], name))
                continue;

            value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[i + 1]
                : null;
            return true;
        }

        value = null;
        return false;
    }

    private static bool IsJsonOutput(string[] args, IReadOnlyDictionary<string, string?> environment)
    {
        var options = IronDevCliFoundation.ResolveOptions(args, environment);
        return IsJson(options.Output);
    }

    private static void WriteRunsText(JsonElement root, TextWriter output, string title)
    {
        var data = GetObject(root, "data");
        var items = EnumerateAnyArray(data, root, "items", "runs", "workflowRuns", "steps", "checkpoints").ToArray();

        output.WriteLine(title);
        output.WriteLine($"Count: {items.Length}");
        output.WriteLine();

        foreach (var item in items)
        {
            output.WriteLine(string.Join("  ", new[]
            {
                Safe(FirstString(item, "workflowRunId", "runId", "workflowRunStepId", "stepId", "workflowCheckpointId", "checkpointId", "id")),
                Safe(FirstString(item, "status", "state", "stepType", "checkpointType")),
                Safe(FirstString(item, "safeSummary", "summary", "subjectType"))
            }.Where(value => !string.IsNullOrWhiteSpace(value))));
        }

        WriteBoundary(output);
        WriteWarnings(root, output);
    }

    private static void WriteSingleText(JsonElement root, TextWriter output, string title, params string[] idNames)
    {
        var data = GetObject(root, "data");
        var item = FirstObject(data, "run", "workflowRun", "step", "workflowStep", "checkpoint", "workflowCheckpoint", "item") ?? data;
        var id = FirstString(item, idNames.Concat(["id"]).ToArray());

        output.WriteLine(title);
        output.WriteLine($"Id: {Safe(id)}");
        output.WriteLine($"Status: {Safe(FirstString(item, "status", "state", "stepStatus", "checkpointStatus"))}");
        output.WriteLine($"Type: {Safe(FirstString(item, "subjectType", "stepType", "checkpointType", "type"))}");
        output.WriteLine($"Summary: {Safe(FirstString(item, "safeSummary", "summary", "description"))}");
        output.WriteLine($"Evidence refs: {CountArray(item, "evidenceRefs", "evidence", "evidenceReferences")}");
        output.WriteLine($"Grounding refs: {CountArray(item, "groundingRefs", "grounding", "groundingReferences")}");
        WriteBoundary(output);
        WriteWarnings(root, output);
    }

    private static void WriteBoundary(TextWriter output)
    {
        output.WriteLine("Workflow inspection is read-only.");
        output.WriteLine("Statuses printed by the CLI are stored facts, not runtime actions.");
        output.WriteLine("The CLI does not create, update, or delete workflow records.");
        output.WriteLine("The CLI does not execute, continue, resume, retry, or dispatch workflow.");
        output.WriteLine("The CLI does not call agents, tools, or models.");
        output.WriteLine("The CLI does not mutate source, promote memory, create accepted memory, approve release, or satisfy approval requirements.");
    }

    private static void WriteWarnings(JsonElement root, TextWriter output)
    {
        foreach (var warning in EnumerateAnyArray(root, root, "warnings"))
        {
            if (warning.ValueKind == JsonValueKind.String)
                output.WriteLine($"Warning: {Safe(warning.GetString())}");
        }
    }

    private static JsonElement GetObject(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Object)
            return property;

        return default;
    }

    private static JsonElement? FirstObject(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Object)
                return property;
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateAnyArray(JsonElement primary, JsonElement fallback, params string[] names)
    {
        foreach (var parent in new[] { primary, fallback })
        {
            if (parent.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in parent.EnumerateArray())
                    yield return item;
                yield break;
            }

            if (parent.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var name in names)
            {
                if (!parent.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in property.EnumerateArray())
                    yield return item;
                yield break;
            }
        }
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
                continue;

            if (property.ValueKind == JsonValueKind.String)
                return property.GetString();

            if (property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return property.ToString();
        }

        return null;
    }

    private static int CountArray(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return 0;

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array)
                return property.GetArrayLength();
        }

        return 0;
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
        JsonElement? data = null)
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
            boundary = new
            {
                readOnly = true,
                createsWorkflowRecord = false,
                updatesWorkflowRecord = false,
                deletesWorkflowRecord = false,
                startsWorkflow = false,
                continuesWorkflow = false,
                resumesWorkflow = false,
                retriesWorkflow = false,
                dispatchesAgent = false,
                callsTool = false,
                callsModel = false,
                mutatesSource = false,
                promotesMemory = false,
                createsAcceptedMemory = false,
                approvesRelease = false,
                satisfiesApprovalRequirements = false,
                transfersAuthority = false
            },
            warnings = warnings.Select(Safe).ToArray(),
            errors
        };

        output.WriteLine(RedactSensitiveMarkers(JsonSerializer.Serialize(envelope, JsonOptions)));
    }

    private static string Safe(string? value) =>
        RedactSensitiveMarkers(value ?? string.Empty);

    private static string RedactSensitiveMarkers(string value)
    {
        var result = value;
        foreach (var marker in SensitiveMarkers)
            result = Regex.Replace(result, Regex.Escape(marker), Redaction, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return result;
    }

    private static bool EqualsToken(string? left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsJson(string? output) =>
        EqualsToken(output, "json");

    private sealed record RequiredValue(string? Value, string? Error);
}
