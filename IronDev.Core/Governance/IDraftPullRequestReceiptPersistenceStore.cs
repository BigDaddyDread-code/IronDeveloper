namespace IronDev.Core.Governance;

public interface IDraftPullRequestReceiptPersistenceStore
{
    Task<DraftPullRequestReceiptPersistenceRecord?> FindByReceiptIdAsync(
        string receiptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DraftPullRequestReceiptPersistenceRecord>> FindByDraftPullRequestAttemptIdAsync(
        string draftPullRequestAttemptId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DraftPullRequestReceiptPersistenceRecord>> FindByPullRequestRefAsync(
        string pullRequestRef,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DraftPullRequestReceiptPersistenceRecord>> FindByPullRequestNumberRefAsync(
        string pullRequestNumberRef,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        DraftPullRequestReceiptPersistenceRecord record,
        CancellationToken cancellationToken = default);
}
