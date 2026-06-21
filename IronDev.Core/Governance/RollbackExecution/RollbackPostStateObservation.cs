namespace IronDev.Core.Governance.RollbackExecution;

public sealed record RollbackPostStateObservation
{
    public required string Repository { get; init; }
    public required string Branch { get; init; }

    public required string SourceApplyReceiptRef { get; init; }
    public required string RollbackTargetId { get; init; }

    public required IReadOnlyCollection<RollbackObservedFileState> ObservedFiles { get; init; }

    public required IReadOnlyCollection<string> RemainingChangedFilePaths { get; init; }
    public required IReadOnlyCollection<string> RemainingStagedFilePaths { get; init; }
    public required IReadOnlyCollection<string> RemainingUntrackedFilePaths { get; init; }

    public required bool IsObservedAfterRollback { get; init; }
    public required bool MatchesExpectedPostRollbackState { get; init; }
}
