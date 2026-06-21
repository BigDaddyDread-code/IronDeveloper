namespace IronDev.Core.Governance.PushExecution;

public sealed record ControlledPushReceipt
{
    public required string ReceiptRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string RemoteName { get; init; }
    public required string RemoteUrl { get; init; }
    public required string RemoteBranch { get; init; }

    public required string PushedCommitId { get; init; }
    public required string PreviousRemoteHeadCommitId { get; init; }
    public required string NewRemoteHeadCommitId { get; init; }

    public required DateTimeOffset PushedAtUtc { get; init; }

    public required bool ForcePushUsed { get; init; }
    public required bool TagsPushed { get; init; }

    public required bool PullRequestCreationAttempted { get; init; }
    public required bool MergeAttempted { get; init; }
    public required bool ReleaseAttempted { get; init; }
    public required bool DeploymentAttempted { get; init; }
    public required bool MemoryWriteAttempted { get; init; }
    public required bool ContinuationAttempted { get; init; }
}
