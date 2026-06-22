namespace IronDev.Core.Governance.RollbackExecution;

public sealed record RollbackObservedFileState
{
    public required string Path { get; init; }
    public required bool Exists { get; init; }
    public string? ContentHash { get; init; }
}

public sealed record RollbackPreStateObservation
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string HeadCommitId { get; init; }

    public required string SourceApplyReceiptRef { get; init; }
    public required string RollbackTargetId { get; init; }

    public required IReadOnlyCollection<RollbackObservedFileState> ObservedFiles { get; init; }

    public required IReadOnlyCollection<string> ChangedFilePaths { get; init; }
    public required IReadOnlyCollection<string> StagedFilePaths { get; init; }
    public required IReadOnlyCollection<string> UntrackedFilePaths { get; init; }

    public required bool IsObservedImmediatelyBeforeRollback { get; init; }
}
