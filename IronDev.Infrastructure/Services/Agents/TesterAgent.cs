using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class TesterAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;
    private readonly IGovernedAgentProcessExecutor _processExecutor;
    private readonly string _repoRoot;

    public TesterAgent(
        AgentDefinition definition,
        IAgentModelResolver modelResolver,
        string repoRoot,
        IGovernedAgentProcessExecutor? processExecutor = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _repoRoot = repoRoot;
        _processExecutor = processExecutor ?? new GovernedAgentProcessExecutor();
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var planPath = RequireInput(request, "plan_path");
        var runId = string.IsNullOrWhiteSpace(request.DogfoodRunId)
            ? $"TesterAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"
            : request.DogfoodRunId;

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

        var processResult = await ExecutePowershellAsync(arguments, ct);
        var stdout = processResult.Stdout;
        var stderr = processResult.Stderr;
        var output = string.IsNullOrWhiteSpace(stderr)
            ? stdout
            : stdout + Environment.NewLine + stderr;

        var evidencePaths = ExtractEvidencePaths(stdout);
        evidencePaths = [.. evidencePaths, .. processResult.EvidencePaths];
        var summary = ExtractSummary(stdout);

        return new AgentResult
        {
            AgentName = AgentName,
            Status = processResult.ExitCode == 0 ? AgentRunStatus.Succeeded : AgentRunStatus.Failed,
            Summary = summary,
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = processResult.ExitCode,
            OutputJson = output,
            CommandsRun = [processResult.Command],
            EvidencePaths = evidencePaths,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string RequireInput(AgentRequest request, string key)
    {
        if (request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException($"TesterAgent requires input '{key}'.");
    }

    private static IReadOnlyList<string> ExtractEvidencePaths(string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (!document.RootElement.TryGetProperty("evidence", out var evidence) ||
                evidence.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return evidence.EnumerateArray()
                .Select(item => item.TryGetProperty("path", out var path) ? path.GetString() : null)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ExtractSummary(string stdout)
    {
        try
        {
            using var document = JsonDocument.Parse(stdout);
            if (document.RootElement.TryGetProperty("summary", out var summary))
                return summary.GetString() ?? "TesterAgent completed.";
        }
        catch (JsonException)
        {
            // Fall through to a compact raw-output summary.
        }

        return string.IsNullOrWhiteSpace(stdout)
            ? "TesterAgent completed with no stdout."
            : stdout.Trim().Split(Environment.NewLine).FirstOrDefault() ?? "TesterAgent completed.";
    }

    private async Task<GovernedAgentProcessResult> ExecutePowershellAsync(
        string[] arguments,
        CancellationToken ct)
    {
        return await _processExecutor.ExecuteAsync(new GovernedAgentProcessRequest
        {
            ToolCallId = ResolveToolCallId(),
            FileName = "powershell",
            Arguments = arguments,
            WorkingDirectory = _repoRoot,
            Purpose = "TesterAgent plan execution"
        }, ct);
    }

    private string ResolveToolCallId() =>
        $"{AgentName}-powershell-{Guid.NewGuid():N}";
}
