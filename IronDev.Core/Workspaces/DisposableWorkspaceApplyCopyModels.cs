namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspaceApplyCopyRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
}

public sealed record DisposableWorkspaceAppliedCopyOperation
{
    public required string Operation { get; init; }
    public required string RelativePath { get; init; }
    public required string SourcePath { get; init; }
    public required string WorkspacePath { get; init; }

    public required string ExpectedSourceSha256 { get; init; }
    public required string ExpectedWorkspaceSha256 { get; init; }
    public string? ActualSourceSha256Before { get; init; }
    public string? ActualWorkspaceSha256Before { get; init; }
    public string? ActualSourceSha256After { get; init; }

    public required bool Applied { get; init; }
}

public sealed record DisposableWorkspaceApplyCopyData
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string SourceRepo { get; init; }

    public required bool Applied { get; init; }
    public required bool SourceRepoMutated { get; init; }

    public IReadOnlyList<DisposableWorkspaceAppliedCopyOperation> Operations { get; init; } = [];

    public int AddCount { get; init; }
    public int ModifyCount { get; init; }
    public int DeleteCount { get; init; }

    public string? WorkspaceMetadataPath { get; init; }
    public string? ApplyDryRunPath { get; init; }
    public string? ApplyPreflightPath { get; init; }
    public string? PromotionApprovalPath { get; init; }
    public string? PromotionPackagePath { get; init; }
    public string? DiffMetadataPath { get; init; }
    public string? ApplyCopyPath { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record DisposableWorkspaceApplyCopyResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required DisposableWorkspaceApplyCopyData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceApplyCopyEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspaceApplyCopyData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspaceApplyCopyService
{
    Task<DisposableWorkspaceApplyCopyResult> ApplyAsync(
        DisposableWorkspaceApplyCopyRequest request,
        CancellationToken cancellationToken = default);
}
