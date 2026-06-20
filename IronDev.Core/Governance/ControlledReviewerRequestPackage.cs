using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public enum ReviewerRequestPackageVerdict
{
    PackageReadyForReviewerRequestExecutor = 0,
    PackageIncomplete,
    PackageBlocked,
    PackageRejected
}

public enum ReviewerRequestPackageBlockReason
{
    MissingReadyForReviewExecutionReceipt = 0,
    ReadyForReviewExecutionNotExecuted,
    ReadyForReviewPostStateNotVerified,
    ReadyForReviewReceiptPrMismatch,
    ReadyForReviewReceiptHeadMismatch,
    ReadyForReviewReceiptBaseMismatch,

    MissingPullRequestIdentity,
    PullRequestNotOpen,
    PullRequestStillDraft,
    HeadBranchMismatch,
    HeadShaMismatch,
    BaseBranchMismatch,
    BaseShaMismatch,

    MissingReviewerTargets,
    MissingRequestRationale,
    DuplicateReviewerTarget,
    ReviewerAlreadyRequested,
    TeamAlreadyRequested,
    RequestedReviewerIsRequester,
    RequestedReviewerIsPullRequestAuthor,
    InvalidReviewerLogin,
    InvalidTeamSlug,
    BlockedBotReviewer,
    TeamMembershipConflict,

    ReviewerRequestMutationNotAllowed,
    ReadyForReviewMutationNotAllowed,
    ApprovalNotAllowed,
    MergeReleaseDeployNotAllowed,
    WorkflowContinuationNotAllowed
}

public enum ReviewerRequestTargetKind
{
    GitHubUser = 0,
    GitHubTeam
}

public sealed record ReviewerRequestPackageBoundary
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

    public static ReviewerRequestPackageBoundary Evidence { get; } = new();
}

public static class ReviewerRequestPackageBoundaryText
{
    public const string Boundary = """
        Block AV packages reviewer request intent for a ready PR.
        It does not request reviewers.
        It does not remove reviewers.
        It does not resolve review threads.
        It does not reply to review threads.
        It does not approve.
        It does not submit a review.
        It does not merge.
        It does not release.
        It does not deploy.
        It does not tag.
        It does not publish.
        It does not promote memory.
        It does not commit.
        It does not push.
        It does not mutate source.
        It does not continue workflow.
        Reviewer request package is not reviewer request execution.
        Reviewer request is not approval.
        Approval is not merge.
        Merge is not release.
        Release is not deployment.
        Validation evidence is not approval.
        No self-approval.
        No hidden mutation.
        """;
}

public sealed record ReviewerRequestTarget
{
    public required ReviewerRequestTargetKind Kind { get; init; }
    public required string SlugOrLogin { get; init; }
    public string? DisplayName { get; init; }
    public required string Reason { get; init; }
    public bool Required { get; init; } = true;
    public bool AlreadyRequested { get; init; }
    public bool Duplicate { get; init; }
    public bool SelfRequest { get; init; }
    public bool PullRequestAuthorRequest { get; init; }
    public bool TeamMembershipUnknown { get; init; }
    public string? BlockedReason { get; init; }
}

public sealed record ReviewerRequestObservedPrState
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
    public string[] ExistingRequestedReviewers { get; init; } = [];
    public string[] ExistingRequestedTeams { get; init; } = [];
    public required string Author { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string ObservationSource { get; init; }
}

public sealed record ReviewerRequestPackageInput
{
    public ReadyForReviewExecutionReceipt? ReadyExecutionReceipt { get; init; }
    public required ReviewerRequestObservedPrState ObservedPullRequest { get; init; }
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ExpectedBaseBranch { get; init; }
    public string? ExpectedBaseSha { get; init; }
    public string[] RequestedReviewers { get; init; } = [];
    public string[] RequestedTeams { get; init; } = [];
    public required string RequestRationale { get; init; }
    public required string RequestedBy { get; init; }
    public DateTimeOffset? PackageCreatedAtUtc { get; init; }
}

