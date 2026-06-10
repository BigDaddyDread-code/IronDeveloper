using IronDev.Core.Agents;

namespace IronDev.Core.Agents.Audit;

public sealed record AgentRunAuditListQuery
{
    public string? AgentId { get; init; }
    public AgentKind? AgentKind { get; init; }
    public AgentRunStatus? Status { get; init; }
    public AgentRunTriggerType? TriggerType { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public int Take { get; init; } = 50;
    public int Skip { get; init; }
}

public sealed record AgentRunListResponseDto
{
    public required string ProjectId { get; init; }
    public required IReadOnlyList<AgentRunListItemDto> Items { get; init; }
    public required int TotalCount { get; init; }
    public IReadOnlyList<AgentRunAuditQueryIssueDto> Issues { get; init; } = [];
}

public sealed record AgentRunDetailResponseDto
{
    public required string ProjectId { get; init; }
    public required string AgentRunId { get; init; }
    public AgentRunDetailDto? Run { get; init; }
    public IReadOnlyList<AgentRunAuditQueryIssueDto> Issues { get; init; } = [];
}

public sealed record AgentRunThoughtLedgerResponseDto
{
    public required string ProjectId { get; init; }
    public required string AgentRunId { get; init; }
    public required IReadOnlyList<ThoughtLedgerEntryDto> Items { get; init; }
    public IReadOnlyList<AgentRunAuditQueryIssueDto> Issues { get; init; } = [];
}

public sealed record AgentRunCapabilitiesResponseDto
{
    public required string ProjectId { get; init; }
    public required string AgentRunId { get; init; }
    public required IReadOnlyList<AgentCapabilityUseDto> Items { get; init; }
    public IReadOnlyList<AgentRunAuditQueryIssueDto> Issues { get; init; } = [];
}

public sealed record AgentRunBoundariesResponseDto
{
    public required string ProjectId { get; init; }
    public required string AgentRunId { get; init; }
    public required IReadOnlyList<AgentBoundaryDecisionDto> Items { get; init; }
    public IReadOnlyList<AgentRunAuditQueryIssueDto> Issues { get; init; } = [];
}

public sealed record AgentRunOutputsResponseDto
{
    public required string ProjectId { get; init; }
    public required string AgentRunId { get; init; }
    public required IReadOnlyList<AgentRunOutputRefDto> Items { get; init; }
    public IReadOnlyList<AgentRunAuditQueryIssueDto> Issues { get; init; } = [];
}

public sealed record AgentRunInputsResponseDto
{
    public required string ProjectId { get; init; }
    public required string AgentRunId { get; init; }
    public required IReadOnlyList<AgentRunInputRefDto> Items { get; init; }
    public IReadOnlyList<AgentRunAuditQueryIssueDto> Issues { get; init; } = [];
}

public sealed record AgentRunListItemDto
{
    public required string AgentRunId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    public required AgentKind AgentKind { get; init; }
    public required AgentExecutionMode ExecutionMode { get; init; }
    public required AgentRunStatus Status { get; init; }
    public required AgentRunTriggerType TriggerType { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string RequestedByUserId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public int InputCount { get; init; }
    public int OutputCount { get; init; }
    public int ThoughtLedgerCount { get; init; }
    public int CapabilityUseCount { get; init; }
    public int BoundaryDecisionCount { get; init; }
    public int BlockedCapabilityCount { get; init; }
    public bool HasBoundaryBlocks { get; init; }
    public bool HasUnsafeAttempt { get; init; }
    public bool HasRawPrivateReasoning { get; init; }
    public bool HasAuthorityClaim { get; init; }
    public bool HasApprovalClaim { get; init; }
    public bool HasMemoryPromotionClaim { get; init; }
}

public sealed record AgentRunDetailDto
{
    public required AgentRunRecordDto Run { get; init; }
    public required AgentDefinitionSnapshotDto AgentDefinition { get; init; }
    public IReadOnlyList<AgentRunInputRefDto> Inputs { get; init; } = [];
    public IReadOnlyList<AgentRunOutputRefDto> Outputs { get; init; } = [];
    public IReadOnlyList<AgentCapabilityUseDto> CapabilityUses { get; init; } = [];
    public IReadOnlyList<AgentBoundaryDecisionDto> BoundaryDecisions { get; init; } = [];
    public IReadOnlyList<ThoughtLedgerEntryDto> ThoughtLedger { get; init; } = [];
    public IReadOnlyList<AgentRunStepDto> Steps { get; init; } = [];
    public required AgentRunSafetySummaryDto SafetySummary { get; init; }
}

public sealed record AgentRunRecordDto
{
    public required string AgentRunId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string CampaignId { get; init; }
    public required string RunId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    public required AgentRunStatus Status { get; init; }
    public required AgentRunTriggerType TriggerType { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string RequestedByUserId { get; init; } = string.Empty;
    public string RequestedByAgentId { get; init; } = string.Empty;
    public string RequestSummary { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
}

public sealed record AgentDefinitionSnapshotDto
{
    public required string AgentId { get; init; }
    public required string Name { get; init; }
    public required AgentKind Kind { get; init; }
    public required AgentExecutionMode ExecutionMode { get; init; }
    public IReadOnlyList<AgentCapability> Capabilities { get; init; } = [];
    public IReadOnlyList<AgentCapability> ForbiddenCapabilities { get; init; } = [];
    public string PersonaDisplayName { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
}

public sealed record AgentRunInputRefDto
{
    public required string InputRefId { get; init; }
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsAuthoritativeForAction { get; init; }
    public bool ContainsRawPrivateReasoning { get; init; }
}

public sealed record AgentRunOutputRefDto
{
    public required string OutputRefId { get; init; }
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsReviewOnly { get; init; }
    public bool IsProposalOnly { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool CreatesRuntimeAction { get; init; }
    public bool ContainsRawPrivateReasoning { get; init; }
}

public sealed record AgentCapabilityUseDto
{
    public required string CapabilityUseId { get; init; }
    public required AgentCapability Capability { get; init; }
    public required AgentCapabilityUseOutcome Outcome { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string BoundaryDecisionId { get; init; } = string.Empty;
    public string EvidenceRef { get; init; } = string.Empty;
    public bool WasDeclaredOnAgent { get; init; }
    public bool WasForbiddenOnAgent { get; init; }
}

public sealed record AgentBoundaryDecisionDto
{
    public required string BoundaryDecisionId { get; init; }
    public required AgentBoundaryDecisionType BoundaryType { get; init; }
    public required string Decision { get; init; }
    public required string Reason { get; init; }
    public string SourceRefId { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool GrantsAuthority { get; init; }
    public bool GrantsHumanApproval { get; init; }
    public bool GrantsPolicyApproval { get; init; }
    public bool GrantsMemoryPromotion { get; init; }
}

public sealed record ThoughtLedgerEntryDto
{
    public required string ThoughtLedgerEntryId { get; init; }
    public required ThoughtLedgerEntryType EntryType { get; init; }
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

public sealed record AgentRunStepDto
{
    public required string StepId { get; init; }
    public required int Sequence { get; init; }
    public required AgentRunStepType StepType { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
}

public sealed record AgentRunSafetySummaryDto
{
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool HasAuthorityClaim { get; init; }
    public bool HasApprovalClaim { get; init; }
    public bool HasMemoryPromotionClaim { get; init; }
    public bool HasRuntimeActionOutput { get; init; }
    public bool HasAuthorityCreatingOutput { get; init; }
    public bool HasBlockedCapabilityAttempt { get; init; }
    public bool HasBoundaryBlock { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record AgentRunAuditQueryIssueDto
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
}

public sealed record AgentRunAuditQueryResultDto
{
    public required string Status { get; init; }
    public IReadOnlyList<AgentRunAuditQueryIssueDto> Issues { get; init; } = [];
}
