namespace IronDev.Core.Governance.PullRequestExecution;

public sealed record ControlledDraftPullRequestReceipt
{
    public required string ReceiptRef { get; init; }

    public required string Repository { get; init; }
    public required string HeadBranch { get; init; }
    public required string BaseBranch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }
    public required string HeadCommitId { get; init; }

    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }
    public required bool IsDraft { get; init; }

    public required bool WasCreated { get; init; }
    public required bool WasUpdated { get; init; }

    public required DateTimeOffset CreatedOrUpdatedAtUtc { get; init; }

    public required bool ReadyForReviewAttempted { get; init; }
    public required bool ReviewerRequestAttempted { get; init; }
    public required bool MergeAttempted { get; init; }
    public required bool ReleaseAttempted { get; init; }
    public required bool DeploymentAttempted { get; init; }
    public required bool MemoryWriteAttempted { get; init; }
    public required bool ContinuationAttempted { get; init; }
}
