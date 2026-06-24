namespace IronDev.Core.Governance;

public enum RollbackRecoveryMaterialKind
{
    Unknown = 0,
    RollbackPlan = 1,
    RollbackEvidence = 2,
    RollbackReceipt = 3,
    RollbackExecutionObserved = 4,
    RollbackExecutionFailed = 5,
    RecoveryPlan = 6,
    RecoveryEvidence = 7,
    RecoveryReceipt = 8,
    RecoveryExecutionObserved = 9,
    RecoveryExecutionFailed = 10,
    RecoveryValidationObserved = 11,
    RecoveryPatchObserved = 12,
    OperatorNote = 13
}

public enum RollbackRecoveryStateKind
{
    Unknown = 0,
    NoRollbackOrRecoveryObserved = 1,
    RollbackMaterialAvailable = 2,
    RollbackMaterialMissing = 3,
    RollbackObserved = 4,
    RollbackFailed = 5,
    RecoveryMaterialAvailable = 6,
    RecoveryMaterialMissing = 7,
    RecoveryObserved = 8,
    RecoveryFailed = 9,
    RollbackAndRecoveryObserved = 10,
    Ambiguous = 11,
    Unassessable = 12
}

public enum RollbackRecoveryGapKind
{
    Unknown = 0,
    NoneObserved = 1,
    InterruptedNoRollbackPlan = 2,
    InterruptedNoRecoveryPlan = 3,
    RollbackPlanNoEvidence = 4,
    RollbackEvidenceNoReceipt = 5,
    RecoveryPlanNoEvidence = 6,
    RecoveryEvidenceNoReceipt = 7,
    RollbackFailed = 8,
    RecoveryFailed = 9,
    Ambiguous = 10
}

public enum RollbackRecoveryReadModelStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoMaterial = 2,
    Assessed = 3,
    MissingMaterial = 4,
    FailureObserved = 5,
    AmbiguousMaterial = 6,
    Unassessable = 7
}

public sealed record RollbackRecoveryMaterialObservation
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required string CorrelationId { get; init; }
    public required string MaterialId { get; init; }
    public required RollbackRecoveryMaterialKind MaterialKind { get; init; }
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

public sealed record RollbackRecoveryDiagnosticSnapshot
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required InterruptedRunReadModelStatus InterruptedRunStatus { get; init; }
    public required InterruptedRunStateKind InterruptedRunState { get; init; }
    public required InterruptedRunGapKind InterruptedRunGap { get; init; }
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

public sealed record RollbackRecoveryReadModelRequest
{
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public required IReadOnlyList<RollbackRecoveryMaterialObservation> Materials { get; init; }
    public RollbackRecoveryDiagnosticSnapshot? DiagnosticSnapshot { get; init; }
}

public sealed record RollbackRecoveryAssessment
{
    public required RollbackRecoveryStateKind StateKind { get; init; }
    public required RollbackRecoveryGapKind GapKind { get; init; }
    public string? LastMaterialId { get; init; }
    public required RollbackRecoveryMaterialKind LastMaterialKind { get; init; }
    public DateTimeOffset? LastMaterialObservedAtUtc { get; init; }
    public DateTimeOffset? LastMaterialRecordedAtUtc { get; init; }
    public string? DiagnosticSummary { get; init; }
    public required string Reason { get; init; }
    public required OperationCorrelationSurfaceKind SurfaceKind { get; init; }
    public string? SurfaceId { get; init; }
    public required OperationReferenceKind ReferenceKind { get; init; }
    public string? ReferenceId { get; init; }
    public required bool IsRedacted { get; init; }
}

public sealed record RollbackRecoveryReadModel
{
    public required bool IsValid { get; init; }
    public required RollbackRecoveryReadModelStatus ResolutionStatus { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string OperationId { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
    public RollbackRecoveryAssessment? Assessment { get; init; }
    public required IReadOnlyList<string> MaterialIds { get; init; }
    public required IReadOnlyList<string> AmbiguousMaterial { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> ForbiddenAuthorityImplications { get; init; }
}
