using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Validation;

namespace IronDev.Core.Governance;

public enum MergeDecisionPackageVerdict
{
    PackageReadyForMergeExecutor = 0,
    PackageIncomplete,
    PackageBlocked,
    PackageRejected
}

public enum MergeDecisionPackageBlockReason
{
    MissingReviewerRequestExecutionReceipt = 0,
    ReviewerRequestExecutionNotExecuted,
    ReviewerRequestPostStateNotVerified,

    MissingPullRequestIdentity,
    PullRequestNotOpen,
    PullRequestStillDraft,
    HeadBranchMismatch,
    HeadShaMismatch,
    BaseBranchMismatch,
    BaseShaMismatch,
    PullRequestHasConflicts,
    PullRequestNotMergeable,

    MissingReviewEvidence,
    ReviewEvidenceStale,
    RequiredApprovalsMissing,
    RequestedChangesPresent,
    BlockingReviewThreadsPresent,
    StaleApprovalPresent,
    PullRequestAuthorApprovalNotAllowed,

    MissingValidationEvidence,
    ValidationEvidenceStale,
    RequiredValidationMissing,
    ValidationFailed,

    MissingMergeDecision,
    MergeDecisionRejected,
    MergeDecisionBlocked,
    MergeDecisionStale,
    MissingDecisionMaker,
    DecisionMakerIsPullRequestAuthor,
    MissingDecisionRationale,
    MissingMergeStrategy,
    UnsupportedMergeStrategy,

    MergeMutationNotAllowed,
    AutoMergeNotAllowed,
    ApprovalNotAllowed,
    ReviewMutationNotAllowed,
    ReleaseDeployNotAllowed,
    WorkflowContinuationNotAllowed,
    BoundaryViolation
}

public enum MergeDecision
{
    ApprovedForMergeExecutor = 0,
    Blocked,
    Rejected
}

public enum MergeDecisionStrategy
{
    MergeCommit = 0,
    Squash,
    Rebase
}

public sealed record MergeDecisionPackageBoundary
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

    public static MergeDecisionPackageBoundary Evidence { get; } = new();
}

public static class MergeDecisionPackageBoundaryText
{
    public const string Boundary = """
        Block AX packages an explicit merge decision for a reviewed, current, non-draft PR.
        It does not merge.
        It does not enable auto-merge.
        It does not approve.
        It does not submit reviews.
        It does not resolve review threads.
        It does not release.
        It does not deploy.
        It does not tag.
        It does not publish.
        It does not promote memory.
        It does not commit.
        It does not push.
        It does not mutate source.
        It does not continue workflow.
        Approval is not merge decision.
        Merge decision package is not merge execution.
        Merge execution is not release.
        Release is not deployment.
        Validation evidence is not approval.
        No self-approval.
        No hidden mutation.
        """;
}

public sealed record MergeDecisionObservedPrState
{
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }
    public required string PullRequestState { get; init; }
    public required bool PullRequestDraft { get; init; }
    public required string HeadBranch { get; init; }
    public required string HeadSha { get; init; }
    public required string BaseBranch { get; init; }
    public string? BaseSha { get; init; }
    public required string Author { get; init; }
    public required bool Mergeable { get; init; }
    public required string MergeStateStatus { get; init; }
    public required bool IsBehindBase { get; init; }
    public required bool HasConflicts { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string ObservationSource { get; init; }
}

public sealed record MergeReviewEvidence
{
    public required int RequiredApprovalCount { get; init; }
    public required int ActualApprovalCount { get; init; }
    public string[] ApprovingReviewers { get; init; } = [];
    public string[] RequestedChangesReviewers { get; init; } = [];
    public string[] DismissedReviewers { get; init; } = [];
    public string[] StaleReviewers { get; init; } = [];
    public string[] UnresolvedReviewThreads { get; init; } = [];
    public required string ReviewEvidenceHeadSha { get; init; }
    public required DateTimeOffset ReviewEvidenceObservedAtUtc { get; init; }
    public string? ReviewEvidenceReceiptId { get; init; }
}

public sealed record MergeValidationEvidence
{
    public required string ValidationRunId { get; init; }
    public required string ValidationPlanId { get; init; }
    public required string CommitSha { get; init; }
    public required ValidationRunVerdict Verdict { get; init; }
    public string[] RequiredLaneNames { get; init; } = [];
    public string[] ResultLaneNames { get; init; } = [];
    public string[] MissingLaneNames { get; init; } = [];
    public string[] FailedLaneNames { get; init; } = [];
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; init; }
    public string? ValidationEvidenceReceiptId { get; init; }
}

