using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Validation;

namespace IronDev.Core.Governance;

public enum ReleaseCandidatePackageVerdict
{
    PackageReadyForReleaseExecutor = 0,
    PackageIncomplete,
    PackageBlocked,
    PackageRejected
}

public enum ReleaseCandidatePackageBlockReason
{
    MissingMergeExecutionReceipt = 0,
    MergeExecutionNotExecuted,
    MergeExecutionPostStateNotVerified,
    MissingMergeCommitSha,

    MergeReceiptRepositoryMismatch,
    MergeReceiptCommitMismatch,
    MergeReceiptBaseBranchMismatch,
    MergeReceiptBoundaryAuthorityViolation,

    MissingReleaseSourceState,
    ReleaseSourceObservationFailed,
    ReleaseSourceCommitMismatch,
    ReleaseSourceCommitNotPresent,
    ReleaseSourceBranchMismatch,
    ReleaseSourceStateStale,

    MissingReleaseValidationEvidence,
    ReleaseValidationEvidenceStale,
    ReleaseValidationFailed,
    RequiredReleaseValidationMissing,

    MissingVersionEvidence,
    InvalidCandidateVersion,
    MissingCandidateTagName,
    CandidateTagAlreadyExists,
    CandidateReleaseAlreadyExists,
    MissingVersionDecisionMaker,
    MissingVersionRationale,

    MissingReleaseNotesEvidence,
    EmptyReleaseNotes,
    MissingMigrationNotes,
    MissingKnownIssuesRecord,

    MissingArtifactManifest,
    ArtifactManifestCommitMismatch,
    ArtifactChecksumMissing,

    MissingReleaseCandidateDecision,
    ReleaseCandidateDecisionBlocked,
    ReleaseCandidateDecisionRejected,
    ReleaseCandidateDecisionStale,
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

public enum ReleaseCandidateDecision
{
    ApprovedForReleaseExecutor = 0,
    Blocked,
    Rejected
}

public enum ReleaseVersionScheme
{
    SemVer = 0,
    CalendarVersion,
    InternalBuildNumber
}

public enum ReleaseCandidateChannel
{
    Internal = 0,
    Preview,
    ReleaseCandidate,
    Stable,
    Hotfix
}

public sealed record ReleaseCandidatePackageBoundary
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

    public static ReleaseCandidatePackageBoundary Evidence { get; } = new();
}

public static class ReleaseCandidatePackageBoundaryText
{
    public const string Boundary = """
        Block AZ packages release-candidate readiness for an already merged commit.
        It does not create a tag.
        It does not create a GitHub release.
        It does not publish artifacts.
        It does not upload artifacts.
        It does not deploy.
        It does not promote memory.
        It does not commit.
        It does not push.
        It does not mutate source.
        It does not continue workflow.
        Merge execution is not release readiness.
        Release candidate package is not release execution.
        Release execution is not deployment.
        Release is not deployment.
        Validation evidence is not release authority.
        Release notes are not release authority.
        Version selection is not tag creation.
        No hidden publication.
        No hidden deployment.
        No hidden workflow continuation.
        """;
}

