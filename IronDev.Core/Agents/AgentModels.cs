namespace IronDev.Core.Agents;

public enum AgentRunStatus
{
    Succeeded,
    Failed,
    Blocked,
    Skipped
}

public enum AgentActionImpact
{
    Unknown,
    ReadOnly,
    Diagnostic,
    ProcessExecution,
    WorkspaceMutation,
    MemoryMutation,
    ExternalNetwork
}

public enum AgentApprovalDecision
{
    NotRequired,
    Missing,
    Approved,
    Rejected,
    Expired,
    Invalid
}

public sealed class AgentDefinition
{
    public required string Name { get; init; }
    public required string Purpose { get; init; }
    public required string DefaultModelProfile { get; init; }
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
}

public sealed class ModelProfile
{
    public required string Name { get; init; }
    public string Provider { get; init; } = "OpenAI";
    public required string Model { get; init; }
    public string? BaseUrl { get; init; }
    public string? ApiKeyEnvironmentVariable { get; init; }
    public double Temperature { get; init; } = 0.2;
    public int MaxOutputTokens { get; init; } = 2000;
    public decimal? MaxCostPerRun { get; init; }
    public int TimeoutSeconds { get; init; } = 60;
}

public sealed class AgentRequest
{
    public required string AgentName { get; init; }
    public string GoalId { get; init; } = string.Empty;
    public string DogfoodRunId { get; init; } = string.Empty;
    public IReadOnlyList<string> RequestedTools { get; init; } = [];
    public IReadOnlyList<AgentToolCallRequest> RequestedToolCalls { get; init; } = [];
    public AgentApprovalEvidence? ApprovalEvidence { get; init; }
    public string ProposalId { get; init; } = string.Empty;
    public string ProposalHash { get; init; } = string.Empty;
    public bool DryRunOnly { get; init; } = true;
    public IReadOnlyDictionary<string, string> Inputs { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class AgentEvidenceItem
{
    public required string EvidenceId { get; init; }
    public required string Kind { get; init; }
    public string Path { get; init; } = string.Empty;
    public required string Sha256 { get; init; }
    public string ProducedBy { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class AgentApprovalEvidence
{
    public required string ApprovalId { get; init; }
    public required string ProposalId { get; init; }
    public required string ProposalHash { get; init; }
    public string ApprovedBy { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public AgentApprovalDecision Decision { get; init; } = AgentApprovalDecision.Approved;
    public DateTimeOffset ApprovedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public IReadOnlyList<AgentEvidenceItem> Evidence { get; init; } = [];
}

public sealed class AgentToolCallRequest
{
    public required string ToolName { get; init; }
    public AgentActionImpact Impact { get; init; } = AgentActionImpact.ReadOnly;
    public bool RequiresApproval { get; init; }
    public bool AllowsFileWrites { get; init; }
    public bool AllowsProcessExecution { get; init; }
    public bool AllowsWorkspaceMutation { get; init; }
    public bool EvidenceRequired { get; init; }
    public string ApprovalScope { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceSourceIds { get; init; } = [];
}

public sealed class AgentToolCallResult
{
    public required string ToolName { get; init; }
    public AgentActionImpact Impact { get; init; } = AgentActionImpact.ReadOnly;
    public AgentRunStatus Status { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<AgentEvidenceItem> Evidence { get; init; } = [];
}

public sealed class AgentGovernanceDecision
{
    public bool IsAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public AgentApprovalDecision ApprovalDecision { get; init; } = AgentApprovalDecision.NotRequired;
    public AgentActionImpact MaxImpact { get; init; } = AgentActionImpact.ReadOnly;
    public bool RequiresApproval { get; init; }
    public IReadOnlyList<string> Violations { get; init; } = [];
}

public sealed class AgentResult
{
    public required string AgentName { get; init; }
    public AgentRunStatus Status { get; init; }
    public string GoalId { get; init; } = string.Empty;
    public string DogfoodRunId { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string ModelProfileName { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public string OutputJson { get; init; } = string.Empty;
    public IReadOnlyList<string> RequestedTools { get; init; } = [];
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
    public IReadOnlyList<string> CommandsRun { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<AgentToolCallResult> ToolCalls { get; init; } = [];
    public AgentApprovalDecision ApprovalDecision { get; init; } = AgentApprovalDecision.NotRequired;
    public string ApprovalFailureReason { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public bool WasDryRun { get; init; } = true;
    public bool MutatedState { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public long DurationMs { get; init; }
}

public sealed class AgentLlmCallResult
{
    public bool WasAttempted { get; init; }
    public bool WasSuccessful { get; init; }
    public string InvocationMode { get; init; } = "disabled";
    public string ResponseText { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public long DurationMs { get; init; }
}

public interface IAgentLlmClient
{
    Task<AgentLlmCallResult> CompleteAsync(ModelProfile profile, string prompt, CancellationToken ct = default);
}

public sealed class ThoughtLedgerEntry
{
    public required string Category { get; init; }
    public required string Text { get; init; }
    public string? Project { get; init; }
}

public sealed class ThoughtLedgerResult
{
    public string Subject { get; init; } = string.Empty;
    public string CurrentBelief { get; init; } = string.Empty;
    public IReadOnlyList<ThoughtLedgerEntry> Evidence { get; init; } = [];
    public IReadOnlyList<ThoughtLedgerEntry> Uncertainties { get; init; } = [];
    public IReadOnlyList<ThoughtLedgerEntry> Assumptions { get; init; } = [];
    public IReadOnlyList<ThoughtLedgerEntry> TemptingActions { get; init; } = [];
    public IReadOnlyList<ThoughtLedgerEntry> BlockedActions { get; init; } = [];
    public IReadOnlyList<ThoughtLedgerEntry> SaferAlternatives { get; init; } = [];
    public string RecommendedNextMove { get; init; } = string.Empty;
    public string ObservedProject { get; init; } = string.Empty;
    public string AffectedProject { get; init; } = string.Empty;
    public string Boundary { get; init; } = "Visible reasoning summary only. No raw hidden chain-of-thought. No writes, patches, tickets, or memory mutation.";
}
