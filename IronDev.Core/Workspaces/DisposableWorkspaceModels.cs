namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspaceCommand
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string DisplayName { get; init; } = string.Empty;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}

public sealed record DisposableWorkspaceRunRequest
{
    public required string RunId { get; init; }
    public required string SourcePath { get; init; }
    public required string WorkspaceRoot { get; init; }
    public IReadOnlyList<DisposableWorkspaceCommand> Commands { get; init; } = [];
}

public sealed record DisposableWorkspaceCommandResult
{
    public required string DisplayName { get; init; }
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
}

public sealed record DisposableWorkspaceRunResult
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public bool Succeeded { get; init; }
    public bool UsedGitWorktree { get; init; }
    public bool CleanedUp { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<DisposableWorkspaceCommandResult> Commands { get; init; } = [];
}

public interface IDisposableWorkspaceExecutionService
{
    Task<DisposableWorkspaceRunResult> RunAsync(
        DisposableWorkspaceRunRequest request,
        CancellationToken cancellationToken = default);
}
