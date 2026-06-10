namespace IronDev.Core.AgentMemory;

public enum MemoryActivityKind
{
    LocalMemoryCreated = 1,
    LocalMemoryLifecycleEvent = 2,
    MemoryInfluenceRecorded = 3,
    MemoryHandoffOutgoing = 4,
    MemoryHandoffIncoming = 5
}

public enum MemoryReviewCandidateSeverity
{
    Info = 1,
    Warning = 2,
    High = 3
}

public enum RunMemoryFindingType
{
    LowConfidenceInfluence = 1,
    NeedsVerificationHandoff = 2,
    ProposedMemoryHandedOff = 3,
    CandidatePatternMemory = 4,
    ExpiredMemoryHadInfluence = 5,
    MissingThoughtLedgerReference = 6
}

public sealed record MemoryActivityReference
{
    public required MemoryActivityKind Kind { get; init; }

    public required string ActivityId { get; init; }

    public required string AgentId { get; init; }

    public string? MemoryItemId { get; init; }

    public string? DecisionId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string Summary { get; init; }
}

public sealed record MemoryThoughtLedgerEntryDraft
{
    public required string ThoughtLedgerEntryId { get; init; }

    public required string AgentId { get; init; }

    public required MemoryActivityKind ActivityKind { get; init; }

    public required string ActivityId { get; init; }

    public required string Summary { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
}

public sealed record RunMemoryReportRequest
{
    public required string TenantId { get; init; }

    public required string ProjectId { get; init; }

    public required string CampaignId { get; init; }

    public required string RunId { get; init; }

    public int TakePerAgent { get; init; } = 100;
}

public sealed record RunMemoryReport
{
    public required string TenantId { get; init; }

    public required string ProjectId { get; init; }

    public required string CampaignId { get; init; }

    public required string RunId { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required int TakePerAgent { get; init; }

    public required int AgentCount { get; init; }

    public required int TotalMemoryItemsCreated { get; init; }

    public required int TotalLifecycleEvents { get; init; }

    public required int TotalInfluenceRecords { get; init; }

    public required int TotalHandoffSlices { get; init; }

    public required IReadOnlyList<AgentRunMemoryReport> Agents { get; init; }

    public required IReadOnlyList<RunMemoryFinding> Findings { get; init; }
}

public sealed record AgentRunMemoryReport
{
    public required string AgentId { get; init; }

    public required int CreatedMemoryCount { get; init; }

    public required int LifecycleEventCount { get; init; }

    public required int InfluenceRecordCount { get; init; }

    public required int OutgoingHandoffCount { get; init; }

    public required int IncomingHandoffCount { get; init; }

    public required IReadOnlyList<AgentMemoryReportItem> MemoryItems { get; init; }

    public required IReadOnlyList<AgentMemoryInfluenceReportItem> InfluenceRecords { get; init; }

    public required IReadOnlyList<AgentMemoryHandoffReportItem> OutgoingHandoffs { get; init; }

    public required IReadOnlyList<AgentMemoryHandoffReportItem> IncomingHandoffs { get; init; }

    public required IReadOnlyList<MemoryActivityReference> ActivityReferences { get; init; }

    public required IReadOnlyList<AgentMemoryReviewCandidate> ReviewCandidates { get; init; }
}

public sealed record AgentMemoryReportItem
{
    public required string MemoryItemId { get; init; }

    public required AgentMemoryType MemoryType { get; init; }

    public required MemoryAuthorityLevel AuthorityLevel { get; init; }

    public required MemoryLifecycleStatus CurrentStatus { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required decimal Confidence { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public required int LifecycleEventCount { get; init; }

    public required IReadOnlyList<string> LifecycleThoughtLedgerEntryIds { get; init; }

    public string? SupersedesMemoryItemId { get; init; }

    public string? KnownLimitations { get; init; }
}

public sealed record AgentMemoryInfluenceReportItem
{
    public required string InfluenceId { get; init; }

    public required string MemoryItemId { get; init; }

    public required string DecisionId { get; init; }

    public required MemoryInfluenceType InfluenceType { get; init; }

    public required string InfluenceSummary { get; init; }

    public required decimal Confidence { get; init; }

    public required MemoryAuthorityLevel MemoryAuthorityLevelAtInfluence { get; init; }

    public required MemoryLifecycleStatus MemoryStatusAtInfluence { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? AffectedArtifactType { get; init; }

    public string? AffectedArtifactId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }
}

public sealed record AgentMemoryHandoffReportItem
{
    public required string HandoffMemorySliceId { get; init; }

    public required string SourceAgentId { get; init; }

    public required string TargetAgentId { get; init; }

    public required IReadOnlyList<string> MemoryItemIds { get; init; }

    public required string Summary { get; init; }

    public required HandoffMemoryAllowedUse AllowedUse { get; init; }

    public required decimal Confidence { get; init; }

    public required IReadOnlyList<HandoffMemoryItemSnapshot> MemorySnapshots { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public IReadOnlyList<string>? InfluenceIds { get; init; }

    public string? DecisionId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? CorrelationId { get; init; }
}

public sealed record AgentMemoryReviewCandidate
{
    public required MemoryReviewCandidateSeverity Severity { get; init; }

    public required RunMemoryFindingType FindingType { get; init; }

    public required string AgentId { get; init; }

    public required string Summary { get; init; }

    public string? MemoryItemId { get; init; }

    public string? InfluenceId { get; init; }

    public string? HandoffMemorySliceId { get; init; }
}

public sealed record RunMemoryFinding
{
    public required RunMemoryFindingType FindingType { get; init; }

    public required MemoryReviewCandidateSeverity Severity { get; init; }

    public required string AgentId { get; init; }

    public required string Summary { get; init; }

    public string? MemoryItemId { get; init; }

    public string? InfluenceId { get; init; }

    public string? HandoffMemorySliceId { get; init; }
}