public sealed record ReleaseSourceObservedState
{
    public required string Repository { get; init; }
    public required string ReleaseSourceBranch { get; init; }
    public required string ReleaseSourceHeadSha { get; init; }
    public required string ExpectedMergeCommitSha { get; init; }
    public required string DefaultBranch { get; init; }
    public required string DefaultBranchHeadSha { get; init; }
    public required bool CommitPresentOnReleaseSource { get; init; }
    public required bool CommitPresentOnDefaultBranch { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string ObservationSource { get; init; }
    public required bool ObservationSucceeded { get; init; }
    public string? ObservationError { get; init; }
}

public sealed record ReleaseValidationEvidence
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

public sealed record ReleaseVersionEvidence
{
    public required string CandidateVersion { get; init; }
    public required string VersionScheme { get; init; }
    public string? PreviousVersion { get; init; }
    public required string VersionSource { get; init; }
    public required string VersionDecisionBy { get; init; }
    public required DateTimeOffset VersionDecisionAtUtc { get; init; }
    public required string VersionRationale { get; init; }
    public required string TagName { get; init; }
    public bool ExistingTagFound { get; init; }
    public bool ExistingReleaseFound { get; init; }
}

public sealed record ReleaseNotesEvidence
{
    public string? ReleaseNotesPath { get; init; }
    public required string ReleaseNotesSummary { get; init; }
    public string? ChangelogPath { get; init; }
    public string[] IncludedPullRequests { get; init; } = [];
    public string[] IncludedCommits { get; init; } = [];
    public string[] KnownIssues { get; init; } = [];
    public string[] BreakingChanges { get; init; } = [];
    public string[] MigrationNotes { get; init; } = [];
    public bool KnownIssuesPresent { get; init; }
    public bool MigrationsPresent { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public required string GeneratedBy { get; init; }
}

public sealed record ArtifactManifestEvidence
{
    public required string ArtifactManifestId { get; init; }
    public required string BuildRunId { get; init; }
    public required string CommitSha { get; init; }
    public string[] Artifacts { get; init; } = [];
    public string[] Checksums { get; init; } = [];
    public string? StorageLocation { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record ReleaseCandidateDecisionRecord
{
    public required string ReleaseCandidateDecisionId { get; init; }
    public required ReleaseCandidateDecision Decision { get; init; }
    public required string DecisionMadeBy { get; init; }
    public required DateTimeOffset DecisionMadeAtUtc { get; init; }
    public required string DecisionRationale { get; init; }
    public required string ExpectedRepository { get; init; }
    public required string ExpectedCommitSha { get; init; }
    public required string ExpectedVersion { get; init; }
    public required string ExpectedReleaseSourceBranch { get; init; }
    public required string ExpectedReleaseChannel { get; init; }
}

public sealed record ReleaseCandidatePackageInput
{
    public MergeExecutionReceipt? MergeExecutionReceipt { get; init; }
    public ReleaseSourceObservedState? ObservedReleaseSourceState { get; init; }
    public ReleaseValidationEvidence? ReleaseValidationEvidence { get; init; }
    public ReleaseVersionEvidence? ReleaseVersionEvidence { get; init; }
    public ReleaseNotesEvidence? ReleaseNotesEvidence { get; init; }
    public ArtifactManifestEvidence? ArtifactManifestEvidence { get; init; }
    public bool ArtifactManifestRequired { get; init; }
    public ReleaseCandidateDecisionRecord? ReleaseCandidateDecision { get; init; }
    public required string Repository { get; init; }
    public required string ReleaseSourceBranch { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string ReleaseChannel { get; init; }
    public required string CreatedBy { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record ReleaseCandidatePackage
{
    public required string ReleaseCandidatePackageId { get; init; }

    public required string Repository { get; init; }
    public required string ReleaseSourceBranch { get; init; }
    public required string ReleaseSourceHeadSha { get; init; }
    public required string CandidateCommitSha { get; init; }
    public required string CandidateVersion { get; init; }
    public required string CandidateTagName { get; init; }
    public required string ReleaseChannel { get; init; }

    public required string SourceMergeExecutionReceiptId { get; init; }
    public required string SourceMergeDecisionPackageId { get; init; }
    public required MergeExecutionVerdict MergeExecutionVerdict { get; init; }
    public required bool MergePostStateVerified { get; init; }
    public required string MergeCommitSha { get; init; }

    public ReleaseSourceObservedState? ObservedReleaseSourceState { get; init; }
    public ReleaseValidationEvidence? ReleaseValidationEvidence { get; init; }
    public ReleaseVersionEvidence? ReleaseVersionEvidence { get; init; }
    public ReleaseNotesEvidence? ReleaseNotesEvidence { get; init; }
    public ArtifactManifestEvidence? ArtifactManifestEvidence { get; init; }
    public ReleaseCandidateDecisionRecord? ReleaseCandidateDecision { get; init; }

    public required ReleaseCandidatePackageVerdict PackageVerdict { get; init; }
    public required bool CanReleaseForExecutor { get; init; }
    public ReleaseCandidatePackageBlockReason[] BlockReasons { get; init; } = [];
    public string[] PackageIssues { get; init; } = [];

    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ReleaseCandidatePackageBoundary Boundary { get; init; } = ReleaseCandidatePackageBoundary.Evidence;
}

public sealed record ReleaseCandidatePackageReceipt
{
    public required string ReleaseCandidatePackageReceiptId { get; init; }
    public required string ReleaseCandidatePackageId { get; init; }
    public required ReleaseCandidatePackageVerdict Verdict { get; init; }
    public required bool CanReleaseForExecutor { get; init; }
    public ReleaseCandidatePackageBlockReason[] BlockReasons { get; init; } = [];
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ReleaseCandidatePackageBoundary Boundary { get; init; } = ReleaseCandidatePackageBoundary.Evidence;
}

public sealed record ReleaseCandidatePackageArtifacts
{
    public required ReleaseCandidatePackage Package { get; init; }
    public required ReleaseCandidatePackageReceipt Receipt { get; init; }
}

public static class ReleaseCandidatePackageBuilder
{
    public static readonly string[] RequiredValidationFamilies =
    [
        "Build",
        "DiffCheck",
        "StablePhase",
        "ReleaseCandidateAuthority",
        "Packaging",
        "Regression"
    ];

    public static ReleaseCandidatePackageArtifacts Build(ReleaseCandidatePackageInput input)
    {
        var now = input.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var incomplete = new List<ReleaseCandidatePackageBlockReason>();
        var blocked = new List<ReleaseCandidatePackageBlockReason>();
        var rejected = new List<ReleaseCandidatePackageBlockReason>();
        var issues = new List<string>();

        ValidateMergeExecution(input, incomplete, blocked, issues);
        ValidateSourceState(input, incomplete, blocked, issues);
        ValidateValidation(input, incomplete, blocked, issues);
        ValidateVersion(input, incomplete, blocked, issues);
        ValidateReleaseNotes(input, incomplete, blocked, issues);
        ValidateArtifactManifest(input, incomplete, blocked, issues);
        ValidateDecision(input, incomplete, blocked, rejected, issues);
        var normalizedChannel = ValidateReleaseChannel(input.ReleaseChannel, incomplete, blocked, issues);
        ValidateBoundary(blocked, issues);

        var blockReasons = blocked.Concat(rejected).Concat(incomplete).Distinct().ToArray();
        var verdict = DetermineVerdict(blocked, rejected, incomplete);
        var canReleaseForExecutor = verdict == ReleaseCandidatePackageVerdict.PackageReadyForReleaseExecutor;
        var receipt = input.MergeExecutionReceipt;
        var observed = input.ObservedReleaseSourceState;
        var version = input.ReleaseVersionEvidence;
        var packageId = $"release_candidate_pkg_{AzReleaseCandidateHashing.ShortHash($"{input.Repository}|{input.CandidateCommitSha}|{version?.CandidateVersion}|{normalizedChannel}|{verdict}|{input.ReleaseCandidateDecision?.ReleaseCandidateDecisionId}")}";
        var package = new ReleaseCandidatePackage
        {
            ReleaseCandidatePackageId = packageId,
            Repository = FeedbackText.Safe(input.Repository),
            ReleaseSourceBranch = FeedbackText.Safe(input.ReleaseSourceBranch),
            ReleaseSourceHeadSha = FeedbackText.Safe(observed?.ReleaseSourceHeadSha ?? string.Empty),
            CandidateCommitSha = FeedbackText.Safe(input.CandidateCommitSha),
            CandidateVersion = FeedbackText.Safe(version?.CandidateVersion ?? string.Empty),
            CandidateTagName = FeedbackText.Safe(version?.TagName ?? string.Empty),
            ReleaseChannel = FeedbackText.Safe(normalizedChannel ?? input.ReleaseChannel),
            SourceMergeExecutionReceiptId = FeedbackText.Safe(receipt?.MergeExecutionId ?? "missing-merge-execution-receipt"),
            SourceMergeDecisionPackageId = FeedbackText.Safe(receipt?.MergeDecisionPackageId ?? "missing-merge-decision-package"),
            MergeExecutionVerdict = receipt?.ExecutionVerdict ?? MergeExecutionVerdict.Incomplete,
            MergePostStateVerified = receipt?.PostStateVerified ?? false,
            MergeCommitSha = FeedbackText.Safe(receipt?.MergeCommitSha ?? string.Empty),
            ObservedReleaseSourceState = observed,
            ReleaseValidationEvidence = input.ReleaseValidationEvidence,
            ReleaseVersionEvidence = version,
            ReleaseNotesEvidence = input.ReleaseNotesEvidence,
            ArtifactManifestEvidence = input.ArtifactManifestEvidence,
            ReleaseCandidateDecision = input.ReleaseCandidateDecision,
            PackageVerdict = verdict,
            CanReleaseForExecutor = canReleaseForExecutor,
            BlockReasons = blockReasons,
            PackageIssues = FeedbackText.SafeList(issues),
            CreatedBy = FeedbackText.Safe(input.CreatedBy),
            CreatedAtUtc = now,
            Boundary = ReleaseCandidatePackageBoundary.Evidence
        };
        var packageReceipt = new ReleaseCandidatePackageReceipt
        {
            ReleaseCandidatePackageReceiptId = $"release_candidate_receipt_{AzReleaseCandidateHashing.ShortHash($"{packageId}|{verdict}|{now:O}")}",
            ReleaseCandidatePackageId = packageId,
            Verdict = verdict,
            CanReleaseForExecutor = canReleaseForExecutor,
            BlockReasons = blockReasons,
            BoundaryStatements =
            [
                "Merge execution is not release readiness.",
                "Release candidate package is not release execution.",
                "Release execution is not deployment.",
                "Release is not deployment.",
                "Validation evidence is not release authority.",
                "Release notes are not release authority.",
                "Version selection is not tag creation.",
                "No hidden publication.",
                "No hidden deployment.",
                "No hidden workflow continuation."
            ],
            CreatedAtUtc = now,
            Boundary = ReleaseCandidatePackageBoundary.Evidence
        };

        return new ReleaseCandidatePackageArtifacts
        {
            Package = package,
            Receipt = packageReceipt
        };
    }

    public static ReleaseValidationEvidence FromValidationReceipt(ValidationRunReceipt receipt) => new()
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

    private static void ValidateMergeExecution(
        ReleaseCandidatePackageInput input,
        List<ReleaseCandidatePackageBlockReason> incomplete,
        List<ReleaseCandidatePackageBlockReason> blocked,
        List<string> issues)
    {
        var receipt = input.MergeExecutionReceipt;
        if (receipt is null)
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingMergeExecutionReceipt);
            issues.Add("MissingMergeExecutionReceipt");
            return;
        }

        if (receipt.ExecutionVerdict != MergeExecutionVerdict.Executed ||
            receipt.FailureClassification != MergeExecutionFailureKind.None ||
            !receipt.MergeAttempted ||
            !receipt.MergeAccepted)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.MergeExecutionNotExecuted);
            issues.Add($"MergeExecutionNotExecuted:{receipt.ExecutionVerdict}/{receipt.FailureClassification}");
        }

        if (!receipt.PostStateVerified)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.MergeExecutionPostStateNotVerified);
            issues.Add("MergeExecutionPostStateNotVerified");
        }

