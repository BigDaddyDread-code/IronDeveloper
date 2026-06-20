using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Validation;

namespace IronDev.Core.Governance;

public enum ReleaseReadinessDecisionPackageVerdict
{
    PackageReadyForReleaseExecutor = 0,
    PackageIncomplete,
    PackageBlocked,
    PackageRejected
}

public enum ReleaseReadinessDecisionPackageBlockReason
{
    MissingReleaseCandidatePackage = 0,
    ReleaseCandidatePackageNotReady,
    ReleaseCandidatePackageBlocked,
    ReleaseCandidatePackageRejected,
    ReleaseCandidatePackageStale,
    ReleaseCandidateBoundaryAuthorityViolation,

    RepositoryMismatch,
    CandidateCommitMismatch,
    CandidateVersionMismatch,
    CandidateTagMismatch,
    ReleaseSourceBranchMismatch,
    ReleaseChannelMismatch,

    MissingCurrentReleaseSourceState,
    ReleaseSourceObservationFailed,
    ReleaseSourceCommitMismatch,
    ReleaseSourceCommitNotPresent,
    ReleaseSourceStateStale,

    MissingCurrentTagReleaseState,
    TagReleaseObservationFailed,
    CandidateTagAlreadyExists,
    CandidateReleaseAlreadyExists,
    TagReleaseStateStale,

    MissingFinalReleaseValidationEvidence,
    FinalReleaseValidationEvidenceStale,
    FinalReleaseValidationFailed,
    RequiredFinalReleaseValidationMissing,
    NotApplicableValidationReasonMissing,

    MissingArtifactReadinessEvidence,
    ArtifactManifestCommitMismatch,
    ArtifactManifestMissingArtifacts,
    ArtifactChecksumMissing,
    ArtifactStorageLocationMissing,
    ArtifactNotApplicableReasonMissing,

    MissingReleaseReadinessDecision,
    ReleaseReadinessDecisionBlocked,
    ReleaseReadinessDecisionRejected,
    ReleaseReadinessDecisionStale,
    MissingDecisionMaker,
    MissingDecisionRationale,
    DecisionMakerNotAllowed,

    MissingReleaseChannel,
    UnsupportedReleaseChannel,

    ReleaseMutationNotAllowed,
    TagMutationNotAllowed,
    PublishNotAllowed,
    DeployNotAllowed,
    MemoryPromotionNotAllowed,
    WorkflowContinuationNotAllowed,
    CommitPushNotAllowed,
    SourceMutationNotAllowed,
    BoundaryViolation
}

public enum ReleaseReadinessDecision
{
    ApprovedForReleaseExecutor = 0,
    Blocked,
    Rejected
}

public sealed record ReleaseReadinessDecisionPackageBoundary
{
    public bool EvidenceOnly { get; init; } = true;

    public bool CanMarkReadyForReview { get; init; }
    public bool CanRequestReviewers { get; init; }
    public bool CanRemoveReviewers { get; init; }
    public bool CanResolveReviewThreads { get; init; }
    public bool CanReplyToReviewThreads { get; init; }
    public bool CanApprove { get; init; }
    public bool CanSubmitReview { get; init; }
    public bool CanMerge { get; init; }
    public bool CanAutoMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanTag { get; init; }
    public bool CanPublish { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }

    public static ReleaseReadinessDecisionPackageBoundary Evidence { get; } = new();
}

public static class ReleaseReadinessDecisionPackageBoundaryText
{
    public const string Boundary = """
        Block BA consumes an eligible AZ release candidate package and packages a release readiness decision.
        It does not create a tag.
        It does not create a GitHub release.
        It does not upload artifacts.
        It does not publish artifacts or packages.
        It does not deploy.
        It does not promote memory.
        It does not commit.
        It does not push.
        It does not mutate source.
        It does not continue workflow.
        Release candidate package is not release readiness decision.
        Release readiness decision package is not release execution.
        Release execution is not deployment.
        Release is not deployment.
        Validation evidence is not release authority.
        Release notes are not release authority.
        Version selection is not tag creation.
        Artifact readiness is not publication.
        No hidden tag creation.
        No hidden release creation.
        No hidden publication.
        No hidden deployment.
        No hidden memory promotion.
        No hidden workflow continuation.
        """;
}

