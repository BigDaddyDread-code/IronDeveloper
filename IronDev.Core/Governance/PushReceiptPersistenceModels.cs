namespace IronDev.Core.Governance;

public enum PushReceiptPersistenceStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    Persisted = 2,
    AlreadyPersisted = 3,
    Conflict = 4,
    RejectedUnsafePayload = 5,
    Unassessable = 6
}

public enum PushReceiptOutcomeKind
{
    Unknown = 0,
    Started = 1,
    Succeeded = 2,
    Failed = 3,
    Interrupted = 4,
    Cancelled = 5
}

public sealed record PushReceiptPersistenceRecord
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReceiptId { get; init; }
    public required string PushAttemptId { get; init; }
    public required string CommitReceiptId { get; init; }
    public required string CommitAttemptId { get; init; }
    public required string CommitSha { get; init; }
    public required string CommitTreeHash { get; init; }
    public required string RepositoryRef { get; init; }
    public required string RemoteRef { get; init; }
    public required string TargetBranchRef { get; init; }
    public required string ExpectedRemoteHeadRef { get; init; }
    public required string ObservedRemoteHeadRef { get; init; }
    public required string PushResultRef { get; init; }
    public required string SourceApplyReceiptId { get; init; }
    public required string CommitPackageId { get; init; }
    public required string PatchArtifactId { get; init; }
    public required string PatchArtifactHash { get; init; }
    public required string ValidationResultRef { get; init; }
    public required string AcceptedApprovalRef { get; init; }
    public required string PolicySatisfactionRef { get; init; }
    public required PushReceiptOutcomeKind OutcomeKind { get; init; }
    public required string OutcomeReasonCode { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required string Source { get; init; }
    public required bool IsRedacted { get; init; }
    public required string RedactionReason { get; init; }
    public required string RecordFingerprint { get; init; }
}

public sealed record PersistPushReceiptRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required PushReceiptPersistenceRecord? Receipt { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
}

public sealed record PersistPushReceiptResult
{
    public required bool IsValid { get; init; }
    public required PushReceiptPersistenceStatus PersistenceStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReceiptId { get; init; }
    public required string PushAttemptId { get; init; }
    public required string CommitSha { get; init; }
    public required string TargetBranchRef { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record PushReceiptPersistenceValidationResult
{
    public required bool IsValid { get; init; }
    public required bool HasUnsafePayload { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}
