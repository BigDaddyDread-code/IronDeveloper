namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspaceSourceReportRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
}

public sealed record DisposableWorkspaceSourceReportFile
{
    public required string Operation { get; init; }
    public required string RelativePath { get; init; }
    public required string SourcePath { get; init; }
    public required string WorkspacePath { get; init; }

    public string? SourceSha256Before { get; init; }
    public string? WorkspaceSha256 { get; init; }
    public string? SourceSha256After { get; init; }

    public required bool Applied { get; init; }
    public required bool Verified { get; init; }
}

public sealed record DisposableWorkspaceSourceReportData
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string SourceRepo { get; init; }

    public required bool SourceRepoMutated { get; init; }
    public required bool ApplyVerified { get; init; }
    public required bool SourceMatchesWorkspace { get; init; }
    public required bool PostApplyValidationSucceeded { get; init; }

    public required string PostApplyValidationStatus { get; init; }
    public required string Recommendation { get; init; }

    public IReadOnlyList<DisposableWorkspaceSourceReportFile> Files { get; init; } = [];

    public int AddCount { get; init; }
    public int ModifyCount { get; init; }
    public int DeleteCount { get; init; }

    public string? WorkspaceMetadataPath { get; init; }
    public string? DiffMetadataPath { get; init; }
    public string? PromotionPackagePath { get; init; }
    public string? PromotionApprovalPath { get; init; }
    public string? ApplyPreflightPath { get; init; }
    public string? ApplyDryRunPath { get; init; }
    public string? ApplyCopyPath { get; init; }
    public string? ApplyVerifyPath { get; init; }
    public string? PostApplyValidationPath { get; init; }
    public string? SourceReportPath { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> RiskNotes { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record DisposableWorkspaceSourceReportResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required DisposableWorkspaceSourceReportData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceSourceReportEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspaceSourceReportData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspaceSourceReportService
{
    Task<DisposableWorkspaceSourceReportResult> CreateAsync(
        DisposableWorkspaceSourceReportRequest request,
        CancellationToken cancellationToken = default);
}
