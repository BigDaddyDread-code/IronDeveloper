using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum DeploymentReadinessDecisionPackageVerdict
{
    PackageReadyForControlledDeploymentExecutor = 0,
    PackageIncomplete,
    PackageBlocked,
    PackageRejected
}

public enum DeploymentReadinessDecision
{
    ApprovedForControlledDeploymentExecutor = 0,
    Rejected,
    NeedsMoreEvidence
}

public enum DeploymentReadinessDecisionPackageBlockReason
{
    MissingDeploymentReadinessSeparationPackage = 0,
    DeploymentReadinessSeparationPackageNotReady,
    DeploymentReadinessSeparationPackageBlocked,
    DeploymentReadinessSeparationPackageRejected,
    DeploymentReadinessSeparationBoundaryAuthorityViolation,

    RepositoryMismatch,
    CandidateCommitMismatch,
    CandidateVersionMismatch,
    CandidateTagMismatch,
    ReleaseChannelMismatch,
    DeploymentTargetMismatch,

    MissingDeploymentEnvironment,
    MissingDeploymentArtifactName,
    MissingDeploymentArtifactChecksum,
    InvalidDeploymentArtifactChecksum,

    MissingDeploymentReadinessDecision,
    DeploymentReadinessDecisionRejected,
    DeploymentReadinessDecisionNeedsMoreEvidence,
    DeploymentReadinessDecisionStale,
    MissingDecisionMaker,
    MissingDecisionTime,
    MissingDecisionRationale,
    DecisionMakerNotAllowed,

    DeploymentMutationNotAllowed,
    PublishPackagesNotAllowed,
    MemoryPromotionNotAllowed,
    WorkflowContinuationNotAllowed,
    EnvironmentMutationNotAllowed,
    SourceMutationNotAllowed,
    CommitPushNotAllowed,
    RollbackExecutionNotAllowed,
    PipelineDispatchNotAllowed,
    BoundaryViolation
}

public sealed record DeploymentReadinessDecisionEvidence
{
    public required string DeploymentReadinessDecisionId { get; init; }
    public required DeploymentReadinessDecision Decision { get; init; }
    public required string DecisionMadeBy { get; init; }
    public required DateTimeOffset DecisionMadeAtUtc { get; init; }
    public required string DecisionRationale { get; init; }

    public required string ExpectedDeploymentReadinessSeparationPackageId { get; init; }
    public required string ExpectedRepository { get; init; }
    public required string ExpectedCandidateCommitSha { get; init; }
    public required string ExpectedVersion { get; init; }
    public required string ExpectedTagName { get; init; }
    public required string ExpectedReleaseChannel { get; init; }
    public required string ExpectedDeploymentTarget { get; init; }
    public required string ExpectedDeploymentEnvironment { get; init; }
    public required string ExpectedDeploymentArtifactName { get; init; }
    public required string ExpectedDeploymentArtifactSha256 { get; init; }
}

public sealed record DeploymentReadinessDecisionPackageInput
{
    public DeploymentReadinessSeparationPackage? DeploymentReadinessSeparationPackage { get; init; }
    public DeploymentReadinessDecisionEvidence? DeploymentReadinessDecision { get; init; }

