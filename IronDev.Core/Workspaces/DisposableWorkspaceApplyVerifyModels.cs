namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspaceApplyVerifyRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
}

public sealed record DisposableWorkspaceApplyVerifyOperation
{
    public required string Operation { get; init; }
    public required string RelativePath { get; init; }
    public required string SourcePath { get; init; }
    public required string WorkspacePath { get; init; }

    public bool SourceExists { get; init; }
    public bool WorkspaceExists { get; init; }

    public string? ExpectedSourceSha256After { get; init; }
    public string? ActualSourceSha256After { get; init; }
    public string? ActualWorkspaceSha256 { get; init; }

    public required bool Verified { get; init; }
}

public sealed record DisposableWorkspaceApplyVerifyData
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string SourceRepo { get; init; }

    public required bool Verified { get; init; }
    public required bool SourceMatchesWorkspace { get; init; }

    public IReadOnlyList<DisposableWorkspaceApplyVerifyOperation> Operations { get; init; } = [];

    public int VerifiedCount { get; init; }
    public int FailedCount { get; init; }

    public string? WorkspaceMetadataPath { get; init; }
    public string? ApplyCopyPath { get; init; }
    public string? ApplyDryRunPath { get; init; }
    public string? ApplyPreflightPath { get; init; }
    public string? PromotionApprovalPath { get; init; }
    public string? PromotionPackagePath { get; init; }
    public string? DiffMetadataPath { get; init; }
    public string? ApplyVerifyPath { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record DisposableWorkspaceApplyVerifyResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required DisposableWorkspaceApplyVerifyData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceApplyVerifyEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspaceApplyVerifyData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspaceApplyVerifyService
{
    Task<DisposableWorkspaceApplyVerifyResult> VerifyAsync(
        DisposableWorkspaceApplyVerifyRequest request,
        CancellationToken cancellationToken = default);
}
