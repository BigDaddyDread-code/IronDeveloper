namespace IronDev.Core.Models;

public sealed record LinkedRunSummaryDto
{
    public string RunId { get; init; } = string.Empty;
    public string? TraceId { get; init; }
    public string? Title { get; init; }
    public string Status { get; init; } = "unknown";
    public string? Recommendation { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
}

public sealed record LinkedPromotionPackageSummaryDto
{
    public string? PackageId { get; init; }
    public string? ProposedChangeId { get; init; }
    public string? ApprovalState { get; init; }
    public string? Recommendation { get; init; }
    public string? RuntimeProfile { get; init; }
    public string? TargetLanguage { get; init; }
    public int? FilesToPromoteCount { get; init; }
    public int? FilesBlockedCount { get; init; }
    public int? ActiveRepoMutationCount { get; init; }
    public string? SourceRunId { get; init; }
}

public sealed record TicketEvidenceSummaryDto
{
    public long TicketId { get; init; }
    public string Status { get; init; } = "loaded";
    public string Message { get; init; } = string.Empty;
    public LinkedRunSummaryDto? LatestRun { get; init; }
    public LinkedPromotionPackageSummaryDto? LatestPromotionPackage { get; init; }
    public int LinkedTraceCount { get; init; }
    public int LinkedDocumentCount { get; init; }
    public int LinkedDecisionCount { get; init; }
    public int LinkedRunCount { get; init; }
    public bool HasBlockingWarnings { get; init; }
    public IReadOnlyList<string> BlockedActions { get; init; } = [];
    public string? NextSafeAction { get; init; }
}
