namespace IronDev.Core.AgentMemory.Collective;

public enum CollectiveMemoryStabilityBand
{
    Unknown = 1,
    Unstable = 2,
    Emerging = 3,
    Stable = 4,
    StronglyStable = 5
}

public enum CollectiveMemoryAttractorSignalType
{
    EvidenceSupport = 1,
    SourceDiversity = 2,
    HumanReview = 3,
    AcceptanceAuthority = 4,
    RecentConfirmation = 5,
    ReuseEvidence = 6,
    ContradictionPressure = 7,
    RejectionPressure = 8,
    DeprecationPressure = 9,
    ExpiryPressure = 10
}

public sealed record CollectiveMemoryAttractorSignal
{
    public CollectiveMemoryAttractorSignalType SignalType { get; init; }
    public decimal Weight { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool IsNegative { get; init; }
}

public sealed record CollectiveMemoryStabilityInput
{
    public string StabilityRunId { get; init; } = string.Empty;
    public CollectiveMemoryItem Memory { get; init; } = null!;
    public CollectiveMemoryEvidenceAggregate EvidenceAggregate { get; init; } = null!;
    public IReadOnlyList<CollectiveMemoryEventRecord> Events { get; init; } = [];
    public DateTimeOffset EvaluatedAt { get; init; }
    public string? RequestedByUserId { get; init; }
    public string? RequestedByAgentId { get; init; }
    public string? DecisionId { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record CollectiveMemoryStabilityBreakdown
{
    public decimal EvidenceSupportScore { get; init; }
    public decimal SourceDiversityScore { get; init; }
    public decimal HumanReviewScore { get; init; }
    public decimal AuthorityScore { get; init; }
    public decimal RecencyScore { get; init; }
    public decimal ReuseScore { get; init; }
    public decimal ContradictionPenalty { get; init; }
    public decimal LifecyclePenalty { get; init; }
    public decimal ExpiryPenalty { get; init; }
}

public sealed record CollectiveMemoryScoringIssue
{
    public string Code { get; init; } = string.Empty;
    public string Severity { get; init; } = "Error";
    public string Message { get; init; } = string.Empty;
}

public sealed record CollectiveMemoryStabilityScore
{
    public string StabilityRunId { get; init; } = string.Empty;
    public string CollectiveMemoryId { get; init; } = string.Empty;
    public CollectiveMemoryScope? Scope { get; init; }
    public decimal Score { get; init; }
    public CollectiveMemoryStabilityBand Band { get; init; } = CollectiveMemoryStabilityBand.Unknown;
    public CollectiveMemoryStabilityBreakdown Breakdown { get; init; } = new();
    public IReadOnlyList<CollectiveMemoryAttractorSignal> Signals { get; init; } = [];
    public IReadOnlyList<CollectiveMemoryScoringIssue> Issues { get; init; } = [];
    public DateTimeOffset EvaluatedAt { get; init; }
    public bool HasErrors => Issues.Any(issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase));
}
