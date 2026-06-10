namespace IronDev.Core.AgentMemory.Collective;

public enum CollectiveMemoryEvidenceContributionType
{
    SupportsClaim = 1,
    WeaklySupportsClaim = 2,
    NeutralContext = 3,
    ContradictsClaim = 4,
    WeaklyContradictsClaim = 5
}

public enum CollectiveMemoryEvidenceQuality
{
    Unknown = 1,
    Weak = 2,
    Moderate = 3,
    Strong = 4
}

public enum CollectiveMemoryEvidenceCoverage
{
    None = 1,
    SingleSource = 2,
    MultipleSameTypeSources = 3,
    MultipleIndependentSourceTypes = 4
}

public enum CollectiveMemoryEvidenceConflictLevel
{
    None = 1,
    Low = 2,
    Medium = 3,
    High = 4
}

public enum CollectiveMemoryEvidenceReadiness
{
    InsufficientEvidence = 1,
    NeedsMoreSources = 2,
    NeedsContradictionReview = 3,
    ReadyForHumanReview = 4
}

public sealed record CollectiveMemoryEvidenceContribution
{
    public required string ContributionId { get; init; }

    public required CollectiveMemoryEvidenceContributionType ContributionType { get; init; }

    public required CollectiveMemorySourceRef Source { get; init; }

    public required CollectiveMemoryEvidenceRef Evidence { get; init; }

    public decimal Weight { get; init; } = 1.0m;

    public string? Summary { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }
}

public sealed record CollectiveMemoryContradictionContribution
{
    public required string ContributionId { get; init; }

    public required CollectiveMemoryContradictionRef Contradiction { get; init; }

    public decimal Weight { get; init; } = 1.0m;

    public string? Summary { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }
}

public sealed record CollectiveMemoryAggregationInput
{
    public required string AggregationId { get; init; }

    public required CollectiveMemoryItem Candidate { get; init; }

    public required IReadOnlyList<CollectiveMemoryEvidenceContribution> EvidenceContributions { get; init; }

    public IReadOnlyList<CollectiveMemoryContradictionContribution> ContradictionContributions { get; init; } = [];

    public DateTimeOffset AggregatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? RequestedByAgentId { get; init; }

    public string? RequestedByUserId { get; init; }

    public string? DecisionId { get; init; }

    public string? ThoughtLedgerEntryId { get; init; }

    public string? CorrelationId { get; init; }
}

public sealed record CollectiveMemoryEvidenceAggregate
{
    public required string AggregationId { get; init; }

    public required string CollectiveMemoryId { get; init; }

    public required CollectiveMemoryScope Scope { get; init; }

    public required int SupportingEvidenceCount { get; init; }

    public required int WeakSupportingEvidenceCount { get; init; }

    public required int NeutralEvidenceCount { get; init; }

    public required int ContradictingEvidenceCount { get; init; }

    public required int WeakContradictingEvidenceCount { get; init; }

    public required int UniqueSourceCount { get; init; }

    public required int UniqueSourceTypeCount { get; init; }

    public required decimal SupportWeight { get; init; }

    public required decimal ContradictionWeight { get; init; }

    public required CollectiveMemoryEvidenceQuality EvidenceQuality { get; init; }

    public required CollectiveMemoryEvidenceCoverage EvidenceCoverage { get; init; }

    public required CollectiveMemoryEvidenceConflictLevel ConflictLevel { get; init; }

    public required CollectiveMemoryEvidenceReadiness Readiness { get; init; }

    public required DateTimeOffset AggregatedAt { get; init; }

    public IReadOnlyList<string> EvidenceContributionIds { get; init; } = [];

    public IReadOnlyList<string> ContradictionContributionIds { get; init; } = [];

    public IReadOnlyList<string> ReviewWarnings { get; init; } = [];
}

public sealed record CollectiveMemoryAggregationResult
{
    public required CollectiveMemoryEvidenceAggregate Aggregate { get; init; }

    public required IReadOnlyList<CollectiveMemoryAggregationIssue> Issues { get; init; }

    public bool HasErrors => Issues.Any(issue => string.Equals(issue.Severity, "Error", StringComparison.Ordinal));
}

public sealed record CollectiveMemoryAggregationIssue
{
    public required string Code { get; init; }

    public required string Severity { get; init; }

    public required string Message { get; init; }
}
