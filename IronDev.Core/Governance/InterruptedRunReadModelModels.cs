namespace IronDev.Core.Governance;

public enum InterruptedRunCheckpointKind
{
    Unknown = 0,
    OperationMinted = 1,
    RunStarted = 2,
    WorkspaceCreated = 3,
    PatchArtifactCreated = 4,
    ValidationStarted = 5,
    ValidationPassed = 6,
    ValidationFailed = 7,
    SourceApplyStarted = 8,
    SourceApplyCompleted = 9,
    CommitPackageCreated = 10,
    CommitCreated = 11,
    PushCompleted = 12,
    PullRequestCreated = 13,
    Completed = 14,
    Failed = 15,
    Cancelled = 16
}

public enum InterruptedRunGapKind
{
    Unknown = 0,
    NoneObserved = 1,
    WorkspaceCreatedNoPatch = 2,
    PatchCreatedNoValidation = 3,
    ValidationFailed = 4,
    ApplyStartedNotCompleted = 5,
    CommitPackageCreatedNoCommit = 6,
    CommitCreatedNoPush = 7,
    PushCompletedNoPullRequest = 8,
    Cancelled = 9,
    Failed = 10,
    Ambiguous = 11
}

public enum InterruptedRunStateKind
{
    Unknown = 0,
    NoInterruptionObserved = 1,
    Interrupted = 2,
    Failed = 3,
    Cancelled = 4,
    Ambiguous = 5,
    Unassessable = 6
}

public enum InterruptedRunReadModelStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoCheckpoints = 2,
    NoInterruptionObserved = 3,
    Interrupted = 4,
    Failed = 5,
    Cancelled = 6,
    AmbiguousCheckpoints = 7,
    Unassessable = 8
}

public sealed record InterruptedRunCheckpointObservation
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string CheckpointId { get; init; }
    public required InterruptedRunCheckpointKind CheckpointKind { get; init; }
    public required long AppendPosition { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public required string SurfaceId { get; init; }
    public OperationReferenceKind ReferenceKind { get; init; } = OperationReferenceKind.Unknown;
    public string? ReferenceId { get; init; }
    public required string Source { get; init; }
    public bool IsRedacted { get; init; }
    public string? RedactionReason { get; init; }
}

public sealed record InterruptedRunDiagnosticSnapshot
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required GovernedOperationState ProjectedStatusKind { get; init; }
    public required MissingEvidenceResolutionStatus MissingEvidenceStatus { get; init; }
    public required ForbiddenActionResolutionStatus ForbiddenActionStatus { get; init; }
    public required ReceiptReferenceResolutionStatus ReceiptResolutionStatus { get; init; }
    public required EvidenceResolutionStatus EvidenceResolutionStatus { get; init; }
    public required ValidationStalenessResolutionStatus ValidationStalenessStatus { get; init; }
    public required PatchBaseFreshnessResolutionStatus PatchBaseFreshnessStatus { get; init; }
    public required WorktreeBaseHeadFreshnessResolutionStatus WorktreeBaseHeadFreshnessStatus { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
}

public sealed record InterruptedRunReadModelRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<InterruptedRunCheckpointObservation> Checkpoints { get; init; }
    public InterruptedRunDiagnosticSnapshot? DiagnosticSnapshot { get; init; }
}

public sealed record InterruptedRunAssessment
{
    public required InterruptedRunStateKind StateKind { get; init; }
    public required InterruptedRunGapKind GapKind { get; init; }
    public string? LastCheckpointId { get; init; }
    public required InterruptedRunCheckpointKind LastCheckpointKind { get; init; }
    public DateTimeOffset? LastCheckpointObservedAtUtc { get; init; }
    public DateTimeOffset? LastCheckpointRecordedAtUtc { get; init; }
    public string? DiagnosticSummary { get; init; }
    public required string Reason { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public string? SurfaceId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public string? ReferenceId { get; init; }
    public required bool IsRedacted { get; init; }
}

public sealed record InterruptedRunReadModel
{
    public required bool IsValid { get; init; }
    public required InterruptedRunReadModelStatus ResolutionStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public InterruptedRunAssessment? Assessment { get; init; }
    public required IReadOnlyList<string> CheckpointIds { get; init; }
    public required IReadOnlyList<string> AmbiguousCheckpoints { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}
