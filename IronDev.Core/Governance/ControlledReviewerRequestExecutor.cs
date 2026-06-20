using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum ReviewerRequestExecutionVerdict
{
    Executed = 0,
    Blocked,
    Failed,
    Incomplete
}

public enum ReviewerRequestExecutionFailureKind
{
    None = 0,

    MissingPackage,
    PackageNotEligible,
    PackageBlocked,
    PackageStale,

    PullRequestNotFound,
    PullRequestNotOpen,
    PullRequestStillDraft,

    RepositoryMismatch,
    PullRequestNumberMismatch,
    HeadBranchMismatch,
    HeadShaMismatch,
    BaseBranchMismatch,
    BaseShaMismatch,

    NoExecutableReviewerTargets,
    ReviewerAlreadyRequested,
    TeamAlreadyRequested,
    RequestedReviewerIsRequester,
    RequestedReviewerIsPullRequestAuthor,
    InvalidReviewerTarget,

    ReviewerRequestMutationNotAllowed,
    ReviewerRequestMutationFailed,
    PostReviewerRequestVerificationFailed,

    ReadyForReviewMutationNotAllowed,
    ReviewResolutionNotAllowed,
    ApprovalNotAllowed,
    MergeNotAllowed,
    ReleaseDeployNotAllowed,
    MemoryPromotionNotAllowed,
    WorkflowContinuationNotAllowed,

    BoundaryViolation
}

public sealed record ReviewerRequestExecutionBoundary
{
    public bool CanRequestReviewers { get; init; } = true;

    public bool CanMarkReadyForReview { get; init; }
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

    public static ReviewerRequestExecutionBoundary Executor { get; } = new();
    public static ReviewerRequestExecutionBoundary Blocked { get; } = new()
    {
        CanRequestReviewers = false
    };
}

public static class ReviewerRequestExecutionBoundaryText
{
    public const string Boundary = """
        Block AW consumes an eligible AV reviewer request package and requests only the package-declared reviewers and teams.
        It re-observes PR state before mutation.
        It re-observes PR state after mutation.
        It does not mark ready for review.
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
        Reviewer request execution is not approval.
        """;
}

public sealed record ReviewerRequestExecutionRequest
{
    public ReviewerRequestPackage? Package { get; init; }
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ExpectedBaseBranch { get; init; }
    public string? ExpectedBaseSha { get; init; }
    public required string RequestedBy { get; init; }
    public DateTimeOffset? RequestedAtUtc { get; init; }
    public string? OutputDirectory { get; init; }
    public bool DryRun { get; init; }
}

public sealed record ReviewerRequestExecutionObservedPrState
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
    public string[] RequestedReviewers { get; init; } = [];
    public string[] RequestedTeams { get; init; } = [];
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required bool ObservationSucceeded { get; init; }
    public string? ObservationError { get; init; }
}

