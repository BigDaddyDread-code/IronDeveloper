namespace IronDev.Core.Governance;

public interface ICommitReceiptPersistenceStore
{
    Task<CommitReceiptPersistenceRecord?> FindByReceiptIdAsync(
        string receiptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommitReceiptPersistenceRecord>> FindByCommitAttemptIdAsync(
        string commitAttemptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommitReceiptPersistenceRecord>> FindByCommitShaAsync(
        string commitSha,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        CommitReceiptPersistenceRecord record,
        CancellationToken cancellationToken = default);
}
