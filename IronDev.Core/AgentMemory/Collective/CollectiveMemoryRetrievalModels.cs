namespace IronDev.Core.AgentMemory.Collective;

public enum CollectiveMemoryRetrievalMode
{
    KeywordOnly = 1,
    MetadataOnly = 2,
    HybridReadOnly = 3
}

public enum CollectiveMemoryRetrievalAuthorityFilter
{
    AcceptedOnly = 1,
    ReviewedOrAccepted = 2,
    IncludeCandidates = 3,
    IncludeRejectedAndDeprecated = 4
}

public sealed record CollectiveMemoryRetrievalQuery
{
    public required CollectiveMemoryScope Scope { get; init; }

    public string? Text { get; init; }

    public CollectiveMemoryType? MemoryType { get; init; }

    public CollectiveMemoryRetrievalMode Mode { get; init; } = CollectiveMemoryRetrievalMode.KeywordOnly;

    public CollectiveMemoryRetrievalAuthorityFilter AuthorityFilter { get; init; } =
        CollectiveMemoryRetrievalAuthorityFilter.ReviewedOrAccepted;

    public CollectiveMemoryStabilityBand? MinimumStabilityBand { get; init; }

    public string? SourceId { get; init; }

    public string? DecisionId { get; init; }

    public bool IncludeExpired { get; init; }

    public bool IncludeContradicted { get; init; }

    public int Take { get; init; } = 10;
}

public sealed record CollectiveMemoryRetrievalCandidate
{
    public required string RetrievalCandidateId { get; init; }

    public required CollectiveMemoryItem Memory { get; init; }

    public required decimal RankScore { get; init; }

    public required string RankReason { get; init; }

    public CollectiveMemoryStabilityScore? StabilityScore { get; init; }

    public required CollectiveMemoryRetrievalAuthorityFilter AuthorityFilterApplied { get; init; }

    public required bool IsAuthoritativeForAction { get; init; }

    public required bool RequiresConscienceBeforeUse { get; init; }

    public required bool RequiresPolicyApprovalForAction { get; init; }

    public required IReadOnlyList<string> EvidenceIds { get; init; }

    public required IReadOnlyList<string> SourceIds { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record CollectiveMemoryRetrievalResult
{
    public required string RetrievalId { get; init; }

    public required CollectiveMemoryRetrievalQuery Query { get; init; }

    public required IReadOnlyList<CollectiveMemoryRetrievalCandidate> Candidates { get; init; }

    public required IReadOnlyList<CollectiveMemoryRetrievalIssue> Issues { get; init; }

    public required DateTimeOffset RetrievedAt { get; init; }

    public bool HasErrors => Issues.Any(issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase));
}

public sealed record CollectiveMemoryRetrievalIssue
{
    public required string Code { get; init; }

    public required string Severity { get; init; }

    public required string Message { get; init; }
}