public sealed record CurrentReleaseSourceState
{
    public required string Repository { get; init; }
    public required string ReleaseSourceBranch { get; init; }
    public required string ReleaseSourceHeadSha { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string DefaultBranch { get; init; }
    public required string DefaultBranchHeadSha { get; init; }
    public required bool CommitPresentOnReleaseSource { get; init; }
    public required bool CommitPresentOnDefaultBranch { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string ObservationSource { get; init; }
    public required bool ObservationSucceeded { get; init; }
    public string? ObservationError { get; init; }
}

public sealed record CurrentTagReleaseState
{
    public required string Repository { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public bool ExistingTagFound { get; init; }
    public string? ExistingTagSha { get; init; }
    public bool ExistingReleaseFound { get; init; }
    public string? ExistingReleaseId { get; init; }
    public string? ExistingReleaseUrl { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string ObservationSource { get; init; }
    public required bool ObservationSucceeded { get; init; }
    public string? ObservationError { get; init; }
}

public sealed record FinalReleaseValidationEvidence
{
    public required string ValidationRunId { get; init; }
    public required string ValidationPlanId { get; init; }
    public required string CommitSha { get; init; }
    public required ValidationRunVerdict Verdict { get; init; }
    public string[] RequiredLaneNames { get; init; } = [];
    public string[] ResultLaneNames { get; init; } = [];
    public string[] MissingLaneNames { get; init; } = [];
    public string[] FailedLaneNames { get; init; } = [];
    public string[] NotApplicableLaneNames { get; init; } = [];
    public string[] NotApplicableLaneReasons { get; init; } = [];
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; init; }
    public string? ValidationEvidenceReceiptId { get; init; }
}

public sealed record ReleaseArtifactReadinessEvidence
{
    public string? ArtifactManifestId { get; init; }
    public string? BuildRunId { get; init; }
    public required string CommitSha { get; init; }
    public string[] Artifacts { get; init; } = [];
    public string[] Checksums { get; init; } = [];
    public string? StorageLocation { get; init; }
    public string? ArtifactPolicy { get; init; }
    public bool ArtifactsRequired { get; init; }
    public bool ArtifactsReady { get; init; }
    public string? NotApplicableReason { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record ReleaseReadinessDecisionEvidence
{
    public required string ReleaseReadinessDecisionId { get; init; }
    public required ReleaseReadinessDecision Decision { get; init; }
    public required string DecisionMadeBy { get; init; }
    public required DateTimeOffset DecisionMadeAtUtc { get; init; }
    public required string DecisionRationale { get; init; }
    public required string ExpectedRepository { get; init; }
    public required string ExpectedCandidateCommitSha { get; init; }
    public required string ExpectedVersion { get; init; }
    public required string ExpectedTagName { get; init; }
    public required string ExpectedReleaseSourceBranch { get; init; }
    public required string ExpectedReleaseChannel { get; init; }
    public string? ExpectedArtifactManifestId { get; init; }
    public required string ExpectedReleaseCandidatePackageId { get; init; }
}

public sealed record ReleaseReadinessDecisionPackageInput
{
    public ReleaseCandidatePackage? ReleaseCandidatePackage { get; init; }
    public CurrentReleaseSourceState? CurrentReleaseSourceState { get; init; }
    public CurrentTagReleaseState? CurrentTagReleaseState { get; init; }
    public FinalReleaseValidationEvidence? FinalReleaseValidationEvidence { get; init; }
    public ReleaseArtifactReadinessEvidence? ReleaseArtifactReadinessEvidence { get; init; }
    public ReleaseReadinessDecisionEvidence? ReleaseReadinessDecision { get; init; }
    public required string Repository { get; init; }
    public required string ReleaseSourceBranch { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }
    public required string CreatedBy { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record ReleaseReadinessDecisionPackage
{
    public required string ReleaseReadinessDecisionPackageId { get; init; }

    public required string Repository { get; init; }
    public required string ReleaseSourceBranch { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public required string SourceReleaseCandidatePackageId { get; init; }
    public required string SourceMergeExecutionReceiptId { get; init; }
    public required string SourceMergeDecisionPackageId { get; init; }

    public CurrentReleaseSourceState? CurrentReleaseSourceState { get; init; }
    public CurrentTagReleaseState? CurrentTagReleaseState { get; init; }
    public FinalReleaseValidationEvidence? FinalReleaseValidationEvidence { get; init; }
    public ReleaseArtifactReadinessEvidence? ReleaseArtifactReadinessEvidence { get; init; }
    public ReleaseReadinessDecisionEvidence? ReleaseReadinessDecision { get; init; }

    public required ReleaseReadinessDecisionPackageVerdict PackageVerdict { get; init; }
    public required bool CanReleaseForExecutor { get; init; }
    public ReleaseReadinessDecisionPackageBlockReason[] BlockReasons { get; init; } = [];
    public string[] PackageIssues { get; init; } = [];

    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ReleaseReadinessDecisionPackageBoundary Boundary { get; init; } = ReleaseReadinessDecisionPackageBoundary.Evidence;
}

public sealed record ReleaseReadinessDecisionPackageReceipt
{
    public required string ReleaseReadinessDecisionPackageReceiptId { get; init; }
    public required string ReleaseReadinessDecisionPackageId { get; init; }
    public required ReleaseReadinessDecisionPackageVerdict Verdict { get; init; }
    public required bool CanReleaseForExecutor { get; init; }
    public ReleaseReadinessDecisionPackageBlockReason[] BlockReasons { get; init; } = [];
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ReleaseReadinessDecisionPackageBoundary Boundary { get; init; } = ReleaseReadinessDecisionPackageBoundary.Evidence;
}

public sealed record ReleaseReadinessDecisionPackageArtifacts
{
    public required ReleaseReadinessDecisionPackage Package { get; init; }
    public required ReleaseReadinessDecisionPackageReceipt Receipt { get; init; }
}

public static class ReleaseReadinessDecisionPackageBuilder
{
    public static readonly string[] RequiredValidationFamilies =
    [
        "Build",
        "DiffCheck",
        "StablePhase",
        "ReleaseCandidateAuthority",
        "ReleaseReadinessAuthority",
        "Packaging",
        "Regression"
    ];

    private static readonly string[] AllowedChannels =
    [
        "Internal",
        "Preview",
        "ReleaseCandidate",
        "Stable",
        "Hotfix"
    ];

    public static ReleaseReadinessDecisionPackageArtifacts Build(ReleaseReadinessDecisionPackageInput input)
    {
        var now = input.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var incomplete = new List<ReleaseReadinessDecisionPackageBlockReason>();
        var blocked = new List<ReleaseReadinessDecisionPackageBlockReason>();
        var rejected = new List<ReleaseReadinessDecisionPackageBlockReason>();
        var issues = new List<string>();

        ValidateReleaseCandidatePackage(input, incomplete, blocked, rejected, issues);
        ValidateCurrentReleaseSource(input, incomplete, blocked, issues);
        ValidateCurrentTagRelease(input, incomplete, blocked, issues);
        ValidateFinalValidation(input, incomplete, blocked, issues);
        ValidateArtifactReadiness(input, incomplete, blocked, issues);
        ValidateDecision(input, incomplete, blocked, rejected, issues);
        var normalizedChannel = ValidateReleaseChannel(input.ReleaseChannel, incomplete, blocked, issues);
        ValidateBoundary(blocked, issues);

        var blockReasons = blocked.Concat(rejected).Concat(incomplete).Distinct().ToArray();
        var verdict = DetermineVerdict(blocked, rejected, incomplete);
        var canReleaseForExecutor = verdict == ReleaseReadinessDecisionPackageVerdict.PackageReadyForReleaseExecutor;
        var source = input.ReleaseCandidatePackage;
        var packageId = $"release_readiness_pkg_{ReleaseReadinessDecisionPackageHashing.ShortHash($"{input.Repository}|{input.CandidateCommitSha}|{input.CandidateVersion}|{input.CandidateTagName}|{normalizedChannel}|{verdict}|{input.ReleaseReadinessDecision?.ReleaseReadinessDecisionId}")}";
        var package = new ReleaseReadinessDecisionPackage
        {
            ReleaseReadinessDecisionPackageId = packageId,
            Repository = FeedbackText.Safe(input.Repository),
            ReleaseSourceBranch = FeedbackText.Safe(input.ReleaseSourceBranch),
            CandidateCommitSha = FeedbackText.Safe(input.CandidateCommitSha),
            CandidateVersion = FeedbackText.Safe(input.CandidateVersion),
            CandidateTagName = FeedbackText.Safe(input.CandidateTagName),
            ReleaseChannel = FeedbackText.Safe(normalizedChannel ?? input.ReleaseChannel),
            SourceReleaseCandidatePackageId = FeedbackText.Safe(source?.ReleaseCandidatePackageId ?? "missing-release-candidate-package"),
            SourceMergeExecutionReceiptId = FeedbackText.Safe(source?.SourceMergeExecutionReceiptId ?? "missing-merge-execution-receipt"),
            SourceMergeDecisionPackageId = FeedbackText.Safe(source?.SourceMergeDecisionPackageId ?? "missing-merge-decision-package"),
            CurrentReleaseSourceState = input.CurrentReleaseSourceState,
            CurrentTagReleaseState = input.CurrentTagReleaseState,
            FinalReleaseValidationEvidence = input.FinalReleaseValidationEvidence,
            ReleaseArtifactReadinessEvidence = input.ReleaseArtifactReadinessEvidence,
            ReleaseReadinessDecision = input.ReleaseReadinessDecision,
            PackageVerdict = verdict,
            CanReleaseForExecutor = canReleaseForExecutor,
            BlockReasons = blockReasons,
            PackageIssues = FeedbackText.SafeList(issues),
            CreatedBy = FeedbackText.Safe(input.CreatedBy),
            CreatedAtUtc = now,
            Boundary = ReleaseReadinessDecisionPackageBoundary.Evidence
        };
        var receipt = new ReleaseReadinessDecisionPackageReceipt
        {
            ReleaseReadinessDecisionPackageReceiptId = $"release_readiness_receipt_{ReleaseReadinessDecisionPackageHashing.ShortHash($"{packageId}|{verdict}|{now:O}")}",
            ReleaseReadinessDecisionPackageId = packageId,
            Verdict = verdict,
            CanReleaseForExecutor = canReleaseForExecutor,
            BlockReasons = blockReasons,
            BoundaryStatements =
            [
                "Release candidate package is not release readiness decision.",
                "Release readiness decision package is not release execution.",
                "Release execution is not deployment.",
                "Release is not deployment.",
                "Validation evidence is not release authority.",
                "Release notes are not release authority.",
                "Version selection is not tag creation.",
                "Artifact readiness is not publication.",
                "No hidden tag creation.",
                "No hidden release creation.",
                "No hidden publication.",
                "No hidden deployment.",
                "No hidden memory promotion.",
                "No hidden workflow continuation."
            ],
            CreatedAtUtc = now,
            Boundary = ReleaseReadinessDecisionPackageBoundary.Evidence
        };

        return new ReleaseReadinessDecisionPackageArtifacts
        {
            Package = package,
            Receipt = receipt
        };
    }

    public static FinalReleaseValidationEvidence FromValidationReceipt(ValidationRunReceipt receipt) => new()
    {
        ValidationRunId = receipt.ValidationRunId,
        ValidationPlanId = receipt.ValidationPlanId,
        CommitSha = receipt.CommitSha,
        Verdict = receipt.Verdict,
        RequiredLaneNames = receipt.RequiredLanes.Select(lane => lane.Name).ToArray(),
        ResultLaneNames = receipt.Results.Select(result => result.LaneName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        MissingLaneNames = receipt.RequiredLanes
            .Select(lane => lane.Name)
            .Except(receipt.Results.Select(result => result.LaneName), StringComparer.OrdinalIgnoreCase)
            .Concat(receipt.SkippedLanes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray(),
        FailedLaneNames = receipt.Results
            .Where(result => result.ExitCode != 0 || result.FailureClassification != ValidationFailureKind.Passed)
            .Select(result => result.LaneName)
            .ToArray(),
        StartedAtUtc = receipt.StartedUtc,
        FinishedAtUtc = receipt.FinishedUtc,
        ValidationEvidenceReceiptId = receipt.ValidationRunId
    };

    private static void ValidateReleaseCandidatePackage(
        ReleaseReadinessDecisionPackageInput input,
        List<ReleaseReadinessDecisionPackageBlockReason> incomplete,
        List<ReleaseReadinessDecisionPackageBlockReason> blocked,
        List<ReleaseReadinessDecisionPackageBlockReason> rejected,
        List<string> issues)
    {
        var package = input.ReleaseCandidatePackage;
        if (package is null)
        {
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.MissingReleaseCandidatePackage);
            issues.Add("MissingReleaseCandidatePackage");
            return;
        }

        if (package.PackageVerdict == ReleaseCandidatePackageVerdict.PackageIncomplete || !package.CanReleaseForExecutor)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidatePackageNotReady);
            issues.Add($"ReleaseCandidatePackageNotReady:{package.PackageVerdict}");
        }

        if (package.PackageVerdict == ReleaseCandidatePackageVerdict.PackageBlocked || package.BlockReasons.Length > 0)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidatePackageBlocked);
            issues.Add("ReleaseCandidatePackageBlocked");
        }

        if (package.PackageVerdict == ReleaseCandidatePackageVerdict.PackageRejected)
        {
            rejected.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidatePackageRejected);
            issues.Add("ReleaseCandidatePackageRejected");
        }

        if (!package.Boundary.EvidenceOnly || HasAuthority(package.Boundary))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidateBoundaryAuthorityViolation);
            issues.Add("ReleaseCandidateBoundaryAuthorityViolation");
        }

        if (!Same(package.Repository, input.Repository))
            AddMismatch(blocked, issues, ReleaseReadinessDecisionPackageBlockReason.RepositoryMismatch, "RepositoryMismatch");
        if (!Same(package.CandidateCommitSha, input.CandidateCommitSha))
            AddMismatch(blocked, issues, ReleaseReadinessDecisionPackageBlockReason.CandidateCommitMismatch, "CandidateCommitMismatch");
        if (!Same(package.CandidateVersion, input.CandidateVersion))
            AddMismatch(blocked, issues, ReleaseReadinessDecisionPackageBlockReason.CandidateVersionMismatch, "CandidateVersionMismatch");
        if (!Same(package.CandidateTagName, input.CandidateTagName))
            AddMismatch(blocked, issues, ReleaseReadinessDecisionPackageBlockReason.CandidateTagMismatch, "CandidateTagMismatch");
        if (!Same(package.ReleaseSourceBranch, input.ReleaseSourceBranch))
            AddMismatch(blocked, issues, ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceBranchMismatch, "ReleaseSourceBranchMismatch");
        if (!Same(package.ReleaseChannel, input.ReleaseChannel))
            AddMismatch(blocked, issues, ReleaseReadinessDecisionPackageBlockReason.ReleaseChannelMismatch, "ReleaseChannelMismatch");

        if (string.IsNullOrWhiteSpace(package.CandidateCommitSha))
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.CandidateCommitMismatch);
        if (string.IsNullOrWhiteSpace(package.CandidateVersion))
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.CandidateVersionMismatch);
        if (string.IsNullOrWhiteSpace(package.CandidateTagName))
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.CandidateTagMismatch);
        if (package.ReleaseValidationEvidence is null || package.ReleaseNotesEvidence is null || package.ReleaseCandidateDecision is null)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidatePackageNotReady);
            issues.Add("ReleaseCandidatePackageMissingRequiredEvidence");
        }
        else if (package.ReleaseCandidateDecision.Decision != ReleaseCandidateDecision.ApprovedForReleaseExecutor)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseCandidatePackageNotReady);
            issues.Add("ReleaseCandidatePackageDecisionNotApproved");
        }
    }

    private static void ValidateCurrentReleaseSource(
        ReleaseReadinessDecisionPackageInput input,
        List<ReleaseReadinessDecisionPackageBlockReason> incomplete,
        List<ReleaseReadinessDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var state = input.CurrentReleaseSourceState;
        if (state is null)
        {
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.MissingCurrentReleaseSourceState);
            issues.Add("MissingCurrentReleaseSourceState");
            return;
        }

        if (!state.ObservationSucceeded)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceObservationFailed);
            issues.Add($"ReleaseSourceObservationFailed:{state.ObservationError ?? "observation failed"}");
        }

        if (!Same(state.Repository, input.Repository) ||
            !Same(state.CandidateCommitSha, input.CandidateCommitSha) ||
            !Same(state.ReleaseSourceHeadSha, input.CandidateCommitSha) ||
            (Same(state.ReleaseSourceBranch, state.DefaultBranch) && !Same(state.DefaultBranchHeadSha, input.CandidateCommitSha)))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceCommitMismatch);
            issues.Add("ReleaseSourceCommitMismatch");
        }

        if (!Same(state.ReleaseSourceBranch, input.ReleaseSourceBranch))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceBranchMismatch);
            issues.Add("ReleaseSourceBranchMismatch");
        }

        if (!state.CommitPresentOnReleaseSource || (Same(state.ReleaseSourceBranch, state.DefaultBranch) && !state.CommitPresentOnDefaultBranch))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceCommitNotPresent);
            issues.Add("ReleaseSourceCommitNotPresent");
        }

        if (input.ReleaseCandidatePackage is not null && state.ObservedAtUtc < input.ReleaseCandidatePackage.CreatedAtUtc)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseSourceStateStale);
            issues.Add("ReleaseSourceStateStale");
        }
    }

    private static void ValidateCurrentTagRelease(
        ReleaseReadinessDecisionPackageInput input,
        List<ReleaseReadinessDecisionPackageBlockReason> incomplete,
        List<ReleaseReadinessDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var state = input.CurrentTagReleaseState;
        if (state is null)
        {
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.MissingCurrentTagReleaseState);
            issues.Add("MissingCurrentTagReleaseState");
            return;
        }

        if (!state.ObservationSucceeded)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.TagReleaseObservationFailed);
            issues.Add($"TagReleaseObservationFailed:{state.ObservationError ?? "observation failed"}");
        }

        if (!Same(state.Repository, input.Repository) ||
            !Same(state.CandidateVersion, input.CandidateVersion) ||
            !Same(state.CandidateTagName, input.CandidateTagName))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.CandidateTagMismatch);
            issues.Add("TagReleaseIdentityMismatch");
        }

        if (state.ExistingTagFound)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.CandidateTagAlreadyExists);
            issues.Add("CandidateTagAlreadyExists");
        }

        if (state.ExistingReleaseFound)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.CandidateReleaseAlreadyExists);
            issues.Add("CandidateReleaseAlreadyExists");
        }

        if (input.ReleaseCandidatePackage is not null && state.ObservedAtUtc < input.ReleaseCandidatePackage.CreatedAtUtc)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.TagReleaseStateStale);
            issues.Add("TagReleaseStateStale");
        }
    }

    private static void ValidateFinalValidation(
        ReleaseReadinessDecisionPackageInput input,
        List<ReleaseReadinessDecisionPackageBlockReason> incomplete,
        List<ReleaseReadinessDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var evidence = input.FinalReleaseValidationEvidence;
        if (evidence is null)
        {
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.MissingFinalReleaseValidationEvidence);
            issues.Add("MissingFinalReleaseValidationEvidence");
            return;
        }

        if (!Same(evidence.CommitSha, input.CandidateCommitSha))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.FinalReleaseValidationEvidenceStale);
            issues.Add("FinalReleaseValidationEvidenceStale");
        }

        if (evidence.Verdict != ValidationRunVerdict.Passed || evidence.FailedLaneNames.Length > 0)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.FinalReleaseValidationFailed);
            issues.Add("FinalReleaseValidationFailed");
        }

        if (input.ReleaseCandidatePackage is not null &&
            evidence.FinishedAtUtc.HasValue &&
            evidence.FinishedAtUtc.Value < input.ReleaseCandidatePackage.CreatedAtUtc)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.FinalReleaseValidationEvidenceStale);
            issues.Add("FinalReleaseValidationEvidenceStale");
        }

        var executedFamilies = evidence.ResultLaneNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var notApplicable = GetReasonedNotApplicableLanes(evidence, blocked, issues);
        var missingRequired = RequiredValidationFamilies
            .Where(required => !executedFamilies.Any(name => ContainsToken(name, required)))
            .Where(required => !notApplicable.Any(name => ContainsToken(name, required)))
            .ToArray();
        if (missingRequired.Length > 0 || evidence.MissingLaneNames.Length > 0)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.RequiredFinalReleaseValidationMissing);
            foreach (var missing in missingRequired.Concat(evidence.MissingLaneNames).Distinct(StringComparer.OrdinalIgnoreCase))
                issues.Add($"RequiredFinalReleaseValidationMissing:{missing}");
        }
    }

    private static string[] GetReasonedNotApplicableLanes(
        FinalReleaseValidationEvidence evidence,
        List<ReleaseReadinessDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var reasonedLanes = new List<string>();
        for (var index = 0; index < evidence.NotApplicableLaneNames.Length; index++)
        {
            var laneName = evidence.NotApplicableLaneNames[index];
            if (string.IsNullOrWhiteSpace(laneName))
                continue;

            if (index >= evidence.NotApplicableLaneReasons.Length ||
                string.IsNullOrWhiteSpace(evidence.NotApplicableLaneReasons[index]))
            {
                blocked.Add(ReleaseReadinessDecisionPackageBlockReason.NotApplicableValidationReasonMissing);
                issues.Add($"NotApplicableValidationReasonMissing:{laneName}");
                continue;
            }

            reasonedLanes.Add(laneName);
        }

        return reasonedLanes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ValidateArtifactReadiness(
        ReleaseReadinessDecisionPackageInput input,
        List<ReleaseReadinessDecisionPackageBlockReason> incomplete,
        List<ReleaseReadinessDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var evidence = input.ReleaseArtifactReadinessEvidence;
        if (evidence is null)
        {
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.MissingArtifactReadinessEvidence);
            issues.Add("MissingArtifactReadinessEvidence");
            return;
        }

        if (!Same(evidence.CommitSha, input.CandidateCommitSha))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ArtifactManifestCommitMismatch);
            issues.Add("ArtifactManifestCommitMismatch");
        }

        if (evidence.ArtifactsRequired)
        {
            if (string.IsNullOrWhiteSpace(evidence.ArtifactManifestId) || !evidence.ArtifactsReady || evidence.Artifacts.Length == 0)
            {
                blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ArtifactManifestMissingArtifacts);
                issues.Add("ArtifactManifestMissingArtifacts");
            }

            if (evidence.Artifacts.Length > 0 && evidence.Checksums.Length == 0)
            {
                blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ArtifactChecksumMissing);
                issues.Add("ArtifactChecksumMissing");
            }

            if (string.IsNullOrWhiteSpace(evidence.StorageLocation))
            {
                blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ArtifactStorageLocationMissing);
                issues.Add("ArtifactStorageLocationMissing");
            }
        }
        else if (string.IsNullOrWhiteSpace(evidence.NotApplicableReason))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ArtifactNotApplicableReasonMissing);
            issues.Add("ArtifactNotApplicableReasonMissing");
        }
    }

    private static void ValidateDecision(
        ReleaseReadinessDecisionPackageInput input,
        List<ReleaseReadinessDecisionPackageBlockReason> incomplete,
        List<ReleaseReadinessDecisionPackageBlockReason> blocked,
        List<ReleaseReadinessDecisionPackageBlockReason> rejected,
        List<string> issues)
    {
        var decision = input.ReleaseReadinessDecision;
        if (decision is null)
        {
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.MissingReleaseReadinessDecision);
            issues.Add("MissingReleaseReadinessDecision");
            return;
        }

        if (decision.Decision == ReleaseReadinessDecision.Blocked)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionBlocked);
            issues.Add("ReleaseReadinessDecisionBlocked");
        }
        else if (decision.Decision == ReleaseReadinessDecision.Rejected)
        {
            rejected.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionRejected);
            issues.Add("ReleaseReadinessDecisionRejected");
        }

        if (string.IsNullOrWhiteSpace(decision.DecisionMadeBy))
        {
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.MissingDecisionMaker);
            issues.Add("MissingDecisionMaker");
        }

        if (string.IsNullOrWhiteSpace(decision.DecisionRationale))
        {
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.MissingDecisionRationale);
            issues.Add("MissingDecisionRationale");
        }

        if (input.ReleaseCandidatePackage is not null &&
            (Same(decision.DecisionMadeBy, input.ReleaseCandidatePackage.CreatedBy) ||
             Same(decision.DecisionMadeBy, input.ReleaseCandidatePackage.ReleaseCandidateDecision?.DecisionMadeBy)))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.DecisionMakerNotAllowed);
            issues.Add("DecisionMakerNotAllowed");
        }

        if (!Same(decision.ExpectedRepository, input.Repository) ||
            !Same(decision.ExpectedCandidateCommitSha, input.CandidateCommitSha) ||
            !Same(decision.ExpectedVersion, input.CandidateVersion) ||
            !Same(decision.ExpectedTagName, input.CandidateTagName) ||
            !Same(decision.ExpectedReleaseSourceBranch, input.ReleaseSourceBranch) ||
            !Same(decision.ExpectedReleaseChannel, input.ReleaseChannel) ||
            !Same(decision.ExpectedReleaseCandidatePackageId, input.ReleaseCandidatePackage?.ReleaseCandidatePackageId ?? string.Empty))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionStale);
            issues.Add("ReleaseReadinessDecisionStale");
        }

        var expectedManifest = input.ReleaseArtifactReadinessEvidence?.ArtifactManifestId;
        if (!string.IsNullOrWhiteSpace(expectedManifest) &&
            !Same(decision.ExpectedArtifactManifestId ?? string.Empty, expectedManifest))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionStale);
            issues.Add("ReleaseReadinessDecisionArtifactManifestStale");
        }

        if (DecisionPredatesCurrentEvidence(input, decision.DecisionMadeAtUtc))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.ReleaseReadinessDecisionStale);
            issues.Add("ReleaseReadinessDecisionPredatesCurrentEvidence");
        }
    }

    private static bool DecisionPredatesCurrentEvidence(
        ReleaseReadinessDecisionPackageInput input,
        DateTimeOffset decisionMadeAtUtc)
    {
        if (input.ReleaseCandidatePackage is not null &&
            decisionMadeAtUtc < input.ReleaseCandidatePackage.CreatedAtUtc)
            return true;

        if (input.CurrentReleaseSourceState is not null &&
            decisionMadeAtUtc < input.CurrentReleaseSourceState.ObservedAtUtc)
            return true;

        if (input.CurrentTagReleaseState is not null &&
            decisionMadeAtUtc < input.CurrentTagReleaseState.ObservedAtUtc)
            return true;

        if (input.FinalReleaseValidationEvidence?.FinishedAtUtc is DateTimeOffset validationFinished &&
            decisionMadeAtUtc < validationFinished)
            return true;

        return input.ReleaseArtifactReadinessEvidence is not null &&
            decisionMadeAtUtc < input.ReleaseArtifactReadinessEvidence.CreatedAtUtc;
    }

    private static string? ValidateReleaseChannel(
        string channel,
        List<ReleaseReadinessDecisionPackageBlockReason> incomplete,
        List<ReleaseReadinessDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            incomplete.Add(ReleaseReadinessDecisionPackageBlockReason.MissingReleaseChannel);
            issues.Add("MissingReleaseChannel");
            return null;
        }

        var normalized = AllowedChannels.FirstOrDefault(item => Same(item, channel));
        if (normalized is null)
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.UnsupportedReleaseChannel);
            issues.Add("UnsupportedReleaseChannel");
        }

        return normalized;
    }

    private static void ValidateBoundary(
        List<ReleaseReadinessDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var boundary = ReleaseReadinessDecisionPackageBoundary.Evidence;
        if (!boundary.EvidenceOnly || HasAuthority(boundary))
        {
            blocked.Add(ReleaseReadinessDecisionPackageBlockReason.BoundaryViolation);
            issues.Add("BoundaryViolation");
        }
    }

    private static ReleaseReadinessDecisionPackageVerdict DetermineVerdict(
        List<ReleaseReadinessDecisionPackageBlockReason> blocked,
        List<ReleaseReadinessDecisionPackageBlockReason> rejected,
        List<ReleaseReadinessDecisionPackageBlockReason> incomplete)
    {
        if (rejected.Count > 0)
            return ReleaseReadinessDecisionPackageVerdict.PackageRejected;
        if (blocked.Count > 0)
            return ReleaseReadinessDecisionPackageVerdict.PackageBlocked;
        return incomplete.Count > 0
            ? ReleaseReadinessDecisionPackageVerdict.PackageIncomplete
            : ReleaseReadinessDecisionPackageVerdict.PackageReadyForReleaseExecutor;
    }

    private static void AddMismatch(
        List<ReleaseReadinessDecisionPackageBlockReason> blocked,
        List<string> issues,
        ReleaseReadinessDecisionPackageBlockReason reason,
        string issue)
    {
        blocked.Add(reason);
        issues.Add(issue);
    }

    private static bool HasAuthority(ReleaseCandidatePackageBoundary boundary) =>
        boundary.CanMarkReadyForReview ||
        boundary.CanRequestReviewers ||
        boundary.CanRemoveReviewers ||
        boundary.CanResolveReviewThreads ||
        boundary.CanReplyToReviewThreads ||
        boundary.CanApprove ||
        boundary.CanSubmitReview ||
        boundary.CanMerge ||
        boundary.CanAutoMerge ||
        boundary.CanRelease ||
        boundary.CanDeploy ||
        boundary.CanTag ||
        boundary.CanPublish ||
        boundary.CanPromoteMemory ||
        boundary.CanContinueWorkflow ||
        boundary.CanCommit ||
        boundary.CanPush ||
        boundary.CanMutateSource ||
        boundary.CanMutateWorkspace;

    private static bool HasAuthority(ReleaseReadinessDecisionPackageBoundary boundary) =>
        boundary.CanMarkReadyForReview ||
        boundary.CanRequestReviewers ||
        boundary.CanRemoveReviewers ||
        boundary.CanResolveReviewThreads ||
        boundary.CanReplyToReviewThreads ||
        boundary.CanApprove ||
        boundary.CanSubmitReview ||
        boundary.CanMerge ||
        boundary.CanAutoMerge ||
        boundary.CanRelease ||
        boundary.CanDeploy ||
        boundary.CanTag ||
        boundary.CanPublish ||
        boundary.CanPromoteMemory ||
        boundary.CanContinueWorkflow ||
        boundary.CanCommit ||
        boundary.CanPush ||
        boundary.CanMutateSource ||
        boundary.CanMutateWorkspace;

    private static bool ContainsToken(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class ReleaseReadinessDecisionPackageBypassEvaluator
{
    public static bool CanRelease(ReleaseReadinessDecisionPackage package) => package.Boundary.CanRelease;
    public static bool CanDeploy(ReleaseReadinessDecisionPackage package) => package.Boundary.CanDeploy;
    public static bool CanTag(ReleaseReadinessDecisionPackage package) => package.Boundary.CanTag;
    public static bool CanPublish(ReleaseReadinessDecisionPackage package) => package.Boundary.CanPublish;
    public static bool CanPromoteMemory(ReleaseReadinessDecisionPackage package) => package.Boundary.CanPromoteMemory;
    public static bool CanContinueWorkflow(ReleaseReadinessDecisionPackage package) => package.Boundary.CanContinueWorkflow;
    public static bool CanCommit(ReleaseReadinessDecisionPackage package) => package.Boundary.CanCommit;
    public static bool CanPush(ReleaseReadinessDecisionPackage package) => package.Boundary.CanPush;
    public static bool CanMutateSource(ReleaseReadinessDecisionPackage package) => package.Boundary.CanMutateSource;
    public static bool CanMutateWorkspace(ReleaseReadinessDecisionPackage package) => package.Boundary.CanMutateWorkspace;
}

internal static class ReleaseReadinessDecisionPackageHashing
{
    public static string ShortHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
    }
}
