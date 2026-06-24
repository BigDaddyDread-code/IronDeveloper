namespace IronDev.Core.Governance;

public interface ISourceApplyReceiptPersistenceStore
{
    Task<SourceApplyReceiptPersistenceRecord?> FindByReceiptIdAsync(
        string receiptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyReceiptPersistenceRecord>> FindBySourceApplyAttemptIdAsync(
        string sourceApplyAttemptId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        SourceApplyReceiptPersistenceRecord record,
        CancellationToken cancellationToken = default);
}
