namespace IronDev.Core.Governance;

public interface IRollbackSupportReceiptStore
{
    Task SaveAsync(
        RollbackSupportReceipt receipt,
        CancellationToken cancellationToken = default);

    Task<RollbackSupportReceipt?> GetAsync(
        Guid projectId,
        Guid rollbackSupportReceiptId,
        CancellationToken cancellationToken = default);

    Task<RollbackSupportReceipt?> GetByReceiptHashAsync(
        Guid projectId,
        string rollbackSupportReceiptHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackSupportReceipt>> ListByPatchArtifactAsync(
        Guid projectId,
        Guid patchArtifactId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackSupportReceipt>> ListByPatchHashAsync(
        Guid projectId,
        string patchHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackSupportReceipt>> ListByRollbackPlanAsync(
        Guid projectId,
        Guid rollbackPlanId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackSupportReceipt>> ListBySourceBaselineHashAsync(
        Guid projectId,
        string sourceBaselineHash,
        CancellationToken cancellationToken = default);
}