public sealed record ReviewerRequestMutationResult
{
    public required bool Attempted { get; init; }
    public required bool Accepted { get; init; }
    public required string Provider { get; init; }
    public required string CommandOrMutationName { get; init; }
    public string[] RequestedReviewers { get; init; } = [];
    public string[] RequestedTeams { get; init; } = [];
    public string? Message { get; init; }
    public string? Error { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public interface IReviewerRequestCommandClient
{
    Task<ReviewerRequestExecutionObservedPrState> ObserveAsync(
        ReviewerRequestExecutionRequest request,
        CancellationToken cancellationToken);

    Task<ReviewerRequestMutationResult> RequestReviewersAsync(
        ReviewerRequestExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record ReviewerRequestExecutionReceipt
{
    public required string ReviewerRequestExecutionId { get; init; }
    public required string ReviewerRequestPackageId { get; init; }
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }

    public ReviewerRequestExecutionObservedPrState? PreState { get; init; }
    public ReviewerRequestExecutionObservedPrState? PostState { get; init; }

    public required string ExpectedHeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ExpectedBaseBranch { get; init; }
    public string? ExpectedBaseSha { get; init; }

    public string[] RequestedReviewers { get; init; } = [];
    public string[] RequestedTeams { get; init; } = [];

    public required bool ReviewerRequestAttempted { get; init; }
    public required bool ReviewerRequestAccepted { get; init; }
    public required bool PostStateVerified { get; init; }

    public required ReviewerRequestExecutionVerdict ExecutionVerdict { get; init; }
    public required ReviewerRequestExecutionFailureKind FailureClassification { get; init; }
    public string[] Issues { get; init; } = [];

    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required DateTimeOffset ExecutedAtUtc { get; init; }

    public ReviewerRequestExecutionBoundary Boundary { get; init; } = ReviewerRequestExecutionBoundary.Executor;
}

public sealed record ReviewerRequestExecutionResult
{
    public required ReviewerRequestExecutionVerdict Verdict { get; init; }
    public required ReviewerRequestExecutionFailureKind FailureKind { get; init; }
    public string[] Issues { get; init; } = [];
    public ReviewerRequestExecutionReceipt? Receipt { get; init; }
}

public static class ReviewerRequestExecutor
{
    public static async Task<ReviewerRequestExecutionResult> ExecuteAsync(
        ReviewerRequestExecutionRequest request,
        IReviewerRequestCommandClient client,
        CancellationToken cancellationToken = default)
    {
        var now = request.RequestedAtUtc ?? DateTimeOffset.UtcNow;
        var package = request.Package;
        var issues = new List<string>();
        if (package is null)
            return Blocked(null, request, null, ReviewerRequestExecutionFailureKind.MissingPackage, ["MissingReviewerRequestPackage"], now);

        var expectedReviewers = ExecutableReviewers(package);
        var expectedTeams = ExecutableTeams(package);
        ValidatePackage(package, request, expectedReviewers, expectedTeams, issues);
        ValidateBoundary(issues);
        if (issues.Count > 0)
            return Blocked(package, request, null, Classify(issues), issues.ToArray(), now);

        var preState = await client.ObserveAsync(request, cancellationToken).ConfigureAwait(false);
        ValidatePreState(package, request, preState, expectedReviewers, expectedTeams, issues);
        if (issues.Count > 0)
            return Blocked(package, request, preState, Classify(issues), issues.ToArray(), now);

        if (request.DryRun)
            return Blocked(package, request, preState, ReviewerRequestExecutionFailureKind.ReviewerRequestMutationNotAllowed, ["DryRunDoesNotRequestReviewers"], now);

        var mutation = await client.RequestReviewersAsync(request, cancellationToken).ConfigureAwait(false);
        if (!mutation.Attempted || !mutation.Accepted)
        {
            var mutationIssues = FeedbackText.SafeList(["ReviewerRequestMutationFailed", mutation.Error ?? mutation.Message ?? string.Empty]);
            return Failed(package, request, preState, preState, mutation, ReviewerRequestExecutionFailureKind.ReviewerRequestMutationFailed, mutationIssues, now, postStateVerified: false);
        }

        var mutationIssuesAfterAccept = new List<string>();
        ValidateMutationTargets(mutation, expectedReviewers, expectedTeams, mutationIssuesAfterAccept);
        if (mutationIssuesAfterAccept.Count > 0)
        {
            return Failed(package, request, preState, preState, mutation, ReviewerRequestExecutionFailureKind.ReviewerRequestMutationFailed, mutationIssuesAfterAccept.ToArray(), now, postStateVerified: false);
        }

        var postState = await client.ObserveAsync(request, cancellationToken).ConfigureAwait(false);
        var postIssues = new List<string>();
        ValidatePostState(package, request, postState, expectedReviewers, expectedTeams, postIssues);
        if (postIssues.Count > 0)
        {
            return Failed(package, request, preState, postState, mutation, ReviewerRequestExecutionFailureKind.PostReviewerRequestVerificationFailed, postIssues.ToArray(), now, postStateVerified: false);
        }

        var receipt = BuildReceipt(
            package,
            request,
            preState,
            postState,
            mutation,
            ReviewerRequestExecutionVerdict.Executed,
            ReviewerRequestExecutionFailureKind.None,
            [],
            now,
            postStateVerified: true,
            ReviewerRequestExecutionBoundary.Executor);
        return new ReviewerRequestExecutionResult
        {
            Verdict = ReviewerRequestExecutionVerdict.Executed,
            FailureKind = ReviewerRequestExecutionFailureKind.None,
            Issues = [],
            Receipt = receipt
        };
    }

    private static void ValidatePackage(
        ReviewerRequestPackage package,
        ReviewerRequestExecutionRequest request,
        string[] expectedReviewers,
        string[] expectedTeams,
        List<string> issues)
    {
        if (package.PackageVerdict != ReviewerRequestPackageVerdict.PackageReadyForReviewerRequestExecutor)
            issues.Add($"PackageNotEligible:{package.PackageVerdict}");
        if (!package.CanRequestReviewersForExecutor)
            issues.Add("PackageCannotRequestReviewersForExecutor");
        if (package.BlockReasons.Length > 0)
            issues.Add("PackageBlocked");
        if (!package.Boundary.EvidenceOnly)
            issues.Add("PackageBoundaryNotEvidenceOnly");
        if (PackageBoundaryCarriesAuthority(package.Boundary))
            issues.Add("PackageBoundaryAuthorityViolation");
        if (expectedReviewers.Length == 0 && expectedTeams.Length == 0)
            issues.Add("NoExecutableReviewerTargets");
        if (!Same(package.Repository, request.Repository))
            issues.Add("RepositoryMismatch");
        if (package.PullRequestNumber != request.PullRequestNumber)
            issues.Add("PullRequestNumberMismatch");
        if (!Same(package.HeadBranch, request.ExpectedHeadBranch))
            issues.Add("HeadBranchMismatch");
        if (!Same(package.ExpectedHeadSha, request.ExpectedHeadSha))
            issues.Add("PackageStale");
        if (!Same(package.BaseBranch, request.ExpectedBaseBranch))
            issues.Add("BaseBranchMismatch");
        if (!string.IsNullOrWhiteSpace(request.ExpectedBaseSha) && !Same(package.BaseSha, request.ExpectedBaseSha))
            issues.Add("BaseShaMismatch");
        if (string.IsNullOrWhiteSpace(package.SourceReadyForReviewExecutionReceiptId) ||
            package.SourceReadyForReviewExecutionReceiptId.Contains("missing", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("MissingSourceReadyForReviewExecutionReceipt");
        }

        foreach (var target in package.RequestedReviewers.Concat(package.RequestedTeams))
        {
            if (target.AlreadyRequested)
                issues.Add(target.Kind == ReviewerRequestTargetKind.GitHubTeam ? "TeamAlreadyRequested" : "ReviewerAlreadyRequested");
            if (target.SelfRequest)
                issues.Add("RequestedReviewerIsRequester");
            if (target.PullRequestAuthorRequest)
                issues.Add("RequestedReviewerIsPullRequestAuthor");
            if (target.Duplicate || !string.IsNullOrWhiteSpace(target.BlockedReason))
                issues.Add("InvalidReviewerTarget");
        }
    }

    private static bool PackageBoundaryCarriesAuthority(ReviewerRequestPackageBoundary boundary) =>
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

    private static void ValidateBoundary(List<string> issues)
    {
        var boundary = ReviewerRequestExecutionBoundary.Executor;
        if (!boundary.CanRequestReviewers)
            issues.Add("ReviewerRequestMutationNotAllowed");
        if (boundary.CanMarkReadyForReview)
            issues.Add("ReadyForReviewMutationNotAllowed");
        if (boundary.CanRemoveReviewers || boundary.CanResolveReviewThreads || boundary.CanReplyToReviewThreads)
            issues.Add("ReviewResolutionNotAllowed");
        if (boundary.CanApprove || boundary.CanSubmitReview)
            issues.Add("ApprovalNotAllowed");
        if (boundary.CanMerge || boundary.CanAutoMerge)
            issues.Add("MergeNotAllowed");
        if (boundary.CanRelease || boundary.CanDeploy || boundary.CanTag || boundary.CanPublish)
            issues.Add("ReleaseDeployNotAllowed");
        if (boundary.CanPromoteMemory)
            issues.Add("MemoryPromotionNotAllowed");
        if (boundary.CanContinueWorkflow)
            issues.Add("WorkflowContinuationNotAllowed");
        if (boundary.CanCommit || boundary.CanPush || boundary.CanMutateSource || boundary.CanMutateWorkspace)
            issues.Add("BoundaryViolation");
    }

    private static void ValidatePreState(
        ReviewerRequestPackage package,
        ReviewerRequestExecutionRequest request,
        ReviewerRequestExecutionObservedPrState observed,
        string[] expectedReviewers,
        string[] expectedTeams,
        List<string> issues)
    {
        if (!observed.ObservationSucceeded)
        {
            issues.Add($"PullRequestNotFound:{observed.ObservationError ?? "observation failed"}");
            return;
        }

        ValidateSharedState(package, request, observed, issues);
        if (!Same(observed.PullRequestState, "open"))
            issues.Add("PullRequestNotOpen");
        if (observed.PullRequestDraft)
            issues.Add("PullRequestStillDraft");

        var alreadyReviewers = new HashSet<string>(NormalizeMany(observed.RequestedReviewers), StringComparer.OrdinalIgnoreCase);
        foreach (var reviewer in expectedReviewers)
        {
            if (alreadyReviewers.Contains(reviewer))
                issues.Add($"ReviewerAlreadyRequested:{reviewer}");
            if (Same(reviewer, NormalizeLogin(request.RequestedBy)))
                issues.Add($"RequestedReviewerIsRequester:{reviewer}");
            if (Same(reviewer, NormalizeLogin(observed.Author)))
                issues.Add($"RequestedReviewerIsPullRequestAuthor:{reviewer}");
        }

        var alreadyTeams = new HashSet<string>(NormalizeMany(observed.RequestedTeams), StringComparer.OrdinalIgnoreCase);
        foreach (var team in expectedTeams)
        {
            if (alreadyTeams.Contains(team))
                issues.Add($"TeamAlreadyRequested:{team}");
        }
    }

    private static void ValidatePostState(
        ReviewerRequestPackage package,
        ReviewerRequestExecutionRequest request,
        ReviewerRequestExecutionObservedPrState observed,
        string[] expectedReviewers,
        string[] expectedTeams,
        List<string> issues)
    {
        if (!observed.ObservationSucceeded)
        {
            issues.Add($"PostReviewerRequestVerificationFailed:{observed.ObservationError ?? "post observation failed"}");
            return;
        }

        ValidateSharedState(package, request, observed, issues);
        if (!Same(observed.PullRequestState, "open"))
            issues.Add("PostReviewerRequestPullRequestNotOpen");
        if (observed.PullRequestDraft)
            issues.Add("PostReviewerRequestPullRequestDraft");

        var reviewers = new HashSet<string>(NormalizeMany(observed.RequestedReviewers), StringComparer.OrdinalIgnoreCase);
        foreach (var reviewer in expectedReviewers)
        {
            if (!reviewers.Contains(reviewer))
                issues.Add($"PostReviewerMissing:{reviewer}");
        }

        var teams = new HashSet<string>(NormalizeMany(observed.RequestedTeams), StringComparer.OrdinalIgnoreCase);
        foreach (var team in expectedTeams)
        {
            if (!teams.Contains(team))
                issues.Add($"PostTeamMissing:{team}");
        }
    }

    private static void ValidateSharedState(
        ReviewerRequestPackage package,
        ReviewerRequestExecutionRequest request,
        ReviewerRequestExecutionObservedPrState observed,
        List<string> issues)
    {
        if (!Same(observed.Repository, request.Repository) || !Same(observed.Repository, package.Repository))
            issues.Add("RepositoryMismatch");
        if (observed.PullRequestNumber != request.PullRequestNumber || observed.PullRequestNumber != package.PullRequestNumber)
            issues.Add("PullRequestNumberMismatch");
        if (!Same(observed.HeadBranch, request.ExpectedHeadBranch) || !Same(observed.HeadBranch, package.HeadBranch))
            issues.Add("HeadBranchMismatch");
        if (!Same(observed.HeadSha, request.ExpectedHeadSha) || !Same(observed.HeadSha, package.ExpectedHeadSha))
            issues.Add("HeadShaMismatch");
        if (!Same(observed.BaseBranch, request.ExpectedBaseBranch) || !Same(observed.BaseBranch, package.BaseBranch))
            issues.Add("BaseBranchMismatch");
        if (!string.IsNullOrWhiteSpace(request.ExpectedBaseSha) &&
            !string.IsNullOrWhiteSpace(package.BaseSha) &&
            !Same(observed.BaseSha, request.ExpectedBaseSha))
        {
            issues.Add("BaseShaMismatch");
        }
    }

    private static void ValidateMutationTargets(
        ReviewerRequestMutationResult mutation,
        string[] expectedReviewers,
        string[] expectedTeams,
        List<string> issues)
    {
        if (!SetEquals(mutation.RequestedReviewers, expectedReviewers))
            issues.Add("ReviewerRequestMutationTargetMismatch");
        if (!SetEquals(mutation.RequestedTeams, expectedTeams))
            issues.Add("TeamRequestMutationTargetMismatch");
    }

    private static ReviewerRequestExecutionResult Blocked(
        ReviewerRequestPackage? package,
        ReviewerRequestExecutionRequest request,
        ReviewerRequestExecutionObservedPrState? preState,
        ReviewerRequestExecutionFailureKind kind,
        string[] issues,
        DateTimeOffset now)
    {
        var receipt = BuildReceipt(
            package,
            request,
            preState,
            preState,
            null,
            ReviewerRequestExecutionVerdict.Blocked,
            kind,
            issues,
            now,
            postStateVerified: false,
            ReviewerRequestExecutionBoundary.Blocked);
        return new ReviewerRequestExecutionResult
        {
            Verdict = ReviewerRequestExecutionVerdict.Blocked,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = receipt
        };
    }

    private static ReviewerRequestExecutionResult Failed(
        ReviewerRequestPackage package,
        ReviewerRequestExecutionRequest request,
        ReviewerRequestExecutionObservedPrState preState,
        ReviewerRequestExecutionObservedPrState postState,
        ReviewerRequestMutationResult mutation,
        ReviewerRequestExecutionFailureKind kind,
        string[] issues,
        DateTimeOffset now,
        bool postStateVerified)
    {
        var receipt = BuildReceipt(
            package,
            request,
            preState,
            postState,
            mutation,
            ReviewerRequestExecutionVerdict.Failed,
            kind,
            issues,
            now,
            postStateVerified,
            ReviewerRequestExecutionBoundary.Executor);
        return new ReviewerRequestExecutionResult
        {
            Verdict = ReviewerRequestExecutionVerdict.Failed,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = receipt
        };
    }

    private static ReviewerRequestExecutionReceipt BuildReceipt(
        ReviewerRequestPackage? package,
        ReviewerRequestExecutionRequest request,
        ReviewerRequestExecutionObservedPrState? preState,
        ReviewerRequestExecutionObservedPrState? postState,
        ReviewerRequestMutationResult? mutation,
        ReviewerRequestExecutionVerdict verdict,
        ReviewerRequestExecutionFailureKind failureKind,
        string[] issues,
        DateTimeOffset now,
        bool postStateVerified,
        ReviewerRequestExecutionBoundary boundary)
    {
        var packageId = package?.ReviewerRequestPackageId ?? "missing-reviewer-request-package";
        var expectedReviewers = package is null ? [] : ExecutableReviewers(package);
        var expectedTeams = package is null ? [] : ExecutableTeams(package);
        return new ReviewerRequestExecutionReceipt
        {
            ReviewerRequestExecutionId = $"reviewer_request_exec_{AwReviewerRequestHashing.ShortHash($"{packageId}|{request.Repository}|{request.PullRequestNumber}|{verdict}|{now:O}")}",
            ReviewerRequestPackageId = packageId,
            Repository = FeedbackText.Safe(request.Repository),
            PullRequestNumber = request.PullRequestNumber,
            PullRequestUrl = FeedbackText.Safe(package?.PullRequestUrl ?? preState?.PullRequestUrl ?? postState?.PullRequestUrl ?? string.Empty),
            PreState = preState,
            PostState = postState,
            ExpectedHeadBranch = FeedbackText.Safe(request.ExpectedHeadBranch),
            ExpectedHeadSha = FeedbackText.Safe(request.ExpectedHeadSha),
            ExpectedBaseBranch = FeedbackText.Safe(request.ExpectedBaseBranch),
            ExpectedBaseSha = FeedbackText.SafeOrNull(request.ExpectedBaseSha),
            RequestedReviewers = FeedbackText.SafeList(expectedReviewers),
            RequestedTeams = FeedbackText.SafeList(expectedTeams),
            ReviewerRequestAttempted = mutation?.Attempted ?? false,
            ReviewerRequestAccepted = mutation?.Accepted ?? false,
            PostStateVerified = postStateVerified,
            ExecutionVerdict = verdict,
            FailureClassification = failureKind,
            Issues = FeedbackText.SafeList(issues),
            RequestedBy = FeedbackText.Safe(request.RequestedBy),
            RequestedAtUtc = request.RequestedAtUtc ?? now,
            ExecutedAtUtc = now,
            Boundary = boundary
        };
    }

    private static ReviewerRequestExecutionFailureKind Classify(IEnumerable<string> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Contains("MissingReviewerRequestPackage", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.MissingPackage;
            if (issue.Contains("PackageStale", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.PackageStale;
            if (issue.Contains("PackageBlocked", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.PackageBlocked;
            if (issue.Contains("Package", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.PackageNotEligible;
            if (issue.Contains("PullRequestNotFound", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.PullRequestNotFound;
            if (issue.Contains("PullRequestNotOpen", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.PullRequestNotOpen;
            if (issue.Contains("PullRequestStillDraft", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.PullRequestStillDraft;
            if (issue.Contains("RepositoryMismatch", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.RepositoryMismatch;
            if (issue.Contains("PullRequestNumberMismatch", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.PullRequestNumberMismatch;
            if (issue.Contains("HeadBranchMismatch", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.HeadBranchMismatch;
            if (issue.Contains("HeadShaMismatch", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.HeadShaMismatch;
            if (issue.Contains("BaseBranchMismatch", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.BaseBranchMismatch;
            if (issue.Contains("BaseShaMismatch", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.BaseShaMismatch;
            if (issue.Contains("NoExecutableReviewerTargets", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.NoExecutableReviewerTargets;
            if (issue.Contains("ReviewerAlreadyRequested", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.ReviewerAlreadyRequested;
            if (issue.Contains("TeamAlreadyRequested", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.TeamAlreadyRequested;
            if (issue.Contains("RequestedReviewerIsRequester", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.RequestedReviewerIsRequester;
            if (issue.Contains("RequestedReviewerIsPullRequestAuthor", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.RequestedReviewerIsPullRequestAuthor;
            if (issue.Contains("InvalidReviewerTarget", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.InvalidReviewerTarget;
            if (issue.Contains("ReviewerRequestMutationNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.ReviewerRequestMutationNotAllowed;
            if (issue.Contains("ReadyForReviewMutationNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.ReadyForReviewMutationNotAllowed;
            if (issue.Contains("ReviewResolutionNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.ReviewResolutionNotAllowed;
            if (issue.Contains("ApprovalNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.ApprovalNotAllowed;
            if (issue.Contains("MergeNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.MergeNotAllowed;
            if (issue.Contains("ReleaseDeployNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.ReleaseDeployNotAllowed;
            if (issue.Contains("MemoryPromotionNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.MemoryPromotionNotAllowed;
            if (issue.Contains("WorkflowContinuationNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReviewerRequestExecutionFailureKind.WorkflowContinuationNotAllowed;
        }

        return ReviewerRequestExecutionFailureKind.BoundaryViolation;
    }

    private static string[] ExecutableReviewers(ReviewerRequestPackage package) =>
        package.RequestedReviewers
            .Where(IsExecutableTarget)
            .Select(target => NormalizeLogin(target.SlugOrLogin))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] ExecutableTeams(ReviewerRequestPackage package) =>
        package.RequestedTeams
            .Where(IsExecutableTarget)
            .Select(target => NormalizeLogin(target.SlugOrLogin))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsExecutableTarget(ReviewerRequestTarget target) =>
        !target.AlreadyRequested &&
        !target.Duplicate &&
        !target.SelfRequest &&
        !target.PullRequestAuthorRequest &&
        string.IsNullOrWhiteSpace(target.BlockedReason);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLogin(string? value) => (value ?? string.Empty).Trim().TrimStart('@').ToLowerInvariant();

    private static IEnumerable<string> NormalizeMany(IEnumerable<string> values) =>
        values.Select(NormalizeLogin).Where(value => !string.IsNullOrWhiteSpace(value));

    private static bool SetEquals(IEnumerable<string> left, IEnumerable<string> right) =>
        new HashSet<string>(NormalizeMany(left), StringComparer.OrdinalIgnoreCase)
            .SetEquals(NormalizeMany(right));
}

public static class ReviewerRequestExecutionBypassEvaluator
{
    public static bool CanRequestReviewers(object? evidence) => false;
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

internal static class AwReviewerRequestHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
