namespace IronDev.Core.Governance;

public static class RollbackSupportReceiptReadBoundaryText
{
    public const string AuthorityBoundary = "Rollback support receipt read API is read-only and does not authorize rollback execution, source apply, workflow continuation, release readiness, or source mutation.";

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Rollback support receipt read API is read-only.",
        "Rollback support receipt read API does not execute rollback.",
        "Rollback support receipt read API does not mark rollback success.",
        "Rollback support receipt read API does not apply source.",
        "Rollback support receipt read API does not continue workflow.",
        "Rollback support receipt read API does not approve release.",
        "Rollback support receipt read API does not authorize source mutation.",
        "Rollback support receipt is evidence for review/gating only.",
        "Real source apply must still pass the source-apply gate before mutation."
    ];
}

public sealed record RollbackSupportReceiptReadBoundary
{
    public bool RollbackReadExecutesRollback { get; init; }
    public bool RollbackReadMarksRollbackSuccess { get; init; }
    public bool RollbackReadAppliesSource { get; init; }
    public bool RollbackReadContinuesWorkflow { get; init; }
    public bool RollbackReadApprovesRelease { get; init; }
    public bool RollbackReadInfersReleaseReadiness { get; init; }
    public bool RollbackReadAuthorizesSourceMutation { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; } = true;
    public bool HumanReviewRequiredForMemoryPromotion { get; init; } = true;
}

public sealed record RollbackSupportReceiptReadModel
{
    public required Guid RollbackSupportReceiptId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid RollbackPlanId { get; init; }
    public required string RollbackPlanHash { get; init; }
    public required bool RollbackGateSatisfied { get; init; }
    public required string RollbackGateEvaluationHash { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required Guid ControlledDryRunRequestId { get; init; }
    public required Guid DryRunExecutionAuditId { get; init; }
    public required string DryRunAuditHash { get; init; }
    public required string DryRunReceiptHash { get; init; }
    public required Guid PolicySatisfactionId { get; init; }
    public required string PolicySatisfactionHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string SourceSnapshotReference { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public required string Boundary { get; init; }
    public required string AuthorityBoundary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public interface IRollbackSupportReceiptQueryService
{
    Task<RollbackSupportReceiptReadModel?> GetAsync(
        Guid projectId,
        Guid rollbackSupportReceiptId,
        CancellationToken cancellationToken = default);

    Task<RollbackSupportReceiptReadModel?> GetByReceiptHashAsync(
        Guid projectId,
        string rollbackSupportReceiptHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackSupportReceiptReadModel>> ListByPatchArtifactAsync(
        Guid projectId,
        Guid patchArtifactId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackSupportReceiptReadModel>> ListByPatchHashAsync(
        Guid projectId,
        string patchHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackSupportReceiptReadModel>> ListByRollbackPlanAsync(
        Guid projectId,
        Guid rollbackPlanId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackSupportReceiptReadModel>> ListBySourceBaselineHashAsync(
        Guid projectId,
        string sourceBaselineHash,
        CancellationToken cancellationToken = default);
}
