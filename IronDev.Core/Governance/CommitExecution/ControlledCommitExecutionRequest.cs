using CommitPackageManifestModel = IronDev.Core.Governance.Commit.CommitPackageManifest;
using CommitPackageRequestModel = IronDev.Core.Governance.Commit.CommitPackageRequest;

namespace IronDev.Core.Governance.CommitExecution;

public sealed record ControlledCommitExecutionRequest
{
    public required string ExecutionId { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string WorktreeRoot { get; init; }

    public required CommitPackageRequestModel? CommitPackageRequest { get; init; }
    public required CommitPackageManifestModel? CommitPackageManifest { get; init; }

    public required IReadOnlyCollection<string> ExpectedFilePaths { get; init; }
    public required string ExpectedDiffHash { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
}