public sealed record ReviewerRequestPackage
{
    public required string ReviewerRequestPackageId { get; init; }
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
    public required string SourceReadyForReviewExecutionReceiptId { get; init; }
    public required string SourceReadyForReviewPackageId { get; init; }
    public required ReadyForReviewExecutionVerdict ReadyExecutionVerdict { get; init; }
    public required bool ReadyExecutionPostStateVerified { get; init; }
    public ReviewerRequestTarget[] RequestedReviewers { get; init; } = [];
    public ReviewerRequestTarget[] RequestedTeams { get; init; } = [];
    public string[] AlreadyRequestedReviewers { get; init; } = [];
    public string[] AlreadyRequestedTeams { get; init; } = [];
    public ReviewerRequestTarget[] SkippedReviewerTargets { get; init; } = [];
    public required string RequestRationale { get; init; }
    public required ReviewerRequestPackageVerdict PackageVerdict { get; init; }
    public required bool CanRequestReviewersForExecutor { get; init; }
    public ReviewerRequestPackageBlockReason[] BlockReasons { get; init; } = [];
    public string[] PackageIssues { get; init; } = [];
    public required string CreatedBy { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ReviewerRequestPackageBoundary Boundary { get; init; } = ReviewerRequestPackageBoundary.Evidence;
}

public sealed record ReviewerRequestPackageReceipt
{
    public required string ReviewerRequestReceiptId { get; init; }
    public required string ReviewerRequestPackageId { get; init; }
    public required ReviewerRequestPackageVerdict Verdict { get; init; }
    public required bool CanRequestReviewersForExecutor { get; init; }
    public ReviewerRequestPackageBlockReason[] BlockReasons { get; init; } = [];
    public string[] BoundaryStatements { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public ReviewerRequestPackageBoundary Boundary { get; init; } = ReviewerRequestPackageBoundary.Evidence;
}

public sealed record ReviewerRequestPackageArtifacts
{
    public required ReviewerRequestPackage Package { get; init; }
    public required ReviewerRequestPackageReceipt Receipt { get; init; }
}

public static partial class ReviewerRequestPackageBuilder
{
    private static readonly Regex ReviewerLoginPattern = ReviewerLoginRegex();
    private static readonly Regex TeamSlugPattern = TeamSlugRegex();

    public static ReviewerRequestPackageArtifacts Build(ReviewerRequestPackageInput input)
    {
        var now = input.PackageCreatedAtUtc ?? DateTimeOffset.UtcNow;
        var incomplete = new List<ReviewerRequestPackageBlockReason>();
        var blocked = new List<ReviewerRequestPackageBlockReason>();
        var issues = new List<string>();

        ValidateReadyExecutionReceipt(input, incomplete, blocked, issues);
        ValidateObservedPullRequest(input, incomplete, blocked, issues);
        ValidateBoundary(blocked, issues);

        if (string.IsNullOrWhiteSpace(input.RequestRationale))
        {
            incomplete.Add(ReviewerRequestPackageBlockReason.MissingRequestRationale);
            issues.Add("MissingRequestRationale");
        }

        var targetResult = BuildTargets(input, blocked, incomplete, issues);
        var blockReasons = blocked.Concat(incomplete).Distinct().ToArray();
        var verdict = DetermineVerdict(blocked, incomplete);
        var canRequestForExecutor = verdict == ReviewerRequestPackageVerdict.PackageReadyForReviewerRequestExecutor &&
            (targetResult.NewReviewers.Length > 0 || targetResult.NewTeams.Length > 0);
        var receipt = input.ReadyExecutionReceipt;
        var observed = input.ObservedPullRequest;
        var packageId = $"reviewer_request_pkg_{AvReviewerRequestHashing.ShortHash($"{input.Repository}|{input.PullRequestNumber}|{input.ExpectedHeadSha}|{string.Join(",", targetResult.NewReviewers.Select(t => t.SlugOrLogin))}|{string.Join(",", targetResult.NewTeams.Select(t => t.SlugOrLogin))}|{verdict}")}";
        var package = new ReviewerRequestPackage
        {
            ReviewerRequestPackageId = packageId,
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
            SourceReadyForReviewExecutionReceiptId = FeedbackText.Safe(receipt?.ReadyForReviewExecutionId ?? "missing-ready-for-review-execution-receipt"),
            SourceReadyForReviewPackageId = FeedbackText.Safe(receipt?.ReadyForReviewPackageId ?? "missing-ready-for-review-package"),
            ReadyExecutionVerdict = receipt?.ExecutionVerdict ?? ReadyForReviewExecutionVerdict.Incomplete,
            ReadyExecutionPostStateVerified = receipt?.PostStateVerified ?? false,
            RequestedReviewers = targetResult.NewReviewers,
            RequestedTeams = targetResult.NewTeams,
            AlreadyRequestedReviewers = targetResult.AlreadyReviewers,
            AlreadyRequestedTeams = targetResult.AlreadyTeams,
            SkippedReviewerTargets = targetResult.Skipped,
            RequestRationale = FeedbackText.Safe(input.RequestRationale),
            PackageVerdict = verdict,
            CanRequestReviewersForExecutor = canRequestForExecutor,
            BlockReasons = blockReasons,
            PackageIssues = FeedbackText.SafeList(issues),
            CreatedBy = FeedbackText.Safe(input.RequestedBy),
            CreatedAtUtc = now,
            Boundary = ReviewerRequestPackageBoundary.Evidence
        };
        var packageReceipt = new ReviewerRequestPackageReceipt
        {
            ReviewerRequestReceiptId = $"reviewer_request_receipt_{AvReviewerRequestHashing.ShortHash($"{packageId}|{verdict}|{now:O}")}",
            ReviewerRequestPackageId = packageId,
            Verdict = verdict,
            CanRequestReviewersForExecutor = canRequestForExecutor,
            BlockReasons = blockReasons,
            BoundaryStatements =
            [
                "Reviewer request package is not reviewer request execution.",
                "Reviewer request is not approval.",
                "Approval is not merge.",
                "Merge is not release.",
                "Release is not deployment.",
                "Validation evidence is not approval.",
                "No self-approval.",
                "No hidden mutation."
            ],
            CreatedAtUtc = now,
            Boundary = ReviewerRequestPackageBoundary.Evidence
        };

        return new ReviewerRequestPackageArtifacts
        {
            Package = package,
            Receipt = packageReceipt
        };
    }