    public required string Repository { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public required string DeploymentTarget { get; init; }
    public required string DeploymentEnvironment { get; init; }

    public required string DeploymentArtifactName { get; init; }
    public required string DeploymentArtifactSha256 { get; init; }

    public required string CreatedBy { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record DeploymentReadinessDecisionPackage
{
    public required string DeploymentReadinessDecisionPackageId { get; init; }
    public required string SourceDeploymentReadinessSeparationPackageId { get; init; }

    public required string Repository { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public required string DeploymentTarget { get; init; }
    public required string DeploymentEnvironment { get; init; }

    public required string DeploymentArtifactName { get; init; }
    public required string DeploymentArtifactSha256 { get; init; }

    public required DeploymentReadinessDecision Decision { get; init; }
    public required string DecisionMadeBy { get; init; }
    public required DateTimeOffset DecisionMadeAtUtc { get; init; }
    public required string DecisionRationale { get; init; }

    public required DeploymentReadinessDecisionPackageVerdict PackageVerdict { get; init; }
    public required bool CanProceedToControlledDeploymentExecutor { get; init; }

    public string[] PackageIssues { get; init; } = [];
    public DeploymentReadinessDecisionPackageBlockReason[] BlockReasons { get; init; } = [];

    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    public DeploymentReadinessDecisionPackageBoundary Boundary { get; init; } =
        DeploymentReadinessDecisionPackageBoundary.Evidence;
}

public sealed record DeploymentReadinessDecisionPackageBoundary
{
    public bool EvidenceOnly { get; init; } = true;

    public bool CanDeploy { get; init; }
    public bool CanPublishPackages { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanMutateEnvironment { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanExecuteRollback { get; init; }
    public bool CanDispatchPipeline { get; init; }

    public static DeploymentReadinessDecisionPackageBoundary Evidence { get; } = new();
}

public static class DeploymentReadinessDecisionPackageBoundaryText
{
    public const string Boundary = """
        Block BD consumes an eligible BC deployment-readiness separation package and packages an explicit deployment-readiness decision for the future deployment executor.
        BC deployment-readiness separation package is not deployment readiness decision.
        Deployment readiness decision package is not deployment execution.
        CanProceedToControlledDeploymentExecutor is not deployment execution.
        BD does not deploy.
        BD does not publish packages.
        BD does not promote memory.
        BD does not continue workflow.
        BD does not mutate environments.
        BD does not mutate source.
        BD does not execute rollback.
        """;
}

public sealed record DeploymentReadinessDecisionPackageReceipt
{
    public required string DeploymentReadinessDecisionPackageReceiptId { get; init; }
    public required string DeploymentReadinessDecisionPackageId { get; init; }
    public required DeploymentReadinessDecisionPackageVerdict Verdict { get; init; }
    public required bool CanProceedToControlledDeploymentExecutor { get; init; }
    public DeploymentReadinessDecisionPackageBlockReason[] BlockReasons { get; init; } = [];
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DeploymentReadinessDecisionPackageBoundary Boundary { get; init; } =
        DeploymentReadinessDecisionPackageBoundary.Evidence;
}

public sealed record DeploymentReadinessDecisionPackageArtifacts
{
    public required DeploymentReadinessDecisionPackage Package { get; init; }
    public required DeploymentReadinessDecisionPackageReceipt Receipt { get; init; }
}

public static class DeploymentReadinessDecisionPackageBuilder
{
    public static DeploymentReadinessDecisionPackageArtifacts Build(DeploymentReadinessDecisionPackageInput input)
    {
        var now = input.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var incomplete = new List<DeploymentReadinessDecisionPackageBlockReason>();
        var blocked = new List<DeploymentReadinessDecisionPackageBlockReason>();
        var rejected = new List<DeploymentReadinessDecisionPackageBlockReason>();
        var issues = new List<string>();

        ValidateSeparationPackage(input, incomplete, blocked, rejected, issues);
        ValidateDeploymentEnvironment(input, incomplete, issues);
        ValidateArtifact(input, incomplete, blocked, issues);
        ValidateDecision(input, incomplete, blocked, rejected, issues);
        ValidateBoundary(blocked, issues);

        var blockReasons = blocked.Concat(rejected).Concat(incomplete).Distinct().ToArray();
        var verdict = DetermineVerdict(blocked, rejected, incomplete);
        var canProceed = verdict == DeploymentReadinessDecisionPackageVerdict.PackageReadyForControlledDeploymentExecutor;
        var source = input.DeploymentReadinessSeparationPackage;
        var decision = input.DeploymentReadinessDecision;
        var packageId = $"deployment_readiness_decision_pkg_{BdDeploymentReadinessDecisionHashing.ShortHash($"{source?.DeploymentReadinessSeparationPackageId}|{input.Repository}|{input.CandidateCommitSha}|{input.CandidateVersion}|{input.CandidateTagName}|{input.ReleaseChannel}|{input.DeploymentTarget}|{input.DeploymentEnvironment}|{input.DeploymentArtifactName}|{input.DeploymentArtifactSha256}|{decision?.DeploymentReadinessDecisionId}|{verdict}")}";
        var package = new DeploymentReadinessDecisionPackage
        {
            DeploymentReadinessDecisionPackageId = packageId,
            SourceDeploymentReadinessSeparationPackageId = FeedbackText.Safe(source?.DeploymentReadinessSeparationPackageId ?? "missing-deployment-readiness-separation-package"),
            Repository = FeedbackText.Safe(input.Repository),
            CandidateCommitSha = FeedbackText.Safe(input.CandidateCommitSha),
            CandidateVersion = FeedbackText.Safe(input.CandidateVersion),
            CandidateTagName = FeedbackText.Safe(input.CandidateTagName),
            ReleaseChannel = FeedbackText.Safe(input.ReleaseChannel),
            DeploymentTarget = FeedbackText.Safe(input.DeploymentTarget),
            DeploymentEnvironment = FeedbackText.Safe(input.DeploymentEnvironment),
            DeploymentArtifactName = FeedbackText.Safe(input.DeploymentArtifactName),
            DeploymentArtifactSha256 = FeedbackText.Safe(input.DeploymentArtifactSha256),
            Decision = decision?.Decision ?? DeploymentReadinessDecision.NeedsMoreEvidence,
            DecisionMadeBy = FeedbackText.Safe(decision?.DecisionMadeBy ?? string.Empty),
            DecisionMadeAtUtc = decision?.DecisionMadeAtUtc ?? DateTimeOffset.MinValue,
            DecisionRationale = FeedbackText.Safe(decision?.DecisionRationale ?? string.Empty),
            PackageVerdict = verdict,
            CanProceedToControlledDeploymentExecutor = canProceed,
            PackageIssues = FeedbackText.SafeList(issues),
            BlockReasons = blockReasons,
            CreatedBy = FeedbackText.Safe(input.CreatedBy),
            CreatedAtUtc = now,
            Boundary = DeploymentReadinessDecisionPackageBoundary.Evidence
        };
        var receipt = new DeploymentReadinessDecisionPackageReceipt
        {
            DeploymentReadinessDecisionPackageReceiptId = $"deployment_readiness_decision_receipt_{BdDeploymentReadinessDecisionHashing.ShortHash($"{packageId}|{verdict}|{now:O}")}",
            DeploymentReadinessDecisionPackageId = packageId,
            Verdict = verdict,
            CanProceedToControlledDeploymentExecutor = canProceed,
            BlockReasons = blockReasons,
            BoundaryStatements =
            [
                "BD consumes BC deployment-readiness separation evidence.",
                "BC separation package is not deployment readiness decision.",
                "Deployment readiness decision package is not deployment execution.",
                "BD does not deploy.",
                "BD does not publish packages.",
                "BD does not promote memory.",
                "BD does not continue workflow.",
                "BD does not mutate environments.",
                "BD does not mutate source.",
                "BD does not execute rollback.",
                "CanProceedToControlledDeploymentExecutor is not deployment execution."
            ],
            CreatedAtUtc = now,
            Boundary = DeploymentReadinessDecisionPackageBoundary.Evidence
        };

        return new DeploymentReadinessDecisionPackageArtifacts
        {
            Package = package,
            Receipt = receipt
        };
    }

    private static void ValidateSeparationPackage(
        DeploymentReadinessDecisionPackageInput input,
        List<DeploymentReadinessDecisionPackageBlockReason> incomplete,
        List<DeploymentReadinessDecisionPackageBlockReason> blocked,
        List<DeploymentReadinessDecisionPackageBlockReason> rejected,
        List<string> issues)
    {
        var package = input.DeploymentReadinessSeparationPackage;
        if (package is null)
        {
            incomplete.Add(DeploymentReadinessDecisionPackageBlockReason.MissingDeploymentReadinessSeparationPackage);
            issues.Add("MissingDeploymentReadinessSeparationPackage");
            return;
        }

        if (package.PackageVerdict == DeploymentReadinessSeparationVerdict.PackageRejected)
        {
            rejected.Add(DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessSeparationPackageRejected);
            issues.Add("DeploymentReadinessSeparationPackageRejected");
        }
        else if (package.PackageVerdict == DeploymentReadinessSeparationVerdict.PackageBlocked)
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessSeparationPackageBlocked);
            issues.Add("DeploymentReadinessSeparationPackageBlocked");
        }
        else if (package.PackageVerdict != DeploymentReadinessSeparationVerdict.PackageReadyForDeploymentReadinessDecision ||
                 !package.CanProceedToDeploymentReadinessDecision)
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessSeparationPackageNotReady);
            issues.Add($"DeploymentReadinessSeparationPackageNotReady:{package.PackageVerdict}");
        }

        if (!package.Boundary.EvidenceOnly || SeparationBoundaryCarriesAuthority(package.Boundary))
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessSeparationBoundaryAuthorityViolation);
            issues.Add("DeploymentReadinessSeparationBoundaryAuthorityViolation");
        }

        if (!Same(package.Repository, input.Repository))
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.RepositoryMismatch);
            issues.Add("RepositoryMismatch");
        }

        if (!Same(package.CandidateCommitSha, input.CandidateCommitSha))
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.CandidateCommitMismatch);
            issues.Add("CandidateCommitMismatch");
        }

        if (!Same(package.CandidateVersion, input.CandidateVersion))
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.CandidateVersionMismatch);
            issues.Add("CandidateVersionMismatch");
        }

        if (!Same(package.CandidateTagName, input.CandidateTagName))
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.CandidateTagMismatch);
            issues.Add("CandidateTagMismatch");
        }

        if (!Same(package.ReleaseChannel, input.ReleaseChannel))
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.ReleaseChannelMismatch);
            issues.Add("ReleaseChannelMismatch");
        }

        if (!Same(package.DeploymentTarget, input.DeploymentTarget))
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.DeploymentTargetMismatch);
            issues.Add("DeploymentTargetMismatch");
        }
    }

    private static bool SeparationBoundaryCarriesAuthority(DeploymentReadinessSeparationBoundary boundary) =>
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

    private static void ValidateDeploymentEnvironment(
        DeploymentReadinessDecisionPackageInput input,
        List<DeploymentReadinessDecisionPackageBlockReason> incomplete,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(input.DeploymentEnvironment))
        {
            incomplete.Add(DeploymentReadinessDecisionPackageBlockReason.MissingDeploymentEnvironment);
            issues.Add("MissingDeploymentEnvironment");
        }
    }

    private static void ValidateArtifact(
        DeploymentReadinessDecisionPackageInput input,
        List<DeploymentReadinessDecisionPackageBlockReason> incomplete,
        List<DeploymentReadinessDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(input.DeploymentArtifactName))
        {
            incomplete.Add(DeploymentReadinessDecisionPackageBlockReason.MissingDeploymentArtifactName);
            issues.Add("MissingDeploymentArtifactName");
        }

        if (string.IsNullOrWhiteSpace(input.DeploymentArtifactSha256))
        {
            incomplete.Add(DeploymentReadinessDecisionPackageBlockReason.MissingDeploymentArtifactChecksum);
            issues.Add("MissingDeploymentArtifactChecksum");
            return;
        }

        if (!IsSha256(input.DeploymentArtifactSha256))
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.InvalidDeploymentArtifactChecksum);
            issues.Add("InvalidDeploymentArtifactChecksum");
        }
    }

    private static void ValidateDecision(
        DeploymentReadinessDecisionPackageInput input,
        List<DeploymentReadinessDecisionPackageBlockReason> incomplete,
        List<DeploymentReadinessDecisionPackageBlockReason> blocked,
        List<DeploymentReadinessDecisionPackageBlockReason> rejected,
        List<string> issues)
    {
        var decision = input.DeploymentReadinessDecision;
        if (decision is null)
        {
            incomplete.Add(DeploymentReadinessDecisionPackageBlockReason.MissingDeploymentReadinessDecision);
            issues.Add("MissingDeploymentReadinessDecision");
            return;
        }

        if (decision.Decision == DeploymentReadinessDecision.Rejected)
        {
            rejected.Add(DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessDecisionRejected);
            issues.Add("DeploymentReadinessDecisionRejected");
        }
        else if (decision.Decision == DeploymentReadinessDecision.NeedsMoreEvidence)
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessDecisionNeedsMoreEvidence);
            issues.Add("DeploymentReadinessDecisionNeedsMoreEvidence");
        }
        else if (decision.Decision != DeploymentReadinessDecision.ApprovedForControlledDeploymentExecutor)
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessDecisionNeedsMoreEvidence);
            issues.Add($"UnsupportedDeploymentReadinessDecision:{decision.Decision}");
        }

        if (string.IsNullOrWhiteSpace(decision.DecisionMadeBy))
        {
            incomplete.Add(DeploymentReadinessDecisionPackageBlockReason.MissingDecisionMaker);
            issues.Add("MissingDecisionMaker");
        }

        if (decision.DecisionMadeAtUtc == default)
        {
            incomplete.Add(DeploymentReadinessDecisionPackageBlockReason.MissingDecisionTime);
            issues.Add("MissingDecisionTime");
        }

        if (string.IsNullOrWhiteSpace(decision.DecisionRationale))
        {
            incomplete.Add(DeploymentReadinessDecisionPackageBlockReason.MissingDecisionRationale);
            issues.Add("MissingDecisionRationale");
        }

        if (input.DeploymentReadinessSeparationPackage is not null)
        {
            if (decision.DecisionMadeAtUtc < input.DeploymentReadinessSeparationPackage.CreatedAtUtc)
            {
                blocked.Add(DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessDecisionStale);
                issues.Add("DeploymentReadinessDecisionPredatesSeparationPackage");
            }

            if (Same(decision.DecisionMadeBy, input.DeploymentReadinessSeparationPackage.CreatedBy))
            {
                blocked.Add(DeploymentReadinessDecisionPackageBlockReason.DecisionMakerNotAllowed);
                issues.Add("DecisionMakerNotAllowed");
            }
        }

        if (input.DeploymentReadinessSeparationPackage is not null &&
            !Same(decision.ExpectedDeploymentReadinessSeparationPackageId, input.DeploymentReadinessSeparationPackage.DeploymentReadinessSeparationPackageId))
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessDecisionStale);
            issues.Add("DeploymentReadinessDecisionStale");
        }

        if (!Same(decision.ExpectedRepository, input.Repository) ||
            !Same(decision.ExpectedCandidateCommitSha, input.CandidateCommitSha) ||
            !Same(decision.ExpectedVersion, input.CandidateVersion) ||
            !Same(decision.ExpectedTagName, input.CandidateTagName) ||
            !Same(decision.ExpectedReleaseChannel, input.ReleaseChannel) ||
            !Same(decision.ExpectedDeploymentTarget, input.DeploymentTarget) ||
            !Same(decision.ExpectedDeploymentEnvironment, input.DeploymentEnvironment) ||
            !Same(decision.ExpectedDeploymentArtifactName, input.DeploymentArtifactName) ||
            !Same(decision.ExpectedDeploymentArtifactSha256, input.DeploymentArtifactSha256))
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.DeploymentReadinessDecisionStale);
            issues.Add("DeploymentReadinessDecisionStale");
        }
    }

    private static void ValidateBoundary(
        List<DeploymentReadinessDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var boundary = DeploymentReadinessDecisionPackageBoundary.Evidence;
        if (!boundary.EvidenceOnly || BoundaryCarriesAuthority(boundary))
        {
            blocked.Add(DeploymentReadinessDecisionPackageBlockReason.BoundaryViolation);
            issues.Add("BoundaryViolation");
        }
    }

    private static bool BoundaryCarriesAuthority(DeploymentReadinessDecisionPackageBoundary boundary) =>
        boundary.CanDeploy ||
        boundary.CanPublishPackages ||
        boundary.CanPromoteMemory ||
        boundary.CanContinueWorkflow ||
        boundary.CanMutateEnvironment ||
        boundary.CanMutateSource ||
        boundary.CanCommit ||
        boundary.CanPush ||
        boundary.CanExecuteRollback ||
        boundary.CanDispatchPipeline;

    private static DeploymentReadinessDecisionPackageVerdict DetermineVerdict(
        IReadOnlyCollection<DeploymentReadinessDecisionPackageBlockReason> blocked,
        IReadOnlyCollection<DeploymentReadinessDecisionPackageBlockReason> rejected,
        IReadOnlyCollection<DeploymentReadinessDecisionPackageBlockReason> incomplete)
    {
        if (rejected.Count > 0)
            return DeploymentReadinessDecisionPackageVerdict.PackageRejected;
        if (blocked.Count > 0)
            return DeploymentReadinessDecisionPackageVerdict.PackageBlocked;
        return incomplete.Count > 0
            ? DeploymentReadinessDecisionPackageVerdict.PackageIncomplete
            : DeploymentReadinessDecisionPackageVerdict.PackageReadyForControlledDeploymentExecutor;
    }

    private static bool IsSha256(string value) =>
        value.Trim().Length == 64 && value.Trim().All(Uri.IsHexDigit);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class DeploymentReadinessDecisionPackageBypassEvaluator
{
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanPublishPackages(object? evidence) => false;
    public static bool CanPromoteMemory(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
    public static bool CanMutateEnvironment(object? evidence) => false;
    public static bool CanMutateSource(object? evidence) => false;
    public static bool CanCommit(object? evidence) => false;
    public static bool CanPush(object? evidence) => false;
    public static bool CanExecuteRollback(object? evidence) => false;
    public static bool CanDispatchPipeline(object? evidence) => false;
}

internal static class BdDeploymentReadinessDecisionHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
