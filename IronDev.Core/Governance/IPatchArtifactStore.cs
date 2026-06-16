namespace IronDev.Core.Governance;

public interface IPatchArtifactStore
{
    Task SaveAsync(
        PatchArtifact patchArtifact,
        CancellationToken cancellationToken = default);

    Task<PatchArtifact?> GetAsync(
        Guid projectId,
        Guid patchArtifactId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifact>> ListByDryRunReceiptHashAsync(
        Guid projectId,
        string dryRunReceiptHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifact>> ListByDryRunAuditHashAsync(
        Guid projectId,
        string dryRunAuditHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifact>> ListByControlledDryRunRequestAsync(
        Guid projectId,
        Guid controlledDryRunRequestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifact>> ListBySubjectAsync(
        Guid projectId,
        string subjectKind,
        string subjectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifact>> ListByPatchHashAsync(
        Guid projectId,
        string patchHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchArtifact>> ListBySourceBaselineHashAsync(
        Guid projectId,
        string sourceBaselineHash,
        CancellationToken cancellationToken = default);
}
