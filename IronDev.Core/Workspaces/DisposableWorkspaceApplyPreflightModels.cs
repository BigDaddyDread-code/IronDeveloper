namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspaceApplyPreflightRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
}

public sealed record DisposableWorkspaceApplyPreflightData
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string SourceRepo { get; init; }

    public required string ApprovalDecision { get; init; }
    public required string ApprovedBy { get; init; }
    public required string ApprovalReason { get; init; }

    public required string PromotionPackagePath { get; init; }
    public required string PromotionPackageSha256 { get; init; }
    public required string ApprovalPromotionPackageSha256 { get; init; }
    public required bool PromotionPackageHashMatchesApproval { get; init; }

    public required string Recommendation { get; init; }
    public required bool ValidationSucceeded { get; init; }
    public required bool DiffChanged { get; init; }

    public IReadOnlyList<string> AddedFiles { get; init; } = [];
    public IReadOnlyList<string> ModifiedFiles { get; init; } = [];
    public IReadOnlyList<string> DeletedFiles { get; init; } = [];

    public required bool ReadyForApply { get; init; }
    public required bool CanApplyNow { get; init; }
    public required bool RequiresSeparateApplyCommand { get; init; }

    public string? WorkspaceMetadataPath { get; init; }
    public string? PromotionPackagePathFromEvidence { get; init; }
    public string? ApprovalEvidencePath { get; init; }
    public string? DiffMetadataPath { get; init; }
    public string? ApplyPreflightPath { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record DisposableWorkspaceApplyPreflightResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required DisposableWorkspaceApplyPreflightData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceApplyPreflightEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspaceApplyPreflightData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspaceApplyPreflightService
{
    Task<DisposableWorkspaceApplyPreflightResult> CheckAsync(
        DisposableWorkspaceApplyPreflightRequest request,
        CancellationToken cancellationToken = default);
}
