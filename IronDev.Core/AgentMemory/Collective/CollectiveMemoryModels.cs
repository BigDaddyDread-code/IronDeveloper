using IronDev.Core.AgentMemory;

namespace IronDev.Core.AgentMemory.Collective;

public sealed record CollectiveMemoryScope
{
    public required string TenantId { get; init; }

    public required string ProjectId { get; init; }

    public string? KnowledgeDomainId { get; init; }

    public string? ComponentId { get; init; }

    public string? RepositoryId { get; init; }
}

public enum CollectiveMemoryType
{
    ProjectFact = 1,
    ArchitectureDecision = 2,
    CodebasePattern = 3,
    FailureMode = 4,
    OperationalRunbook = 5,
    CodeStandard = 6,
    RetrievalCue = 7,
    IntegrationContract = 8,
    TestingPattern = 9,
    RiskPattern = 10
}

public enum CollectiveMemoryAuthorityLevel
{
    Candidate = 1,
    Reviewed = 2,
    Accepted = 3,
    Deprecated = 4,
    Rejected = 5
}

public enum CollectiveMemoryStatus
{
    Draft = 1,
    Proposed = 2,
    UnderReview = 3,
    Active = 4,
    Superseded = 5,
    Deprecated = 6,
    Rejected = 7,
    Invalidated = 8
}

public enum CollectiveMemoryReviewState
{
    None = 1,
    NeedsEvidence = 2,
    NeedsHumanReview = 3,
    NeedsContradictionReview = 4,
    ApprovedForAcceptance = 5,
    RejectedByReview = 6
}

public enum CollectiveMemorySourceType
{
    LocalMemoryItem = 1,
    MemoryInfluenceRecord = 2,
    HandoffMemorySlice = 3,
    RunMemoryReport = 4,
    MemoryImprovementProposal = 5,
    MemoryExecutionAudit = 6,
    HumanAuthoredDecision = 7,
    CodeReviewFinding = 8,
    TestResult = 9,
    ExternalDocument = 10
}

public sealed record CollectiveMemorySourceRef
{
    public required CollectiveMemorySourceType SourceType { get; init; }

    public required string SourceId { get; init; }

    public string? TenantId { get; init; }

    public string? ProjectId { get; init; }

    public string? CampaignId { get; init; }

    public string? RunId { get; init; }

    public string? AgentId { get; init; }

    public string? DecisionId { get; init; }

    public string? EvidenceUri { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }
}

public sealed record CollectiveMemoryEvidenceRef
{
    public required string EvidenceId { get; init; }

    public required EvidenceType EvidenceType { get; init; }

    public required string SourceId { get; init; }

    public string? SourceUri { get; init; }

    public string? Summary { get; init; }

    public decimal? Weight { get; init; }

    public DateTimeOffset? CapturedAt { get; init; }
}

public sealed record CollectiveMemoryContradictionRef
{
    public required string ContradictionId { get; init; }

    public required CollectiveMemorySourceRef Source { get; init; }

    public required string Summary { get; init; }

    public decimal? Weight { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }
}

public sealed record CollectiveMemorySupersessionRef
{
    public required string SupersedesCollectiveMemoryId { get; init; }

    public required string Reason { get; init; }

    public string? DecisionId { get; init; }

    public DateTimeOffset? SupersededAt { get; init; }
}

public sealed record CollectiveMemoryItem
{
    public required string CollectiveMemoryId { get; init; }

    public required CollectiveMemoryScope Scope { get; init; }

    public required CollectiveMemoryType MemoryType { get; init; }

    public required CollectiveMemoryAuthorityLevel AuthorityLevel { get; init; }

    public required CollectiveMemoryStatus Status { get; init; }

    public required CollectiveMemoryReviewState ReviewState { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<CollectiveMemorySourceRef> Sources { get; init; }

    public required IReadOnlyList<CollectiveMemoryEvidenceRef> EvidenceRefs { get; init; }

    public IReadOnlyList<CollectiveMemoryContradictionRef> Contradictions { get; init; } = [];

    public IReadOnlyList<CollectiveMemorySupersessionRef> Supersedes { get; init; } = [];

    public decimal Confidence { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastReviewedAt { get; init; }

    public DateTimeOffset? LastConfirmedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? DecisionId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? CorrelationId { get; init; }

    public string? ContentHashSha256 { get; init; }

    public string? CollectiveMemoryJson { get; init; }
}

public sealed record CollectiveMemoryQuery
{
    public CollectiveMemoryType? MemoryType { get; init; }

    public CollectiveMemoryAuthorityLevel? AuthorityLevel { get; init; }

    public CollectiveMemoryStatus? Status { get; init; }

    public CollectiveMemoryReviewState? ReviewState { get; init; }

    public string? TextSearch { get; init; }

    public string? SourceId { get; init; }

    public string? DecisionId { get; init; }

    public bool IncludeDeprecated { get; init; }

    public bool IncludeRejected { get; init; }

    public int Take { get; init; } = 50;
}

public sealed record CollectiveMemoryValidationIssue
{
    public required string Code { get; init; }

    public required string Severity { get; init; }

    public required string Message { get; init; }
}
