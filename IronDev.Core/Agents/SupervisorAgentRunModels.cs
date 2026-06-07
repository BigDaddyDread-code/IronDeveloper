using System;
using System.Threading;

namespace IronDev.Core.Agents;

public interface ISupervisorAgentRunService
{
    Task<SupervisorAgentRunResult> RunAsync(
        SupervisorAgentRunRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record SupervisorAgentRunRequest
{
    public required string Project { get; init; }
    public required string Query { get; init; }
    public required string PlanPath { get; init; }
    public required string RunId { get; init; }
    public bool LiveLlm { get; init; }
}

public sealed record SupervisorAgentRunResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public string? TraceId { get; init; }
    public required AgentRunSupervisorContractData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public required int ExitCode { get; init; }
}

public sealed record AgentRunSupervisorContractEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required AgentRunSupervisorContractData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record AgentRunSupervisorContractData
{
    public required string Agent { get; init; }
    public string? RunId { get; init; }
    public required string Project { get; init; }
    public required string Query { get; init; }
    public required string PlanPath { get; init; }
    public required string AgentStatus { get; init; }
    public required int ExitCode { get; init; }
    public required string Decision { get; init; }
    public string? DecisionReason { get; init; }
    public required AgentRunSupervisorTesterData Tester { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> CommandsRun { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public SupervisorFailurePackage? FailurePackage { get; init; }
}

public sealed record AgentRunSupervisorTesterData
{
    public string? RunId { get; init; }
    public string? TraceId { get; init; }
    public required string CommandStatus { get; init; }
    public required string RunStatus { get; init; }
    public required AgentRunSupervisorGovernanceData Governance { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record AgentRunSupervisorGovernanceData
{
    public required string Decision { get; init; }
    public required string ApprovalDecision { get; init; }
    public string? BlockedReason { get; init; }
    public required bool RequiresHumanApproval { get; init; }
}

public sealed record SupervisorFailurePackage
{
    public required string RunId { get; init; }
    public required string Status { get; init; }
    public required string Decision { get; init; }
    public string? DecisionReason { get; init; }
    public string? TesterCommandStatus { get; init; }
    public string? TesterRunStatus { get; init; }
    public string? BlockedReason { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> CommandsRun { get; init; } = [];
    public required string RecommendedNextAction { get; init; }
    public SupervisorRecoveryPlan? RecoveryPlan { get; init; }
}

public sealed record SupervisorRecoveryPlan
{
    public required string RunId { get; init; }
    public required string Status { get; init; }
    public required string SourceFailureStatus { get; init; }
    public required string ProblemSummary { get; init; }
    public IReadOnlyList<string> EvidenceToInspect { get; init; } = [];
    public IReadOnlyList<string> SuspectedCauses { get; init; } = [];
    public IReadOnlyList<string> ProposedSteps { get; init; } = [];
    public IReadOnlyList<string> StopConditions { get; init; } = [];
    public IReadOnlyList<string> RequiredHumanChecks { get; init; } = [];
    public required bool AllowsPatching { get; init; }
    public required bool AllowsExecution { get; init; }
}
