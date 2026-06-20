using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum ReleaseExecutionVerdict
{
    ExecutedAndVerified = 0,
    Blocked,
    Failed,
    PartiallyExecuted,
    Rejected
}

public enum ReleaseExecutionFailureKind
{
    None = 0,

    MissingReleaseReadinessPackage,
    ReleaseReadinessPackageNotReady,
    ReleaseReadinessPackageBlocked,
    ReleaseReadinessPackageRejected,
    ReleaseReadinessPackageStale,
    ReleaseReadinessBoundaryAuthorityViolation,

    MissingExecutionRequest,
    ReleaseExecutionNotConfirmed,
    RequestPackageMismatch,
    RepositoryMismatch,
    CandidateCommitMismatch,
    CandidateVersionMismatch,
    CandidateTagMismatch,
    ReleaseSourceBranchMismatch,
    ReleaseChannelMismatch,

    MissingApprovedActions,
    UnsupportedApprovedAction,
    ReleaseCreationRequiresTagCreation,
    ArtifactUploadRequiresReleaseCreation,

    MissingReleaseNotes,
    ReleaseNotesChecksumMismatch,
    MissingArtifact,
    ArtifactChecksumMismatch,

    ObservationFailed,
    SourceBranchMoved,
    CandidateTagAlreadyExists,
    CandidateReleaseAlreadyExists,
    PreStateVerificationFailed,

    ReleaseMutationFailed,
    PostStateVerificationFailed,

    DeployNotAllowed,
    PublishPackagesNotAllowed,
    MemoryPromotionNotAllowed,
    WorkflowContinuationNotAllowed,
    CommitPushNotAllowed,
    SourceMutationNotAllowed,
    RollbackExecutionNotAllowed,
    BoundaryViolation
}

public enum ReleaseExecutionAction
{
    CreateTag = 0,
    CreateGitHubRelease,
    UploadReleaseArtifacts
}

public sealed record ReleaseExecutionBoundary
{
    public bool CanCreateTag { get; init; } = true;
    public bool CanCreateGitHubRelease { get; init; } = true;
    public bool CanUploadReleaseArtifacts { get; init; } = true;

