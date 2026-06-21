using IronDev.Core.Governance;

namespace IronDev.Core.Governance.Commit;

public sealed record CommitPackageResult
{
    public required bool IsPackageCreated { get; init; }
    public required string PackageId { get; init; }
    public required CommitPackageManifest? Manifest { get; init; }
    public required GovernedOperationStatus OperationStatus { get; init; }
    public required GovernedOperationStatusValidationResult StatusValidation { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
}
