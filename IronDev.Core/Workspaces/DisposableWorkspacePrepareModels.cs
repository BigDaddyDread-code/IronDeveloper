namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspacePrepareRequest
{
    public required string RunId { get; init; }
    public required string SourceRepo { get; init; }
    public required string WorkspaceRoot { get; init; }
    public bool AllowDirtySourceRepo { get; init; }
}

public sealed record DisposableWorkspacePrepareData
{
    public required string RunId { get; init; }
    public required string SourceRepo { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string WorkspacePath { get; init; }
    public required string ReadinessStatus { get; init; }
    public bool Prepared { get; init; }
    public required string PreparationMethod { get; init; }
    public int FilesCopied { get; init; }
    public string? MetadataPath { get; init; }
    public bool SourceRepoMutated { get; init; }
    public IReadOnlyList<DisposableWorkspaceReadinessCheck> Checks { get; init; } = [];
}

public sealed record DisposableWorkspacePrepareResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public int ExitCode { get; init; }
    public required DisposableWorkspacePrepareData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspacePrepareEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspacePrepareData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspacePrepareService
{
    Task<DisposableWorkspacePrepareResult> PrepareAsync(
        DisposableWorkspacePrepareRequest request,
        CancellationToken cancellationToken = default);
}