    public bool CanDeploy { get; init; }
    public bool CanPublishPackages { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanMerge { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanExecuteRollback { get; init; }
    public bool CanApprove { get; init; }
    public bool CanMarkReadyForReview { get; init; }
    public bool CanRequestReviewers { get; init; }

    public static ReleaseExecutionBoundary Executor { get; } = new();

    public static ReleaseExecutionBoundary Blocked { get; } = new()
    {
        CanCreateTag = false,
        CanCreateGitHubRelease = false,
        CanUploadReleaseArtifacts = false
    };
}

public static class ReleaseExecutionBoundaryText
{
    public const string Boundary = """
        Block BB consumes an eligible BA release-readiness decision package and an explicit release execution request.
        It re-observes current release source, tag, release, and artifact state before mutation.
        It may create only the expected tag.
        It may create only the expected GitHub release.
        It may upload only the expected release artifacts.
        It re-observes release state after mutation and writes a release execution receipt.
        It does not deploy.
        It does not publish packages.
        It does not promote memory.
        It does not continue workflow.
        It does not mutate source.
        It does not commit.
        It does not push.
        It does not merge.
        It does not execute rollback.
        Release readiness decision package is not release execution.
        Release execution is not deployment.
        Release execution is not package publication.
        Release execution receipt is not deployment authority.
        Release execution receipt is not package publication authority.
        Release execution receipt is not workflow continuation authority.
        No implicit tag creation through release creation.
        No hidden deployment.
        No hidden package publication.
        No hidden memory promotion.
        No hidden workflow continuation.
        """;
}

public sealed record ReleaseExecutionArtifact
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Sha256 { get; init; }
    public string? ContentType { get; init; }
}

public sealed record ReleaseExecutionRequest
{
    public required string ReleaseExecutionRequestId { get; init; }
    public required string ReleaseReadinessDecisionPackageId { get; init; }
    public required string Repository { get; init; }
    public required string ReleaseSourceBranch { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }
    public ReleaseExecutionAction[] ApprovedActions { get; init; } = [];
    public bool ConfirmReleaseExecution { get; init; }
    public string? ReleaseName { get; init; }
    public string? ReleaseNotesPath { get; init; }
    public string? ReleaseNotesBody { get; init; }
    public string? ReleaseNotesSha256 { get; init; }
    public ReleaseExecutionArtifact[] Artifacts { get; init; } = [];
    public required string RequestedBy { get; init; }
    public DateTimeOffset? RequestedAtUtc { get; init; }
    public string? OutputDirectory { get; init; }
}

public sealed record ReleaseExecutionObservedState
{
    public required string Repository { get; init; }
    public required string ReleaseSourceBranch { get; init; }
    public required string ReleaseSourceHeadSha { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required bool CommitPresentOnReleaseSource { get; init; }
    public required string CandidateTagName { get; init; }
    public bool ExistingTagFound { get; init; }
    public string? ExistingTagSha { get; init; }
    public bool ExistingReleaseFound { get; init; }
    public string? ExistingReleaseId { get; init; }
    public string? ExistingReleaseUrl { get; init; }
    public string[] ExistingReleaseArtifactNames { get; init; } = [];
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string ObservationSource { get; init; }
    public required bool ObservationSucceeded { get; init; }
    public string? ObservationError { get; init; }
}

public sealed record ReleaseExecutionMutationResult
{
    public required ReleaseExecutionAction Action { get; init; }
    public required bool Attempted { get; init; }
    public required bool Accepted { get; init; }
    public required string Provider { get; init; }
    public required string CommandOrMutationName { get; init; }
    public required string Target { get; init; }
    public string? ResourceId { get; init; }
    public string? ResourceUrl { get; init; }
    public string[] UploadedArtifacts { get; init; } = [];
    public string? Message { get; init; }
    public string? Error { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public interface IReleaseExecutionGateway
{
    Task<ReleaseExecutionObservedState> ObserveAsync(
        ReleaseReadinessDecisionPackage package,
        ReleaseExecutionRequest request,
        CancellationToken cancellationToken);

    Task<ReleaseExecutionMutationResult> CreateTagAsync(
        ReleaseReadinessDecisionPackage package,
        ReleaseExecutionRequest request,
        CancellationToken cancellationToken);

    Task<ReleaseExecutionMutationResult> CreateGitHubReleaseAsync(
        ReleaseReadinessDecisionPackage package,
        ReleaseExecutionRequest request,
        CancellationToken cancellationToken);

    Task<ReleaseExecutionMutationResult> UploadReleaseArtifactsAsync(
        ReleaseReadinessDecisionPackage package,
        ReleaseExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record ReleaseExecutionReceipt
{
    public required string ReleaseExecutionId { get; init; }
    public required string ReleaseExecutionRequestId { get; init; }
    public required string ReleaseReadinessDecisionPackageId { get; init; }
    public required string Repository { get; init; }
    public required string ReleaseSourceBranch { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public ReleaseExecutionObservedState? PreState { get; init; }
    public ReleaseExecutionObservedState? PostState { get; init; }

    public ReleaseExecutionAction[] ApprovedActions { get; init; } = [];
    public ReleaseExecutionAction[] CompletedActions { get; init; } = [];
    public ReleaseExecutionMutationResult[] MutationResults { get; init; } = [];

    public required bool PreStateVerified { get; init; }
    public required bool TagCreated { get; init; }
    public required bool GitHubReleaseCreated { get; init; }
    public required bool ReleaseArtifactsUploaded { get; init; }
    public string? CreatedTagSha { get; init; }
    public string? GitHubReleaseId { get; init; }
    public string? GitHubReleaseUrl { get; init; }
    public string[] UploadedArtifacts { get; init; } = [];
    public required bool PostStateVerified { get; init; }

    public required bool DeploymentAttempted { get; init; }
    public required bool PackagePublicationAttempted { get; init; }
    public required bool MemoryPromotionAttempted { get; init; }
    public required bool WorkflowContinuationAttempted { get; init; }
    public required bool RollbackExecutionAttempted { get; init; }

    public required ReleaseExecutionVerdict ExecutionVerdict { get; init; }
    public required ReleaseExecutionFailureKind FailureClassification { get; init; }
    public string[] Issues { get; init; } = [];

    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required DateTimeOffset ExecutedAtUtc { get; init; }

    public ReleaseExecutionBoundary Boundary { get; init; } = ReleaseExecutionBoundary.Executor;
}

public sealed record ReleaseExecutionResult
{
    public required ReleaseExecutionVerdict Verdict { get; init; }
    public required ReleaseExecutionFailureKind FailureKind { get; init; }
    public string[] Issues { get; init; } = [];
    public ReleaseExecutionReceipt? Receipt { get; init; }
}

public static class ReleaseExecutionBypassEvaluator
{
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanPublishPackages(object? evidence) => false;
    public static bool CanPromoteMemory(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
    public static bool CanCommit(object? evidence) => false;
    public static bool CanPush(object? evidence) => false;
    public static bool CanMerge(object? evidence) => false;
    public static bool CanMutateSource(object? evidence) => false;
    public static bool CanMutateWorkspace(object? evidence) => false;
    public static bool CanExecuteRollback(object? evidence) => false;
    public static bool CanApprove(object? evidence) => false;
    public static bool CanMarkReadyForReview(object? evidence) => false;
    public static bool CanRequestReviewers(object? evidence) => false;
}

internal static class BbReleaseExecutionHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    public static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string TextHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
