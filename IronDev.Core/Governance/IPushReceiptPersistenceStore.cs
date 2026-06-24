namespace IronDev.Core.Governance;

public interface IPushReceiptPersistenceStore
{
    Task<PushReceiptPersistenceRecord?> FindByReceiptIdAsync(
        string receiptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PushReceiptPersistenceRecord>> FindByPushAttemptIdAsync(
        string pushAttemptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PushReceiptPersistenceRecord>> FindByCommitShaAsync(
        string commitSha,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PushReceiptPersistenceRecord>> FindByTargetBranchRefAsync(
        string targetBranchRef,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        PushReceiptPersistenceRecord record,
        CancellationToken cancellationToken = default);
}
