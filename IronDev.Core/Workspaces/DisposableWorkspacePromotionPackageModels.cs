namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspacePromotionPackageRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
}

public sealed record DisposableWorkspacePromotionPackageData
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string SourceRepo { get; init; }

    public required string ValidationStatus { get; init; }
    public required bool ValidationSucceeded { get; init; }

    public required bool DiffChanged { get; init; }
    public IReadOnlyList<string> AddedFiles { get; init; } = [];
    public IReadOnlyList<string> ModifiedFiles { get; init; } = [];
    public IReadOnlyList<string> DeletedFiles { get; init; } = [];

    public required bool RequiresHumanApproval { get; init; }
    public required bool CanApplyToSourceRepo { get; init; }
    public required bool AutoPromotionAllowed { get; init; }

    public required string Recommendation { get; init; }
    public IReadOnlyList<string> RiskNotes { get; init; } = [];

    public string? WorkspaceMetadataPath { get; init; }
    public string? ValidationMetadataPath { get; init; }
    public string? DiffMetadataPath { get; init; }
    public string? PromotionPackagePath { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspacePromotionPackageResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required DisposableWorkspacePromotionPackageData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspacePromotionPackageEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspacePromotionPackageData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspacePromotionPackageService
{
    Task<DisposableWorkspacePromotionPackageResult> CreateAsync(
        DisposableWorkspacePromotionPackageRequest request,
        CancellationToken cancellationToken = default);
}
