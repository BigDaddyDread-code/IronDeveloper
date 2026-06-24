namespace IronDev.Core.Governance;

public enum SourceApplyReceiptPersistenceStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    Persisted = 2,
    AlreadyPersisted = 3,
    Conflict = 4,
    RejectedUnsafePayload = 5,
    Unassessable = 6
}

public enum SourceApplyReceiptOutcomeKind
{
    Unknown = 0,
    Started = 1,
    Succeeded = 2,
    Failed = 3,
    Interrupted = 4,
    Cancelled = 5
}

public sealed record SourceApplyReceiptPersistenceRecord
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReceiptId { get; init; }
    public required string SourceApplyAttemptId { get; init; }
    public required string PatchArtifactId { get; init; }
    public required string PatchArtifactHash { get; init; }
    public required string PatchBaseRef { get; init; }
    public required string ValidationResultRef { get; init; }
    public required string AcceptedApprovalRef { get; init; }
    public required string PolicySatisfactionRef { get; init; }
    public required string DryRunRef { get; init; }
    public required string WorktreeBeforeRef { get; init; }
    public required string WorktreeAfterRef { get; init; }
    public required SourceApplyReceiptOutcomeKind OutcomeKind { get; init; }
    public required string OutcomeReasonCode { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required string Source { get; init; }
    public required bool IsRedacted { get; init; }
    public required string RedactionReason { get; init; }
    public required string RecordFingerprint { get; init; }
}

public sealed record PersistSourceApplyReceiptRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required SourceApplyReceiptPersistenceRecord? Receipt { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
}

public sealed record PersistSourceApplyReceiptResult
{
    public required bool IsValid { get; init; }
    public required SourceApplyReceiptPersistenceStatus PersistenceStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ReceiptId { get; init; }
    public required string SourceApplyAttemptId { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}

public sealed record SourceApplyReceiptPersistenceValidationResult
{
    public required bool IsValid { get; init; }
    public required bool HasUnsafePayload { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}
