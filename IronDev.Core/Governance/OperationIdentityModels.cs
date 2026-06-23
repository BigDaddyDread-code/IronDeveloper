namespace IronDev.Core.Governance;

public enum OperationIdentityLifecycleState
{
    Unknown = 0,
    Minted = 1,
    LinkedToRun = 2,
    LinkedToPatch = 3,
    LinkedToApply = 4,
    LinkedToCommit = 5,
    LinkedToPush = 6,
    LinkedToPullRequest = 7,
    Completed = 8,
    Failed = 9,
    Interrupted = 10,
    RolledBack = 11
}

public enum OperationReferenceKind
{
    Unknown = 0,
    RunId = 1,
    PatchArtifactId = 2,
    SourceApplyId = 3,
    CommitPackageId = 4,
    CommitSha = 5,
    PushId = 6,
    PullRequestId = 7,
    ReceiptId = 8,
    EvidenceId = 9,
    CorrelationId = 10,
    UiRouteId = 11,
    TimelineEventId = 12,
    StatusRecordId = 13
}

public sealed record OperationIdentityReference
{
    public required OperationReferenceKind ReferenceKind { get; init; }
    public required string ReferenceId { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string Source { get; init; }
}

public sealed record OperationIdentityRecord
{
    public required string OperationId { get; init; }
    public string? TenantId { get; init; }
    public string? ProjectId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string CreatedBy { get; init; }
    public required OperationIdentityLifecycleState LifecycleState { get; init; }
    public required IReadOnlyList<OperationIdentityReference> References { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record OperationIdentityValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
    public required IReadOnlyList<OperationIdentityReference> References { get; init; }
}

public sealed record OperationIdentityLifecycleValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}
