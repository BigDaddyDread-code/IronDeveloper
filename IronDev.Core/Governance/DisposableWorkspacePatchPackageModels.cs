namespace IronDev.Core.Governance;

public sealed record DisposableWorkspacePatchPackageRequest
{
    public required string OperationId { get; init; }
    public required string RepoId { get; init; }
    public required string Branch { get; init; }
    public required string WorkspacePath { get; init; }
    public required string OutputPath { get; init; }
    public required string ProposalId { get; init; }
    public required string TaskSummary { get; init; }
    public IReadOnlyList<string> AllowedPathGlobs { get; init; } = [];
    public IReadOnlyList<string> ForbiddenPathGlobs { get; init; } = [];
    public IReadOnlyList<string> ValidationRefs { get; init; } = [];
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record DisposableWorkspacePatchPackageResult
{
    public required bool IsPackageCreated { get; init; }
    public required GovernedOperationStatus Status { get; init; }
    public required GovernedOperationStatusValidationResult StatusValidation { get; init; }
    public required string PackageId { get; init; }
    public required string PatchHash { get; init; }
    public required string PackagePath { get; init; }
    public required IReadOnlyList<string> ArtifactRefs { get; init; }
    public required IReadOnlyList<string> ValidationRefs { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<string> RedFlags { get; init; }
}

public sealed record DisposableWorkspacePatchPackageManifest
{
    public required string PackageId { get; init; }
    public required string ProposalId { get; init; }
    public required string RepoId { get; init; }
    public required string Branch { get; init; }
    public required string WorkspaceId { get; init; }
    public required string PatchHash { get; init; }
    public required IReadOnlyList<string> ArtifactRefs { get; init; }
    public required IReadOnlyList<string> ValidationRefs { get; init; }
    public required IReadOnlyList<string> ForbiddenActions { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record DisposableWorkspaceMarker
{
    public required string WorkspaceId { get; init; }
    public required string RepoId { get; init; }
    public required string Branch { get; init; }
    public required string SourceRoot { get; init; }
    public required string CreatedFor { get; init; }
    public required bool Disposable { get; init; }
}

public sealed record DisposableWorkspacePatchPackageValidationResult
{
    public required bool CanPackage { get; init; }
    public required string WorkspaceRootPath { get; init; }
    public required string OutputRootPath { get; init; }
    public required string PatchPath { get; init; }
    public required string SourceRootPath { get; init; }
    public required DisposableWorkspaceMarker? Marker { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
}
