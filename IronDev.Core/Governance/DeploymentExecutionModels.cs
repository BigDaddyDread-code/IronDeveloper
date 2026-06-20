using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum DeploymentExecutionVerdict
{
    ExecutedAndVerified = 0,
    Blocked,
    Failed,
    PartiallyExecuted,
    Rejected
}

public enum DeploymentExecutionAction
{
    DeployApprovedArtifact = 0
}

public enum DeploymentExecutionFailureKind
{
    None = 0,

    MissingDeploymentReadinessDecisionPackage,
    DeploymentReadinessDecisionPackageNotReady,
    DeploymentReadinessDecisionPackageRejected,
    DeploymentReadinessDecisionPackageBlocked,
    DeploymentReadinessDecisionBoundaryViolation,

    MissingDeploymentExecutionRequest,
    DeploymentExecutionNotConfirmed,
    RequestPackageMismatch,

    RepositoryMismatch,
    CandidateCommitMismatch,
    CandidateVersionMismatch,
    CandidateTagMismatch,
    ReleaseChannelMismatch,
    DeploymentTargetMismatch,
    DeploymentEnvironmentMismatch,
    DeploymentArtifactMismatch,
    DeploymentArtifactChecksumMismatch,

    MissingApprovedDeploymentAction,
    UnsupportedDeploymentAction,

    DeploymentTargetObservationFailed,
    DeploymentTargetStateMismatch,
    DeploymentAlreadyApplied,
    DeploymentInProgress,
    DeploymentTargetLocked,

    DeploymentMutationFailed,
    PostDeploymentVerificationFailed,

    PackagePublicationNotAllowed,
    MemoryPromotionNotAllowed,
    WorkflowContinuationNotAllowed,
    SourceMutationNotAllowed,
    CommitPushNotAllowed,
    RollbackExecutionNotAllowed,
    BoundaryViolation
}

public sealed record DeploymentExecutionRequest
{
    public required string DeploymentExecutionRequestId { get; init; }
    public required string DeploymentReadinessDecisionPackageId { get; init; }

    public required string Repository { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public required string DeploymentTarget { get; init; }
    public required string DeploymentEnvironment { get; init; }

    public required string DeploymentArtifactName { get; init; }
    public required string DeploymentArtifactSha256 { get; init; }

    public DeploymentExecutionAction[] ApprovedActions { get; init; } = [];

    public required bool ConfirmDeploymentExecution { get; init; }

    public required string RequestedBy { get; init; }
    public DateTimeOffset? RequestedAtUtc { get; init; }
}

public sealed record DeploymentTargetObservedState
{
    public required string DeploymentTarget { get; init; }
    public required string DeploymentEnvironment { get; init; }

    public string? CurrentlyDeployedVersion { get; init; }
    public string? CurrentlyDeployedCommitSha { get; init; }
    public string? CurrentlyDeployedArtifactSha256 { get; init; }

    public required bool DeploymentInProgress { get; init; }
    public required bool DeploymentTargetLocked { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string ObservationSource { get; init; }
    public required bool ObservationSucceeded { get; init; }
    public string? ObservationError { get; init; }
}

public sealed record DeploymentExecutionMutationResult
{
    public required DeploymentExecutionAction Action { get; init; }
    public required bool Attempted { get; init; }
    public required bool Accepted { get; init; }

    public required string Provider { get; init; }
    public required string MutationName { get; init; }

    public required string DeploymentTarget { get; init; }
    public required string DeploymentEnvironment { get; init; }

    public string? DeploymentId { get; init; }
    public string? DeploymentUrl { get; init; }

