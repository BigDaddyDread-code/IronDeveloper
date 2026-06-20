using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum DeploymentReadinessSeparationVerdict
{
    PackageReadyForDeploymentReadinessDecision = 0,
    PackageIncomplete,
    PackageBlocked,
    PackageRejected
}

public enum DeploymentReadinessSeparationBlockReason
{
    MissingReleaseExecutionReceipt = 0,
    ReleaseExecutionNotVerified,
    ReleaseExecutionFailed,
    ReleaseExecutionPartial,
    ReleaseExecutionRejected,
    ReleaseExecutionReceiptStale,

    RepositoryMismatch,
    CandidateCommitMismatch,
    CandidateVersionMismatch,
    CandidateTagMismatch,
    ReleaseChannelMismatch,

    DeploymentAlreadyAttempted,
    PackagePublicationAlreadyAttempted,
    MemoryPromotionAlreadyAttempted,
    WorkflowContinuationAlreadyAttempted,
    RollbackExecutionAlreadyAttempted,

    MissingDeploymentTargetDeclaration,
    MissingDeploymentReadinessScope,
    UnsupportedDeploymentTarget,
    MissingRequiredSeparationStatement,

    DeploymentMutationNotAllowed,
    PublishPackagesNotAllowed,
    MemoryPromotionNotAllowed,
    WorkflowContinuationNotAllowed,
    SourceMutationNotAllowed,
    CommitPushNotAllowed,
    RollbackExecutionNotAllowed,
    BoundaryViolation
}

public sealed record DeploymentReadinessSeparationInput
{
    public ReleaseExecutionReceipt? ReleaseExecutionReceipt { get; init; }

