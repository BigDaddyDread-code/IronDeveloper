using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum ReadyForReviewExecutionVerdict
{
    Executed = 0,
    Blocked,
    Failed,
    Incomplete
}

public enum ReadyForReviewExecutionFailureKind
{
    None = 0,
    MissingPackage,
    PackageNotEligible,
    PackageBlocked,
    PackageStale,
    PullRequestNotFound,
    PullRequestNotOpen,
    PullRequestAlreadyReady,
    PullRequestNotDraft,
    PullRequestNumberMismatch,
    RepositoryMismatch,
    HeadBranchMismatch,
    HeadShaMismatch,
    BaseBranchMismatch,
    BaseShaMismatch,
    ReadyMutationNotAllowed,
    ReadyMutationFailed,
    PostReadyVerificationFailed,
    ReviewerRequestNotAllowed,
    ReviewResolutionNotAllowed,
    ApprovalNotAllowed,
    MergeNotAllowed,
    ReleaseDeployNotAllowed,
    MemoryPromotionNotAllowed,
    WorkflowContinuationNotAllowed,
    BoundaryViolation
}

public sealed record ReadyForReviewExecutionBoundary
{
    public bool CanMarkReadyForReview { get; init; } = true;
    public bool CanRequestReviewers { get; init; }
    public bool CanResolveReviewThreads { get; init; }
    public bool CanApprove { get; init; }
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

    public static ReadyForReviewExecutionBoundary Executor { get; } = new();
    public static ReadyForReviewExecutionBoundary Blocked { get; } = new()
    {
        CanMarkReadyForReview = false
    };
}

public static class ReadyForReviewExecutionBoundaryText
{
    public const string Boundary = """
        Block AU consumes an eligible AT ready-for-review package and marks only the expected draft PR ready for review.
        It re-observes PR state before mutation.
        It re-observes PR state after mutation.
        It does not request reviewers.
        It does not resolve review threads.
        It does not approve.
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
        Ready-for-review execution is not reviewer request.
        """;
}

public sealed record ReadyForReviewExecutionRequest
{
    public ReadyForReviewEligibilityPackage? Package { get; init; }
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

public sealed record ReadyForReviewObservedPrState
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
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required bool ObservationSucceeded { get; init; }
    public string? ObservationError { get; init; }
}