        if (string.IsNullOrWhiteSpace(receipt.MergeCommitSha))
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingMergeCommitSha);
            issues.Add("MissingMergeCommitSha");
        }

        if (!Same(receipt.Repository, input.Repository))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.MergeReceiptRepositoryMismatch);
            issues.Add("MergeReceiptRepositoryMismatch");
        }

        if (!string.IsNullOrWhiteSpace(receipt.MergeCommitSha) && !Same(receipt.MergeCommitSha, input.CandidateCommitSha))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.MergeReceiptCommitMismatch);
            issues.Add("MergeReceiptCommitMismatch");
        }

        if (!Same(receipt.ExpectedBaseBranch, input.ReleaseSourceBranch))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.MergeReceiptBaseBranchMismatch);
            issues.Add("MergeReceiptBaseBranchMismatch");
        }

        if (receipt.Boundary.CanRelease ||
            receipt.Boundary.CanDeploy ||
            receipt.Boundary.CanTag ||
            receipt.Boundary.CanPublish ||
            receipt.Boundary.CanPromoteMemory ||
            receipt.Boundary.CanContinueWorkflow)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.MergeReceiptBoundaryAuthorityViolation);
            issues.Add("MergeReceiptBoundaryAuthorityViolation");
        }
    }

    private static void ValidateSourceState(
        ReleaseCandidatePackageInput input,
        List<ReleaseCandidatePackageBlockReason> incomplete,
        List<ReleaseCandidatePackageBlockReason> blocked,
        List<string> issues)
    {
        var observed = input.ObservedReleaseSourceState;
        if (observed is null)
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingReleaseSourceState);
            issues.Add("MissingReleaseSourceState");
            return;
        }

        if (!observed.ObservationSucceeded)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ReleaseSourceObservationFailed);
            issues.Add($"ReleaseSourceObservationFailed:{observed.ObservationError ?? "observation failed"}");
        }

        if (!Same(observed.Repository, input.Repository) ||
            !Same(observed.ExpectedMergeCommitSha, input.CandidateCommitSha) ||
            !Same(observed.ReleaseSourceHeadSha, input.CandidateCommitSha) ||
            (Same(observed.ReleaseSourceBranch, observed.DefaultBranch) && !Same(observed.DefaultBranchHeadSha, input.CandidateCommitSha)))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ReleaseSourceCommitMismatch);
            issues.Add("ReleaseSourceCommitMismatch");
        }

        if (!Same(observed.ReleaseSourceBranch, input.ReleaseSourceBranch))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ReleaseSourceBranchMismatch);
            issues.Add("ReleaseSourceBranchMismatch");
        }

        if (!observed.CommitPresentOnReleaseSource || (Same(observed.ReleaseSourceBranch, observed.DefaultBranch) && !observed.CommitPresentOnDefaultBranch))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ReleaseSourceCommitNotPresent);
            issues.Add("ReleaseSourceCommitNotPresent");
        }

        if (input.MergeExecutionReceipt is not null && observed.ObservedAtUtc < input.MergeExecutionReceipt.ExecutedAtUtc)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ReleaseSourceStateStale);
            issues.Add("ReleaseSourceStateStale");
        }
    }

    private static void ValidateValidation(
        ReleaseCandidatePackageInput input,
        List<ReleaseCandidatePackageBlockReason> incomplete,
        List<ReleaseCandidatePackageBlockReason> blocked,
        List<string> issues)
    {
        var evidence = input.ReleaseValidationEvidence;
        if (evidence is null)
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingReleaseValidationEvidence);
            issues.Add("MissingReleaseValidationEvidence");
            return;
        }

        if (!Same(evidence.CommitSha, input.CandidateCommitSha))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ReleaseValidationEvidenceStale);
            issues.Add("ReleaseValidationEvidenceStale");
        }

        if (evidence.Verdict != ValidationRunVerdict.Passed || evidence.FailedLaneNames.Length > 0)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ReleaseValidationFailed);
            issues.Add("ReleaseValidationFailed");
        }

        var executedFamilies = evidence.ResultLaneNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var notApplicable = evidence.NotApplicableLaneNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var missingRequired = RequiredValidationFamilies
            .Where(required => !executedFamilies.Any(name => ContainsToken(name, required)))
            .Where(required => !notApplicable.Any(name => ContainsToken(name, required)))
            .ToArray();
        if (missingRequired.Length > 0 || evidence.MissingLaneNames.Length > 0)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.RequiredReleaseValidationMissing);
            foreach (var missing in missingRequired.Concat(evidence.MissingLaneNames).Distinct(StringComparer.OrdinalIgnoreCase))
                issues.Add($"RequiredReleaseValidationMissing:{missing}");
        }
    }

    private static void ValidateVersion(
        ReleaseCandidatePackageInput input,
        List<ReleaseCandidatePackageBlockReason> incomplete,
        List<ReleaseCandidatePackageBlockReason> blocked,
        List<string> issues)
    {
        var evidence = input.ReleaseVersionEvidence;
        if (evidence is null)
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingVersionEvidence);
            issues.Add("MissingVersionEvidence");
            return;
        }

        if (!IsValidVersion(evidence.CandidateVersion, evidence.VersionScheme) ||
            string.IsNullOrWhiteSpace(evidence.VersionSource))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.InvalidCandidateVersion);
            issues.Add("InvalidCandidateVersion");
        }

        if (string.IsNullOrWhiteSpace(evidence.TagName))
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingCandidateTagName);
            issues.Add("MissingCandidateTagName");
        }
        else if (!Same(evidence.TagName, $"v{evidence.CandidateVersion}"))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.InvalidCandidateVersion);
            issues.Add("CandidateTagNameNotDeterministic");
        }

        if (evidence.ExistingTagFound)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.CandidateTagAlreadyExists);
            issues.Add("CandidateTagAlreadyExists");
        }

        if (evidence.ExistingReleaseFound)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.CandidateReleaseAlreadyExists);
            issues.Add("CandidateReleaseAlreadyExists");
        }

        if (string.IsNullOrWhiteSpace(evidence.VersionDecisionBy))
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingVersionDecisionMaker);
            issues.Add("MissingVersionDecisionMaker");
        }

        if (string.IsNullOrWhiteSpace(evidence.VersionRationale))
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingVersionRationale);
            issues.Add("MissingVersionRationale");
        }
    }

    private static void ValidateReleaseNotes(
        ReleaseCandidatePackageInput input,
        List<ReleaseCandidatePackageBlockReason> incomplete,
        List<ReleaseCandidatePackageBlockReason> blocked,
        List<string> issues)
    {
        var evidence = input.ReleaseNotesEvidence;
        if (evidence is null)
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingReleaseNotesEvidence);
            issues.Add("MissingReleaseNotesEvidence");
            return;
        }

        if (string.IsNullOrWhiteSpace(evidence.ReleaseNotesSummary))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.EmptyReleaseNotes);
            issues.Add("EmptyReleaseNotes");
        }

        if (evidence.MigrationsPresent && evidence.MigrationNotes.Length == 0)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.MissingMigrationNotes);
            issues.Add("MissingMigrationNotes");
        }

        if (evidence.KnownIssuesPresent && evidence.KnownIssues.Length == 0)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.MissingKnownIssuesRecord);
            issues.Add("MissingKnownIssuesRecord");
        }
    }

    private static void ValidateArtifactManifest(
        ReleaseCandidatePackageInput input,
        List<ReleaseCandidatePackageBlockReason> incomplete,
        List<ReleaseCandidatePackageBlockReason> blocked,
        List<string> issues)
    {
        var evidence = input.ArtifactManifestEvidence;
        if (evidence is null)
        {
            if (input.ArtifactManifestRequired)
            {
                incomplete.Add(ReleaseCandidatePackageBlockReason.MissingArtifactManifest);
                issues.Add("MissingArtifactManifest");
            }

            return;
        }

        if (!Same(evidence.CommitSha, input.CandidateCommitSha))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ArtifactManifestCommitMismatch);
            issues.Add("ArtifactManifestCommitMismatch");
        }

        if (evidence.Artifacts.Length > 0 && evidence.Checksums.Length == 0)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ArtifactChecksumMissing);
            issues.Add("ArtifactChecksumMissing");
        }
    }

    private static void ValidateDecision(
        ReleaseCandidatePackageInput input,
        List<ReleaseCandidatePackageBlockReason> incomplete,
        List<ReleaseCandidatePackageBlockReason> blocked,
        List<ReleaseCandidatePackageBlockReason> rejected,
        List<string> issues)
    {
        var decision = input.ReleaseCandidateDecision;
        if (decision is null)
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingReleaseCandidateDecision);
            issues.Add("MissingReleaseCandidateDecision");
            return;
        }

        if (decision.Decision == ReleaseCandidateDecision.Blocked)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ReleaseCandidateDecisionBlocked);
            issues.Add("ReleaseCandidateDecisionBlocked");
        }
        else if (decision.Decision == ReleaseCandidateDecision.Rejected)
        {
            rejected.Add(ReleaseCandidatePackageBlockReason.ReleaseCandidateDecisionRejected);
            issues.Add("ReleaseCandidateDecisionRejected");
        }
        else if (decision.Decision != ReleaseCandidateDecision.ApprovedForReleaseExecutor)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ReleaseCandidateDecisionBlocked);
            issues.Add($"ReleaseCandidateDecisionUnsupported:{decision.Decision}");
        }

        if (string.IsNullOrWhiteSpace(decision.DecisionMadeBy))
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingDecisionMaker);
            issues.Add("MissingDecisionMaker");
        }

        if (Same(decision.DecisionMadeBy, input.MergeExecutionReceipt?.RequestedBy))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.DecisionMakerNotAllowed);
            issues.Add("DecisionMakerNotAllowed");
        }

        if (!Same(decision.ExpectedRepository, input.Repository) ||
            !Same(decision.ExpectedCommitSha, input.CandidateCommitSha) ||
            !Same(decision.ExpectedVersion, input.ReleaseVersionEvidence?.CandidateVersion) ||
            !Same(decision.ExpectedReleaseSourceBranch, input.ReleaseSourceBranch) ||
            !Same(NormalizeChannelText(decision.ExpectedReleaseChannel), NormalizeChannelText(input.ReleaseChannel)))
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ReleaseCandidateDecisionStale);
            issues.Add("ReleaseCandidateDecisionStale");
        }

        if (string.IsNullOrWhiteSpace(decision.DecisionRationale))
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingDecisionRationale);
            issues.Add("MissingDecisionRationale");
        }
    }

    private static string? ValidateReleaseChannel(
        string channel,
        List<ReleaseCandidatePackageBlockReason> incomplete,
        List<ReleaseCandidatePackageBlockReason> blocked,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            incomplete.Add(ReleaseCandidatePackageBlockReason.MissingReleaseChannel);
            issues.Add("MissingReleaseChannel");
            return null;
        }

        var normalized = NormalizeChannelText(channel);
        if (normalized is null)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.UnsupportedReleaseChannel);
            issues.Add($"UnsupportedReleaseChannel:{FeedbackText.Safe(channel)}");
        }

        return normalized;
    }

    private static void ValidateBoundary(List<ReleaseCandidatePackageBlockReason> blocked, List<string> issues)
    {
        var boundary = ReleaseCandidatePackageBoundary.Evidence;
        if (!boundary.EvidenceOnly)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.BoundaryViolation);
            issues.Add("BoundaryNotEvidenceOnly");
        }

        if (boundary.CanRelease)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.ReleaseMutationNotAllowed);
            issues.Add("ReleaseMutationNotAllowed");
        }

        if (boundary.CanTag)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.TagMutationNotAllowed);
            issues.Add("TagMutationNotAllowed");
        }

        if (boundary.CanPublish)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.PublishNotAllowed);
            issues.Add("PublishNotAllowed");
        }

        if (boundary.CanDeploy)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.DeployNotAllowed);
            issues.Add("DeployNotAllowed");
        }

        if (boundary.CanPromoteMemory)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.MemoryPromotionNotAllowed);
            issues.Add("MemoryPromotionNotAllowed");
        }

        if (boundary.CanContinueWorkflow)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.WorkflowContinuationNotAllowed);
            issues.Add("WorkflowContinuationNotAllowed");
        }

        if (boundary.CanCommit || boundary.CanPush)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.CommitPushNotAllowed);
            issues.Add("CommitPushNotAllowed");
        }

        if (boundary.CanMutateSource || boundary.CanMutateWorkspace)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.SourceMutationNotAllowed);
            issues.Add("SourceMutationNotAllowed");
        }

        if (boundary.CanMarkReadyForReview ||
            boundary.CanRequestReviewers ||
            boundary.CanRemoveReviewers ||
            boundary.CanResolveReviewThreads ||
            boundary.CanReplyToReviewThreads ||
            boundary.CanApprove ||
            boundary.CanSubmitReview ||
            boundary.CanMerge ||
            boundary.CanAutoMerge)
        {
            blocked.Add(ReleaseCandidatePackageBlockReason.BoundaryViolation);
            issues.Add("ReviewOrMergeAuthorityNotAllowed");
        }
    }

    private static ReleaseCandidatePackageVerdict DetermineVerdict(
        IReadOnlyCollection<ReleaseCandidatePackageBlockReason> blocked,
        IReadOnlyCollection<ReleaseCandidatePackageBlockReason> rejected,
        IReadOnlyCollection<ReleaseCandidatePackageBlockReason> incomplete)
    {
        if (rejected.Count > 0)
            return ReleaseCandidatePackageVerdict.PackageRejected;
        if (blocked.Count > 0)
            return ReleaseCandidatePackageVerdict.PackageBlocked;
        if (incomplete.Count > 0)
            return ReleaseCandidatePackageVerdict.PackageIncomplete;
        return ReleaseCandidatePackageVerdict.PackageReadyForReleaseExecutor;
    }

    private static bool IsValidVersion(string? version, string? scheme)
    {
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(scheme))
            return false;

        var normalized = scheme.Trim().Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        return normalized switch
        {
            "semver" => IsSemVer(version),
            "calendarversion" or "calver" => IsCalendarVersion(version),
            "internalbuildnumber" => version.All(item => char.IsLetterOrDigit(item) || item is '.' or '-' or '_'),
            _ => false
        };
    }

    private static bool IsSemVer(string value)
    {
        var parts = value.Split('-', '+')[0].Split('.');
        return parts.Length == 3 && parts.All(part => int.TryParse(part, out _));
    }

    private static bool IsCalendarVersion(string value)
    {
        var parts = value.Split('-', '+')[0].Split('.');
        return parts.Length >= 3 &&
            parts[0].Length == 4 &&
            parts.Take(3).All(part => int.TryParse(part, out _));
    }

    private static string? NormalizeChannelText(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace("_", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        return normalized switch
        {
            "internal" => ReleaseCandidateChannel.Internal.ToString(),
            "preview" => ReleaseCandidateChannel.Preview.ToString(),
            "release-candidate" or "releasecandidate" => ReleaseCandidateChannel.ReleaseCandidate.ToString(),
            "stable" => ReleaseCandidateChannel.Stable.ToString(),
            "hotfix" => ReleaseCandidateChannel.Hotfix.ToString(),
            _ => null
        };
    }

    private static bool ContainsToken(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class ReleaseCandidatePackageBypassEvaluator
{
    public static bool CanRelease(object? evidence) => false;
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanTag(object? evidence) => false;
    public static bool CanPublish(object? evidence) => false;
    public static bool CanPromoteMemory(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
    public static bool CanCommit(object? evidence) => false;
    public static bool CanPush(object? evidence) => false;
    public static bool CanMutateSource(object? evidence) => false;
    public static bool CanMutateWorkspace(object? evidence) => false;
}

internal static class AzReleaseCandidateHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
