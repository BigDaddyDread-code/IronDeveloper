namespace IronDev.Core.Agents.Concrete;

public enum MemoryImprovementPatternType
{
    RepeatedFailureMode = 1,
    RepeatedGovernanceBlock = 2,
    RepeatedManualCorrection = 3,
    RepeatedRetrievalMiss = 4,
    RepeatedContradiction = 5,
    RepeatedSuccessfulDecision = 6,
    StaleMemoryPattern = 7,
    DuplicateProposalPattern = 8
}

public sealed record MemoryImprovementPatternFinding
{
    public required string PatternFindingId { get; init; }
    public required MemoryImprovementPatternType PatternType { get; init; }
    public required string Summary { get; init; }
    public required decimal Confidence { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> RelatedMemoryIds { get; init; } = [];
    public IReadOnlyList<string> RelatedProposalIds { get; init; } = [];
    public bool IsDuplicateCandidate { get; init; }
    public bool RequiresHumanReview { get; init; } = true;
}

public sealed record MemoryImprovementProposalDraft
{
    public required string ProposalDraftId { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Rationale { get; init; }
    public required MemoryImprovementPatternFinding SourcePattern { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsProposalOnly { get; init; } = true;
    public bool CreatesCollectiveMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool RequiresHumanReview { get; init; } = true;
}

public enum MemoryImprovementNoProposalReason
{
    InsufficientEvidence = 1,
    DuplicateProposalExists = 2,
    PatternTooWeak = 3,
    ContradictoryEvidence = 4,
    OutOfScope = 5
}

public sealed record MemoryImprovementDetectionResult
{
    public required string DetectionResultId { get; init; }
    public required IReadOnlyList<MemoryImprovementPatternFinding> Findings { get; init; }
    public IReadOnlyList<MemoryImprovementProposalDraft> ProposalDrafts { get; init; } = [];
    public MemoryImprovementNoProposalReason? NoProposalReason { get; init; }
    public required DateTimeOffset DetectedAt { get; init; }
    public string? DetectedByAgentId { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
