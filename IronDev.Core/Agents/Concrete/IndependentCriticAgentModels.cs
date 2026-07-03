namespace IronDev.Core.Agents.Concrete;

public enum CriticSeverity
{
    Critical = 1,
    High = 2,
    Medium = 3,
    Low = 4
}

public enum CriticReviewVerdict
{
    NoObjection = 1,
    CommentOnly = 2,
    RequestChanges = 3,
    RecommendBlock = 4
}

public enum CriticReviewSubjectType
{
    PullRequest = 1,
    Ticket = 2,
    ArchitecturePlan = 3,
    MemoryProposal = 4,
    CollectiveMemoryPromotionRequest = 5,
    ExecutionAudit = 6,
    TestReport = 7,
    ReleaseCandidate = 8,

    /// <summary>
    /// A skeleton run's critic package: the full-fidelity work package (diffs,
    /// authored tests, build/test evidence) prepared for independent review.
    /// P1-1: the subject enters by reference id; the critic pulls the package
    /// itself from durable evidence.
    /// </summary>
    WorkPackage = 9
}

public sealed record CriticReviewRequest
{
    public required string ReviewRequestId { get; init; }
    public required CriticReviewSubjectType SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string ScopeId { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public string? RequestedByUserId { get; init; }
    public string? RequestedByAgentId { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record CriticFinding
{
    public required string FindingId { get; init; }
    public required CriticSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Problem { get; init; }
    public required string WhyItMatters { get; init; }
    public required string RequiredFix { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool BlocksMerge { get; init; }
    public bool RequiresHumanReview { get; init; }
}

public sealed record CriticReviewResult
{
    public required string ReviewResultId { get; init; }
    public required string ReviewRequestId { get; init; }
    public required CriticReviewVerdict Verdict { get; init; }
    public required IReadOnlyList<CriticFinding> Findings { get; init; }
    public required DateTimeOffset ReviewedAt { get; init; }
    public string? ReviewedByAgentId { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
