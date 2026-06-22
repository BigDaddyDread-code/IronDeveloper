namespace IronDev.Core.Governance.PullRequestExecution;

public sealed record DraftPullRequestPostStateObservation
{
    public required string Repository { get; init; }
    public required string HeadBranch { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadCommitId { get; init; }

    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }
    public required bool IsDraft { get; init; }

    public required bool IsObservedAfterMutation { get; init; }
}