    private static void ValidateReadyExecutionReceipt(
        ReviewerRequestPackageInput input,
        List<ReviewerRequestPackageBlockReason> incomplete,
        List<ReviewerRequestPackageBlockReason> blocked,
        List<string> issues)
    {
        var receipt = input.ReadyExecutionReceipt;
        if (receipt is null)
        {
            incomplete.Add(ReviewerRequestPackageBlockReason.MissingReadyForReviewExecutionReceipt);
            issues.Add("MissingReadyForReviewExecutionReceipt");
            return;
        }

        if (receipt.ExecutionVerdict != ReadyForReviewExecutionVerdict.Executed ||
            receipt.FailureClassification != ReadyForReviewExecutionFailureKind.None ||
            !receipt.ReadyTransitionAttempted ||
            !receipt.ReadyTransitionAccepted)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.ReadyForReviewExecutionNotExecuted);
            issues.Add($"ReadyForReviewExecutionNotExecuted:{receipt.ExecutionVerdict}/{receipt.FailureClassification}");
        }

        if (!receipt.PostStateVerified)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.ReadyForReviewPostStateNotVerified);
            issues.Add("ReadyForReviewPostStateNotVerified");
        }

        if (!Same(receipt.Repository, input.Repository) || receipt.PullRequestNumber != input.PullRequestNumber)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.ReadyForReviewReceiptPrMismatch);
            issues.Add("ReadyForReviewReceiptPrMismatch");
        }

        if (!Same(receipt.ExpectedHeadBranch, input.ExpectedHeadBranch) ||
            !Same(receipt.ExpectedHeadSha, input.ExpectedHeadSha) ||
            !Same(receipt.PostState?.HeadSha, input.ExpectedHeadSha))
        {
            blocked.Add(ReviewerRequestPackageBlockReason.ReadyForReviewReceiptHeadMismatch);
            issues.Add("ReadyForReviewReceiptHeadMismatch");
        }

        if (!Same(receipt.ExpectedBaseBranch, input.ExpectedBaseBranch) ||
            (!string.IsNullOrWhiteSpace(input.ExpectedBaseSha) && !Same(receipt.ExpectedBaseSha, input.ExpectedBaseSha)))
        {
            blocked.Add(ReviewerRequestPackageBlockReason.ReadyForReviewReceiptBaseMismatch);
            issues.Add("ReadyForReviewReceiptBaseMismatch");
        }

        if (!Same(receipt.PostState?.PullRequestState, "open") || receipt.PostState?.PullRequestDraft != false)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.ReadyForReviewPostStateNotVerified);
            issues.Add("ReadyForReviewPostStateNotOpenAndReady");
        }
    }

    private static void ValidateObservedPullRequest(
        ReviewerRequestPackageInput input,
        List<ReviewerRequestPackageBlockReason> incomplete,
        List<ReviewerRequestPackageBlockReason> blocked,
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
            string.IsNullOrWhiteSpace(observed.Author))
        {
            incomplete.Add(ReviewerRequestPackageBlockReason.MissingPullRequestIdentity);
            issues.Add("MissingPullRequestIdentity");
        }

        if (!Same(observed.Repository, input.Repository) || observed.PullRequestNumber != input.PullRequestNumber)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.ReadyForReviewReceiptPrMismatch);
            issues.Add("ObservedPullRequestPrMismatch");
        }

        if (!Same(observed.PullRequestState, "open"))
        {
            blocked.Add(ReviewerRequestPackageBlockReason.PullRequestNotOpen);
            issues.Add("PullRequestNotOpen");
        }

        if (observed.PullRequestDraft)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.PullRequestStillDraft);
            issues.Add("PullRequestStillDraft");
        }

        if (!Same(observed.HeadBranch, input.ExpectedHeadBranch))
        {
            blocked.Add(ReviewerRequestPackageBlockReason.HeadBranchMismatch);
            issues.Add("HeadBranchMismatch");
        }

        if (!Same(observed.HeadSha, input.ExpectedHeadSha))
        {
            blocked.Add(ReviewerRequestPackageBlockReason.HeadShaMismatch);
            issues.Add("HeadShaMismatch");
        }

        if (!Same(observed.BaseBranch, input.ExpectedBaseBranch))
        {
            blocked.Add(ReviewerRequestPackageBlockReason.BaseBranchMismatch);
            issues.Add("BaseBranchMismatch");
        }

        if (!string.IsNullOrWhiteSpace(input.ExpectedBaseSha) && !Same(observed.BaseSha, input.ExpectedBaseSha))
        {
            blocked.Add(ReviewerRequestPackageBlockReason.BaseShaMismatch);
            issues.Add("BaseShaMismatch");
        }
    }

    private static ReviewerTargetBuildResult BuildTargets(
        ReviewerRequestPackageInput input,
        List<ReviewerRequestPackageBlockReason> blocked,
        List<ReviewerRequestPackageBlockReason> incomplete,
        List<string> issues)
    {
        var newReviewers = new List<ReviewerRequestTarget>();
        var newTeams = new List<ReviewerRequestTarget>();
        var skipped = new List<ReviewerRequestTarget>();
        var alreadyReviewers = new HashSet<string>(NormalizeMany(input.ObservedPullRequest.ExistingRequestedReviewers), StringComparer.OrdinalIgnoreCase);
        var alreadyTeams = new HashSet<string>(NormalizeMany(input.ObservedPullRequest.ExistingRequestedTeams), StringComparer.OrdinalIgnoreCase);
        var seenReviewers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requestedBy = NormalizeLogin(input.RequestedBy);
        var author = NormalizeLogin(input.ObservedPullRequest.Author);

        foreach (var reviewer in input.RequestedReviewers)
        {
            var normalized = NormalizeLogin(reviewer);
            var duplicate = !string.IsNullOrWhiteSpace(normalized) && !seenReviewers.Add(normalized);
            var already = !string.IsNullOrWhiteSpace(normalized) && alreadyReviewers.Contains(normalized);
            var self = !string.IsNullOrWhiteSpace(normalized) && Same(normalized, requestedBy);
            var authorRequest = !string.IsNullOrWhiteSpace(normalized) && Same(normalized, author);
            var blockedReason = default(string?);
            if (string.IsNullOrWhiteSpace(normalized) || !ReviewerLoginPattern.IsMatch(normalized))
            {
                blocked.Add(ReviewerRequestPackageBlockReason.InvalidReviewerLogin);
                issues.Add($"InvalidReviewerLogin:{FeedbackText.Safe(reviewer)}");
                blockedReason = ReviewerRequestPackageBlockReason.InvalidReviewerLogin.ToString();
            }
            else if (IsBlockedBot(normalized))
            {
                blocked.Add(ReviewerRequestPackageBlockReason.BlockedBotReviewer);
                issues.Add($"BlockedBotReviewer:{normalized}");
                blockedReason = ReviewerRequestPackageBlockReason.BlockedBotReviewer.ToString();
            }
            else if (duplicate)
            {
                blocked.Add(ReviewerRequestPackageBlockReason.DuplicateReviewerTarget);
                issues.Add($"DuplicateReviewerTarget:{normalized}");
                blockedReason = ReviewerRequestPackageBlockReason.DuplicateReviewerTarget.ToString();
            }
            else if (self)
            {
                blocked.Add(ReviewerRequestPackageBlockReason.RequestedReviewerIsRequester);
                issues.Add($"RequestedReviewerIsRequester:{normalized}");
                blockedReason = ReviewerRequestPackageBlockReason.RequestedReviewerIsRequester.ToString();
            }
            else if (authorRequest)
            {
                blocked.Add(ReviewerRequestPackageBlockReason.RequestedReviewerIsPullRequestAuthor);
                issues.Add($"RequestedReviewerIsPullRequestAuthor:{normalized}");
                blockedReason = ReviewerRequestPackageBlockReason.RequestedReviewerIsPullRequestAuthor.ToString();
            }
            else if (already)
            {
                issues.Add($"ReviewerAlreadyRequested:{normalized}");
                blockedReason = "AlreadySatisfied";
            }

            var target = new ReviewerRequestTarget
            {
                Kind = ReviewerRequestTargetKind.GitHubUser,
                SlugOrLogin = FeedbackText.Safe(normalized),
                Reason = FeedbackText.Safe(input.RequestRationale),
                AlreadyRequested = already,
                Duplicate = duplicate,
                SelfRequest = self,
                PullRequestAuthorRequest = authorRequest,
                BlockedReason = blockedReason
            };
            if (blockedReason is null)
                newReviewers.Add(target);
            else
                skipped.Add(target);
        }

        foreach (var team in input.RequestedTeams)
        {
            var normalized = NormalizeTeamSlug(team);
            var duplicate = !string.IsNullOrWhiteSpace(normalized) && !seenTeams.Add(normalized);
            var already = !string.IsNullOrWhiteSpace(normalized) && alreadyTeams.Contains(normalized);
            var blockedReason = default(string?);
            if (string.IsNullOrWhiteSpace(normalized) || !TeamSlugPattern.IsMatch(normalized))
            {
                blocked.Add(ReviewerRequestPackageBlockReason.InvalidTeamSlug);
                issues.Add($"InvalidTeamSlug:{FeedbackText.Safe(team)}");
                blockedReason = ReviewerRequestPackageBlockReason.InvalidTeamSlug.ToString();
            }
            else if (duplicate)
            {
                blocked.Add(ReviewerRequestPackageBlockReason.DuplicateReviewerTarget);
                issues.Add($"DuplicateReviewerTarget:{normalized}");
                blockedReason = ReviewerRequestPackageBlockReason.DuplicateReviewerTarget.ToString();
            }
            else if (already)
            {
                issues.Add($"TeamAlreadyRequested:{normalized}");
                blockedReason = "AlreadySatisfied";
            }

            var target = new ReviewerRequestTarget
            {
                Kind = ReviewerRequestTargetKind.GitHubTeam,
                SlugOrLogin = FeedbackText.Safe(normalized),
                Reason = FeedbackText.Safe(input.RequestRationale),
                AlreadyRequested = already,
                Duplicate = duplicate,
                TeamMembershipUnknown = true,
                BlockedReason = blockedReason
            };
            if (blockedReason is null)
                newTeams.Add(target);
            else
                skipped.Add(target);
        }

        if (input.RequestedReviewers.Length == 0 && input.RequestedTeams.Length == 0)
        {
            incomplete.Add(ReviewerRequestPackageBlockReason.MissingReviewerTargets);
            issues.Add("MissingReviewerTargets");
        }

        if ((input.RequestedReviewers.Length > 0 || input.RequestedTeams.Length > 0) &&
            newReviewers.Count == 0 &&
            newTeams.Count == 0 &&
            skipped.Any(target => target.AlreadyRequested))
        {
            incomplete.Add(ReviewerRequestPackageBlockReason.MissingReviewerTargets);
            if (skipped.Any(target => target.Kind == ReviewerRequestTargetKind.GitHubUser && target.AlreadyRequested))
                incomplete.Add(ReviewerRequestPackageBlockReason.ReviewerAlreadyRequested);
            if (skipped.Any(target => target.Kind == ReviewerRequestTargetKind.GitHubTeam && target.AlreadyRequested))
                incomplete.Add(ReviewerRequestPackageBlockReason.TeamAlreadyRequested);
            issues.Add("NoNewReviewerTargetsToRequest");
        }

        return new ReviewerTargetBuildResult(
            newReviewers.ToArray(),
            newTeams.ToArray(),
            alreadyReviewers.ToArray(),
            alreadyTeams.ToArray(),
            skipped.ToArray());
    }

    private static void ValidateBoundary(List<ReviewerRequestPackageBlockReason> blocked, List<string> issues)
    {
        var boundary = ReviewerRequestPackageBoundary.Evidence;
        if (!boundary.EvidenceOnly)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.ReviewerRequestMutationNotAllowed);
            issues.Add("BoundaryNotEvidenceOnly");
        }

        if (boundary.CanRequestReviewers || boundary.CanRemoveReviewers)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.ReviewerRequestMutationNotAllowed);
            issues.Add("ReviewerRequestMutationNotAllowed");
        }

        if (boundary.CanMarkReadyForReview)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.ReadyForReviewMutationNotAllowed);
            issues.Add("ReadyForReviewMutationNotAllowed");
        }

        if (boundary.CanApprove || boundary.CanSubmitReview || boundary.CanResolveReviewThreads || boundary.CanReplyToReviewThreads)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.ApprovalNotAllowed);
            issues.Add("ApprovalOrReviewMutationNotAllowed");
        }

        if (boundary.CanMerge || boundary.CanAutoMerge || boundary.CanRelease || boundary.CanDeploy || boundary.CanTag || boundary.CanPublish)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.MergeReleaseDeployNotAllowed);
            issues.Add("MergeReleaseDeployNotAllowed");
        }

        if (boundary.CanPromoteMemory || boundary.CanContinueWorkflow || boundary.CanCommit || boundary.CanPush || boundary.CanMutateSource || boundary.CanMutateWorkspace)
        {
            blocked.Add(ReviewerRequestPackageBlockReason.WorkflowContinuationNotAllowed);
            issues.Add("WorkflowContinuationNotAllowed");
        }
    }

    private static ReviewerRequestPackageVerdict DetermineVerdict(
        IReadOnlyCollection<ReviewerRequestPackageBlockReason> blocked,
        IReadOnlyCollection<ReviewerRequestPackageBlockReason> incomplete)
    {
        if (blocked.Count > 0)
            return ReviewerRequestPackageVerdict.PackageBlocked;
        if (incomplete.Count > 0)
            return ReviewerRequestPackageVerdict.PackageIncomplete;
        return ReviewerRequestPackageVerdict.PackageReadyForReviewerRequestExecutor;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLogin(string? value) => (value ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();

    private static string NormalizeTeamSlug(string? value) => (value ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();

    private static IEnumerable<string> NormalizeMany(IEnumerable<string> values) =>
        values.Select(NormalizeLogin).Where(value => !string.IsNullOrWhiteSpace(value));

    private static bool IsBlockedBot(string value) =>
        value.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith("-bot", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,37}[a-z0-9])?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReviewerLoginRegex();

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{0,99}[a-z0-9])?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TeamSlugRegex();

    private sealed record ReviewerTargetBuildResult(
        ReviewerRequestTarget[] NewReviewers,
        ReviewerRequestTarget[] NewTeams,
        string[] AlreadyReviewers,
        string[] AlreadyTeams,
        ReviewerRequestTarget[] Skipped);
}

public static class ReviewerRequestPackageBypassEvaluator
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
}

internal static class AvReviewerRequestHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
