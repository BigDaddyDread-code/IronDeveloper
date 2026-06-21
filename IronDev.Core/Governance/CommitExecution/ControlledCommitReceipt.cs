namespace IronDev.Core.Governance.CommitExecution;

public sealed record ControlledCommitReceipt
{
    public required string ReceiptRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string PackageId { get; init; }

    public required string CommitId { get; init; }
    public required string ParentCommitId { get; init; }

    public required IReadOnlyCollection<string> CommittedFilePaths { get; init; }

    public required string CommitSubject { get; init; }

    public required DateTimeOffset CommittedAtUtc { get; init; }

    public required bool HooksDisabled { get; init; }

    public required bool PushAttempted { get; init; }
    public required bool PullRequestCreationAttempted { get; init; }
    public required bool MergeAttempted { get; init; }
    public required bool ReleaseAttempted { get; init; }
    public required bool DeploymentAttempted { get; init; }
    public required bool MemoryWriteAttempted { get; init; }
    public required bool ContinuationAttempted { get; init; }
}
