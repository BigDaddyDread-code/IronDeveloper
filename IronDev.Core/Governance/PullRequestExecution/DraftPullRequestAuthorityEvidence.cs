namespace IronDev.Core.Governance.PullRequestExecution;

public sealed record DraftPullRequestAuthorityEvidence
{
    public required string EvidenceRef { get; init; }

    public required string Repository { get; init; }
    public required string HeadBranch { get; init; }
    public required string BaseBranch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }
    public required string HeadCommitId { get; init; }

    public required OperationEligibilityDecision? Decision { get; init; }
}
