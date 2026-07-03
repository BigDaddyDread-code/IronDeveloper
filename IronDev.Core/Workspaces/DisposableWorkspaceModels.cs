namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspaceCommand
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string DisplayName { get; init; } = string.Empty;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// A declarative file change applied inside the disposable workspace after it is
/// materialized and before any command runs. Data, not code: paths must stay inside
/// the workspace (validated by the execution service) and can never reach the source
/// repository. Workspace mutation is not source mutation.
/// </summary>
public sealed record DisposableWorkspaceFileWrite
{
    public required string RelativePath { get; init; }
    public string? Content { get; init; }
    public bool IsDeletion { get; init; }
}

public sealed record DisposableWorkspaceRunRequest
{
    public required string RunId { get; init; }
    public required string SourcePath { get; init; }
    public required string WorkspaceRoot { get; init; }
    public string? EvidenceRoot { get; init; }
    public bool CleanWorkspaceOnSuccess { get; init; } = true;
    public bool PreserveWorkspaceOnFailure { get; init; } = true;
    public bool PreserveWorkspaceOnCancellation { get; init; } = true;
    public IReadOnlyList<DisposableWorkspaceFileWrite> FileWrites { get; init; } = [];
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
    public string? StandardOutputPath { get; init; }
    public string? StandardErrorPath { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset CompletedUtc { get; init; }
    public long DurationMs { get; init; }
    public bool TimedOut { get; init; }
}

public sealed record DisposableWorkspaceRunResult
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public bool Succeeded { get; init; }
    public bool UsedGitWorktree { get; init; }
    public bool CleanedUp { get; init; }
    public bool WorkspacePreserved { get; init; }
    public bool Cancelled { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string EvidencePath { get; init; } = string.Empty;
    public IReadOnlyList<DisposableWorkspaceCommandResult> Commands { get; init; } = [];
}

public interface IDisposableWorkspaceExecutionService
{
    Task<DisposableWorkspaceRunResult> RunAsync(
        DisposableWorkspaceRunRequest request,
        CancellationToken cancellationToken = default);
}
