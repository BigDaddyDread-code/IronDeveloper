using IronDev.Core.Governance;

namespace IronDev.Core.Governance.Commit;

public sealed record CommitPackageManifest
{
    public required string PackageId { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string SourceApplyReceiptRef { get; init; }
    public required string ExpectedDiffEvidenceRef { get; init; }
    public required string ExpectedDiffHash { get; init; }

    public required string CommitMessageEvidenceRef { get; init; }
    public required string CommitSubject { get; init; }

    public required IReadOnlyCollection<string> FilePaths { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }

    public required GovernedOperationStatus OperationStatus { get; init; }
}
