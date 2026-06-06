using System.Diagnostics;
using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class AgentRunner : IAgentRunner
{
    private readonly IAgentRegistry _registry;
    private readonly IAgentGovernanceGate _governanceGate;

    public AgentRunner(IAgentRegistry registry, IAgentGovernanceGate? governanceGate = null)
    {
        _registry = registry;
        _governanceGate = governanceGate ?? new AgentGovernanceGate();
    }

    public async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var definition = _registry.GetDefinition(request.AgentName);
        var governance = _governanceGate.Evaluate(definition, request);

        if (!governance.IsAllowed)
        {
            stopwatch.Stop();
            return BuildBlockedResult(
                request,
                definition,
                governance,
                startedAtUtc,
                stopwatch.ElapsedMilliseconds);
        }

        var agent = _registry.GetAgent(request.AgentName);
        var result = await agent.RunAsync(request, ct);
        stopwatch.Stop();

        return StampResult(request, definition, governance, result, startedAtUtc, stopwatch.ElapsedMilliseconds);
    }

    private static AgentResult BuildBlockedResult(
        AgentRequest request,
        AgentDefinition definition,
        AgentGovernanceDecision governance,
        DateTimeOffset startedAtUtc,
        long durationMs)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        var output = new
        {
            decision = "Block",
            reason = governance.Reason,
            agent = definition.Name,
            goalId = request.GoalId,
            dogfoodRunId = request.DogfoodRunId,
            requestedTools = request.RequestedTools,
            requestedToolCalls = request.RequestedToolCalls.Select(call => new
            {
                call.ToolName,
                impact = call.Impact.ToString(),
                call.RequiresApproval,
                call.AllowsFileWrites,
                call.AllowsProcessExecution,
                call.AllowsWorkspaceMutation,
                call.EvidenceRequired,
                call.ApprovalScope
            }),
            allowedTools = definition.AllowedTools,
            approvalDecision = governance.ApprovalDecision.ToString(),
            dryRunOnly = request.DryRunOnly,
            violations = governance.Violations,
            boundary = "AgentRunner enforces declared AgentDefinition.AllowedTools and typed agent governance before dispatch."
        };

        return new AgentResult
        {
            AgentName = definition.Name,
            Status = AgentRunStatus.Blocked,
            GoalId = request.GoalId,
            DogfoodRunId = request.DogfoodRunId,
            Summary = governance.Reason,
            ModelProfileName = definition.DefaultModelProfile,
            ExitCode = 1,
            OutputJson = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }),
            RequestedTools = request.RequestedTools,
            AllowedTools = definition.AllowedTools,
            ApprovalDecision = governance.ApprovalDecision,
            ApprovalFailureReason = governance.Reason,
            TraceId = request.ProposalId,
            WasDryRun = request.DryRunOnly,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationMs = durationMs
        };
    }

    private static AgentResult StampResult(
        AgentRequest request,
        AgentDefinition definition,
        AgentGovernanceDecision governance,
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
            ToolCalls = result.ToolCalls,
            ApprovalDecision = governance.ApprovalDecision,
            ApprovalFailureReason = result.ApprovalFailureReason,
            TraceId = string.IsNullOrWhiteSpace(result.TraceId) ? request.ProposalId : result.TraceId,
            WasDryRun = request.DryRunOnly,
            MutatedState = result.MutatedState,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = result.CompletedAtUtc,
            DurationMs = durationMs
        };
}
