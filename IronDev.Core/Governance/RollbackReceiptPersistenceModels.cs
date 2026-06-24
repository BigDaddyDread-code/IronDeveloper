namespace IronDev.Core.Governance;

public enum RollbackReceiptPersistenceStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    Persisted = 2,
    AlreadyPersisted = 3,
    Conflict = 4,
    RejectedUnsafePayload = 5,
    Unassessable = 6
}

public enum RollbackReceiptOutcomeKind
{
    Unknown = 0,
    Started = 1,
    Succeeded = 2,
    Failed = 3,
    Interrupted = 4,
    Cancelled = 5
}

public enum RollbackTargetKind
{
    Unknown = 0,
    SourceApply = 1,
    Commit = 2,
    Push = 3,
    DraftPullRequest = 4,
    OperationState = 5
}

public sealed record RollbackReceiptPersistenceRecord
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReceiptId { get; init; }
    public required string RollbackAttemptId { get; init; }
    public required string RollbackPlanRef { get; init; }
    public required string RollbackResultRef { get; init; }
    public required RollbackTargetKind RollbackTargetKind { get; init; }
    public required string RollbackTargetRef { get; init; }
    public required string RollbackReasonCode { get; init; }
    public required string OriginalOperationId { get; init; }
    public required string OriginalAttemptId { get; init; }
    public required string SourceApplyReceiptId { get; init; }
    public required string CommitReceiptId { get; init; }
    public required string PushReceiptId { get; init; }
    public required string DraftPullRequestReceiptId { get; init; }
    public required string CommitSha { get; init; }
    public required string RepositoryRef { get; init; }
    public required string TargetBranchRef { get; init; }
    public required string PullRequestRef { get; init; }
    public required string PullRequestNumberRef { get; init; }
    public required string WorktreeBeforeRef { get; init; }
    public required string WorktreeAfterRef { get; init; }
    public required string ValidationResultRef { get; init; }
    public required RollbackReceiptOutcomeKind OutcomeKind { get; init; }
    public required string OutcomeReasonCode { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required string Source { get; init; }
    public required bool IsRedacted { get; init; }
    public required string RedactionReason { get; init; }
    public required string RecordFingerprint { get; init; }
}

public sealed record PersistRollbackReceiptRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required RollbackReceiptPersistenceRecord? Receipt { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
}

public sealed record PersistRollbackReceiptResult
{
    public required bool IsValid { get; init; }
    public required RollbackReceiptPersistenceStatus PersistenceStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReceiptId { get; init; }
    public required string RollbackAttemptId { get; init; }
    public required RollbackTargetKind RollbackTargetKind { get; init; }
    public required string RollbackTargetRef { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record RollbackReceiptPersistenceValidationResult
{
    public required bool IsValid { get; init; }
    public required bool HasUnsafePayload { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}
