using System.Diagnostics;
using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class AgentRunner : IAgentRunner
{
    private readonly IAgentRegistry _registry;

    public AgentRunner(IAgentRegistry registry)
    {
        _registry = registry;
    }

    public async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var definition = _registry.GetDefinition(request.AgentName);
        var disallowedTools = request.RequestedTools
            .Where(tool => !definition.AllowedTools.Contains(tool, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tool => tool, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!definition.Enabled)
        {
            stopwatch.Stop();
            return BuildBlockedResult(
                request,
                definition,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                $"Agent '{definition.Name}' is disabled.");
        }

        if (disallowedTools.Length > 0)
        {
            stopwatch.Stop();
            return BuildBlockedResult(
                request,
                definition,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds,
                $"Agent '{definition.Name}' requested tools outside its declared boundary: {string.Join(", ", disallowedTools)}.");
        }

        var agent = _registry.GetAgent(request.AgentName);
        var result = await agent.RunAsync(request, ct);
        stopwatch.Stop();

        return StampResult(request, definition, result, startedAtUtc, stopwatch.ElapsedMilliseconds);
    }

    private static AgentResult BuildBlockedResult(
        AgentRequest request,
        AgentDefinition definition,
        DateTimeOffset startedAtUtc,
        long durationMs,
        string summary)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        var output = new
        {
            decision = "Block",
            reason = summary,
            agent = definition.Name,
            goalId = request.GoalId,
            dogfoodRunId = request.DogfoodRunId,
            requestedTools = request.RequestedTools,
            allowedTools = definition.AllowedTools,
            boundary = "AgentRunner enforces declared AgentDefinition.AllowedTools before dispatch."
        };

        return new AgentResult
        {
            AgentName = definition.Name,
            Status = AgentRunStatus.Blocked,
            GoalId = request.GoalId,
            DogfoodRunId = request.DogfoodRunId,
            Summary = summary,
            ModelProfileName = definition.DefaultModelProfile,
            ExitCode = 1,
            OutputJson = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }),
            RequestedTools = request.RequestedTools,
            AllowedTools = definition.AllowedTools,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationMs = durationMs
        };
    }

    private static AgentResult StampResult(
        AgentRequest request,
        AgentDefinition definition,
        AgentResult result,
        DateTimeOffset startedAtUtc,
        long durationMs) =>
        new()
        {
            AgentName = result.AgentName,
            Status = result.Status,
            GoalId = request.GoalId,
            DogfoodRunId = request.DogfoodRunId,
            Summary = result.Summary,
            ModelProfileName = result.ModelProfileName,
            Provider = result.Provider,
            Model = result.Model,
            ExitCode = result.ExitCode,
            OutputJson = result.OutputJson,
            RequestedTools = request.RequestedTools,
            AllowedTools = definition.AllowedTools,
            CommandsRun = result.CommandsRun,
            EvidencePaths = result.EvidencePaths,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = result.CompletedAtUtc,
            DurationMs = durationMs
        };
}
