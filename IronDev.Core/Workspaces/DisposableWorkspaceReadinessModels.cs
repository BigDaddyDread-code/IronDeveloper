namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspaceReadinessRequest
{
    public required string RunId { get; init; }
    public required string SourceRepo { get; init; }
    public required string WorkspaceRoot { get; init; }
}

public sealed record DisposableWorkspaceReadinessCheck
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
}

public sealed record DisposableWorkspaceReadinessData
{
    public required string RunId { get; init; }
    public required string SourceRepo { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string WorkspacePath { get; init; }
    public bool SourceRepoExists { get; init; }
    public bool WorkspaceRootExists { get; init; }
    public bool WorkspacePathExists { get; init; }
    public bool IsInsideSourceRepo { get; init; }
    public bool GitStatusClean { get; init; }
    public bool CanCreateWorkspaceDirectory { get; init; }
    public IReadOnlyList<DisposableWorkspaceReadinessCheck> Checks { get; init; } = [];
    public bool Ready { get; init; }
    public bool SourceRepoIsGitRepo { get; init; }
    public bool WorkspaceRootSameAsSourceRepo { get; init; }
    public bool WorkspaceRootUnderGitDirectory { get; init; }
    public bool WorkspacePathEscapedWorkspaceRoot { get; init; }
}

public sealed record DisposableWorkspaceReadinessResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public int ExitCode { get; init; }
    public required DisposableWorkspaceReadinessData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceReadinessEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspaceReadinessData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspaceReadinessService
{
    Task<DisposableWorkspaceReadinessResult> CheckAsync(
        DisposableWorkspaceReadinessRequest request,
        CancellationToken cancellationToken = default);
}
