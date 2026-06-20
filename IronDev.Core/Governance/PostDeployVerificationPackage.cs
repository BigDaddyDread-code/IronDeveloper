using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum PostDeployVerificationPackageVerdict
{
    DeploymentVerified = 0,
    VerificationFailed,
    VerificationIncomplete,
    VerificationBlocked,
    RollbackConsiderationRequired
}

public enum PostDeployVerificationBlockReason
{
    MissingDeploymentExecutionReceipt = 0,
    DeploymentExecutionReceiptBlocked,
    DeploymentExecutionReceiptFailed,
    DeploymentExecutionReceiptPartiallyExecuted,
    DeploymentExecutionReceiptRejected,
    DeploymentExecutionReceiptUnverified,
    DeploymentExecutionReceiptBoundaryViolation,

    MissingPostDeployObservation,
    PostDeployObservationFailed,

    RepositoryMismatch,
    CandidateCommitMismatch,
    CandidateVersionMismatch,
    CandidateTagMismatch,
    ReleaseChannelMismatch,
    DeploymentTargetMismatch,
    DeploymentEnvironmentMismatch,
    DeploymentArtifactMismatch,
    DeploymentArtifactChecksumMismatch,

    HealthCheckMissing,
    HealthCheckFailed,

    RollbackExecutionNotAllowed,
    DeploymentRetryNotAllowed,
    PackagePublicationNotAllowed,
    MemoryPromotionNotAllowed,
    WorkflowContinuationNotAllowed,
    EnvironmentMutationNotAllowed,
    SourceMutationNotAllowed,
    CommitPushNotAllowed,
    PipelineDispatchNotAllowed,
    BoundaryViolation
}

public sealed record PostDeployObservationEvidence
{
    public required string ObservationId { get; init; }

