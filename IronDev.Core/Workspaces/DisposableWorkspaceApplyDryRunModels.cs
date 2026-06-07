namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspaceApplyDryRunRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
}

public sealed record DisposableWorkspaceApplyOperation
{
    public required string Operation { get; init; }
    public required string RelativePath { get; init; }
    public required string SourcePath { get; init; }
    public required string WorkspacePath { get; init; }
    public bool SourceExists { get; init; }
    public bool WorkspaceExists { get; init; }
    public string? SourceSha256 { get; init; }
    public string? WorkspaceSha256 { get; init; }
}

public sealed record DisposableWorkspaceApplyDryRunData
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string SourceRepo { get; init; }

    public required bool ReadyForApply { get; init; }
    public required bool CanApplyNow { get; init; }
    public required bool RequiresSeparateApplyCommand { get; init; }
    public required string Recommendation { get; init; }

    public IReadOnlyList<DisposableWorkspaceApplyOperation> Operations { get; init; } = [];

    public int AddCount { get; init; }
    public int ModifyCount { get; init; }
    public int DeleteCount { get; init; }

    public string? WorkspaceMetadataPath { get; init; }
    public string? DiffMetadataPath { get; init; }
    public string? PromotionPackagePath { get; init; }
    public string? ApprovalEvidencePath { get; init; }
    public string? ApplyPreflightPath { get; init; }
    public string? ApplyDryRunPath { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record DisposableWorkspaceApplyDryRunResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required DisposableWorkspaceApplyDryRunData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceApplyDryRunEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspaceApplyDryRunData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspaceApplyDryRunService
{
    Task<DisposableWorkspaceApplyDryRunResult> CheckAsync(
        DisposableWorkspaceApplyDryRunRequest request,
        CancellationToken cancellationToken = default);
}
