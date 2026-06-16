namespace IronDev.Core.Governance;

public sealed record DisposableWorkspaceBoundary
{
    public required Guid ProjectId { get; init; }
    public required string WorkspaceId { get; init; }
    public required string WorkspaceKind { get; init; }
    public required string WorkspaceRootPath { get; init; }
    public required string AllowedWriteRootPath { get; init; }
    public required string SourceSnapshotReference { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required DateTimeOffset PreparedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
}
