namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspaceValidationRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string ProfileId { get; init; }
}

public sealed record DisposableWorkspaceValidationStep
{
    public required string CommandId { get; init; }
    public required string Status { get; init; }
    public int ExitCode { get; init; }
    public bool Succeeded { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceValidationData
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string ProfileId { get; init; }
    public required string Status { get; init; }
    public required bool Succeeded { get; init; }
    public IReadOnlyList<DisposableWorkspaceValidationStep> Steps { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public string? ValidationMetadataPath { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceValidationResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required DisposableWorkspaceValidationData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceValidationEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspaceValidationData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspaceValidationService
{
    Task<DisposableWorkspaceValidationResult> ValidateAsync(
        DisposableWorkspaceValidationRequest request,
        CancellationToken cancellationToken = default);
}