public sealed record MergeDecisionRecord
{
    public required string MergeDecisionId { get; init; }
    public required MergeDecision Decision { get; init; }
    public required string DecisionMadeBy { get; init; }
    public required DateTimeOffset DecisionMadeAtUtc { get; init; }
    public required string DecisionRationale { get; init; }
    public required string ExpectedRepository { get; init; }
    public required int ExpectedPullRequestNumber { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ExpectedBaseBranch { get; init; }
    public required string ExpectedMergeStrategy { get; init; }
    public string? PolicyReceiptId { get; init; }
    public string? ReviewEvidenceReceiptId { get; init; }
    public string? ValidationEvidenceReceiptId { get; init; }
}

public sealed record MergeDecisionPackageInput
{
    public ReviewerRequestExecutionReceipt? ReviewerRequestExecutionReceipt { get; init; }
    public required MergeDecisionObservedPrState ObservedPullRequest { get; init; }
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ExpectedBaseBranch { get; init; }
    public string? ExpectedBaseSha { get; init; }
    public MergeReviewEvidence? ReviewEvidence { get; init; }
    public MergeValidationEvidence? ValidationEvidence { get; init; }
    public MergeDecisionRecord? MergeDecisionRecord { get; init; }
    public required string CreatedBy { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record MergeDecisionPackage
{
    public required string MergeDecisionPackageId { get; init; }
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }
    public required string PullRequestState { get; init; }
    public required bool PullRequestDraft { get; init; }
    public required string PullRequestAuthor { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ObservedHeadSha { get; init; }
    public required string BaseBranch { get; init; }
    public string? BaseSha { get; init; }
    public required string SourceReviewerRequestExecutionReceiptId { get; init; }
    public required string SourceReviewerRequestPackageId { get; init; }
    public required ReviewerRequestExecutionVerdict ReviewerRequestExecutionVerdict { get; init; }
    public required bool ReviewerRequestPostStateVerified { get; init; }
    public MergeReviewEvidence? ReviewEvidence { get; init; }
    public MergeValidationEvidence? ValidationEvidence { get; init; }
    public MergeDecisionRecord? MergeDecisionRecord { get; init; }
    public string? SelectedMergeStrategy { get; init; }
    public required MergeDecisionPackageVerdict PackageVerdict { get; init; }
    public required bool CanMergeForExecutor { get; init; }
    public MergeDecisionPackageBlockReason[] BlockReasons { get; init; } = [];
    public string[] PackageIssues { get; init; } = [];
    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MergeDecisionPackageBoundary Boundary { get; init; } = MergeDecisionPackageBoundary.Evidence;
}

public sealed record MergeDecisionPackageReceipt
{
    public required string MergeDecisionPackageReceiptId { get; init; }
    public required string MergeDecisionPackageId { get; init; }
    public required MergeDecisionPackageVerdict Verdict { get; init; }
    public required bool CanMergeForExecutor { get; init; }
    public MergeDecisionPackageBlockReason[] BlockReasons { get; init; } = [];
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public MergeDecisionPackageBoundary Boundary { get; init; } = MergeDecisionPackageBoundary.Evidence;
}

public sealed record MergeDecisionPackageArtifacts
{
    public required MergeDecisionPackage Package { get; init; }
    public required MergeDecisionPackageReceipt Receipt { get; init; }
}

public static class MergeDecisionPackageBuilder
{
    public static readonly string[] RequiredValidationFamilies =
    [
        "FocusedCurrentBlock",
        "ImpactedArea",
        "FastAuthorityInvariant",
        "Build",
        "DiffCheck",
        "PhaseAuthority",
        "MergeDecisionAuthority"
    ];

    public static MergeDecisionPackageArtifacts Build(MergeDecisionPackageInput input)
    {
        var now = input.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var incomplete = new List<MergeDecisionPackageBlockReason>();
        var blocked = new List<MergeDecisionPackageBlockReason>();
        var issues = new List<string>();

        ValidateReviewerRequestExecution(input, incomplete, blocked, issues);
        ValidateObservedPullRequest(input, incomplete, blocked, issues);
        ValidateReviewEvidence(input, incomplete, blocked, issues);
        ValidateValidationEvidence(input, incomplete, blocked, issues);
        var selectedStrategy = ValidateMergeDecision(input, incomplete, blocked, issues);
        ValidateBoundary(blocked, issues);

        var blockReasons = blocked.Concat(incomplete).Distinct().ToArray();
        var verdict = DetermineVerdict(blocked, incomplete);
        var canMergeForExecutor = verdict == MergeDecisionPackageVerdict.PackageReadyForMergeExecutor;
        var receipt = input.ReviewerRequestExecutionReceipt;
        var observed = input.ObservedPullRequest;
        var packageId = $"merge_decision_pkg_{AxMergeDecisionHashing.ShortHash($"{input.Repository}|{input.PullRequestNumber}|{input.ExpectedHeadSha}|{selectedStrategy}|{verdict}|{input.MergeDecisionRecord?.MergeDecisionId}")}";
        var package = new MergeDecisionPackage
        {
            MergeDecisionPackageId = packageId,
            Repository = FeedbackText.Safe(input.Repository),
            PullRequestNumber = input.PullRequestNumber,
            PullRequestUrl = FeedbackText.Safe(observed.PullRequestUrl),
            PullRequestState = FeedbackText.Safe(observed.PullRequestState),
            PullRequestDraft = observed.PullRequestDraft,
            PullRequestAuthor = FeedbackText.Safe(observed.Author),
            HeadBranch = FeedbackText.Safe(input.ExpectedHeadBranch),
            ExpectedHeadSha = FeedbackText.Safe(input.ExpectedHeadSha),
            ObservedHeadSha = FeedbackText.Safe(observed.HeadSha),
            BaseBranch = FeedbackText.Safe(input.ExpectedBaseBranch),
            BaseSha = FeedbackText.SafeOrNull(input.ExpectedBaseSha ?? observed.BaseSha),
            SourceReviewerRequestExecutionReceiptId = FeedbackText.Safe(receipt?.ReviewerRequestExecutionId ?? "missing-reviewer-request-execution-receipt"),
            SourceReviewerRequestPackageId = FeedbackText.Safe(receipt?.ReviewerRequestPackageId ?? "missing-reviewer-request-package"),
            ReviewerRequestExecutionVerdict = receipt?.ExecutionVerdict ?? ReviewerRequestExecutionVerdict.Incomplete,
            ReviewerRequestPostStateVerified = receipt?.PostStateVerified ?? false,
            ReviewEvidence = input.ReviewEvidence,
            ValidationEvidence = input.ValidationEvidence,
            MergeDecisionRecord = input.MergeDecisionRecord,
            SelectedMergeStrategy = FeedbackText.SafeOrNull(selectedStrategy),
            PackageVerdict = verdict,
            CanMergeForExecutor = canMergeForExecutor,
            BlockReasons = blockReasons,
            PackageIssues = FeedbackText.SafeList(issues),
            CreatedBy = FeedbackText.Safe(input.CreatedBy),
            CreatedAtUtc = now,
            Boundary = MergeDecisionPackageBoundary.Evidence
        };
        var packageReceipt = new MergeDecisionPackageReceipt
        {
            MergeDecisionPackageReceiptId = $"merge_decision_receipt_{AxMergeDecisionHashing.ShortHash($"{packageId}|{verdict}|{now:O}")}",
            MergeDecisionPackageId = packageId,
            Verdict = verdict,
            CanMergeForExecutor = canMergeForExecutor,
            BlockReasons = blockReasons,
            BoundaryStatements =
            [
                "Approval is not merge decision.",
                "Merge decision package is not merge execution.",
                "Merge execution is not release.",
                "Release is not deployment.",
                "Validation evidence is not approval.",
                "No self-approval.",
                "No hidden mutation."
            ],
            CreatedAtUtc = now,
            Boundary = MergeDecisionPackageBoundary.Evidence
        };

        return new MergeDecisionPackageArtifacts
        {
            Package = package,
            Receipt = packageReceipt
        };
    }

    public static MergeValidationEvidence FromValidationReceipt(ValidationRunReceipt receipt) => new()
    {
        ValidationRunId = receipt.ValidationRunId,
        ValidationPlanId = receipt.ValidationPlanId,
        CommitSha = receipt.CommitSha,
        Verdict = receipt.Verdict,
        RequiredLaneNames = receipt.RequiredLanes.Select(lane => lane.Name).ToArray(),
        ResultLaneNames = receipt.Results.Select(result => result.LaneName).ToArray(),
        MissingLaneNames = receipt.RequiredLanes
            .Select(lane => lane.Name)
            .Except(receipt.Results.Select(result => result.LaneName), StringComparer.OrdinalIgnoreCase)
            .ToArray(),
        FailedLaneNames = receipt.Results
            .Where(result => result.ExitCode != 0 || result.FailureClassification != ValidationFailureKind.Passed)
            .Select(result => result.LaneName)
            .ToArray(),
        StartedAtUtc = receipt.StartedUtc,
        FinishedAtUtc = receipt.FinishedUtc,
        ValidationEvidenceReceiptId = receipt.ValidationRunId
    };

    private static void ValidateReviewerRequestExecution(
        MergeDecisionPackageInput input,
        List<MergeDecisionPackageBlockReason> incomplete,
        List<MergeDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var receipt = input.ReviewerRequestExecutionReceipt;
        if (receipt is null)
        {
            incomplete.Add(MergeDecisionPackageBlockReason.MissingReviewerRequestExecutionReceipt);
            issues.Add("MissingReviewerRequestExecutionReceipt");
            return;
        }

        if (receipt.ExecutionVerdict != ReviewerRequestExecutionVerdict.Executed ||
            receipt.FailureClassification != ReviewerRequestExecutionFailureKind.None ||
            !receipt.ReviewerRequestAttempted ||
            !receipt.ReviewerRequestAccepted)
        {
            blocked.Add(MergeDecisionPackageBlockReason.ReviewerRequestExecutionNotExecuted);
            issues.Add($"ReviewerRequestExecutionNotExecuted:{receipt.ExecutionVerdict}/{receipt.FailureClassification}");
        }

        if (!receipt.PostStateVerified)
        {
            blocked.Add(MergeDecisionPackageBlockReason.ReviewerRequestPostStateNotVerified);
            issues.Add("ReviewerRequestPostStateNotVerified");
        }

        if (!Same(receipt.Repository, input.Repository) || receipt.PullRequestNumber != input.PullRequestNumber)
        {
            incomplete.Add(MergeDecisionPackageBlockReason.MissingPullRequestIdentity);
            issues.Add("ReviewerRequestReceiptPrMismatch");
        }

        if (!Same(receipt.ExpectedHeadBranch, input.ExpectedHeadBranch) ||
            !Same(receipt.ExpectedHeadSha, input.ExpectedHeadSha) ||
            !Same(receipt.PostState?.HeadSha, input.ExpectedHeadSha))
        {
            blocked.Add(MergeDecisionPackageBlockReason.HeadShaMismatch);
            issues.Add("ReviewerRequestReceiptHeadMismatch");
        }

        if (!Same(receipt.ExpectedBaseBranch, input.ExpectedBaseBranch) ||
            (!string.IsNullOrWhiteSpace(input.ExpectedBaseSha) && !Same(receipt.ExpectedBaseSha, input.ExpectedBaseSha)))
        {
            blocked.Add(MergeDecisionPackageBlockReason.BaseBranchMismatch);
            issues.Add("ReviewerRequestReceiptBaseMismatch");
        }
    }

    private static void ValidateObservedPullRequest(
        MergeDecisionPackageInput input,
        List<MergeDecisionPackageBlockReason> incomplete,
        List<MergeDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var observed = input.ObservedPullRequest;
        if (string.IsNullOrWhiteSpace(input.Repository) ||
            input.PullRequestNumber <= 0 ||
            string.IsNullOrWhiteSpace(input.ExpectedHeadBranch) ||
            string.IsNullOrWhiteSpace(input.ExpectedHeadSha) ||
            string.IsNullOrWhiteSpace(input.ExpectedBaseBranch) ||
            string.IsNullOrWhiteSpace(observed.Repository) ||
            observed.PullRequestNumber <= 0 ||
            string.IsNullOrWhiteSpace(observed.HeadBranch) ||
            string.IsNullOrWhiteSpace(observed.HeadSha) ||
            string.IsNullOrWhiteSpace(observed.BaseBranch) ||
            string.IsNullOrWhiteSpace(observed.Author) ||
            string.IsNullOrWhiteSpace(observed.MergeStateStatus) ||
            string.IsNullOrWhiteSpace(observed.ObservationSource))
        {
            incomplete.Add(MergeDecisionPackageBlockReason.MissingPullRequestIdentity);
            issues.Add("MissingPullRequestIdentity");
        }

        if (!Same(observed.Repository, input.Repository) || observed.PullRequestNumber != input.PullRequestNumber)
        {
            incomplete.Add(MergeDecisionPackageBlockReason.MissingPullRequestIdentity);
            issues.Add("ObservedPullRequestPrMismatch");
        }

        if (!Same(observed.PullRequestState, "open"))
        {
            blocked.Add(MergeDecisionPackageBlockReason.PullRequestNotOpen);
            issues.Add("PullRequestNotOpen");
        }

        if (observed.PullRequestDraft)
        {
            blocked.Add(MergeDecisionPackageBlockReason.PullRequestStillDraft);
            issues.Add("PullRequestStillDraft");
        }

        if (!Same(observed.HeadBranch, input.ExpectedHeadBranch))
        {
            blocked.Add(MergeDecisionPackageBlockReason.HeadBranchMismatch);
            issues.Add("HeadBranchMismatch");
        }

        if (!Same(observed.HeadSha, input.ExpectedHeadSha))
        {
            blocked.Add(MergeDecisionPackageBlockReason.HeadShaMismatch);
            issues.Add("HeadShaMismatch");
        }

        if (!Same(observed.BaseBranch, input.ExpectedBaseBranch))
        {
            blocked.Add(MergeDecisionPackageBlockReason.BaseBranchMismatch);
            issues.Add("BaseBranchMismatch");
        }

        if (!string.IsNullOrWhiteSpace(input.ExpectedBaseSha) && !Same(observed.BaseSha, input.ExpectedBaseSha))
        {
            blocked.Add(MergeDecisionPackageBlockReason.BaseShaMismatch);
            issues.Add("BaseShaMismatch");
        }

        if (observed.HasConflicts)
        {
            blocked.Add(MergeDecisionPackageBlockReason.PullRequestHasConflicts);
            issues.Add("PullRequestHasConflicts");
        }

        if (!observed.Mergeable)
        {
            blocked.Add(MergeDecisionPackageBlockReason.PullRequestNotMergeable);
            issues.Add("PullRequestNotMergeable");
        }
    }

    private static void ValidateReviewEvidence(
        MergeDecisionPackageInput input,
        List<MergeDecisionPackageBlockReason> incomplete,
        List<MergeDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var evidence = input.ReviewEvidence;
        if (evidence is null)
        {
            incomplete.Add(MergeDecisionPackageBlockReason.MissingReviewEvidence);
            issues.Add("MissingReviewEvidence");
            return;
        }

        if (!Same(evidence.ReviewEvidenceHeadSha, input.ExpectedHeadSha))
        {
            blocked.Add(MergeDecisionPackageBlockReason.ReviewEvidenceStale);
            issues.Add("ReviewEvidenceStale");
        }

        var author = NormalizeLogin(input.ObservedPullRequest.Author);
        var decisionMaker = NormalizeLogin(input.MergeDecisionRecord?.DecisionMadeBy);
        var approvers = NormalizeMany(evidence.ApprovingReviewers).ToArray();
        if (approvers.Any(approver => Same(approver, author)))
        {
            blocked.Add(MergeDecisionPackageBlockReason.PullRequestAuthorApprovalNotAllowed);
            issues.Add("PullRequestAuthorApprovalNotAllowed");
        }

        if (evidence.StaleReviewers.Length > 0)
        {
            blocked.Add(MergeDecisionPackageBlockReason.StaleApprovalPresent);
            issues.Add("StaleApprovalPresent");
        }

        if (evidence.RequestedChangesReviewers.Length > 0)
        {
            blocked.Add(MergeDecisionPackageBlockReason.RequestedChangesPresent);
            issues.Add("RequestedChangesPresent");
        }

        if (evidence.UnresolvedReviewThreads.Length > 0)
        {
            blocked.Add(MergeDecisionPackageBlockReason.BlockingReviewThreadsPresent);
            issues.Add("BlockingReviewThreadsPresent");
        }

        var independentApprovers = approvers
            .Where(approver => !Same(approver, author))
            .Where(approver => string.IsNullOrWhiteSpace(decisionMaker) || !Same(approver, decisionMaker))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (evidence.ActualApprovalCount < evidence.RequiredApprovalCount ||
            approvers.Length < evidence.RequiredApprovalCount ||
            independentApprovers < evidence.RequiredApprovalCount)
        {
            blocked.Add(MergeDecisionPackageBlockReason.RequiredApprovalsMissing);
            issues.Add("RequiredApprovalsMissing");
        }
    }

    private static void ValidateValidationEvidence(
        MergeDecisionPackageInput input,
        List<MergeDecisionPackageBlockReason> incomplete,
        List<MergeDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var evidence = input.ValidationEvidence;
        if (evidence is null)
        {
            incomplete.Add(MergeDecisionPackageBlockReason.MissingValidationEvidence);
            issues.Add("MissingValidationEvidence");
            return;
        }

        if (!Same(evidence.CommitSha, input.ExpectedHeadSha))
        {
            blocked.Add(MergeDecisionPackageBlockReason.ValidationEvidenceStale);
            issues.Add("ValidationEvidenceStale");
        }

        if (evidence.Verdict != ValidationRunVerdict.Passed || evidence.FailedLaneNames.Length > 0)
        {
            blocked.Add(MergeDecisionPackageBlockReason.ValidationFailed);
            issues.Add("ValidationFailed");
        }

        var observedFamilies = evidence.RequiredLaneNames
            .Concat(evidence.ResultLaneNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missingRequired = RequiredValidationFamilies
            .Where(required => !observedFamilies.Any(name => ContainsToken(name, required)))
            .ToArray();
        if (missingRequired.Length > 0 || evidence.MissingLaneNames.Length > 0)
        {
            blocked.Add(MergeDecisionPackageBlockReason.RequiredValidationMissing);
            foreach (var missing in missingRequired.Concat(evidence.MissingLaneNames).Distinct(StringComparer.OrdinalIgnoreCase))
                issues.Add($"RequiredValidationMissing:{missing}");
        }
    }

    private static string? ValidateMergeDecision(
        MergeDecisionPackageInput input,
        List<MergeDecisionPackageBlockReason> incomplete,
        List<MergeDecisionPackageBlockReason> blocked,
        List<string> issues)
    {
        var decision = input.MergeDecisionRecord;
        if (decision is null)
        {
            incomplete.Add(MergeDecisionPackageBlockReason.MissingMergeDecision);
            issues.Add("MissingMergeDecision");
            return null;
        }

        if (decision.Decision == MergeDecision.Blocked)
        {
            blocked.Add(MergeDecisionPackageBlockReason.MergeDecisionBlocked);
            issues.Add("MergeDecisionBlocked");
        }
        else if (decision.Decision == MergeDecision.Rejected)
        {
            blocked.Add(MergeDecisionPackageBlockReason.MergeDecisionRejected);
            issues.Add("MergeDecisionRejected");
        }
        else if (decision.Decision != MergeDecision.ApprovedForMergeExecutor)
        {
            blocked.Add(MergeDecisionPackageBlockReason.MergeDecisionBlocked);
            issues.Add($"MergeDecisionUnsupported:{decision.Decision}");
        }

        if (string.IsNullOrWhiteSpace(decision.DecisionMadeBy))
        {
            incomplete.Add(MergeDecisionPackageBlockReason.MissingDecisionMaker);
            issues.Add("MissingDecisionMaker");
        }

        if (Same(decision.DecisionMadeBy, input.ObservedPullRequest.Author))
        {
            blocked.Add(MergeDecisionPackageBlockReason.DecisionMakerIsPullRequestAuthor);
            issues.Add("DecisionMakerIsPullRequestAuthor");
        }

        if (!Same(decision.ExpectedRepository, input.Repository) ||
            decision.ExpectedPullRequestNumber != input.PullRequestNumber ||
            !Same(decision.ExpectedHeadSha, input.ExpectedHeadSha) ||
            !Same(decision.ExpectedBaseBranch, input.ExpectedBaseBranch))
        {
            blocked.Add(MergeDecisionPackageBlockReason.MergeDecisionStale);
            issues.Add("MergeDecisionStale");
        }

        if (string.IsNullOrWhiteSpace(decision.DecisionRationale))
        {
            incomplete.Add(MergeDecisionPackageBlockReason.MissingDecisionRationale);
            issues.Add("MissingDecisionRationale");
        }

        if (string.IsNullOrWhiteSpace(decision.ExpectedMergeStrategy))
        {
            incomplete.Add(MergeDecisionPackageBlockReason.MissingMergeStrategy);
            issues.Add("MissingMergeStrategy");
            return null;
        }

        var strategy = NormalizeStrategy(decision.ExpectedMergeStrategy);
        if (strategy is null)
        {
            blocked.Add(MergeDecisionPackageBlockReason.UnsupportedMergeStrategy);
            issues.Add($"UnsupportedMergeStrategy:{FeedbackText.Safe(decision.ExpectedMergeStrategy)}");
            return null;
        }

        return strategy.Value.ToString();
    }

    private static void ValidateBoundary(List<MergeDecisionPackageBlockReason> blocked, List<string> issues)
    {
        var boundary = MergeDecisionPackageBoundary.Evidence;
        if (!boundary.EvidenceOnly)
        {
            blocked.Add(MergeDecisionPackageBlockReason.BoundaryViolation);
            issues.Add("BoundaryNotEvidenceOnly");
        }

        if (boundary.CanMerge)
        {
            blocked.Add(MergeDecisionPackageBlockReason.MergeMutationNotAllowed);
            issues.Add("MergeMutationNotAllowed");
        }

        if (boundary.CanAutoMerge)
        {
            blocked.Add(MergeDecisionPackageBlockReason.AutoMergeNotAllowed);
            issues.Add("AutoMergeNotAllowed");
        }

        if (boundary.CanApprove || boundary.CanSubmitReview)
        {
            blocked.Add(MergeDecisionPackageBlockReason.ApprovalNotAllowed);
            issues.Add("ApprovalNotAllowed");
        }

        if (boundary.CanResolveReviewThreads || boundary.CanReplyToReviewThreads || boundary.CanRequestReviewers || boundary.CanRemoveReviewers || boundary.CanMarkReadyForReview)
        {
            blocked.Add(MergeDecisionPackageBlockReason.ReviewMutationNotAllowed);
            issues.Add("ReviewMutationNotAllowed");
        }

        if (boundary.CanRelease || boundary.CanDeploy || boundary.CanTag || boundary.CanPublish)
        {
            blocked.Add(MergeDecisionPackageBlockReason.ReleaseDeployNotAllowed);
            issues.Add("ReleaseDeployNotAllowed");
        }

        if (boundary.CanPromoteMemory || boundary.CanContinueWorkflow)
        {
            blocked.Add(MergeDecisionPackageBlockReason.WorkflowContinuationNotAllowed);
            issues.Add("WorkflowContinuationNotAllowed");
        }

        if (boundary.CanCommit || boundary.CanPush || boundary.CanMutateSource || boundary.CanMutateWorkspace)
        {
            blocked.Add(MergeDecisionPackageBlockReason.BoundaryViolation);
            issues.Add("SourceOrWorkspaceMutationNotAllowed");
        }
    }

    private static MergeDecisionPackageVerdict DetermineVerdict(
        IReadOnlyCollection<MergeDecisionPackageBlockReason> blocked,
        IReadOnlyCollection<MergeDecisionPackageBlockReason> incomplete)
    {
        if (blocked.Count > 0)
            return MergeDecisionPackageVerdict.PackageBlocked;
        if (incomplete.Count > 0)
            return MergeDecisionPackageVerdict.PackageIncomplete;
        return MergeDecisionPackageVerdict.PackageReadyForMergeExecutor;
    }

    private static MergeDecisionStrategy? NormalizeStrategy(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace("_", "-", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        return normalized switch
        {
            "merge-commit" or "mergecommit" => MergeDecisionStrategy.MergeCommit,
            "squash" => MergeDecisionStrategy.Squash,
            "rebase" => MergeDecisionStrategy.Rebase,
            _ => null
        };
    }

    private static bool ContainsToken(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLogin(string? value) => (value ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();

    private static IEnumerable<string> NormalizeMany(IEnumerable<string> values) =>
        values.Select(NormalizeLogin).Where(value => !string.IsNullOrWhiteSpace(value));
}

public static class MergeDecisionPackageBypassEvaluator
{
    public static bool CanMarkReadyForReview(object? evidence) => false;
    public static bool CanRequestReviewers(object? evidence) => false;
    public static bool CanRemoveReviewers(object? evidence) => false;
    public static bool CanResolveReviewThreads(object? evidence) => false;
    public static bool CanReplyToReviewThreads(object? evidence) => false;
    public static bool CanApprove(object? evidence) => false;
    public static bool CanSubmitReview(object? evidence) => false;
    public static bool CanMerge(object? evidence) => false;
    public static bool CanAutoMerge(object? evidence) => false;
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

internal static class AxMergeDecisionHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
