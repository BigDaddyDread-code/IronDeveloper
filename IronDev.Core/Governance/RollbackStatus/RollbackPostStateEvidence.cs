namespace IronDev.Core.Governance.RollbackStatus;

public sealed record RollbackPostStateEvidence
{
    public required string EvidenceRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string SourceApplyReceiptRef { get; init; }

    public required bool IsObservedAfterRollback { get; init; }
    public required bool MatchesExpectedPostRollbackState { get; init; }

    public required IReadOnlyCollection<string> RemainingChangedFilePaths { get; init; }
    public required IReadOnlyCollection<string> RemainingStagedFilePaths { get; init; }
    public required IReadOnlyCollection<string> RemainingUntrackedFilePaths { get; init; }
}
