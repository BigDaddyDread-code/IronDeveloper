namespace IronDev.Core.Governance;

public enum ValidationOutcome
{
    Passed = 1,
    Failed = 2,
    Inconclusive = 3
}

public sealed record ValidationResultPackageRequest
{
    public required string OperationId { get; init; }
    public required string RepoId { get; init; }
    public required string Branch { get; init; }
    public required string WorkspacePath { get; init; }
    public required string OutputPath { get; init; }
    public required string ProposalId { get; init; }
    public required string PatchHash { get; init; }
    public required string ValidationRunId { get; init; }
    public required string ValidationName { get; init; }
    public required ValidationOutcome Outcome { get; init; }
    public IReadOnlyList<string> EvidenceFileNames { get; init; } = [];
    public IReadOnlyList<string> ValidationMessages { get; init; } = [];
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record ValidationResultPackageResult
{
    public required bool IsPackageCreated { get; init; }
    public required GovernedOperationStatus Status { get; init; }
    public required GovernedOperationStatusValidationResult StatusValidation { get; init; }
    public required string PackageId { get; init; }
    public required string PackagePath { get; init; }
    public required string ValidationRef { get; init; }
    public required ValidationOutcome Outcome { get; init; }
    public required IReadOnlyList<string> ArtifactRefs { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> RedFlags { get; init; }
}

public sealed record ValidationResultPackageManifest
{
    public required string PackageId { get; init; }
    public required string ValidationRunId { get; init; }
    public required string ValidationName { get; init; }
    public required string ProposalId { get; init; }
    public required string PatchHash { get; init; }
    public required string RepoId { get; init; }
    public required string Branch { get; init; }
    public required string WorkspaceId { get; init; }
    public required ValidationOutcome Outcome { get; init; }
    public required string ValidationRef { get; init; }
    public required IReadOnlyList<string> ArtifactRefs { get; init; }
    public required IReadOnlyList<string> EvidenceFileNames { get; init; }
    public required IReadOnlyList<string> ValidationMessages { get; init; }
    public required IReadOnlyList<string> ForbiddenActions { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record ValidationEvidenceFile
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
}

public sealed record ValidationResultPackageValidationResult
{
    public required bool CanPackage { get; init; }
    public required string WorkspaceRootPath { get; init; }
    public required string OutputRootPath { get; init; }
    public required string SourceRootPath { get; init; }
    public required DisposableWorkspaceMarker? Marker { get; init; }
    public required IReadOnlyList<ValidationEvidenceFile> EvidenceFiles { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
}
