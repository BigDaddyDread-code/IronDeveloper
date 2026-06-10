using IronDev.Core.Agents;

namespace IronDev.Core.Agents.Audit;

public enum AgentRunStatus
{
    Created = 1,
    Running = 2,
    Completed = 3,
    CompletedWithWarnings = 4,
    Blocked = 5,
    Failed = 6,
    Cancelled = 7
}

public enum AgentRunTriggerType
{
    ManualUserRequest = 1,
    ManualGovernedRequest = 2,
    TestHarness = 3,
    Replay = 4
}

public enum AgentRunStepType
{
    Created = 1,
    InputBound = 2,
    CapabilityEvaluated = 3,
    BoundaryDecision = 4,
    ThoughtLedgerRecorded = 5,
    OutputRecorded = 6,
    Completed = 7
}

public enum AgentCapabilityUseOutcome
{
    Allowed = 1,
    Blocked = 2,
    Warned = 3,
    NotApplicable = 4
}

public enum AgentBoundaryDecisionType
{
    Policy = 1,
    HumanApproval = 2,
    GovernanceDecision = 3,
    Capability = 4,
    Memory = 5,
    Handoff = 6,
    Output = 7,
    Evidence = 8,
    Safety = 9
}

public enum ThoughtLedgerEntryType
{
    DecisionRationale = 1,
    EvidenceUsed = 2,
    Assumption = 3,
    RejectedAlternative = 4,
    Risk = 5,
    BoundaryDecision = 6,
    OutputRationale = 7,
    FollowUp = 8
}

public sealed record AgentRunRecord
{
    public required string AgentRunId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string CampaignId { get; init; }
    public required string RunId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    public string RequestedByUserId { get; init; } = string.Empty;
    public string RequestedByAgentId { get; init; } = string.Empty;
    public AgentRunTriggerType TriggerType { get; init; }
    public AgentRunStatus Status { get; init; }
    public string RequestSummary { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
}

public sealed record AgentRunStep
{
    public required string StepId { get; init; }
    public required string AgentRunId { get; init; }
    public int Sequence { get; init; }
    public AgentRunStepType StepType { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public required string Summary { get; init; }
    public bool ContainsRawPrivateReasoning { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed record AgentRunInputRef
{
    public required string InputRefId { get; init; }
    public required string AgentRunId { get; init; }
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public bool IsAuthoritativeForAction { get; init; }
    public bool ContainsRawPrivateReasoning { get; init; }
}

public sealed record AgentRunOutputRef
{
    public required string OutputRefId { get; init; }
    public required string AgentRunId { get; init; }
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public bool IsReviewOnly { get; init; }
    public bool IsProposalOnly { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool CreatesRuntimeAction { get; init; }
    public bool ContainsRawPrivateReasoning { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed record AgentCapabilityUseRecord
{
    public required string CapabilityUseId { get; init; }
    public required string AgentRunId { get; init; }
    public AgentCapability Capability { get; init; }
    public AgentCapabilityUseOutcome Outcome { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string PolicyDecisionId { get; init; } = string.Empty;
    public string BoundaryDecisionId { get; init; } = string.Empty;
    public string EvidenceRef { get; init; } = string.Empty;
    public bool WasDeclaredOnAgent { get; init; }
    public bool WasForbiddenOnAgent { get; init; }
}

public sealed record AgentBoundaryDecision
{
    public required string BoundaryDecisionId { get; init; }
    public required string AgentRunId { get; init; }
    public AgentBoundaryDecisionType BoundaryType { get; init; }
    public required string Decision { get; init; }
    public required string Reason { get; init; }
    public string SourceRefId { get; init; } = string.Empty;
    public bool GrantsAuthority { get; init; }
    public bool GrantsHumanApproval { get; init; }
    public bool GrantsPolicyApproval { get; init; }
    public bool GrantsMemoryPromotion { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed record ThoughtLedgerEntry
{
    public required string ThoughtLedgerEntryId { get; init; }
    public required string AgentRunId { get; init; }
    public ThoughtLedgerEntryType EntryType { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> Assumptions { get; init; } = [];
    public IReadOnlyList<string> RejectedAlternatives { get; init; } = [];
    public IReadOnlyList<string> Risks { get; init; } = [];
    public IReadOnlyList<string> RequiredFollowUps { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool GrantsAuthority { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsMemoryPromotion { get; init; }
    public DateTimeOffset RecordedAtUtc { get; init; }
}

public sealed record AgentRunAuditEnvelope
{
    public required AgentRunRecord Run { get; init; }
    public required AgentDefinition AgentDefinitionSnapshot { get; init; }
    public IReadOnlyList<AgentRunInputRef> Inputs { get; init; } = [];
    public IReadOnlyList<AgentRunOutputRef> Outputs { get; init; } = [];
    public IReadOnlyList<AgentRunStep> Steps { get; init; } = [];
    public IReadOnlyList<AgentCapabilityUseRecord> CapabilityUses { get; init; } = [];
    public IReadOnlyList<AgentBoundaryDecision> BoundaryDecisions { get; init; } = [];
    public IReadOnlyList<ThoughtLedgerEntry> ThoughtLedger { get; init; } = [];
}
