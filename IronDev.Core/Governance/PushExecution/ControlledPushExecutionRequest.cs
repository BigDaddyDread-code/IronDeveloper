using IronDev.Core.Governance.CommitExecution;

namespace IronDev.Core.Governance.PushExecution;

public sealed record ControlledPushExecutionRequest
{
    public required string ExecutionId { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string RemoteName { get; init; }
    public required string RemoteUrl { get; init; }
    public required string RemoteBranch { get; init; }

    public required string ExpectedLocalCommitId { get; init; }
    public required string ExpectedRemoteHeadCommitId { get; init; }

    public required ControlledCommitReceipt? CommitReceipt { get; init; }
    public required PushAuthorityEvidence? PushAuthority { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
}
