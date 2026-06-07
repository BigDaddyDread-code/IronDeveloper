namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspacePostApplyValidationRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string ProfileId { get; init; }
}

public sealed record DisposableWorkspacePostApplyValidationStep
{
    public required string CommandId { get; init; }
    public required string Status { get; init; }
    public int ExitCode { get; init; }
    public bool Succeeded { get; init; }
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspacePostApplyValidationData
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string SourceRepo { get; init; }

    public required string ProfileId { get; init; }

    public required string ValidationWorkspacePath { get; init; }
    public required bool ValidationWorkspacePrepared { get; init; }

    public required string ValidationStatus { get; init; }
    public required bool ValidationSucceeded { get; init; }

    public IReadOnlyList<DisposableWorkspacePostApplyValidationStep> Steps { get; init; } = [];

    public string? WorkspaceMetadataPath { get; init; }
    public string? ApplyCopyPath { get; init; }
    public string? ApplyVerifyPath { get; init; }
    public string? ApplyDryRunPath { get; init; }
    public string? ApplyPreflightPath { get; init; }
    public string? PromotionApprovalPath { get; init; }
    public string? PromotionPackagePath { get; init; }
    public string? DiffMetadataPath { get; init; }
    public string? PostApplyValidationPath { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed record DisposableWorkspacePostApplyValidationResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required DisposableWorkspacePostApplyValidationData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspacePostApplyValidationEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspacePostApplyValidationData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspacePostApplyValidationService
{
    Task<DisposableWorkspacePostApplyValidationResult> ValidateAsync(
        DisposableWorkspacePostApplyValidationRequest request,
        CancellationToken cancellationToken = default);
}
