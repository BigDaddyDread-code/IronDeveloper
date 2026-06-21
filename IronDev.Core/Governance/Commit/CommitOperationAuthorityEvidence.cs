using IronDev.Core.Governance;

namespace IronDev.Core.Governance.Commit;

public sealed record CommitOperationAuthorityEvidence
{
    public required string EvidenceRef { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required IReadOnlyCollection<string> FilePaths { get; init; }

    public required OperationEligibilityDecision? Decision { get; init; }
}
