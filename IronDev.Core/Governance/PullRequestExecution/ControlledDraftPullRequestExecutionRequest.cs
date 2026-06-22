using IronDev.Core.Governance.PushExecution;

namespace IronDev.Core.Governance.PullRequestExecution;

public sealed record ControlledDraftPullRequestExecutionRequest
{
    public required string ExecutionId { get; init; }

    public required string Repository { get; init; }
    public required string HeadBranch { get; init; }
    public required string BaseBranch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }
    public required string HeadCommitId { get; init; }

    public int? ExistingPullRequestNumber { get; init; }

    public required ControlledPushReceipt? PushReceipt { get; init; }
    public required DraftPullRequestAuthorityEvidence? DraftPullRequestAuthority { get; init; }
    public required DraftPullRequestTextPackage? TextPackage { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
}