    public required string Repository { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public required string DeploymentTarget { get; init; }
    public required string DeploymentReadinessScope { get; init; }

    public required string CreatedBy { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record DeploymentReadinessSeparationPackage
{
    public required string DeploymentReadinessSeparationPackageId { get; init; }

    public required string SourceReleaseExecutionReceiptId { get; init; }
    public required string Repository { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public required string DeploymentTarget { get; init; }
    public required string DeploymentReadinessScope { get; init; }

    public required DeploymentReadinessSeparationVerdict PackageVerdict { get; init; }
    public required bool CanProceedToDeploymentReadinessDecision { get; init; }

    public DeploymentReadinessSeparationBlockReason[] BlockReasons { get; init; } = [];
    public string[] PackageIssues { get; init; } = [];

    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DeploymentReadinessSeparationBoundary Boundary { get; init; } =
        DeploymentReadinessSeparationBoundary.Evidence;
}

public sealed record DeploymentReadinessSeparationBoundary
{
    public bool EvidenceOnly { get; init; } = true;

    public bool CanDecideDeploymentReadiness { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanPublishPackages { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanExecuteRollback { get; init; }
    public bool CanDispatchPipeline { get; init; }
    public bool CanMutateEnvironment { get; init; }

    public static DeploymentReadinessSeparationBoundary Evidence { get; } = new();
}

public static class DeploymentReadinessSeparationBoundaryText
{
    public const string Boundary = """
        Block BC consumes a verified BB release execution receipt and packages deployment-readiness separation evidence.
        Release execution is not deployment readiness.
        Release execution receipt is not deployment authority.
        Deployment readiness separation is not deployment readiness decision.
        Deployment readiness decision is not deployment execution.
        BC does not deploy.
        BC does not publish packages.
        BC does not promote memory.
        BC does not continue workflow.
        BC does not mutate source.
        BC does not dispatch deployment pipelines.
        BC does not mutate environments.
        BC does not execute rollback.
        A tag is not deployment.
        A GitHub release is not deployment.
        Uploaded release artifacts are not package publication.
        """;
}

public sealed record DeploymentReadinessSeparationReceipt
{
    public required string DeploymentReadinessSeparationReceiptId { get; init; }
    public required string DeploymentReadinessSeparationPackageId { get; init; }
    public required DeploymentReadinessSeparationVerdict Verdict { get; init; }
    public required bool CanProceedToDeploymentReadinessDecision { get; init; }
    public DeploymentReadinessSeparationBlockReason[] BlockReasons { get; init; } = [];
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DeploymentReadinessSeparationBoundary Boundary { get; init; } =
        DeploymentReadinessSeparationBoundary.Evidence;
}

public sealed record DeploymentReadinessSeparationArtifacts
{
    public required DeploymentReadinessSeparationPackage Package { get; init; }
    public required DeploymentReadinessSeparationReceipt Receipt { get; init; }
}

public static class DeploymentReadinessSeparationPackageBuilder
{
    public static DeploymentReadinessSeparationArtifacts Build(DeploymentReadinessSeparationInput input)
    {
        var now = input.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var incomplete = new List<DeploymentReadinessSeparationBlockReason>();
        var blocked = new List<DeploymentReadinessSeparationBlockReason>();
        var rejected = new List<DeploymentReadinessSeparationBlockReason>();
        var issues = new List<string>();

        ValidateReleaseExecutionReceipt(input, incomplete, blocked, rejected, issues);
        ValidateDeploymentTarget(input, incomplete, blocked, issues);
        ValidateDeploymentReadinessScope(input, incomplete, blocked, issues);
        ValidateBoundary(blocked, issues);

        var blockReasons = blocked.Concat(rejected).Concat(incomplete).Distinct().ToArray();
        var verdict = DetermineVerdict(blocked, rejected, incomplete);
        var canProceed = verdict == DeploymentReadinessSeparationVerdict.PackageReadyForDeploymentReadinessDecision;
        var packageId = $"deployment_readiness_sep_{BcDeploymentReadinessHashing.ShortHash($"{input.Repository}|{input.CandidateCommitSha}|{input.CandidateVersion}|{input.CandidateTagName}|{input.ReleaseChannel}|{input.DeploymentTarget}|{input.DeploymentReadinessScope}|{verdict}")}";
        var package = new DeploymentReadinessSeparationPackage
        {
            DeploymentReadinessSeparationPackageId = packageId,
            SourceReleaseExecutionReceiptId = FeedbackText.Safe(input.ReleaseExecutionReceipt?.ReleaseExecutionId ?? "missing-release-execution-receipt"),
            Repository = FeedbackText.Safe(input.Repository),
            CandidateCommitSha = FeedbackText.Safe(input.CandidateCommitSha),
            CandidateVersion = FeedbackText.Safe(input.CandidateVersion),
            CandidateTagName = FeedbackText.Safe(input.CandidateTagName),
            ReleaseChannel = FeedbackText.Safe(input.ReleaseChannel),
            DeploymentTarget = FeedbackText.Safe(input.DeploymentTarget),
            DeploymentReadinessScope = FeedbackText.Safe(input.DeploymentReadinessScope),
            PackageVerdict = verdict,
            CanProceedToDeploymentReadinessDecision = canProceed,
            BlockReasons = blockReasons,
            PackageIssues = FeedbackText.SafeList(issues),
            CreatedBy = FeedbackText.Safe(input.CreatedBy),
            CreatedAtUtc = now,
            Boundary = DeploymentReadinessSeparationBoundary.Evidence
        };
        var receipt = new DeploymentReadinessSeparationReceipt
        {
            DeploymentReadinessSeparationReceiptId = $"deployment_readiness_sep_receipt_{BcDeploymentReadinessHashing.ShortHash($"{packageId}|{verdict}|{now:O}")}",
            DeploymentReadinessSeparationPackageId = packageId,
            Verdict = verdict,
            CanProceedToDeploymentReadinessDecision = canProceed,
            BlockReasons = blockReasons,
            BoundaryStatements =
            [
                "BC consumes BB release execution receipt.",
                "Release execution is not deployment readiness.",
                "Release execution receipt is not deployment authority.",
                "Deployment readiness separation is not deployment readiness decision.",
                "Deployment readiness decision is not deployment execution.",
                "BC does not deploy.",
                "BC does not publish packages.",
                "BC does not promote memory.",
                "BC does not continue workflow.",
                "BC does not mutate source.",
                "BC does not dispatch deployment pipelines.",
                "BC does not execute rollback.",
                "A tag is not deployment.",
                "A GitHub release is not deployment.",
                "Uploaded release artifacts are not package publication."
            ],
            CreatedAtUtc = now,
            Boundary = DeploymentReadinessSeparationBoundary.Evidence
        };

        return new DeploymentReadinessSeparationArtifacts
        {
            Package = package,
            Receipt = receipt
        };
    }

    private static void ValidateReleaseExecutionReceipt(
        DeploymentReadinessSeparationInput input,
        List<DeploymentReadinessSeparationBlockReason> incomplete,
        List<DeploymentReadinessSeparationBlockReason> blocked,
        List<DeploymentReadinessSeparationBlockReason> rejected,
        List<string> issues)
    {
        var receipt = input.ReleaseExecutionReceipt;
        if (receipt is null)
        {
            incomplete.Add(DeploymentReadinessSeparationBlockReason.MissingReleaseExecutionReceipt);
            issues.Add("MissingReleaseExecutionReceipt");
            return;
        }

        if (receipt.ExecutionVerdict == ReleaseExecutionVerdict.Rejected)
        {
            rejected.Add(DeploymentReadinessSeparationBlockReason.ReleaseExecutionRejected);
            issues.Add("ReleaseExecutionRejected");
        }
        else if (receipt.ExecutionVerdict == ReleaseExecutionVerdict.PartiallyExecuted)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.ReleaseExecutionPartial);
            issues.Add("ReleaseExecutionPartial");
        }
        else if (receipt.ExecutionVerdict == ReleaseExecutionVerdict.Failed)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.ReleaseExecutionFailed);
            issues.Add("ReleaseExecutionFailed");
        }
        else if (receipt.ExecutionVerdict != ReleaseExecutionVerdict.ExecutedAndVerified)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.ReleaseExecutionNotVerified);
            issues.Add($"ReleaseExecutionNotVerified:{receipt.ExecutionVerdict}");
        }

        if (receipt.FailureClassification != ReleaseExecutionFailureKind.None)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.ReleaseExecutionFailed);
            issues.Add($"ReleaseExecutionFailureClassification:{receipt.FailureClassification}");
        }

        if (!receipt.PreStateVerified || !receipt.PostStateVerified)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.ReleaseExecutionNotVerified);
            issues.Add("ReleaseExecutionPostStateNotVerified");
        }

        if (!receipt.TagCreated || !receipt.GitHubReleaseCreated)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.ReleaseExecutionPartial);
            issues.Add("ReleaseExecutionSurfaceIncomplete");
        }

        if (receipt.ApprovedActions.Contains(ReleaseExecutionAction.UploadReleaseArtifacts) && !receipt.ReleaseArtifactsUploaded)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.ReleaseExecutionPartial);
            issues.Add("ReleaseExecutionArtifactUploadIncomplete");
        }

        if (!Same(receipt.Repository, input.Repository))
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.RepositoryMismatch);
            issues.Add("RepositoryMismatch");
        }

        if (!Same(receipt.CandidateCommitSha, input.CandidateCommitSha))
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.CandidateCommitMismatch);
            issues.Add("CandidateCommitMismatch");
        }

        if (!Same(receipt.CandidateVersion, input.CandidateVersion))
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.CandidateVersionMismatch);
            issues.Add("CandidateVersionMismatch");
        }

        if (!Same(receipt.CandidateTagName, input.CandidateTagName))
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.CandidateTagMismatch);
            issues.Add("CandidateTagMismatch");
        }

        if (!Same(receipt.ReleaseChannel, input.ReleaseChannel))
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.ReleaseChannelMismatch);
            issues.Add("ReleaseChannelMismatch");
        }

        if (receipt.DeploymentAttempted)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.DeploymentAlreadyAttempted);
            issues.Add("DeploymentAlreadyAttempted");
        }

        if (receipt.PackagePublicationAttempted)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.PackagePublicationAlreadyAttempted);
            issues.Add("PackagePublicationAlreadyAttempted");
        }

        if (receipt.MemoryPromotionAttempted)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.MemoryPromotionAlreadyAttempted);
            issues.Add("MemoryPromotionAlreadyAttempted");
        }

        if (receipt.WorkflowContinuationAttempted)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.WorkflowContinuationAlreadyAttempted);
            issues.Add("WorkflowContinuationAlreadyAttempted");
        }

        if (receipt.RollbackExecutionAttempted)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.RollbackExecutionAlreadyAttempted);
            issues.Add("RollbackExecutionAlreadyAttempted");
        }

        ValidateReleaseExecutionBoundary(receipt.Boundary, blocked, issues);
    }

    private static void ValidateReleaseExecutionBoundary(
        ReleaseExecutionBoundary boundary,
        List<DeploymentReadinessSeparationBlockReason> blocked,
        List<string> issues)
    {
        if (boundary.CanDeploy)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.DeploymentMutationNotAllowed);
            issues.Add("ReleaseExecutionBoundaryCanDeploy");
        }

        if (boundary.CanPublishPackages)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.PublishPackagesNotAllowed);
            issues.Add("ReleaseExecutionBoundaryCanPublishPackages");
        }

        if (boundary.CanPromoteMemory)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.MemoryPromotionNotAllowed);
            issues.Add("ReleaseExecutionBoundaryCanPromoteMemory");
        }

        if (boundary.CanContinueWorkflow)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.WorkflowContinuationNotAllowed);
            issues.Add("ReleaseExecutionBoundaryCanContinueWorkflow");
        }

        if (boundary.CanCommit || boundary.CanPush)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.CommitPushNotAllowed);
            issues.Add("ReleaseExecutionBoundaryCanCommitOrPush");
        }

        if (boundary.CanMutateSource || boundary.CanMutateWorkspace)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.SourceMutationNotAllowed);
            issues.Add("ReleaseExecutionBoundaryCanMutateSourceOrWorkspace");
        }

        if (boundary.CanExecuteRollback)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.RollbackExecutionNotAllowed);
            issues.Add("ReleaseExecutionBoundaryCanExecuteRollback");
        }

        if (boundary.CanMerge ||
            boundary.CanApprove ||
            boundary.CanMarkReadyForReview ||
            boundary.CanRequestReviewers)
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.BoundaryViolation);
            issues.Add("ReleaseExecutionBoundaryCarriesReviewOrMergeAuthority");
        }
    }

    private static void ValidateDeploymentTarget(
        DeploymentReadinessSeparationInput input,
        List<DeploymentReadinessSeparationBlockReason> incomplete,
        List<DeploymentReadinessSeparationBlockReason> blocked,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(input.DeploymentTarget))
        {
            incomplete.Add(DeploymentReadinessSeparationBlockReason.MissingDeploymentTargetDeclaration);
            issues.Add("MissingDeploymentTargetDeclaration");
            return;
        }

        if (IsUnsupportedDeploymentTarget(input.DeploymentTarget))
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.UnsupportedDeploymentTarget);
            issues.Add("UnsupportedDeploymentTarget");
        }

        if (ContainsUnsafeAuthorityClaim(input.DeploymentTarget))
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.MissingRequiredSeparationStatement);
            issues.Add("UnsafeDeploymentReadinessClaim");
        }
    }

    private static void ValidateDeploymentReadinessScope(
        DeploymentReadinessSeparationInput input,
        List<DeploymentReadinessSeparationBlockReason> incomplete,
        List<DeploymentReadinessSeparationBlockReason> blocked,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(input.DeploymentReadinessScope))
        {
            incomplete.Add(DeploymentReadinessSeparationBlockReason.MissingDeploymentReadinessScope);
            issues.Add("MissingDeploymentReadinessScope");
            return;
        }

        if (ContainsUnsafeAuthorityClaim(input.DeploymentReadinessScope))
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.MissingRequiredSeparationStatement);
            issues.Add("UnsafeDeploymentReadinessClaim");
        }
    }

    private static bool IsUnsupportedDeploymentTarget(string value)
    {
        var text = value.Trim();
        if (text.Contains('\n') || text.Contains('\r'))
            return true;

        string[] unsupportedMarkers =
        [
            "..",
            "$",
            "%",
            "`",
            ";",
            "&",
            "|",
            "<",
            ">",
            "\\",
            "kubectl",
            "terraform apply",
            "az webapp",
            "docker push",
            "nuget push",
            "npm publish",
            "gh workflow run"
        ];
        return unsupportedMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsUnsafeAuthorityClaim(string value)
    {
        string[] markers =
        [
            "release execution means deployment readiness",
            "release receipt authorizes deployment",
            "tag creation means deployable",
            "github release means deployed",
            "artifact upload means published package",
            "deployment readiness separation is deployment approval"
        ];
        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateBoundary(
        List<DeploymentReadinessSeparationBlockReason> blocked,
        List<string> issues)
    {
        var boundary = DeploymentReadinessSeparationBoundary.Evidence;
        if (!boundary.EvidenceOnly || HasAuthority(boundary))
        {
            blocked.Add(DeploymentReadinessSeparationBlockReason.BoundaryViolation);
            issues.Add("BoundaryViolation");
        }
    }

    private static bool HasAuthority(DeploymentReadinessSeparationBoundary boundary) =>
        boundary.CanDecideDeploymentReadiness ||
        boundary.CanDeploy ||
        boundary.CanPublishPackages ||
        boundary.CanPromoteMemory ||
        boundary.CanContinueWorkflow ||
        boundary.CanCommit ||
        boundary.CanPush ||
        boundary.CanMutateSource ||
        boundary.CanMutateWorkspace ||
        boundary.CanExecuteRollback ||
        boundary.CanDispatchPipeline ||
        boundary.CanMutateEnvironment;

    private static DeploymentReadinessSeparationVerdict DetermineVerdict(
        IReadOnlyCollection<DeploymentReadinessSeparationBlockReason> blocked,
        IReadOnlyCollection<DeploymentReadinessSeparationBlockReason> rejected,
        IReadOnlyCollection<DeploymentReadinessSeparationBlockReason> incomplete)
    {
        if (rejected.Count > 0)
            return DeploymentReadinessSeparationVerdict.PackageRejected;
        if (blocked.Count > 0)
            return DeploymentReadinessSeparationVerdict.PackageBlocked;
        return incomplete.Count > 0
            ? DeploymentReadinessSeparationVerdict.PackageIncomplete
            : DeploymentReadinessSeparationVerdict.PackageReadyForDeploymentReadinessDecision;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class DeploymentReadinessSeparationBypassEvaluator
{
    public static bool CanDecideDeploymentReadiness(object? evidence) => false;
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanPublishPackages(object? evidence) => false;
    public static bool CanPromoteMemory(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
    public static bool CanCommit(object? evidence) => false;
    public static bool CanPush(object? evidence) => false;
    public static bool CanMutateSource(object? evidence) => false;
    public static bool CanMutateWorkspace(object? evidence) => false;
    public static bool CanExecuteRollback(object? evidence) => false;
    public static bool CanDispatchPipeline(object? evidence) => false;
    public static bool CanMutateEnvironment(object? evidence) => false;
}

internal static class BcDeploymentReadinessHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