    public string? Message { get; init; }
    public string? Error { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public sealed record DeploymentExecutionReceipt
{
    public required string DeploymentExecutionReceiptId { get; init; }
    public required string DeploymentExecutionRequestId { get; init; }
    public required string DeploymentReadinessDecisionPackageId { get; init; }

    public required string Repository { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public required string DeploymentTarget { get; init; }
    public required string DeploymentEnvironment { get; init; }

    public required string DeployedArtifactName { get; init; }
    public required string DeployedArtifactSha256 { get; init; }

    public DeploymentExecutionAction[] ApprovedActions { get; init; } = [];
    public DeploymentExecutionAction[] AttemptedActions { get; init; } = [];
    public DeploymentExecutionAction[] CompletedActions { get; init; } = [];

    public DeploymentTargetObservedState? PreDeploymentState { get; init; }
    public DeploymentTargetObservedState? PostDeploymentState { get; init; }

    public DeploymentExecutionMutationResult[] MutationResults { get; init; } = [];

    public required bool PreDeploymentStateVerified { get; init; }
    public required bool DeploymentAttempted { get; init; }
    public required bool DeploymentAccepted { get; init; }
    public required bool PostDeploymentStateVerified { get; init; }

    public required bool PackagePublicationAttempted { get; init; }
    public required bool MemoryPromotionAttempted { get; init; }
    public required bool WorkflowContinuationAttempted { get; init; }
    public required bool RollbackExecutionAttempted { get; init; }
    public required bool SourceMutationAttempted { get; init; }

    public required DeploymentExecutionVerdict ExecutionVerdict { get; init; }
    public required DeploymentExecutionFailureKind FailureClassification { get; init; }
    public string[] Issues { get; init; } = [];

    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required DateTimeOffset ExecutedAtUtc { get; init; }

    public DeploymentExecutionBoundary Boundary { get; init; } =
        DeploymentExecutionBoundary.Executor;
}

public sealed record DeploymentExecutionBoundary
{
    public bool CanDeployApprovedArtifact { get; init; } = true;

    public bool CanPublishPackages { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanExecuteRollback { get; init; }
    public bool CanCreateTag { get; init; }
    public bool CanCreateGitHubRelease { get; init; }
    public bool CanDispatchPipeline { get; init; }

    public static DeploymentExecutionBoundary Executor { get; } = new();

    public static DeploymentExecutionBoundary Blocked { get; } = new()
    {
        CanDeployApprovedArtifact = false
    };

    public static DeploymentExecutionBoundary ReadOnly { get; } = new()
    {
        CanDeployApprovedArtifact = false
    };
}

public static class DeploymentExecutionBoundaryText
{
    public const string Boundary = """
        Block BE consumes an eligible BD deployment-readiness decision package and an explicit deployment execution request.
        It re-observes deployment target state before mutation.
        It deploys only the approved artifact to the approved target and environment.
        It re-observes deployment target state after mutation and writes a deployment execution receipt.
        BC package is not deployment execution authority.
        Release execution receipt is not deployment execution authority.
        Deployment readiness decision package is not deployment execution by itself.
        Deployment execution is not package publication.
        Deployment execution is not workflow continuation.
        Deployment execution is not memory promotion.
        Deployment execution is not rollback execution.
        BE does not publish packages.
        BE does not mutate source.
        BE does not commit.
        BE does not push.
        BE does not merge.
        BE does not promote memory.
        BE does not continue workflow.
        BE does not execute rollback.
        Partial deployment is non-success.
        Post-deployment verification failure is non-success.
        """;
}

public interface IDeploymentExecutionGateway
{
    Task<DeploymentTargetObservedState> ObserveAsync(
        DeploymentReadinessDecisionPackage package,
        DeploymentExecutionRequest request,
        CancellationToken cancellationToken);

    Task<DeploymentExecutionMutationResult> DeployApprovedArtifactAsync(
        DeploymentReadinessDecisionPackage package,
        DeploymentExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record DeploymentExecutionResult
{
    public required DeploymentExecutionVerdict Verdict { get; init; }
    public required DeploymentExecutionFailureKind FailureKind { get; init; }
    public string[] Issues { get; init; } = [];
    public DeploymentExecutionReceipt? Receipt { get; init; }
}

public static class DeploymentExecutionBypassEvaluator
{
    public static bool CanPublishPackages(object? evidence) => false;
    public static bool CanPromoteMemory(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
    public static bool CanCommit(object? evidence) => false;
    public static bool CanPush(object? evidence) => false;
    public static bool CanMutateSource(object? evidence) => false;
    public static bool CanMutateWorkspace(object? evidence) => false;
    public static bool CanExecuteRollback(object? evidence) => false;
    public static bool CanCreateTag(object? evidence) => false;
    public static bool CanCreateGitHubRelease(object? evidence) => false;
    public static bool CanDispatchPipeline(object? evidence) => false;
}

internal static class BeDeploymentExecutionHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
