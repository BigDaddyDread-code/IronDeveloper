namespace IronDev.Core.Governance;

public interface IRollbackReceiptPersistenceStore
{
    Task<RollbackReceiptPersistenceRecord?> FindByReceiptIdAsync(
        string receiptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackReceiptPersistenceRecord>> FindByRollbackAttemptIdAsync(
        string rollbackAttemptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackReceiptPersistenceRecord>> FindByRollbackTargetRefAsync(
        string rollbackTargetRef,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackReceiptPersistenceRecord>> FindByRollbackResultRefAsync(
        string rollbackResultRef,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        RollbackReceiptPersistenceRecord record,
        CancellationToken cancellationToken = default);
}
