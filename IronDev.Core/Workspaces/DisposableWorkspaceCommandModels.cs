namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspaceCommandRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string CommandId { get; init; }
}

public sealed record DisposableWorkspaceCommandData
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string CommandId { get; init; }
    public required string WorkingDirectory { get; init; }
    public required int ExitCode { get; init; }
    public required bool Succeeded { get; init; }
    public string? StdoutPath { get; init; }
    public string? StderrPath { get; init; }
    public string? CommandMetadataPath { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceCommandExecutionResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required DisposableWorkspaceCommandData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceCommandEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspaceCommandData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspaceCommandService
{
    Task<DisposableWorkspaceCommandExecutionResult> RunAsync(
        DisposableWorkspaceCommandRequest request,
        CancellationToken cancellationToken = default);
}
