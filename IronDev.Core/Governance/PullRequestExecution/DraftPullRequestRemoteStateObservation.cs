namespace IronDev.Core.Governance.PullRequestExecution;

public sealed record DraftPullRequestRemoteStateObservation
{
    public required string Repository { get; init; }
    public required string HeadBranch { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadCommitId { get; init; }

    public int? ExistingPullRequestNumber { get; init; }
    public string? ExistingPullRequestUrl { get; init; }
    public bool? ExistingPullRequestIsDraft { get; init; }

    public required bool IsRepositoryReachable { get; init; }
    public required bool HeadBranchExists { get; init; }
    public required bool BaseBranchExists { get; init; }
}
