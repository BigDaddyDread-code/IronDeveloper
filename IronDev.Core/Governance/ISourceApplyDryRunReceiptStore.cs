namespace IronDev.Core.Governance;

public interface ISourceApplyDryRunReceiptStore
{
    Task SaveAsync(SourceApplyDryRunReceipt receipt, CancellationToken cancellationToken = default);

    Task<SourceApplyDryRunReceipt?> GetAsync(
        Guid projectId,
        Guid sourceApplyDryRunReceiptId,
        CancellationToken cancellationToken = default);

    Task<SourceApplyDryRunReceipt?> GetByReceiptHashAsync(
        Guid projectId,
        string sourceApplyDryRunReceiptHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyDryRunReceipt>> ListBySourceApplyRequestAsync(
        Guid projectId,
        Guid sourceApplyRequestId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyDryRunReceipt>> ListBySourceApplyGateEvaluationAsync(
        Guid projectId,
        Guid sourceApplyGateEvaluationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyDryRunReceipt>> ListByPatchArtifactAsync(
        Guid projectId,
        Guid patchArtifactId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyDryRunReceipt>> ListByRollbackSupportReceiptAsync(
        Guid projectId,
        Guid rollbackSupportReceiptId,
        CancellationToken cancellationToken = default);
}
