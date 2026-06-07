namespace IronDev.Core.Workspaces;

public sealed record DisposableWorkspaceFailurePackageRequest
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string FailedStage { get; init; }
}

public sealed record DisposableWorkspaceFailureEvidenceFile
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required bool Exists { get; init; }
    public string? Status { get; init; }
    public bool? Succeeded { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Blockers { get; init; } = [];
}

public sealed record DisposableWorkspaceFailurePackageData
{
    public required string RunId { get; init; }
    public required string WorkspacePath { get; init; }
    public required string SourceRepo { get; init; }

    public required string FailedStage { get; init; }

    public required bool SourceRepoMutated { get; init; }
    public required bool ApplyCopyAttempted { get; init; }
    public required bool ApplyCopySucceeded { get; init; }
    public required bool ApplyVerified { get; init; }
    public required bool PostApplyValidationSucceeded { get; init; }

    public required string FailureSeverity { get; init; }
    public required string RecommendedNextAction { get; init; }

    public IReadOnlyList<string> MissingEvidence { get; init; } = [];
    public IReadOnlyList<string> ExistingEvidencePaths { get; init; } = [];
    public IReadOnlyList<DisposableWorkspaceFailureEvidenceFile> EvidenceFiles { get; init; } = [];

    public IReadOnlyList<string> AggregatedErrors { get; init; } = [];
    public IReadOnlyList<string> AggregatedWarnings { get; init; } = [];
    public IReadOnlyList<string> AggregatedBlockers { get; init; } = [];

    public IReadOnlyList<string> RiskNotes { get; init; } = [];

    public string? WorkspaceMetadataPath { get; init; }
    public string? FailurePackagePath { get; init; }

    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceFailurePackageResult
{
    public required string Status { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required DisposableWorkspaceFailurePackageData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record DisposableWorkspaceFailurePackageEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required DisposableWorkspaceFailurePackageData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public interface IDisposableWorkspaceFailurePackageService
{
    Task<DisposableWorkspaceFailurePackageResult> CreateAsync(
        DisposableWorkspaceFailurePackageRequest request,
        CancellationToken cancellationToken = default);
}