public sealed record ReadyForReviewMutationResult
{
    public required bool Attempted { get; init; }
    public required bool Accepted { get; init; }
    public required string Provider { get; init; }
    public required string CommandOrMutationName { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public interface IReadyForReviewCommandClient
{
    Task<ReadyForReviewObservedPrState> ObserveAsync(
        ReadyForReviewExecutionRequest request,
        CancellationToken cancellationToken);

    Task<ReadyForReviewMutationResult> MarkReadyAsync(
        ReadyForReviewExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record ReadyForReviewExecutionReceipt
{
    public required string ReadyForReviewExecutionId { get; init; }
    public required string ReadyForReviewPackageId { get; init; }
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }
    public ReadyForReviewObservedPrState? PreState { get; init; }
    public ReadyForReviewObservedPrState? PostState { get; init; }
    public required string ExpectedHeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ExpectedBaseBranch { get; init; }
    public string? ExpectedBaseSha { get; init; }
    public required bool ReadyTransitionAttempted { get; init; }
    public required bool ReadyTransitionAccepted { get; init; }
    public required bool PostStateVerified { get; init; }
    public required ReadyForReviewExecutionVerdict ExecutionVerdict { get; init; }
    public required ReadyForReviewExecutionFailureKind FailureClassification { get; init; }
    public string[] Issues { get; init; } = [];
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required DateTimeOffset ExecutedAtUtc { get; init; }
    public ReadyForReviewExecutionBoundary Boundary { get; init; } = ReadyForReviewExecutionBoundary.Executor;
}

public sealed record ReadyForReviewExecutionResult
{
    public required ReadyForReviewExecutionVerdict Verdict { get; init; }
    public required ReadyForReviewExecutionFailureKind FailureKind { get; init; }
    public string[] Issues { get; init; } = [];
    public ReadyForReviewExecutionReceipt? Receipt { get; init; }
}

public static class ReadyForReviewExecutor
{
    public static async Task<ReadyForReviewExecutionResult> ExecuteAsync(
        ReadyForReviewExecutionRequest request,
        IReadyForReviewCommandClient client,
        CancellationToken cancellationToken = default)
    {
        var now = request.RequestedAtUtc ?? DateTimeOffset.UtcNow;
        var package = request.Package;
        var issues = new List<string>();
        if (package is null)
            return Blocked(null, request, null, ReadyForReviewExecutionFailureKind.MissingPackage, ["MissingReadyForReviewPackage"], now);

        ValidatePackage(package, request, issues);
        ValidateBoundary(issues);
        if (issues.Count > 0)
            return Blocked(package, request, null, Classify(issues), issues.ToArray(), now);

        var preState = await client.ObserveAsync(request, cancellationToken).ConfigureAwait(false);
        ValidatePreState(package, request, preState, issues);
        if (issues.Count > 0)
            return Blocked(package, request, preState, Classify(issues), issues.ToArray(), now);

        if (request.DryRun)
            return Blocked(package, request, preState, ReadyForReviewExecutionFailureKind.ReadyMutationNotAllowed, ["DryRunDoesNotMarkReady"], now);

        var mutation = await client.MarkReadyAsync(request, cancellationToken).ConfigureAwait(false);
        if (!mutation.Attempted || !mutation.Accepted)
        {
            var mutationIssues = FeedbackText.SafeList(["ReadyMutationFailed", mutation.Error ?? mutation.Message ?? string.Empty]);
            return Failed(package, request, preState, preState, mutation, ReadyForReviewExecutionFailureKind.ReadyMutationFailed, mutationIssues, now, postStateVerified: false);
        }

        var postState = await client.ObserveAsync(request, cancellationToken).ConfigureAwait(false);
        var postIssues = new List<string>();
        ValidatePostState(package, request, postState, postIssues);
        if (postIssues.Count > 0)
        {
            return Failed(
                package,
                request,
                preState,
                postState,
                mutation,
                ReadyForReviewExecutionFailureKind.PostReadyVerificationFailed,
                postIssues.ToArray(),
                now,
                postStateVerified: false);
        }

        var receipt = BuildReceipt(
            package,
            request,
            preState,
            postState,
            mutation,
            ReadyForReviewExecutionVerdict.Executed,
            ReadyForReviewExecutionFailureKind.None,
            [],
            now,
            postStateVerified: true,
            ReadyForReviewExecutionBoundary.Executor);
        return new ReadyForReviewExecutionResult
        {
            Verdict = ReadyForReviewExecutionVerdict.Executed,
            FailureKind = ReadyForReviewExecutionFailureKind.None,
            Issues = [],
            Receipt = receipt
        };
    }

    private static void ValidatePackage(ReadyForReviewEligibilityPackage package, ReadyForReviewExecutionRequest request, List<string> issues)
    {
        if (package.Verdict != ReadyForReviewEligibilityVerdict.EligibleForReadyExecutor)
            issues.Add($"PackageNotEligible:{package.Verdict}");
        if (!package.CanMarkReadyForReview)
            issues.Add("PackageCannotMarkReady");
        if (package.BlockReasons.Length > 0)
            issues.Add("PackageBlocked");
        if (!package.Boundary.EvidenceOnly)
            issues.Add("PackageBoundaryNotEvidenceOnly");
        if (!Same(package.Target.PullRequestState, "open"))
            issues.Add("PackageTargetPrNotOpen");
        if (!package.Target.PullRequestDraft)
            issues.Add("PackageTargetPrNotDraft");
        if (!Same(package.Target.ExpectedHeadSha, package.Target.ObservedHeadSha))
            issues.Add("PackageTargetHeadObservationMismatch");
        if (!Same(package.Target.Repository, request.Repository))
            issues.Add("RepositoryMismatch");
        if (package.Target.PullRequestNumber != request.PullRequestNumber)
            issues.Add("PullRequestNumberMismatch");
        if (!Same(package.Target.HeadBranch, request.ExpectedHeadBranch))
            issues.Add("HeadBranchMismatch");
        if (!Same(package.Target.ExpectedHeadSha, request.ExpectedHeadSha))
            issues.Add("PackageStale");
        if (!Same(package.Target.BaseBranch, request.ExpectedBaseBranch))
            issues.Add("BaseBranchMismatch");
        if (!string.IsNullOrWhiteSpace(request.ExpectedBaseSha) && !Same(package.Target.BaseSha, request.ExpectedBaseSha))
            issues.Add("BaseShaMismatch");
        if (package.ValidationEvidence.Length == 0 || package.MissingValidationFamilies.Length > 0)
            issues.Add("PackageMissingValidationEvidence");
        if (string.IsNullOrWhiteSpace(package.PhaseAuthorityReceiptId) || !package.PhaseAuthorityReceiptValid)
            issues.Add("PackageMissingPhaseAuthorityEvidence");
    }

    private static void ValidateBoundary(List<string> issues)
    {
        var boundary = ReadyForReviewExecutionBoundary.Executor;
        if (!boundary.CanMarkReadyForReview)
            issues.Add("ReadyMutationNotAllowed");
        if (boundary.CanRequestReviewers)
            issues.Add("ReviewerRequestNotAllowed");
        if (boundary.CanResolveReviewThreads)
            issues.Add("ReviewResolutionNotAllowed");
        if (boundary.CanApprove)
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
        ReadyForReviewEligibilityPackage package,
        ReadyForReviewExecutionRequest request,
        ReadyForReviewObservedPrState observed,
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
        if (!observed.PullRequestDraft)
        {
            issues.Add("PullRequestAlreadyReady");
            issues.Add("PullRequestNotDraft");
        }
    }

    private static void ValidatePostState(
        ReadyForReviewEligibilityPackage package,
        ReadyForReviewExecutionRequest request,
        ReadyForReviewObservedPrState observed,
        List<string> issues)
    {
        if (!observed.ObservationSucceeded)
        {
            issues.Add($"PostReadyVerificationFailed:{observed.ObservationError ?? "post observation failed"}");
            return;
        }

        ValidateSharedState(package, request, observed, issues);
        if (!Same(observed.PullRequestState, "open"))
            issues.Add("PostReadyPullRequestNotOpen");
        if (observed.PullRequestDraft)
            issues.Add("PostReadyStillDraft");
    }

    private static void ValidateSharedState(
        ReadyForReviewEligibilityPackage package,
        ReadyForReviewExecutionRequest request,
        ReadyForReviewObservedPrState observed,
        List<string> issues)
    {
        if (!Same(observed.Repository, request.Repository) || !Same(observed.Repository, package.Target.Repository))
            issues.Add("RepositoryMismatch");
        if (observed.PullRequestNumber != request.PullRequestNumber || observed.PullRequestNumber != package.Target.PullRequestNumber)
            issues.Add("PullRequestNumberMismatch");
        if (!Same(observed.HeadBranch, request.ExpectedHeadBranch) || !Same(observed.HeadBranch, package.Target.HeadBranch))
            issues.Add("HeadBranchMismatch");
        if (!Same(observed.HeadSha, request.ExpectedHeadSha) || !Same(observed.HeadSha, package.Target.ExpectedHeadSha))
            issues.Add("HeadShaMismatch");
        if (!Same(observed.BaseBranch, request.ExpectedBaseBranch) || !Same(observed.BaseBranch, package.Target.BaseBranch))
            issues.Add("BaseBranchMismatch");
        if (!string.IsNullOrWhiteSpace(request.ExpectedBaseSha) &&
            !string.IsNullOrWhiteSpace(package.Target.BaseSha) &&
            !Same(observed.BaseSha, request.ExpectedBaseSha))
        {
            issues.Add("BaseShaMismatch");
        }
    }

    private static ReadyForReviewExecutionResult Blocked(
        ReadyForReviewEligibilityPackage? package,
        ReadyForReviewExecutionRequest request,
        ReadyForReviewObservedPrState? preState,
        ReadyForReviewExecutionFailureKind kind,
        string[] issues,
        DateTimeOffset now)
    {
        var receipt = BuildReceipt(
            package,
            request,
            preState,
            preState,
            null,
            ReadyForReviewExecutionVerdict.Blocked,
            kind,
            issues,
            now,
            postStateVerified: false,
            ReadyForReviewExecutionBoundary.Blocked);
        return new ReadyForReviewExecutionResult
        {
            Verdict = ReadyForReviewExecutionVerdict.Blocked,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = receipt
        };
    }

    private static ReadyForReviewExecutionResult Failed(
        ReadyForReviewEligibilityPackage package,
        ReadyForReviewExecutionRequest request,
        ReadyForReviewObservedPrState preState,
        ReadyForReviewObservedPrState postState,
        ReadyForReviewMutationResult mutation,
        ReadyForReviewExecutionFailureKind kind,
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
            ReadyForReviewExecutionVerdict.Failed,
            kind,
            issues,
            now,
            postStateVerified,
            ReadyForReviewExecutionBoundary.Executor);
        return new ReadyForReviewExecutionResult
        {
            Verdict = ReadyForReviewExecutionVerdict.Failed,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = receipt
        };
    }

    private static ReadyForReviewExecutionReceipt BuildReceipt(
        ReadyForReviewEligibilityPackage? package,
        ReadyForReviewExecutionRequest request,
        ReadyForReviewObservedPrState? preState,
        ReadyForReviewObservedPrState? postState,
        ReadyForReviewMutationResult? mutation,
        ReadyForReviewExecutionVerdict verdict,
        ReadyForReviewExecutionFailureKind failureKind,
        string[] issues,
        DateTimeOffset now,
        bool postStateVerified,
        ReadyForReviewExecutionBoundary boundary)
    {
        var packageId = package?.ReadyForReviewPackageId ?? "missing-ready-for-review-package";
        var url = package?.Target.PullRequestUrl ?? preState?.PullRequestUrl ?? postState?.PullRequestUrl ?? string.Empty;
        return new ReadyForReviewExecutionReceipt
        {
            ReadyForReviewExecutionId = $"ready_review_exec_{AuReadyForReviewHashing.ShortHash($"{packageId}|{request.Repository}|{request.PullRequestNumber}|{verdict}|{now:O}")}",
            ReadyForReviewPackageId = packageId,
            Repository = FeedbackText.Safe(request.Repository),
            PullRequestNumber = request.PullRequestNumber,
            PullRequestUrl = FeedbackText.Safe(url),
            PreState = preState,
            PostState = postState,
            ExpectedHeadBranch = FeedbackText.Safe(request.ExpectedHeadBranch),
            ExpectedHeadSha = FeedbackText.Safe(request.ExpectedHeadSha),
            ExpectedBaseBranch = FeedbackText.Safe(request.ExpectedBaseBranch),
            ExpectedBaseSha = FeedbackText.SafeOrNull(request.ExpectedBaseSha),
            ReadyTransitionAttempted = mutation?.Attempted ?? false,
            ReadyTransitionAccepted = mutation?.Accepted ?? false,
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

    private static ReadyForReviewExecutionFailureKind Classify(IEnumerable<string> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Contains("MissingReadyForReviewPackage", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.MissingPackage;
            if (issue.Contains("PackageStale", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.PackageStale;
            if (issue.Contains("PackageBlocked", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.PackageBlocked;
            if (issue.Contains("Package", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.PackageNotEligible;
            if (issue.Contains("PullRequestNotFound", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.PullRequestNotFound;
            if (issue.Contains("PullRequestNotOpen", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.PullRequestNotOpen;
            if (issue.Contains("PullRequestAlreadyReady", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.PullRequestAlreadyReady;
            if (issue.Contains("PullRequestNotDraft", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.PullRequestNotDraft;
            if (issue.Contains("PullRequestNumberMismatch", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.PullRequestNumberMismatch;
            if (issue.Contains("RepositoryMismatch", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.RepositoryMismatch;
            if (issue.Contains("HeadBranchMismatch", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.HeadBranchMismatch;
            if (issue.Contains("HeadShaMismatch", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.HeadShaMismatch;
            if (issue.Contains("BaseBranchMismatch", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.BaseBranchMismatch;
            if (issue.Contains("BaseShaMismatch", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.BaseShaMismatch;
            if (issue.Contains("ReadyMutationNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.ReadyMutationNotAllowed;
            if (issue.Contains("ReviewerRequestNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.ReviewerRequestNotAllowed;
            if (issue.Contains("ReviewResolutionNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.ReviewResolutionNotAllowed;
            if (issue.Contains("ApprovalNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.ApprovalNotAllowed;
            if (issue.Contains("MergeNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.MergeNotAllowed;
            if (issue.Contains("ReleaseDeployNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.ReleaseDeployNotAllowed;
            if (issue.Contains("MemoryPromotionNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.MemoryPromotionNotAllowed;
            if (issue.Contains("WorkflowContinuationNotAllowed", StringComparison.OrdinalIgnoreCase)) return ReadyForReviewExecutionFailureKind.WorkflowContinuationNotAllowed;
        }

        return ReadyForReviewExecutionFailureKind.BoundaryViolation;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class ReadyForReviewExecutionBypassEvaluator
{
    public static bool CanMarkReadyForReview(object? evidence) => false;
    public static bool CanRequestReviewers(object? evidence) => false;
    public static bool CanResolveReviewThreads(object? evidence) => false;
    public static bool CanApprove(object? evidence) => false;
    public static bool CanMerge(object? evidence) => false;
    public static bool CanAutoMerge(object? evidence) => false;
    public static bool CanRelease(object? evidence) => false;
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanTag(object? evidence) => false;
    public static bool CanPublish(object? evidence) => false;
    public static bool CanPromoteMemory(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
}

internal static class AuReadyForReviewHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
