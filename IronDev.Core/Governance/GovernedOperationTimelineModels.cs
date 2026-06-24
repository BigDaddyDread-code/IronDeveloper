namespace IronDev.Core.Governance;

public enum GovernedOperationTimelineEventKind
{
    Unknown = 0,
    OperationMinted = 1,
    RunLinked = 2,
    PatchArtifactLinked = 3,
    SourceApplyLinked = 4,
    CommitPackageLinked = 5,
    CommitObserved = 6,
    PushObserved = 7,
    PullRequestObserved = 8,
    EvidenceObserved = 9,
    ReceiptObserved = 10,
    ValidationObserved = 11,
    PatchPackageObserved = 12,
    StatusObserved = 13,
    BlockedStateObserved = 14,
    InterruptedObserved = 15,
    RollbackObserved = 16,
    RecoveryObserved = 17,
    CompletedObserved = 18,
    FailedObserved = 19,
    AuthorityBoundaryObserved = 20
}

public sealed record GovernedOperationTimelineEntry
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string TimelineEventId { get; init; }
    public required GovernedOperationTimelineEventKind EventKind { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required string Source { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public required string ReferenceId { get; init; }
    public required string DisplayTitle { get; init; }
    public required string DisplaySummary { get; init; }
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record GovernedOperationTimelineReadModel
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required IReadOnlyList<GovernedOperationTimelineEntry> Entries { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record GovernedOperationTimelineAssemblyResult
{
    public required bool IsValid { get; init; }
    public GovernedOperationTimelineReadModel? ReadModel { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}
