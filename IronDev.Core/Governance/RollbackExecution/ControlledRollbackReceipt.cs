namespace IronDev.Core.Governance.RollbackExecution;

public sealed record ControlledRollbackReceipt
{
    public required string ReceiptRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string SourceApplyReceiptRef { get; init; }
    public required string RollbackTargetId { get; init; }

    public required IReadOnlyCollection<string> RolledBackFilePaths { get; init; }

    public required bool CompleteRollbackExecuted { get; init; }
    public required bool PartialRollbackAttempted { get; init; }
    public required bool PartialRollbackFailed { get; init; }

    public required DateTimeOffset ExecutedAtUtc { get; init; }

    public required bool CommitAttempted { get; init; }
    public required bool PushAttempted { get; init; }
    public required bool PullRequestAttempted { get; init; }
    public required bool MergeAttempted { get; init; }
    public required bool ReleaseAttempted { get; init; }
    public required bool DeploymentAttempted { get; init; }
    public required bool MemoryWriteAttempted { get; init; }
    public required bool ContinuationAttempted { get; init; }
}
