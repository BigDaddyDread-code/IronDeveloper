namespace IronDev.Core.Governance.CommitExecution;

public sealed record CommitWorktreeObservation
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string WorktreeRoot { get; init; }

    public required string HeadCommitId { get; init; }

    public required string CurrentDiffHash { get; init; }

    public required IReadOnlyCollection<string> ChangedFilePaths { get; init; }
    public required IReadOnlyCollection<string> StagedFilePaths { get; init; }
    public required IReadOnlyCollection<string> UntrackedFilePaths { get; init; }

    public required bool IsWorktreeReadable { get; init; }
}
