using System.Diagnostics;
using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class QualityAgent : StaticIronDevAgent
{
    private const string DefaultPlanPath = "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json";

    private readonly IAgentModelResolver _modelResolver;
    private readonly string _repoRoot;

    public QualityAgent(AgentDefinition definition, IAgentModelResolver modelResolver, string repoRoot)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _repoRoot = repoRoot;
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

        var scriptPath = Path.Combine(_repoRoot, "tools", "dogfood", "Invoke-TestAgentPlan.ps1");
        var fullPlanPath = Path.GetFullPath(Path.Combine(_repoRoot, planPath));
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

        var command = "powershell " + string.Join(" ", arguments.Select(QuoteIfNeeded));
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                WorkingDirectory = _repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var report = BuildQualityReport(stdout, stderr, planPath, runId, process.ExitCode);
        var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        return new AgentResult
        {
            AgentName = AgentName,
            Status = process.ExitCode == 0 ? AgentRunStatus.Succeeded : AgentRunStatus.Failed,
            Summary = report.Summary,
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = process.ExitCode,
            OutputJson = reportJson,
            CommandsRun = [command],
            EvidencePaths = report.EvidencePaths,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static QualityReport BuildQualityReport(string stdout, string stderr, string planPath, string runId, int exitCode)
    {
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

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

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
    }
}
