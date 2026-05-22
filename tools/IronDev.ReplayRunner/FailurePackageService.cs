using System.Text.Json;
using System.Text.Json.Nodes;

public static class FailurePackageService
{
    public static async Task<FailurePackageCommandResult?> TryWriteLatestAsync(
        string runsRoot,
        string? selectedRunId,
        JsonSerializerOptions options)
    {
        var replayFailure = FindLatestReplayFailure(runsRoot, selectedRunId);
        var testAgentFailure = FindLatestTestAgentFailure(runsRoot, selectedRunId);

        if (replayFailure is null && testAgentFailure is null)
            return null;

        var useTestAgent = testAgentFailure is not null &&
                           (replayFailure is null ||
                            testAgentFailure.ReportFile.LastWriteTimeUtc >= replayFailure.ResultFile.LastWriteTimeUtc);

        var runRoot = useTestAgent
            ? testAgentFailure!.ReportFile.DirectoryName!
            : Directory.GetParent(replayFailure!.ResultFile.DirectoryName!)!.FullName;
        var package = useTestAgent
            ? BuildTestAgentFailurePackage(testAgentFailure!, runRoot)
            : BuildReplayFailurePackage(replayFailure!, runRoot);

        var jsonPath = Path.Combine(runRoot, "failure-package.json");
        var markdownPath = Path.Combine(runRoot, "failure-package.md");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(package, options));
        await File.WriteAllTextAsync(markdownPath, BuildFailurePackageMarkdown(package));

