namespace IronDev.Core.AgentMemory;

public enum MemoryImprovementProposalType
{
    PromoteCandidatePattern = 1,
    PromoteObservedMemory = 2,
    MarkMemoryInvalid = 3,
    MarkMemorySuperseded = 4,
    MergeDuplicateMemory = 5,
    CreateProjectGuidanceCandidate = 6,
    CreateCodeStandardCandidate = 7,
    CreateOperationalRunbookCandidate = 8
}

public enum MemoryImprovementProposalStatus
{
    Submitted = 1,
    Withdrawn = 2,
    Rejected = 3,
    AcceptedForFutureImplementation = 4,
    Superseded = 5
}

public enum MemoryImprovementProposalEventType
{
    Submitted = 1,
    Withdrawn = 2,
    Rejected = 3,
    AcceptedForFutureImplementation = 4,
    Superseded = 5
}

public sealed record MemoryImprovementProposalSource
{
    public string? MemoryItemId { get; init; }

    public string? InfluenceId { get; init; }

    public string? HandoffMemorySliceId { get; init; }

    public string? RunMemoryFindingType { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? DecisionId { get; init; }
}

public sealed record MemoryImprovementProposalDraft
{
    public required string ProposalId { get; init; }

    public required AgentMemoryScope Scope { get; init; }

    public required MemoryImprovementProposalType ProposalType { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<MemoryImprovementProposalSource> Sources { get; init; }

    public required IReadOnlyList<EvidenceRef> EvidenceRefs { get; init; }

    public required decimal Confidence { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? ProposedByAgentId { get; init; }

    public string? ProposedByUserId { get; init; }

    public string? CorrelationId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? ProposalJson { get; init; }
}

public sealed record MemoryImprovementProposalRecord
{
    public required string ProposalId { get; init; }

    public required AgentMemoryScope Scope { get; init; }

    public required MemoryImprovementProposalType ProposalType { get; init; }

    public required MemoryImprovementProposalStatus CurrentStatus { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<MemoryImprovementProposalSource> Sources { get; init; }

    public required IReadOnlyList<EvidenceRef> EvidenceRefs { get; init; }

    public required decimal Confidence { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? ProposedByAgentId { get; init; }

    public string? ProposedByUserId { get; init; }

    public string? CorrelationId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? ProposalJson { get; init; }
}

public sealed record MemoryImprovementProposalEventDraft
{
    public required string ProposalEventId { get; init; }

    public required string ProposalId { get; init; }

    public required MemoryImprovementProposalEventType EventType { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? Reason { get; init; }

    public string? CreatedByUserId { get; init; }

    public string? CreatedByAgentId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? CorrelationId { get; init; }

    public string? EventJson { get; init; }
}

public sealed record MemoryImprovementProposalEventRecord
{
    public required string ProposalEventId { get; init; }

    public required string ProposalId { get; init; }

    public required MemoryImprovementProposalEventType EventType { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? Reason { get; init; }

    public string? CreatedByUserId { get; init; }

    public string? CreatedByAgentId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? CorrelationId { get; init; }

    public string? EventJson { get; init; }
}

public sealed record MemoryImprovementProposalQuery
{
    public MemoryImprovementProposalStatus? Status { get; init; }

    public MemoryImprovementProposalType? ProposalType { get; init; }

    public string? AgentId { get; init; }

    public DateTimeOffset? CreatedAfter { get; init; }

    public DateTimeOffset? CreatedBefore { get; init; }

    public int Take { get; init; } = 50;
}
