using IronDev.Core.Governance;

namespace IronDev.Core.Governance.Commit;

public sealed record CommitPackageRequest
{
    public required string PackageId { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required SourceApplyReceiptEvidence? SourceApplyReceipt { get; init; }
    public required ExpectedDiffEvidence? ExpectedDiff { get; init; }
    public required CommitOperationAuthorityEvidence? CommitAuthority { get; init; }
    public required CommitMessageEvidence? MessageEvidence { get; init; }
    public required CommitValidationRequirementEvidence? ValidationRequirement { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
}
