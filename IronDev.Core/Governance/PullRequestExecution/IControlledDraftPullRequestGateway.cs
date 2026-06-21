namespace IronDev.Core.Governance.PullRequestExecution;

public interface IControlledDraftPullRequestGateway
{
    Task<ControlledDraftPullRequestReceipt?> CreateOrUpdateDraftPullRequestAsync(
        ControlledDraftPullRequestGatewayRequest request,
        CancellationToken cancellationToken);
}

public sealed record ControlledDraftPullRequestGatewayRequest
{
    public required string Repository { get; init; }
    public required string HeadBranch { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadCommitId { get; init; }

    public required string Title { get; init; }
    public required string Body { get; init; }

    public int? ExistingPullRequestNumber { get; init; }

    public required bool DraftOnly { get; init; }
    public required bool ReadyForReviewDisabled { get; init; }
    public required bool ReviewerRequestsDisabled { get; init; }
    public required bool MergeDisabled { get; init; }

    public required string RunId { get; init; }
    public required string PatchHash { get; init; }
}
