namespace IronDev.Core.Governance.PullRequestExecution;

public sealed record DraftPullRequestTextPackage
{
    public required string TextPackageId { get; init; }

    public required string Repository { get; init; }
    public required string HeadBranch { get; init; }
    public required string BaseBranch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }
    public required string HeadCommitId { get; init; }

    public required string Title { get; init; }
    public required string Body { get; init; }
    public required string TextSource { get; init; }
}
