namespace IronDev.Core.Agents;

public sealed record GovernedAgentToolCapability
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool RequiresMutation { get; init; }
    public bool RequiresHumanApproval { get; init; }
    public IReadOnlyList<string> SupportedRuntimes { get; init; } = [];
    public IReadOnlyList<string> EvidenceKinds { get; init; } = [];
    public string Boundary { get; init; } = string.Empty;
}

public sealed record ProjectRuntimeProfile
{
    public required string Project { get; init; }
    public required string Runtime { get; init; }
    public string SourceRoot { get; init; } = string.Empty;
    public string BuildCommand { get; init; } = string.Empty;
    public string TestCommand { get; init; } = string.Empty;
    public IReadOnlyList<string> SupportedTools { get; init; } = [];
    public string Boundary { get; init; } = string.Empty;
}

public sealed record AgentToolRequest
{
    public required string RequestId { get; init; }
    public required string RequestedBy { get; init; }
    public required string ToolName { get; init; }
    public required string Project { get; init; }
    public required string Goal { get; init; }
    public required string Reason { get; init; }
    public bool RequiresMutation { get; init; }
    public string Runtime { get; init; } = "dotnet";
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AgentToolResult
{
    public required string RequestId { get; init; }
    public required string ToolName { get; init; }
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public int ExitCode { get; init; }
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Boundary { get; init; } = string.Empty;
}

public sealed record AgentLoopStageTrace
{
    public required string StageName { get; init; }
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record EvidenceValidationFinding
{
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string EvidenceRef { get; init; } = string.Empty;
}

public sealed record EvidenceValidationResult
{
    public required string Status { get; init; }
    public IReadOnlyList<string> RequiredEvidence { get; init; } = [];
    public IReadOnlyList<string> PresentEvidence { get; init; } = [];
    public IReadOnlyList<string> MissingEvidence { get; init; } = [];
    public IReadOnlyList<EvidenceValidationFinding> Findings { get; init; } = [];
    public string Boundary { get; init; } = "Evidence validation reports sufficiency only. It does not approve writes or mutate memory.";
}

public sealed record HumanEscalationGate
{
    public required string Decision { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<string> RequiredApprovals { get; init; } = [];
    public IReadOnlyList<string> BlockedActions { get; init; } = [];
    public string Boundary { get; init; } = "Human escalation gates decide what needs review. They do not execute actions.";
}

public sealed record AgentLoopTrace
{
    public required string TraceId { get; init; }
    public required string RunId { get; init; }
    public required string Project { get; init; }
    public required string Goal { get; init; }
    public string Runtime { get; init; } = "dotnet";
    public IReadOnlyList<AgentLoopStageTrace> Stages { get; init; } = [];
    public IReadOnlyList<AgentToolRequest> ToolRequests { get; init; } = [];
    public IReadOnlyList<AgentToolResult> ToolResults { get; init; } = [];
    public EvidenceValidationResult? EvidenceValidation { get; init; }
    public HumanEscalationGate? HumanEscalation { get; init; }
    public IReadOnlyList<ProjectRuntimeProfile> RuntimeProfiles { get; init; } = [];
    public string Boundary { get; init; } = "Governed loop trace only. Agents request capabilities; they do not execute raw commands directly.";
}

public sealed record PlannerCriticLoopResult
{
    public required string Command { get; init; }
    public required string Status { get; init; }
    public required string RunId { get; init; }
    public required string TraceId { get; init; }
    public required string Project { get; init; }
    public required string Goal { get; init; }
    public required string Summary { get; init; }
    public required AgentLoopTrace Trace { get; init; }
    public required object PlannerDraft { get; init; }
    public required object CriticReview { get; init; }
    public required object RevisedPlan { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public string Recommendation { get; init; } = string.Empty;
    public string Boundary { get; init; } = "Planner/Critic loop is read/test/report only. No writes, patches, memory mutation, ticket creation, or self-approval.";
}
