namespace IronDev.Core.Governance.CommitExecution;

public sealed record CommitPostStateObservation
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string HeadCommitId { get; init; }

    public required IReadOnlyCollection<string> RemainingChangedFilePaths { get; init; }
    public required IReadOnlyCollection<string> RemainingStagedFilePaths { get; init; }
    public required IReadOnlyCollection<string> RemainingUntrackedFilePaths { get; init; }

    public required bool IsObservedAfterCommit { get; init; }
}
