namespace IronDev.Core.Governance;

public enum DraftPullRequestReceiptPersistenceStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    Persisted = 2,
    AlreadyPersisted = 3,
    Conflict = 4,
    RejectedUnsafePayload = 5,
    Unassessable = 6
}

public enum DraftPullRequestReceiptOutcomeKind
{
    Unknown = 0,
    Started = 1,
    Succeeded = 2,
    Failed = 3,
    Interrupted = 4,
    Cancelled = 5
}

public enum DraftPullRequestObservedState
{
    Unknown = 0,
    Draft = 1,
    NotDraft = 2,
    Unavailable = 3
}

public sealed record DraftPullRequestReceiptPersistenceRecord
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReceiptId { get; init; }
    public required string DraftPullRequestAttemptId { get; init; }
    public required string PushReceiptId { get; init; }
    public required string PushAttemptId { get; init; }
    public required string CommitReceiptId { get; init; }
    public required string CommitAttemptId { get; init; }
    public required string CommitSha { get; init; }
    public required string RepositoryRef { get; init; }
    public required string ProviderRef { get; init; }
    public required string BaseBranchRef { get; init; }
    public required string HeadBranchRef { get; init; }
    public required string PullRequestRef { get; init; }
    public required string PullRequestNumberRef { get; init; }
    public required string PullRequestWebRef { get; init; }
    public required string PullRequestTitleHash { get; init; }
    public required string PullRequestBodyHash { get; init; }
    public required DraftPullRequestObservedState ObservedDraftState { get; init; }
    public required DraftPullRequestReceiptOutcomeKind OutcomeKind { get; init; }
    public required string OutcomeReasonCode { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required string Source { get; init; }
    public required bool IsRedacted { get; init; }
    public required string RedactionReason { get; init; }
    public required string RecordFingerprint { get; init; }
}

public sealed record PersistDraftPullRequestReceiptRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required DraftPullRequestReceiptPersistenceRecord? Receipt { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
}

public sealed record PersistDraftPullRequestReceiptResult
{
    public required bool IsValid { get; init; }
    public required DraftPullRequestReceiptPersistenceStatus PersistenceStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReceiptId { get; init; }
    public required string DraftPullRequestAttemptId { get; init; }
    public required string PullRequestRef { get; init; }
    public required string PullRequestNumberRef { get; init; }
    public required DraftPullRequestObservedState ObservedDraftState { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record DraftPullRequestReceiptPersistenceValidationResult
{
    public required bool IsValid { get; init; }
    public required bool HasUnsafePayload { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}
