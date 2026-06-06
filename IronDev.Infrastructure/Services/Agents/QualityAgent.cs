using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class QualityAgent : StaticIronDevAgent
{
    private const string DefaultPlanPath = "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json";

    private readonly IAgentModelResolver _modelResolver;
    private readonly string _repoRoot;
    private readonly IAgentLlmClient? _llmClient;
    private readonly IGovernedAgentProcessExecutor _processExecutor;

    public QualityAgent(
        AgentDefinition definition,
        IAgentModelResolver modelResolver,
        string repoRoot,
        IAgentLlmClient? llmClient = null,
        IGovernedAgentProcessExecutor? processExecutor = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _repoRoot = repoRoot;
        _llmClient = llmClient;
        _processExecutor = processExecutor ?? new GovernedAgentProcessExecutor();
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var planPath = request.Inputs.TryGetValue("plan_path", out var configuredPlanPath) &&
                       !string.IsNullOrWhiteSpace(configuredPlanPath)
            ? configuredPlanPath
            : DefaultPlanPath;
        var runId = string.IsNullOrWhiteSpace(request.DogfoodRunId)
            ? $"QualityAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            : request.DogfoodRunId;
        var liveLlmRequested = ReadBoolInput(request, "live_llm");

        var scriptPath = Path.Combine(_repoRoot, "tools", "dogfood", "Invoke-TestAgentPlan.ps1");
        var fullPlanPath = AgentPlanPathResolver.ResolveApprovedPlanPath(_repoRoot, planPath, AgentName);
        var arguments = new[]
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath,
            "-PlanPath",
            fullPlanPath,
            "-RunId",
            runId,
            "-Json"
        };

        var processResult = await RunProcessAsync("powershell", arguments, ct);
        var exitCode = processResult.ExitCode;
        var stdout = processResult.Stdout;
        var stderr = processResult.Stderr;

        var report = BuildQualityReport(stdout, stderr, planPath, runId, exitCode);
        var prompt = BuildPrompt(report);
        report.LlmIntelligence = BuildLlmEvidence(
            profile,
            prompt,
            liveLlmRequested,
            await ResolveLlmResultAsync(profile, prompt, liveLlmRequested, request, ct));
        var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        return new AgentResult
        {
            AgentName = AgentName,
            Status = exitCode == 0 ? AgentRunStatus.Succeeded : AgentRunStatus.Failed,
            Summary = report.Summary,
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = exitCode,
            OutputJson = reportJson,
            CommandsRun = [processResult.Command],
            EvidencePaths = report.EvidencePaths.Concat(processResult.EvidencePaths).ToArray(),
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<GovernedAgentProcessResult> RunProcessAsync(
        string fileName, string[] arguments, CancellationToken ct)
    {
        return await _processExecutor.ExecuteAsync(new GovernedAgentProcessRequest
        {
            ToolCallId = ResolveToolCallId(),
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = _repoRoot,
            Purpose = "QualityAgent deterministic quality command"
        }, ct);
    }

    private async Task<AgentLlmCallResult> ResolveLlmResultAsync(
        ModelProfile profile,
        string prompt,
        bool liveLlmRequested,
        AgentRequest request,
        CancellationToken ct)
    {
        if (request.Inputs.TryGetValue("llm_response", out var providedResponse) &&
            !string.IsNullOrWhiteSpace(providedResponse))
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = true,
                InvocationMode = "provided_llm_response",
                ResponseText = providedResponse
            };
        }

        if (!liveLlmRequested)
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = true,
                InvocationMode = "llm_ready_deterministic_fallback",
                ResponseText = "No live model response supplied; deterministic quality gate evidence remained authoritative."
            };
        }

        if (_llmClient is null)
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = false,
                InvocationMode = "live_model_requested_without_client_fallback",
                ErrorMessage = "No governed agent LLM client was configured."
            };
        }

        return await _llmClient.CompleteAsync(profile, prompt, ct);
    }

    private static QualityReport BuildQualityReport(string stdout, string stderr, string planPath, string runId, int exitCode)
    {
        if (exitCode == -1)
        {
            var timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS"), out var parsed) ? parsed : 300;
            return new QualityReport
            {
                PlanPath = planPath,
                DogfoodRunId = runId,
                Status = "failed",
                Summary = $"QualityAgent subprocess timed out after {timeoutSeconds}s.",
                BuildSucceeded = false,
                FocusedTestsSucceeded = false,
                FormatSucceeded = false,
                PackageAuditSucceeded = false,
                CodeStandardsSucceeded = false,
                EvidencePaths = [],
                Boundary = "037 wraps the deterministic code standards/toolchain gate only; it does not perform LLM code review or patch code."
            };
        }

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            var steps = root.TryGetProperty("steps", out var stepsElement) && stepsElement.ValueKind == JsonValueKind.Array
                ? stepsElement.EnumerateArray().ToArray()
                : [];
            var codeStandardsStep = steps.FirstOrDefault(step =>
                string.Equals(ReadString(step, "action"), "code_standards_check", StringComparison.OrdinalIgnoreCase));
            var codeStandards = codeStandardsStep.ValueKind == JsonValueKind.Object &&
                                codeStandardsStep.TryGetProperty("parsed", out var parsed)
                ? parsed
                : default;

            var warningCount = codeStandards.ValueKind == JsonValueKind.Object &&
                               codeStandards.TryGetProperty("warning_count", out var warningCountElement) &&
                               warningCountElement.TryGetInt32(out var parsedWarningCount)
                ? parsedWarningCount
                : 0;
            var errorCount = codeStandards.ValueKind == JsonValueKind.Object &&
                             codeStandards.TryGetProperty("error_count", out var errorCountElement) &&
                             errorCountElement.TryGetInt32(out var parsedErrorCount)
                ? parsedErrorCount
                : 0;

            var reportStatus = ReadString(root, "status");
            return new QualityReport
            {
                PlanPath = planPath,
                DogfoodRunId = runId,
                Status = exitCode == 0 ? "passed" : "failed",
                Summary = $"QualityAgent ran {ReadString(root, "summary")} warnings={warningCount}; errors={errorCount}.",
                BuildSucceeded = StepSucceeded(steps, "dotnet_build"),
                FocusedTestsSucceeded = StepSucceeded(steps, "dotnet_test"),
                FormatSucceeded = StepSucceeded(steps, "format_check"),
                PackageAuditSucceeded = StepSucceeded(steps, "package_audit"),
                CodeStandardsSucceeded = StepSucceeded(steps, "code_standards_check"),
                WarningCount = warningCount,
                ErrorCount = errorCount,
                GateStatus = reportStatus,
                EvidencePaths = ExtractEvidencePaths(root),
                Boundary = "037 wraps the deterministic code standards/toolchain gate only; it does not perform LLM code review or patch code."
            };
        }
        catch (JsonException)
        {
            return new QualityReport
            {
                PlanPath = planPath,
                DogfoodRunId = runId,
                Status = "failed",
                Summary = string.IsNullOrWhiteSpace(stderr)
                    ? "QualityAgent could not parse quality report."
                    : stderr.Trim().Split(Environment.NewLine).FirstOrDefault() ?? "QualityAgent failed.",
                Boundary = "037 wraps the deterministic code standards/toolchain gate only; it does not perform LLM code review or patch code."
            };
        }
    }

    private static bool StepSucceeded(JsonElement[] steps, string action) =>
        steps.Any(step =>
            string.Equals(ReadString(step, "action"), action, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ReadString(step, "status"), "SUCCESS", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ExtractEvidencePaths(JsonElement root)
    {
        if (!root.TryGetProperty("evidence", out var evidence) || evidence.ValueKind != JsonValueKind.Array)
            return [];

        return evidence.EnumerateArray()
            .Select(item => item.TryGetProperty("path", out var path) ? path.GetString() : null)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
    }

    private static string ReadString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool ReadBoolInput(AgentRequest request, string key) =>
        request.Inputs.TryGetValue(key, out var value) &&
        bool.TryParse(value, out var parsed) &&
        parsed;

    private string ResolveToolCallId() =>
        $"{AgentName}-quality-{Guid.NewGuid():N}";

    private static string BuildPrompt(QualityReport report) =>
        $"""
        You are QualityAgent / KilljoyAgent for IronDev/IDA.
        Review this deterministic quality gate evidence and return concise JSON with risk notes, debt notes, and recommended follow-up questions.
        Do not refactor code, hide warnings, weaken standards, patch files, approve writes, or override deterministic gate results.
        Status: {report.Status}
        Summary: {report.Summary}
        Warnings: {report.WarningCount}
        Errors: {report.ErrorCount}
        BuildSucceeded: {report.BuildSucceeded}
        FocusedTestsSucceeded: {report.FocusedTestsSucceeded}
        FormatSucceeded: {report.FormatSucceeded}
        PackageAuditSucceeded: {report.PackageAuditSucceeded}
        CodeStandardsSucceeded: {report.CodeStandardsSucceeded}
        """;

    private static object BuildLlmEvidence(ModelProfile profile, string prompt, bool liveLlmRequested, AgentLlmCallResult result) => new
    {
        modelProfile = profile.Name,
        profileProvider = profile.Provider,
        profileModel = profile.Model,
        prompt,
        invocationMode = result.InvocationMode,
        liveLlmRequested,
        wasAttempted = result.WasAttempted,
        wasSuccessful = result.WasSuccessful,
        durationMs = result.DurationMs,
        modelSummary = string.IsNullOrWhiteSpace(result.ResponseText)
            ? result.WasAttempted
                ? "Live model call did not return usable content; deterministic quality gate evidence remained authoritative."
                : "No live model response supplied; deterministic quality gate evidence remained authoritative."
            : result.ResponseText,
        error = result.WasSuccessful ? string.Empty : result.ErrorMessage,
        boundary = "Live QualityAgent output is advisory debt/risk commentary only. Deterministic quality gates remain authoritative."
    };

    private sealed record QualityReport
    {
        public string PlanPath { get; init; } = string.Empty;
        public string DogfoodRunId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public bool BuildSucceeded { get; init; }
        public bool FocusedTestsSucceeded { get; init; }
        public bool FormatSucceeded { get; init; }
        public bool PackageAuditSucceeded { get; init; }
        public bool CodeStandardsSucceeded { get; init; }
        public int WarningCount { get; init; }
        public int ErrorCount { get; init; }
        public string GateStatus { get; init; } = string.Empty;
        public IReadOnlyList<string> EvidencePaths { get; init; } = [];
        public string Boundary { get; init; } = string.Empty;
        public object? LlmIntelligence { get; set; }
    }
}
