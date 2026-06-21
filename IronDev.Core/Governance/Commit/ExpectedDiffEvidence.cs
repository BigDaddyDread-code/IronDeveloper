namespace IronDev.Core.Governance.Commit;

public sealed record ExpectedDiffEvidence
{
    public required string EvidenceRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string ExpectedDiffHash { get; init; }
    public required IReadOnlyCollection<string> ExpectedChangedFilePaths { get; init; }

    public required bool IsCleanExpectedDiff { get; init; }
}