        return new FailurePackageCommandResult
        {
            DogfoodRunId = package.DogfoodRunId,
            ScenarioId = package.ScenarioId,
            CaseId = package.CaseId,
            Prompt = package.Prompt,
            ExpectedIntent = package.ExpectedIntent,
            ActualIntent = package.ActualIntent,
            FailureReason = package.FailureReason,
            JsonPath = jsonPath,
            MarkdownPath = markdownPath,
            ReportPath = package.ReportPath,
            ReproCommand = package.ReproCommand,
            ValidationCommand = package.ValidationCommand
        };
    }

    private static ReplayFailure? FindLatestReplayFailure(string runsRoot, string? selectedRunId)
    {
        var resultFiles = Directory
            .EnumerateFiles(runsRoot, "replay-results.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => string.IsNullOrWhiteSpace(selectedRunId) ||
                           IsInsideSelectedRun(file.FullName, selectedRunId))
            .OrderByDescending(file => file.LastWriteTimeUtc);

        foreach (var file in resultFiles)
        {
            var text = File.ReadAllText(file.FullName);
            var results = JsonSerializer.Deserialize<List<ReplayCaseResult>>(text, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];
            var failed = results.FirstOrDefault(result => !result.Passed);
            if (failed is null)
                continue;

            var replayDirectory = file.DirectoryName!;
            var planPath = Path.Combine(replayDirectory, "replay-plan.json");
            ReplayCase? replayCase = null;
            ReplayPlan? plan = null;
            if (File.Exists(planPath))
            {
                plan = JsonSerializer.Deserialize<ReplayPlan>(
                    File.ReadAllText(planPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                replayCase = plan?.Cases.FirstOrDefault(item => string.Equals(item.CaseId, failed.CaseId, StringComparison.OrdinalIgnoreCase));
            }

            return new ReplayFailure(file, failed, replayCase, plan);
        }

        return null;
    }

    private static TestAgentFailure? FindLatestTestAgentFailure(string runsRoot, string? selectedRunId)
    {
        var reportFiles = Directory
            .EnumerateFiles(runsRoot, "test-agent-report.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => string.IsNullOrWhiteSpace(selectedRunId) ||
                           IsInsideSelectedRun(file.FullName, selectedRunId))
            .OrderByDescending(file => file.LastWriteTimeUtc);

        foreach (var file in reportFiles)
        {
            var text = File.ReadAllText(file.FullName);
            using var document = JsonDocument.Parse(text);
            var report = document.RootElement.Clone();
            var status = ReadJsonString(report, "status");
            var overall = ReadJsonString(report, "overall_result");
            var failures = ReadJsonInt(GetJsonPropertyOrDefault(report, "actual"), "steps_failed");
            var isFailure = failures > 0 ||
                            status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                            overall.Equals("FAILED", StringComparison.OrdinalIgnoreCase) ||
                            overall.Equals("PARTIAL_SUCCESS", StringComparison.OrdinalIgnoreCase);

            if (isFailure)
                return new TestAgentFailure(file, report);
        }

        return null;
    }

    private static bool IsInsideSelectedRun(string path, string selectedRunId)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}{selectedRunId}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{Path.AltDirectorySeparatorChar}{selectedRunId}{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static FailurePackage BuildReplayFailurePackage(ReplayFailure failure, string runRoot)
    {
        var result = failure.Result;
        var replayCase = failure.ReplayCase;
        var plan = failure.Plan;
        var replayPlanPath = Path.Combine(failure.ResultFile.DirectoryName!, "replay-plan.json");
        var seed = replayCase?.Seed;
        var reps = plan?.Cases.Count > 0 ? plan.Cases.Count : 1;
        var reproCommand = seed is null
            ? $"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -- \"{replayPlanPath}\""
            : $"powershell -NoProfile -ExecutionPolicy Bypass -File .\\tools\\dogfood\\Start-BookSellerReplay.ps1 -RunId {result.DogfoodRunId}-repro -Scenario .\\tools\\dogfood\\dogfood-scenarios\\BookSellerMvp.json -Reps {reps} -DryRun -StopOnFailure -Seed {seed} -RunnerCommand \"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj --\"";

        return new FailurePackage
        {
            DogfoodRunId = result.DogfoodRunId,
            ScenarioId = replayCase?.ScenarioId ?? plan?.ScenarioId ?? "Unknown",
            GoalId = replayCase?.ScenarioId ?? plan?.ScenarioId ?? "Unknown",
            Step = replayCase?.Step,
            PromptVariantId = result.CaseId,
            CaseId = result.CaseId,
            CaseName = result.Name,
            Workspace = result.Workspace,
            Prompt = result.Prompt,
            ExpectedIntent = result.ExpectedIntent,
            ActualIntent = result.ActualIntent,
            AllowsProseResponse = result.AllowsProseResponse,
            RequiresAction = result.RequiresAction,
            ContextReference = result.ContextReference,
            DraftCountMode = result.DraftCountMode,
            FailureReason = result.FailureReason,
            MatchedSignals = result.MatchedSignals,
            ResultPath = failure.ResultFile.FullName,
            ReportPath = string.Empty,
            FailedStepLogPath = string.Empty,
            ReplayPlanPath = replayPlanPath,
            RunRoot = runRoot,
            ExpectedJson = "{}",
            ActualJson = "{}",
            EvidencePaths = [failure.ResultFile.FullName],
            LikelyAreas = InferLikelyAreas(result).ToArray(),
            ReproCommand = reproCommand,
            ValidationCommand = "dotnet build .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -p:UseSharedCompilation=false -nr:false; dotnet test .\\IronDev.IntegrationTests\\IronDev.IntegrationTests.csproj --no-restore --filter \"ChatCommandRouter_CreateTickets_IsActionFirst|ChatCommandRouter_SaveDecision_IsActionFirst|ChatCommandRouter_CreatePlan_IsActionFirst|ChatCommandRouter_BuildTicketQuestion_AllowsProse\" -p:UseSharedCompilation=false -nr:false",
            SafetyRules = [
                "Replay defaults to dry-run.",
                "Do not modify target project files while repairing routing failures.",
                "Patch deterministic routing/context logic before weakening assertions.",
                "Rerun the same seed before running chaos batches."
            ],
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static FailurePackage BuildTestAgentFailurePackage(TestAgentFailure failure, string runRoot)
    {
        var report = failure.Report;
        var failedStep = FirstFailedStep(report);
        var command = ReadJsonString(failedStep, "command");
        var failureReason = ReadJsonString(failedStep, "summary");
        if (string.IsNullOrWhiteSpace(failureReason))
            failureReason = string.Join("; ", ReadStringArray(report, "critical_issues"));

        var runId = ReadJsonString(report, "test_run_id");
        var goalId = ReadJsonString(report, "goal_id");
        var trace = GetJsonPropertyOrDefault(report, "trace");
        var traceGroupId = ReadJsonString(trace, "trace_group_id");
        var stepNumber = ReadJsonInt(failedStep, "step");
        var action = ReadJsonString(failedStep, "action");
        var logPath = ReadJsonString(failedStep, "log_path");
        var planPath = ReadJsonString(report, "plan_path");
        var reproPlanPath = string.IsNullOrWhiteSpace(planPath)
            ? $"<PLAN_PATH_FOR_{goalId}>"
            : planPath;

        return new FailurePackage
        {
            DogfoodRunId = runId,
            ScenarioId = goalId,
            GoalId = goalId,
            Step = stepNumber == 0 ? null : stepNumber,
            PromptVariantId = string.IsNullOrWhiteSpace(action) ? "test-agent-step" : action,
            CaseId = string.IsNullOrWhiteSpace(action) ? "test-agent-step" : action,
            CaseName = action,
            Workspace = "TestAgent",
            Prompt = command,
            ExpectedIntent = "TestAgentStepSuccess",
            ActualIntent = ReadJsonString(failedStep, "status"),
            AllowsProseResponse = false,
            RequiresAction = true,
            ContextReference = traceGroupId,
            DraftCountMode = "N/A",
            FailureReason = failureReason,
            MatchedSignals = ReadStringArray(report, "critical_issues"),
            ResultPath = failure.ReportFile.FullName,
            ReportPath = failure.ReportFile.FullName,
            FailedStepLogPath = logPath,
            ReplayPlanPath = string.Empty,
            RunRoot = runRoot,
            ExpectedJson = ToCompactJson(GetJsonPropertyOrDefault(report, "expected")),
            ActualJson = ToCompactJson(GetJsonPropertyOrDefault(report, "actual")),
            EvidencePaths = ReadEvidencePaths(report),
            LikelyAreas = InferLikelyAreasFromTestAgentFailure(goalId, action, failureReason).ToArray(),
            ReproCommand = $"powershell -NoProfile -ExecutionPolicy Bypass -File .\\tools\\dogfood\\Invoke-TestAgentPlan.ps1 -PlanPath \"{reproPlanPath}\" -RunId {runId}-repro -Json",
            ValidationCommand = $"dotnet build .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -p:UseSharedCompilation=false -nr:false; powershell -NoProfile -ExecutionPolicy Bypass -File .\\tools\\dogfood\\Invoke-TestAgentPlan.ps1 -PlanPath \"{reproPlanPath}\" -RunId validation-after-fix -Json",
            SafetyRules = [
                "Do not fix without preserving the failing report and log paths as evidence.",
                "Do not weaken the Test Agent assertion just to make the package pass.",
                "Patch the smallest command, router, memory, or harness behaviour that explains the observed failure.",
                "Rerun the failing plan before broad regression plans.",
                "Replay and Test Agent commands default to dry-run unless a plan explicitly allows writes."
            ],
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static IEnumerable<string> InferLikelyAreasFromTestAgentFailure(string goalId, string action, string reason)
    {
        if (action.Contains("memory", StringComparison.OrdinalIgnoreCase) ||
            goalId.Contains("memory", StringComparison.OrdinalIgnoreCase))
        {
            yield return "tools/IronDev.ReplayRunner/Program.cs";
            yield return "IronDev.Infrastructure/Services/SemanticMemory";
            yield return "tools/dogfood/Invoke-TestAgentPlan.ps1";
            yield break;
        }

        if (action.Contains("builder", StringComparison.OrdinalIgnoreCase) ||
            goalId.Contains("builder", StringComparison.OrdinalIgnoreCase))
        {
            yield return "IronDev.Infrastructure/Builder";
            yield return "tools/IronDev.ReplayRunner/Program.cs";
            yield return "tools/dogfood/Invoke-TestAgentPlan.ps1";
            yield break;
        }

        if (reason.Contains("schema", StringComparison.OrdinalIgnoreCase))
        {
            yield return "tools/dogfood/TestAgentReport.schema.json";
            yield return "tools/dogfood/Invoke-TestAgentPlan.ps1";
            yield break;
        }

        yield return "tools/dogfood/Invoke-TestAgentPlan.ps1";
        yield return "tools/IronDev.ReplayRunner/Program.cs";
    }

    private static IEnumerable<string> InferLikelyAreas(ReplayCaseResult result)
    {
        if (result.AllowsProseResponse && result.ExpectedIntent.Contains("Ticket", StringComparison.OrdinalIgnoreCase))
        {
            yield return "IronDev.Infrastructure/Services/ChatIntentParser.cs";
            yield return "IronDev.Infrastructure/Services/ChatCommandRouter.cs";
            yield break;
        }

        if (result.ExpectedIntent.Contains("Discussion", StringComparison.OrdinalIgnoreCase) ||
            result.ExpectedIntent.Contains("Document", StringComparison.OrdinalIgnoreCase))
        {
            yield return "IronDev.Infrastructure/Services/ChatCommandRouter.cs";
            yield break;
        }

        yield return "tools/IronDev.ReplayRunner/Program.cs";
    }

    private static string BuildFailurePackageMarkdown(FailurePackage package)
    {
        return $"""
        # Codex Failure Package

        DogfoodRunId: {package.DogfoodRunId}
        ScenarioId: {package.ScenarioId}
        GoalId: {package.GoalId}
        CaseId: {package.CaseId}
        Step: {package.Step?.ToString() ?? "Unknown"}
        Workspace: {package.Workspace}

        ## Prompt

        ```text
        {package.Prompt}
        ```

        ## Expected

        ```text
        Intent: {package.ExpectedIntent}
        ExpectedJson: {package.ExpectedJson}
        ```

        ## Actual

        ```text
        Intent: {package.ActualIntent}
        AllowsProseResponse: {package.AllowsProseResponse}
        RequiresAction: {package.RequiresAction}
        ContextReference: {package.ContextReference}
        DraftCountMode: {package.DraftCountMode}
        Failure: {package.FailureReason}
        ActualJson: {package.ActualJson}
        ```

        ## Evidence

        ResultPath: {package.ResultPath}
        ReportPath: {package.ReportPath}
        FailedStepLogPath: {package.FailedStepLogPath}

        {string.Join(Environment.NewLine, package.EvidencePaths.Select(path => $"- {path}"))}

        ## Likely Areas

        {string.Join(Environment.NewLine, package.LikelyAreas.Select(area => $"- {area}"))}

        ## Repro

        ```powershell
        {package.ReproCommand}
        ```

        ## Validation

        ```powershell
        {package.ValidationCommand}
        ```

        ## Safety Rules

        {string.Join(Environment.NewLine, package.SafetyRules.Select(rule => $"- {rule}"))}
        """;
    }

    private static JsonElement FirstFailedStep(JsonElement report)
    {
        if (!report.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            return default;

        foreach (var step in steps.EnumerateArray())
        {
            if (ReadJsonString(step, "status").Equals("FAILED", StringComparison.OrdinalIgnoreCase))
                return step.Clone();
        }

        return default;
    }

    private static IReadOnlyList<string> ReadEvidencePaths(JsonElement report)
    {
        if (!report.TryGetProperty("evidence", out var evidence) || evidence.ValueKind != JsonValueKind.Array)
            return [];

        return evidence.EnumerateArray()
            .Select(item => ReadJsonString(item, "path"))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static string ReadJsonString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !element.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }

    private static int ReadJsonInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined ||
            !element.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
            return parsed;

        return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
    }

    private static string ToCompactJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
            return "{}";

        return JsonSerializer.Serialize(JsonNode.Parse(element.GetRawText()), new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonElement GetJsonPropertyOrDefault(JsonElement element, string propertyName)
    {
        return element.ValueKind != JsonValueKind.Undefined &&
               element.TryGetProperty(propertyName, out var value)
            ? value
            : default;
    }
}

public sealed record ReplayFailure(
    FileInfo ResultFile,
    ReplayCaseResult Result,
    ReplayCase? ReplayCase,
    ReplayPlan? Plan);

public sealed record TestAgentFailure(
    FileInfo ReportFile,
    JsonElement Report);

public sealed class FailurePackageCommandResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string ScenarioId { get; init; } = string.Empty;
    public string CaseId { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string ExpectedIntent { get; init; } = string.Empty;
    public string ActualIntent { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public string JsonPath { get; init; } = string.Empty;
    public string MarkdownPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string ReproCommand { get; init; } = string.Empty;
    public string ValidationCommand { get; init; } = string.Empty;
}

public sealed class FailurePackage
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string ScenarioId { get; init; } = string.Empty;
    public string GoalId { get; init; } = string.Empty;
    public int? Step { get; init; }
    public string PromptVariantId { get; init; } = string.Empty;
    public string CaseId { get; init; } = string.Empty;
    public string CaseName { get; init; } = string.Empty;
    public string Workspace { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string ExpectedIntent { get; init; } = string.Empty;
    public string ActualIntent { get; init; } = string.Empty;
    public bool AllowsProseResponse { get; init; }
    public bool RequiresAction { get; init; }
    public string ContextReference { get; init; } = string.Empty;
    public string DraftCountMode { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public IReadOnlyList<string> MatchedSignals { get; init; } = [];
    public string ResultPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string FailedStepLogPath { get; init; } = string.Empty;
    public string ReplayPlanPath { get; init; } = string.Empty;
    public string RunRoot { get; init; } = string.Empty;
    public string ExpectedJson { get; init; } = string.Empty;
    public string ActualJson { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> LikelyAreas { get; init; } = [];
    public string ReproCommand { get; init; } = string.Empty;
    public string ValidationCommand { get; init; } = string.Empty;
    public IReadOnlyList<string> SafetyRules { get; init; } = [];
    public DateTimeOffset CreatedAtUtc { get; init; }
}
