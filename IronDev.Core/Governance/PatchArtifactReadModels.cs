namespace IronDev.Core.Governance;

public static class PatchArtifactReadBoundaryText
{
    public const string AuthorityBoundary = "Patch artifact read API is read-only and does not authorize source mutation, rollback, workflow continuation, or release readiness.";

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Patch artifact read API is read-only.",
        "Patch artifact read API does not create patch artifacts.",
        "Patch artifact read API does not apply source.",
        "Patch artifact read API does not execute rollback.",
        "Patch artifact read API does not continue workflow.",
        "Patch artifact read API does not approve release.",
        "Reading a patch artifact does not authorize source mutation.",
        "Patch artifact must still be reviewed before source apply."
    ];
}

public sealed record PatchArtifactReadBoundary
{
    public bool PatchArtifactReadIsCreation { get; init; }
    public bool PatchArtifactReadAppliesSource { get; init; }
    public bool PatchArtifactReadExecutesRollback { get; init; }
    public bool PatchArtifactReadContinuesWorkflow { get; init; }
    public bool PatchArtifactReadApprovesRelease { get; init; }
    public bool PatchArtifactReadAuthorizesSourceMutation { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; } = true;
    public bool HumanReviewRequiredForMemoryPromotion { get; init; } = true;
}

public sealed record PatchArtifactReadModel
{
    public required Guid PatchArtifactId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string PatchArtifactKind { get; init; }
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
    public required string ValidationPlanId { get; init; }
    public required string ValidationPlanHash { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required IReadOnlyList<PatchArtifactFileChangeReadModel> FileChanges { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public required string Boundary { get; init; }
    public required string AuthorityBoundary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record PatchArtifactFileChangeReadModel
{
    public required string Path { get; init; }
    public string? PreviousPath { get; init; }
    public required string ChangeKind { get; init; }
    public string? BeforeContentHash { get; init; }
    public string? AfterContentHash { get; init; }
    public required string DiffHash { get; init; }
    public required string NormalizedDiff { get; init; }
    public bool IsBinary { get; init; }
}

public interface IPatchArtifactQueryService
{
    Task<PatchArtifactReadModel?> GetAsync(
        Guid projectId,
        Guid patchArtifactId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifactReadModel>> ListByDryRunReceiptHashAsync(
        Guid projectId,
        string dryRunReceiptHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifactReadModel>> ListByDryRunAuditHashAsync(
        Guid projectId,
        string dryRunAuditHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifactReadModel>> ListByControlledDryRunRequestAsync(
        Guid projectId,
        Guid controlledDryRunRequestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifactReadModel>> ListBySubjectAsync(
        Guid projectId,
        string subjectKind,
        string subjectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifactReadModel>> ListByPatchHashAsync(
        Guid projectId,
        string patchHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifactReadModel>> ListBySourceBaselineHashAsync(
        Guid projectId,
        string sourceBaselineHash,
        CancellationToken cancellationToken = default);
}
