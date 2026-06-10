namespace IronDev.Core.AgentMemory;

public enum MemoryGovernanceActionType
{
    ContextUse = 1,
    AvoidRepeat = 2,
    ToolCallJustification = 3,
    HandoffCreation = 4,
    ProposalCreation = 5,
    Escalation = 6,
    CriticFinding = 7,
    SourceMutation = 8,
    ExternalEffect = 9
}

public enum MemoryGovernanceDecision
{
    Allow = 1,
    Warn = 2,
    Block = 3
}

public enum MemoryGovernanceIssueSeverity
{
    Info = 1,
    Warning = 2,
    High = 3,
    Critical = 4
}

public enum MemoryGovernanceIssueCode
{
    MissingScope = 1,
    MissingDecisionId = 2,
    MissingReferencedArtifacts = 3,

    MemoryNotFoundInScope = 10,
    MemoryExpired = 11,
    MemoryInvalidated = 12,
    MemorySuperseded = 13,
    MemoryRejected = 14,
    MemoryAcceptedButNotLocalAuthority = 15,
    MemorySystemRuleUsedAsLocalMemory = 16,

    MissingInfluenceRecord = 30,
    InfluenceNotFoundInScope = 31,
    InfluenceDecisionMismatch = 32,
    InfluenceMemoryMismatch = 33,

    HandoffNotFound = 50,
    HandoffNotAddressedToAgent = 51,
    HandoffExpired = 52,
    HandoffAllowedUseViolation = 53,
    HandoffSourceMemoryTerminalAtHandoff = 54,

    ProposedMemoryRequiresVerification = 70,
    CandidatePatternCannotJustifyExternalEffect = 71,
    LowConfidenceMemoryUse = 72,
    SourceMutationRequiresApprovalBeyondMemory = 73,
    ExternalEffectRequiresApprovalBeyondMemory = 74,

    GovernanceResultMismatch = 90
}

public sealed record MemoryGovernanceReferencedArtifact
{
    public string? MemoryItemId { get; init; }

    public string? InfluenceId { get; init; }

    public string? HandoffMemorySliceId { get; init; }

    public string? DecisionId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }
}

public sealed record MemoryGovernanceCheckRequest
{
    public required AgentMemoryScope Scope { get; init; }

    public required MemoryGovernanceActionType ActionType { get; init; }

    public required string DecisionId { get; init; }

    public required IReadOnlyList<MemoryGovernanceReferencedArtifact> ReferencedArtifacts { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }

    public string? TargetAgentId { get; init; }

    public string? ToolName { get; init; }

    public string? AffectedArtifactType { get; init; }

    public string? AffectedArtifactId { get; init; }

    public string? CorrelationId { get; init; }

    public bool InfluenceRecordRequired { get; init; } = true;
}

public sealed record MemoryGovernanceIssue
{
    public required MemoryGovernanceIssueCode Code { get; init; }

    public required MemoryGovernanceIssueSeverity Severity { get; init; }

    public required string Summary { get; init; }

    public string? MemoryItemId { get; init; }

    public string? InfluenceId { get; init; }

    public string? HandoffMemorySliceId { get; init; }
}

public sealed record MemoryGovernanceCheckResult
{
    public required string GovernanceCheckId { get; init; }

    public required AgentMemoryScope Scope { get; init; }

    public required string DecisionId { get; init; }

    public required MemoryGovernanceActionType ActionType { get; init; }

    public required MemoryGovernanceDecision Decision { get; init; }

    public required IReadOnlyList<MemoryGovernanceIssue> Issues { get; init; }

    public required DateTimeOffset CheckedAt { get; init; }

    public string? CorrelationId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }
}
