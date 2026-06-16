namespace IronDev.Core.Governance;

public static class SourceApplyDryRunReceiptReadBoundaryText
{
    public const string AuthorityBoundary = "Source-apply dry-run receipt read API is read-only and does not create receipts, perform dry-runs, apply source, write files, apply patches, continue workflow, satisfy approval or policy, approve release, or authorize source mutation.";

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Source-apply dry-run receipt read API is read-only.",
        "Source-apply dry-run receipt read API does not create dry-run receipts.",
        "Source-apply dry-run receipt read API does not perform dry-runs.",
        "Source-apply dry-run receipt read API does not apply source.",
        "Source-apply dry-run receipt read API does not write files.",
        "Source-apply dry-run receipt read API does not apply patches.",
        "Source-apply dry-run receipt read API does not continue workflow.",
        "Source-apply dry-run receipt read API does not approve release.",
        "Source-apply dry-run receipt read API does not satisfy approval or policy.",
        "Source-apply dry-run receipt is rehearsal evidence for review/gating only.",
        "Real source apply still requires accepted approval, policy satisfaction, source-apply gate success, and human review."
    ];
}

public sealed record SourceApplyDryRunReceiptReadBoundary
{
    public bool ReadCreatesSourceApplyDryRunReceipt { get; init; }
    public bool ReadPerformsDryRun { get; init; }
    public bool ReadAppliesSource { get; init; }
    public bool ReadWritesFiles { get; init; }
    public bool ReadAppliesPatch { get; init; }
    public bool ReadRunsGit { get; init; }
    public bool ReadInspectsWorktree { get; init; }
    public bool ReadContinuesWorkflow { get; init; }
    public bool ReadApprovesRelease { get; init; }
    public bool ReadInfersReleaseReadiness { get; init; }
    public bool ReadSatisfiesApproval { get; init; }
    public bool ReadSatisfiesPolicy { get; init; }
    public bool ReadPromotesMemory { get; init; }
    public bool ReadActivatesRetrieval { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; } = true;
    public bool HumanReviewRequiredForMemoryPromotion { get; init; } = true;
}

public sealed record SourceApplyDryRunReceiptReadModel
{
    public required Guid SourceApplyDryRunReceiptId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid SourceApplyDryRunRequestId { get; init; }
    public required string SourceApplyDryRunRequestHash { get; init; }
    public required bool DryRunSatisfied { get; init; }
    public required string DryRunResultHash { get; init; }
    public required Guid SourceApplyRequestId { get; init; }
    public required string SourceApplyRequestHash { get; init; }
    public required Guid SourceApplyGateEvaluationId { get; init; }
    public required string SourceApplyGateEvaluationHash { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required Guid RollbackSupportReceiptId { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public required IReadOnlyList<SourceApplyDryRunReceiptFileResultReadModel> FileResults { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required string SourceApplyDryRunReceiptHash { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public required string Boundary { get; init; }
    public required string AuthorityBoundary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record SourceApplyDryRunReceiptFileResultReadModel
{
    public required string Path { get; init; }
    public string? PreviousPath { get; init; }
    public required string OperationKind { get; init; }
    public required string PatchArtifactChangeHash { get; init; }
    public required string OperationHash { get; init; }
    public required string ExpectedBeforeContentHash { get; init; }
    public required string ExpectedAfterContentHash { get; init; }
    public required string ObservedCurrentContentHash { get; init; }
    public required bool PreconditionsSatisfied { get; init; }
    public required bool WouldCreate { get; init; }
    public required bool WouldModify { get; init; }
    public required bool WouldDelete { get; init; }
    public required bool WouldRename { get; init; }
    public required bool WouldNoop { get; init; }
    public required IReadOnlyList<string> IssueCodes { get; init; }
    public required string FileResultHash { get; init; }
}

public interface ISourceApplyDryRunReceiptQueryService
{
    Task<SourceApplyDryRunReceiptReadModel?> GetAsync(
        Guid projectId,
        Guid sourceApplyDryRunReceiptId,
        CancellationToken cancellationToken = default);

    Task<SourceApplyDryRunReceiptReadModel?> GetByReceiptHashAsync(
        Guid projectId,
        string sourceApplyDryRunReceiptHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyDryRunReceiptReadModel>> ListBySourceApplyRequestAsync(
        Guid projectId,
        Guid sourceApplyRequestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyDryRunReceiptReadModel>> ListBySourceApplyGateEvaluationAsync(
        Guid projectId,
        Guid sourceApplyGateEvaluationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyDryRunReceiptReadModel>> ListByPatchArtifactAsync(
        Guid projectId,
        Guid patchArtifactId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyDryRunReceiptReadModel>> ListByRollbackSupportReceiptAsync(
        Guid projectId,
        Guid rollbackSupportReceiptId,
        CancellationToken cancellationToken = default);
}
