namespace IronDev.Core.Governance.RollbackStatus;

public sealed record RollbackWorktreeStateEvidence
{
    public required string EvidenceRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string HeadCommitId { get; init; }

    public required IReadOnlyCollection<string> ChangedFilePaths { get; init; }
    public required IReadOnlyCollection<string> StagedFilePaths { get; init; }
    public required IReadOnlyCollection<string> UntrackedFilePaths { get; init; }

    public required bool IsObservedImmediatelyBeforeRollback { get; init; }
}
