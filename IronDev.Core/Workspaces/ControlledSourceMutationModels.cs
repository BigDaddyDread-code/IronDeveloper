namespace IronDev.Core.Workspaces;

public static class ControlledSourceMutationReasonCodes
{
    public const string Applied = "ProjectApplyMutationApplied";
    public const string Validated = "ProjectApplyMutationValidated";
    public const string CapabilityContextMissing = "ProjectApplyCapabilityContextMissing";
    public const string CapabilityChangedBeforeMutation = "ProjectApplyCapabilityChangedBeforeMutation";
    public const string PlatformUnsupported = "ProjectApplyMutationPlatformUnsupported";
    public const string DestinationPathUnsafe = "ProjectApplyMutationDestinationPathUnsafe";
    public const string WorkspacePathUnsafe = "ProjectApplyMutationWorkspacePathUnsafe";
    public const string SourceHashMismatch = "ProjectApplyMutationSourceHashMismatch";
    public const string WorkspaceHashMismatch = "ProjectApplyMutationWorkspaceHashMismatch";
    public const string ResultHashMismatch = "ProjectApplyMutationResultHashMismatch";
    public const string OperationUnsupported = "ProjectApplyMutationOperationUnsupported";
    public const string Failed = "ProjectApplyMutationFailed";
}

/// <summary>
/// Immutable run-start capability truth carried through the apply spine. This
/// context is evidence to compare with live authority; it does not grant it.
/// </summary>
public sealed record ControlledSourceMutationContext
{
    public required int ProjectId { get; init; }
    public required string RunId { get; init; }
    public required string ApplyAttemptId { get; init; }
    public required string ExpectedReadinessEvidenceHash { get; init; }
    public required string QualifiedSandboxRoot { get; init; }
    public required string QualifiedProjectRoot { get; init; }
    public required string QualifiedWorkspaceRoot { get; init; }
    public string ExpectedLauncherSessionId { get; init; } = string.Empty;
    public string ExpectedSandboxRootFingerprint { get; init; } = string.Empty;
    public string ExpectedProjectPathFingerprint { get; init; } = string.Empty;
    public string ExpectedQualificationId { get; init; } = string.Empty;
    public string ExpectedQualificationFingerprint { get; init; } = string.Empty;
}

public sealed record ControlledSourceMutationRequest
{
    public required int ProjectId { get; init; }
    public required string RunId { get; init; }
    public required string ApplyAttemptId { get; init; }
    public required string ExpectedReadinessEvidenceHash { get; init; }
    public required string QualifiedSandboxRoot { get; init; }
    public required string QualifiedProjectRoot { get; init; }
    public required string QualifiedWorkspaceRoot { get; init; }
    public required string OperationKind { get; init; }
    public required string RelativePath { get; init; }
    public required string WorkspaceSourcePath { get; init; }
    public required string ExpectedSourceHash { get; init; }
    public required string ExpectedWorkspaceHash { get; init; }
    public string ExpectedLauncherSessionId { get; init; } = string.Empty;
    public string ExpectedSandboxRootFingerprint { get; init; } = string.Empty;
    public string ExpectedProjectPathFingerprint { get; init; } = string.Empty;
    public string ExpectedQualificationId { get; init; } = string.Empty;
    public string ExpectedQualificationFingerprint { get; init; } = string.Empty;
}

public sealed record ControlledSourceMutationEvidence
{
    public required string ReasonCode { get; init; }
    public required string Reason { get; init; }
    public required bool Applied { get; init; }
    public required bool SourceRepoMutated { get; init; }
    public required string ProjectRoot { get; init; }
    public required string RelativePath { get; init; }
    public required string OperationKind { get; init; }
    public required string PreviousReadinessEvidenceHash { get; init; }
    public required string LiveReadinessEvidenceHash { get; init; }
    public required string NextSafeAction { get; init; }
    public string? ActualSourceHashBefore { get; init; }
    public string? ActualWorkspaceHashBefore { get; init; }
    public string? ActualSourceHashAfter { get; init; }
    public IReadOnlyList<string> ChangedBindings { get; init; } = [];
}

public sealed record ControlledSourceMutationResult
{
    public required bool Succeeded { get; init; }
    public required ControlledSourceMutationEvidence Evidence { get; init; }
}

public sealed record ControlledSourceMutationBatchResult
{
    public required bool Succeeded { get; init; }
    public required bool SourceRepoMutated { get; init; }
    public required IReadOnlyList<ControlledSourceMutationResult> Results { get; init; }
    public int? FailureOperationIndex { get; init; }
    public ControlledSourceMutationEvidence? FailureEvidence { get; init; }
}

/// <summary>
/// Owns authority verification, no-follow target resolution, mutation, and
/// result-hash verification as one indivisible operation.
/// </summary>
public interface IControlledSourceMutationExecutor
{
    Task<ControlledSourceMutationResult> ExecuteAsync(
        ControlledSourceMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<ControlledSourceMutationBatchResult> ExecuteBatchAsync(
        IReadOnlyList<ControlledSourceMutationRequest> requests,
        CancellationToken cancellationToken = default);
}