    public required string Repository { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public required string DeploymentTarget { get; init; }
    public required string DeploymentEnvironment { get; init; }

    public required string ObservedVersion { get; init; }
    public required string ObservedCommitSha { get; init; }
    public required string ObservedArtifactName { get; init; }
    public required string ObservedArtifactSha256 { get; init; }

    public required bool ObservationSucceeded { get; init; }
    public string? ObservationError { get; init; }

    public required bool HealthCheckSucceeded { get; init; }
    public string? HealthCheckSummary { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string ObservationSource { get; init; }
}

public sealed record PostDeployVerificationPackageInput
{
    public DeploymentExecutionReceipt? DeploymentExecutionReceipt { get; init; }
    public PostDeployObservationEvidence? Observation { get; init; }

    public required string Repository { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public required string DeploymentTarget { get; init; }
    public required string DeploymentEnvironment { get; init; }

    public required string ExpectedArtifactName { get; init; }
    public required string ExpectedArtifactSha256 { get; init; }

    public required string CreatedBy { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record PostDeployVerificationPackage
{
    public required string PostDeployVerificationPackageId { get; init; }
    public required string SourceDeploymentExecutionReceiptId { get; init; }

    public required string Repository { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public required string DeploymentTarget { get; init; }
    public required string DeploymentEnvironment { get; init; }

    public required string ExpectedArtifactName { get; init; }
    public required string ExpectedArtifactSha256 { get; init; }

    public required string? ObservedVersion { get; init; }
    public required string? ObservedCommitSha { get; init; }
    public required string? ObservedArtifactName { get; init; }
    public required string? ObservedArtifactSha256 { get; init; }

    public required bool DeploymentVerified { get; init; }
    public required bool CanProceedToRollbackDecision { get; init; }

    public required PostDeployVerificationPackageVerdict PackageVerdict { get; init; }

    public PostDeployVerificationBlockReason[] BlockReasons { get; init; } = [];
    public string[] PackageIssues { get; init; } = [];

    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    public PostDeployVerificationBoundary Boundary { get; init; } =
        PostDeployVerificationBoundary.Evidence;
}

public sealed record PostDeployVerificationBoundary
{
    public bool EvidenceOnly { get; init; } = true;

    public bool CanDeploy { get; init; }
    public bool CanRetryDeployment { get; init; }
    public bool CanExecuteRollback { get; init; }
    public bool CanDecideRollback { get; init; }
    public bool CanPublishPackages { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanMutateEnvironment { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanDispatchPipeline { get; init; }

    public static PostDeployVerificationBoundary Evidence { get; } = new();

    public static PostDeployVerificationBoundary ReadOnly { get; } = new();
}

public static class PostDeployVerificationBoundaryText
{
    public const string Boundary = """
        Block BF consumes a controlled deployment execution receipt and post-deployment observation evidence.
        Deployment execution is not post-deployment verification.
        Post-deployment verification is not workflow continuation.
        Post-deployment verification is not memory promotion.
        Post-deployment verification is not package publication.
        Failed verification is not rollback approval.
        Rollback consideration is not rollback decision.
        Rollback decision is not rollback execution.
        BF does not rollback.
        BF does not deploy again.
        BF does not retry deployment.
        BF does not publish packages.
        BF does not promote memory.
        BF does not continue workflow.
        BF does not mutate source.
        BF does not mutate environments.
        BF does not dispatch pipelines.
        CanProceedToRollbackDecision is not rollback execution.
        """;
}

public sealed record PostDeployVerificationPackageReceipt
{
    public required string PostDeployVerificationPackageReceiptId { get; init; }
    public required string PostDeployVerificationPackageId { get; init; }
    public required PostDeployVerificationPackageVerdict Verdict { get; init; }
    public required bool DeploymentVerified { get; init; }
    public required bool CanProceedToRollbackDecision { get; init; }
    public PostDeployVerificationBlockReason[] BlockReasons { get; init; } = [];
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public PostDeployVerificationBoundary Boundary { get; init; } =
        PostDeployVerificationBoundary.Evidence;
}

public sealed record PostDeployVerificationPackageArtifacts
{
    public required PostDeployVerificationPackage Package { get; init; }
    public required PostDeployVerificationPackageReceipt Receipt { get; init; }
}

public static class PostDeployVerificationPackageBuilder
{
    public static PostDeployVerificationPackageArtifacts Build(PostDeployVerificationPackageInput input)
    {
        var now = input.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var incomplete = new List<PostDeployVerificationBlockReason>();
        var blocked = new List<PostDeployVerificationBlockReason>();
        var failed = new List<PostDeployVerificationBlockReason>();
        var rollbackConsideration = new List<PostDeployVerificationBlockReason>();
        var issues = new List<string>();

        ValidateReceipt(input, incomplete, blocked, rollbackConsideration, issues);
        ValidateObservation(input, incomplete, blocked, failed, rollbackConsideration, issues);
        ValidateBoundary(blocked, issues);

        var blockReasons = blocked
            .Concat(failed)
            .Concat(rollbackConsideration)
            .Concat(incomplete)
            .Distinct()
            .ToArray();
        var verdict = DetermineVerdict(blocked, incomplete, failed, rollbackConsideration);
        var deploymentVerified = verdict == PostDeployVerificationPackageVerdict.DeploymentVerified;
        var canProceedToRollbackDecision = verdict == PostDeployVerificationPackageVerdict.RollbackConsiderationRequired;
        var receipt = input.DeploymentExecutionReceipt;
        var observation = input.Observation;
        var packageId = $"post_deploy_verification_pkg_{BfPostDeployVerificationHashing.ShortHash($"{receipt?.DeploymentExecutionReceiptId}|{observation?.ObservationId}|{input.Repository}|{input.CandidateCommitSha}|{input.CandidateVersion}|{input.CandidateTagName}|{input.ReleaseChannel}|{input.DeploymentTarget}|{input.DeploymentEnvironment}|{input.ExpectedArtifactName}|{input.ExpectedArtifactSha256}|{verdict}")}";

        var package = new PostDeployVerificationPackage
        {
            PostDeployVerificationPackageId = packageId,
            SourceDeploymentExecutionReceiptId = FeedbackText.Safe(receipt?.DeploymentExecutionReceiptId ?? "missing-deployment-execution-receipt"),
            Repository = FeedbackText.Safe(input.Repository),
            CandidateCommitSha = FeedbackText.Safe(input.CandidateCommitSha),
            CandidateVersion = FeedbackText.Safe(input.CandidateVersion),
            CandidateTagName = FeedbackText.Safe(input.CandidateTagName),
            ReleaseChannel = FeedbackText.Safe(input.ReleaseChannel),
            DeploymentTarget = FeedbackText.Safe(input.DeploymentTarget),
            DeploymentEnvironment = FeedbackText.Safe(input.DeploymentEnvironment),
            ExpectedArtifactName = FeedbackText.Safe(input.ExpectedArtifactName),
            ExpectedArtifactSha256 = FeedbackText.Safe(input.ExpectedArtifactSha256),
            ObservedVersion = FeedbackText.SafeOrNull(observation?.ObservedVersion),
            ObservedCommitSha = FeedbackText.SafeOrNull(observation?.ObservedCommitSha),
            ObservedArtifactName = FeedbackText.SafeOrNull(observation?.ObservedArtifactName),
            ObservedArtifactSha256 = FeedbackText.SafeOrNull(observation?.ObservedArtifactSha256),
            DeploymentVerified = deploymentVerified,
            CanProceedToRollbackDecision = canProceedToRollbackDecision,
            PackageVerdict = verdict,
            BlockReasons = blockReasons,
            PackageIssues = FeedbackText.SafeList(issues),
            CreatedBy = FeedbackText.Safe(input.CreatedBy),
            CreatedAtUtc = now,
            Boundary = PostDeployVerificationBoundary.Evidence
        };
        var packageReceipt = new PostDeployVerificationPackageReceipt
        {
            PostDeployVerificationPackageReceiptId = $"post_deploy_verification_receipt_{BfPostDeployVerificationHashing.ShortHash($"{packageId}|{verdict}|{now:O}")}",
            PostDeployVerificationPackageId = packageId,
            Verdict = verdict,
            DeploymentVerified = deploymentVerified,
            CanProceedToRollbackDecision = canProceedToRollbackDecision,
            BlockReasons = blockReasons,
            BoundaryStatements =
            [
                "BF consumes BE deployment execution receipt.",
                "Deployment execution is not post-deployment verification.",
                "Post-deployment verification is not workflow continuation.",
                "Post-deployment verification is not memory promotion.",
                "Post-deployment verification is not package publication.",
                "Failed verification is not rollback approval.",
                "Rollback consideration is not rollback decision.",
                "Rollback decision is not rollback execution.",
                "BF does not rollback.",
                "BF does not deploy again.",
                "BF does not retry deployment.",
                "BF does not mutate environments.",
                "CanProceedToRollbackDecision is not rollback execution."
            ],
            CreatedAtUtc = now,
            Boundary = PostDeployVerificationBoundary.Evidence
        };

        return new PostDeployVerificationPackageArtifacts
        {
            Package = package,
            Receipt = packageReceipt
        };
    }

    private static void ValidateReceipt(
        PostDeployVerificationPackageInput input,
        List<PostDeployVerificationBlockReason> incomplete,
        List<PostDeployVerificationBlockReason> blocked,
        List<PostDeployVerificationBlockReason> rollbackConsideration,
        List<string> issues)
    {
        var receipt = input.DeploymentExecutionReceipt;
        if (receipt is null)
        {
            incomplete.Add(PostDeployVerificationBlockReason.MissingDeploymentExecutionReceipt);
            issues.Add("MissingDeploymentExecutionReceipt");
            return;
        }

        if (receipt.ExecutionVerdict == DeploymentExecutionVerdict.Rejected)
        {
            blocked.Add(PostDeployVerificationBlockReason.DeploymentExecutionReceiptRejected);
            issues.Add("DeploymentExecutionReceiptRejected");
        }
        else if (receipt.ExecutionVerdict == DeploymentExecutionVerdict.Blocked)
        {
            blocked.Add(PostDeployVerificationBlockReason.DeploymentExecutionReceiptBlocked);
            issues.Add("DeploymentExecutionReceiptBlocked");
        }
        else if (receipt.ExecutionVerdict == DeploymentExecutionVerdict.Failed)
        {
            rollbackConsideration.Add(PostDeployVerificationBlockReason.DeploymentExecutionReceiptFailed);
            issues.Add("DeploymentExecutionReceiptFailed");
        }
        else if (receipt.ExecutionVerdict == DeploymentExecutionVerdict.PartiallyExecuted)
        {
            rollbackConsideration.Add(PostDeployVerificationBlockReason.DeploymentExecutionReceiptPartiallyExecuted);
            issues.Add("DeploymentExecutionReceiptPartiallyExecuted");
        }
        else if (receipt.ExecutionVerdict != DeploymentExecutionVerdict.ExecutedAndVerified)
        {
            blocked.Add(PostDeployVerificationBlockReason.DeploymentExecutionReceiptUnverified);
            issues.Add($"DeploymentExecutionReceiptUnverified:{receipt.ExecutionVerdict}");
        }

        if (!receipt.PreDeploymentStateVerified ||
            !receipt.PostDeploymentStateVerified ||
            !receipt.DeploymentAttempted ||
            !receipt.DeploymentAccepted)
        {
            rollbackConsideration.Add(PostDeployVerificationBlockReason.DeploymentExecutionReceiptUnverified);
            issues.Add("DeploymentExecutionReceiptUnverified");
        }

        if (ReceiptBoundaryCarriesForbiddenAuthority(receipt.Boundary) ||
            receipt.PackagePublicationAttempted ||
            receipt.MemoryPromotionAttempted ||
            receipt.WorkflowContinuationAttempted ||
            receipt.RollbackExecutionAttempted ||
            receipt.SourceMutationAttempted)
        {
            blocked.Add(PostDeployVerificationBlockReason.DeploymentExecutionReceiptBoundaryViolation);
            issues.Add("DeploymentExecutionReceiptBoundaryViolation");
        }

        if (!Same(receipt.Repository, input.Repository))
        {
            blocked.Add(PostDeployVerificationBlockReason.RepositoryMismatch);
            issues.Add("RepositoryMismatch");
        }

        if (!Same(receipt.CandidateCommitSha, input.CandidateCommitSha))
        {
            blocked.Add(PostDeployVerificationBlockReason.CandidateCommitMismatch);
            issues.Add("CandidateCommitMismatch");
        }

        if (!Same(receipt.CandidateVersion, input.CandidateVersion))
        {
            blocked.Add(PostDeployVerificationBlockReason.CandidateVersionMismatch);
            issues.Add("CandidateVersionMismatch");
        }

        if (!Same(receipt.CandidateTagName, input.CandidateTagName))
        {
            blocked.Add(PostDeployVerificationBlockReason.CandidateTagMismatch);
            issues.Add("CandidateTagMismatch");
        }

        if (!Same(receipt.ReleaseChannel, input.ReleaseChannel))
        {
            blocked.Add(PostDeployVerificationBlockReason.ReleaseChannelMismatch);
            issues.Add("ReleaseChannelMismatch");
        }

        if (!Same(receipt.DeploymentTarget, input.DeploymentTarget))
        {
            blocked.Add(PostDeployVerificationBlockReason.DeploymentTargetMismatch);
            issues.Add("DeploymentTargetMismatch");
        }

        if (!Same(receipt.DeploymentEnvironment, input.DeploymentEnvironment))
        {
            blocked.Add(PostDeployVerificationBlockReason.DeploymentEnvironmentMismatch);
            issues.Add("DeploymentEnvironmentMismatch");
        }

        if (!Same(receipt.DeployedArtifactName, input.ExpectedArtifactName))
        {
            blocked.Add(PostDeployVerificationBlockReason.DeploymentArtifactMismatch);
            issues.Add("DeploymentArtifactMismatch");
        }

        if (!Same(receipt.DeployedArtifactSha256, input.ExpectedArtifactSha256))
        {
            blocked.Add(PostDeployVerificationBlockReason.DeploymentArtifactChecksumMismatch);
            issues.Add("DeploymentArtifactChecksumMismatch");
        }
    }

    private static bool ReceiptBoundaryCarriesForbiddenAuthority(DeploymentExecutionBoundary boundary) =>
        boundary.CanPublishPackages ||
        boundary.CanPromoteMemory ||
        boundary.CanContinueWorkflow ||
        boundary.CanCommit ||
        boundary.CanPush ||
        boundary.CanMutateSource ||
        boundary.CanMutateWorkspace ||
        boundary.CanExecuteRollback ||
        boundary.CanCreateTag ||
        boundary.CanCreateGitHubRelease ||
        boundary.CanDispatchPipeline;

    private static void ValidateObservation(
        PostDeployVerificationPackageInput input,
        List<PostDeployVerificationBlockReason> incomplete,
        List<PostDeployVerificationBlockReason> blocked,
        List<PostDeployVerificationBlockReason> failed,
        List<PostDeployVerificationBlockReason> rollbackConsideration,
        List<string> issues)
    {
        var observation = input.Observation;
        if (observation is null)
        {
            incomplete.Add(PostDeployVerificationBlockReason.MissingPostDeployObservation);
            issues.Add("MissingPostDeployObservation");
            return;
        }

        if (string.IsNullOrWhiteSpace(observation.ObservationSource) || observation.ObservedAtUtc == default)
        {
            incomplete.Add(PostDeployVerificationBlockReason.MissingPostDeployObservation);
            issues.Add("PostDeployObservationMissingSourceOrTimestamp");
        }

        ValidateObservationIdentity(input, observation, blocked, issues);

        if (!observation.ObservationSucceeded)
        {
            rollbackConsideration.Add(PostDeployVerificationBlockReason.PostDeployObservationFailed);
            issues.Add($"PostDeployObservationFailed:{observation.ObservationError ?? "post-deploy observation failed"}");
            return;
        }

        ValidateObservedDeployment(input, observation, failed, rollbackConsideration, issues);
        ValidateHealthCheck(observation, failed, rollbackConsideration, issues);
    }

    private static void ValidateObservationIdentity(
        PostDeployVerificationPackageInput input,
        PostDeployObservationEvidence observation,
        List<PostDeployVerificationBlockReason> blocked,
        List<string> issues)
    {
        if (!Same(observation.Repository, input.Repository))
        {
            blocked.Add(PostDeployVerificationBlockReason.RepositoryMismatch);
            issues.Add("ObservationRepositoryMismatch");
        }

        if (!Same(observation.CandidateCommitSha, input.CandidateCommitSha))
        {
            blocked.Add(PostDeployVerificationBlockReason.CandidateCommitMismatch);
            issues.Add("ObservationCandidateCommitMismatch");
        }

        if (!Same(observation.CandidateVersion, input.CandidateVersion))
        {
            blocked.Add(PostDeployVerificationBlockReason.CandidateVersionMismatch);
            issues.Add("ObservationCandidateVersionMismatch");
        }

        if (!Same(observation.CandidateTagName, input.CandidateTagName))
        {
            blocked.Add(PostDeployVerificationBlockReason.CandidateTagMismatch);
            issues.Add("ObservationCandidateTagMismatch");
        }

        if (!Same(observation.ReleaseChannel, input.ReleaseChannel))
        {
            blocked.Add(PostDeployVerificationBlockReason.ReleaseChannelMismatch);
            issues.Add("ObservationReleaseChannelMismatch");
        }

        if (!Same(observation.DeploymentTarget, input.DeploymentTarget))
        {
            blocked.Add(PostDeployVerificationBlockReason.DeploymentTargetMismatch);
            issues.Add("ObservationDeploymentTargetMismatch");
        }

        if (!Same(observation.DeploymentEnvironment, input.DeploymentEnvironment))
        {
            blocked.Add(PostDeployVerificationBlockReason.DeploymentEnvironmentMismatch);
            issues.Add("ObservationDeploymentEnvironmentMismatch");
        }
    }

    private static void ValidateObservedDeployment(
        PostDeployVerificationPackageInput input,
        PostDeployObservationEvidence observation,
        List<PostDeployVerificationBlockReason> failed,
        List<PostDeployVerificationBlockReason> rollbackConsideration,
        List<string> issues)
    {
        if (!Same(observation.ObservedVersion, input.CandidateVersion))
        {
            failed.Add(PostDeployVerificationBlockReason.CandidateVersionMismatch);
            rollbackConsideration.Add(PostDeployVerificationBlockReason.CandidateVersionMismatch);
            issues.Add("ObservedVersionMismatch");
        }

        if (!Same(observation.ObservedCommitSha, input.CandidateCommitSha))
        {
            failed.Add(PostDeployVerificationBlockReason.CandidateCommitMismatch);
            rollbackConsideration.Add(PostDeployVerificationBlockReason.CandidateCommitMismatch);
            issues.Add("ObservedCommitMismatch");
        }

        if (!Same(observation.ObservedArtifactName, input.ExpectedArtifactName))
        {
            failed.Add(PostDeployVerificationBlockReason.DeploymentArtifactMismatch);
            rollbackConsideration.Add(PostDeployVerificationBlockReason.DeploymentArtifactMismatch);
            issues.Add("ObservedArtifactNameMismatch");
        }

        if (!Same(observation.ObservedArtifactSha256, input.ExpectedArtifactSha256))
        {
            failed.Add(PostDeployVerificationBlockReason.DeploymentArtifactChecksumMismatch);
            rollbackConsideration.Add(PostDeployVerificationBlockReason.DeploymentArtifactChecksumMismatch);
            issues.Add("ObservedArtifactChecksumMismatch");
        }
    }

    private static void ValidateHealthCheck(
        PostDeployObservationEvidence observation,
        List<PostDeployVerificationBlockReason> failed,
        List<PostDeployVerificationBlockReason> rollbackConsideration,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(observation.HealthCheckSummary))
        {
            failed.Add(PostDeployVerificationBlockReason.HealthCheckMissing);
            rollbackConsideration.Add(PostDeployVerificationBlockReason.HealthCheckMissing);
            issues.Add("HealthCheckMissing");
        }

        if (!observation.HealthCheckSucceeded)
        {
            failed.Add(PostDeployVerificationBlockReason.HealthCheckFailed);
            rollbackConsideration.Add(PostDeployVerificationBlockReason.HealthCheckFailed);
            issues.Add("HealthCheckFailed");
        }
    }

    private static void ValidateBoundary(
        List<PostDeployVerificationBlockReason> blocked,
        List<string> issues)
    {
        var boundary = PostDeployVerificationBoundary.Evidence;
        if (!boundary.EvidenceOnly)
        {
            blocked.Add(PostDeployVerificationBlockReason.BoundaryViolation);
            issues.Add("BoundaryViolation");
        }

        if (boundary.CanDeploy || boundary.CanRetryDeployment)
        {
            blocked.Add(PostDeployVerificationBlockReason.DeploymentRetryNotAllowed);
            issues.Add("DeploymentRetryNotAllowed");
        }

        if (boundary.CanExecuteRollback || boundary.CanDecideRollback)
        {
            blocked.Add(PostDeployVerificationBlockReason.RollbackExecutionNotAllowed);
            issues.Add("RollbackExecutionNotAllowed");
        }

        if (boundary.CanPublishPackages)
        {
            blocked.Add(PostDeployVerificationBlockReason.PackagePublicationNotAllowed);
            issues.Add("PackagePublicationNotAllowed");
        }

        if (boundary.CanPromoteMemory)
        {
            blocked.Add(PostDeployVerificationBlockReason.MemoryPromotionNotAllowed);
            issues.Add("MemoryPromotionNotAllowed");
        }

        if (boundary.CanContinueWorkflow)
        {
            blocked.Add(PostDeployVerificationBlockReason.WorkflowContinuationNotAllowed);
            issues.Add("WorkflowContinuationNotAllowed");
        }

        if (boundary.CanMutateEnvironment)
        {
            blocked.Add(PostDeployVerificationBlockReason.EnvironmentMutationNotAllowed);
            issues.Add("EnvironmentMutationNotAllowed");
        }

        if (boundary.CanMutateSource)
        {
            blocked.Add(PostDeployVerificationBlockReason.SourceMutationNotAllowed);
            issues.Add("SourceMutationNotAllowed");
        }

        if (boundary.CanCommit || boundary.CanPush)
        {
            blocked.Add(PostDeployVerificationBlockReason.CommitPushNotAllowed);
            issues.Add("CommitPushNotAllowed");
        }

        if (boundary.CanDispatchPipeline)
        {
            blocked.Add(PostDeployVerificationBlockReason.PipelineDispatchNotAllowed);
            issues.Add("PipelineDispatchNotAllowed");
        }
    }

    private static PostDeployVerificationPackageVerdict DetermineVerdict(
        IReadOnlyCollection<PostDeployVerificationBlockReason> blocked,
        IReadOnlyCollection<PostDeployVerificationBlockReason> incomplete,
        IReadOnlyCollection<PostDeployVerificationBlockReason> failed,
        IReadOnlyCollection<PostDeployVerificationBlockReason> rollbackConsideration)
    {
        if (blocked.Count > 0)
            return PostDeployVerificationPackageVerdict.VerificationBlocked;
        if (incomplete.Count > 0)
            return PostDeployVerificationPackageVerdict.VerificationIncomplete;
        if (rollbackConsideration.Count > 0)
            return PostDeployVerificationPackageVerdict.RollbackConsiderationRequired;
        if (failed.Count > 0)
            return PostDeployVerificationPackageVerdict.VerificationFailed;
        return PostDeployVerificationPackageVerdict.DeploymentVerified;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class PostDeployVerificationBypassEvaluator
{
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanRetryDeployment(object? evidence) => false;
    public static bool CanExecuteRollback(object? evidence) => false;
    public static bool CanDecideRollback(object? evidence) => false;
    public static bool CanPublishPackages(object? evidence) => false;
    public static bool CanPromoteMemory(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
    public static bool CanMutateEnvironment(object? evidence) => false;
    public static bool CanMutateSource(object? evidence) => false;
    public static bool CanCommit(object? evidence) => false;
    public static bool CanPush(object? evidence) => false;
    public static bool CanDispatchPipeline(object? evidence) => false;
}

internal static class BfPostDeployVerificationHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
