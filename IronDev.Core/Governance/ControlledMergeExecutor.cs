using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum MergeExecutionVerdict
{
    Executed = 0,
    Blocked,
    Failed,
    Incomplete
}

public enum MergeExecutionFailureKind
{
    None = 0,

    MissingPackage,
    PackageNotEligible,
    PackageBlocked,
    PackageStale,

    PullRequestNotFound,
    PullRequestNotOpen,
    PullRequestStillDraft,
    PullRequestAlreadyMerged,

    RepositoryMismatch,
    PullRequestNumberMismatch,
    HeadBranchMismatch,
    HeadShaMismatch,
    BaseBranchMismatch,
    BaseShaMismatch,

    PullRequestHasConflicts,
    PullRequestNotMergeable,
    PullRequestBehindBase,

    MissingMergeStrategy,
    UnsupportedMergeStrategy,
    MergeStrategyOverrideNotAllowed,

    MergeMutationNotAllowed,
    MergeMutationFailed,
    PostMergeVerificationFailed,

    AutoMergeNotAllowed,
    ApprovalNotAllowed,
    ReviewMutationNotAllowed,
    ReviewerRequestMutationNotAllowed,
    ReleaseDeployNotAllowed,
    MemoryPromotionNotAllowed,
    WorkflowContinuationNotAllowed,
    CommitPushNotAllowed,
    SourceMutationNotAllowed,

    BoundaryViolation
}

public sealed record MergeExecutionBoundary
{
    public bool CanMerge { get; init; } = true;

    public bool CanAutoMerge { get; init; }
    public bool CanMarkReadyForReview { get; init; }
    public bool CanRequestReviewers { get; init; }
    public bool CanRemoveReviewers { get; init; }
    public bool CanResolveReviewThreads { get; init; }
    public bool CanReplyToReviewThreads { get; init; }
    public bool CanApprove { get; init; }
    public bool CanSubmitReview { get; init; }
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

    public static MergeExecutionBoundary Executor { get; } = new();

    public static MergeExecutionBoundary Blocked { get; } = new()
    {
        CanMerge = false
    };
}

public static class MergeExecutionBoundaryText
{
    public const string Boundary = """
        Block AY consumes an eligible AX merge decision package and merges only the expected PR head into the expected base branch.
        It re-observes PR state before mutation.
        It re-observes PR state after mutation.
        It does not enable auto-merge.
        It does not approve.
        It does not submit reviews.
        It does not resolve review threads.
        It does not request reviewers.
        It does not mark ready for review.
        It does not release.
        It does not deploy.
        It does not tag.
        It does not publish.
        It does not promote memory.
        It does not commit locally.
        It does not push local branches.
        It does not mutate source.
        It does not continue workflow.
        Merge decision package is not merge execution.
        Merge execution is not release.
        Release is not deployment.
        Merge execution is not tag creation.
        Merge execution is not publishing.
        Merge execution is not memory promotion.
        Merge execution is not workflow continuation.
        Approval is not merge.
        Validation evidence is not approval.
        No self-approval.
        No hidden mutation.
        """;
}

public sealed record MergeExecutionRequest
{
    public MergeDecisionPackage? Package { get; init; }
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ExpectedBaseBranch { get; init; }
    public string? ExpectedBaseSha { get; init; }
    public string? RequestedMergeStrategy { get; init; }
    public required string RequestedBy { get; init; }
    public DateTimeOffset? RequestedAtUtc { get; init; }
    public string? OutputDirectory { get; init; }
    public bool DryRun { get; init; }
}

public sealed record MergeExecutionObservedPrState
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
    public required bool Merged { get; init; }
    public string? MergeCommitSha { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required bool ObservationSucceeded { get; init; }
    public string? ObservationError { get; init; }
}

public sealed record MergeMutationResult
{
    public required bool Attempted { get; init; }
    public required bool Accepted { get; init; }
    public required string Provider { get; init; }
    public required string CommandOrMutationName { get; init; }
    public required string MergeStrategy { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public string? MergeCommitSha { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public interface IControlledMergeCommandClient
{
    Task<MergeExecutionObservedPrState> ObserveAsync(
        MergeExecutionRequest request,
        CancellationToken cancellationToken);

    Task<MergeMutationResult> MergeAsync(
        MergeExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record MergeExecutionReceipt
{
    public required string MergeExecutionId { get; init; }
    public required string MergeDecisionPackageId { get; init; }
    public required string Repository { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }

    public MergeExecutionObservedPrState? PreState { get; init; }
    public MergeExecutionObservedPrState? PostState { get; init; }

    public required string ExpectedHeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string ExpectedBaseBranch { get; init; }
    public string? ExpectedBaseSha { get; init; }

    public required string SelectedMergeStrategy { get; init; }
    public string? MergeCommitSha { get; init; }

    public required bool MergeAttempted { get; init; }
    public required bool MergeAccepted { get; init; }
    public required bool PostStateVerified { get; init; }

    public required MergeExecutionVerdict ExecutionVerdict { get; init; }
    public required MergeExecutionFailureKind FailureClassification { get; init; }
    public string[] Issues { get; init; } = [];

    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required DateTimeOffset ExecutedAtUtc { get; init; }

    public MergeExecutionBoundary Boundary { get; init; } = MergeExecutionBoundary.Executor;
}

public sealed record MergeExecutionResult
{
    public required MergeExecutionVerdict Verdict { get; init; }
    public required MergeExecutionFailureKind FailureKind { get; init; }
    public string[] Issues { get; init; } = [];
    public MergeExecutionReceipt? Receipt { get; init; }
}

public static class ControlledMergeExecutor
{
    public static async Task<MergeExecutionResult> ExecuteAsync(
        MergeExecutionRequest request,
        IControlledMergeCommandClient client,
        CancellationToken cancellationToken = default)
    {
        var now = request.RequestedAtUtc ?? DateTimeOffset.UtcNow;
        var package = request.Package;
        var issues = new List<string>();
        if (package is null)
            return Blocked(null, request, null, MergeExecutionFailureKind.MissingPackage, ["MissingMergeDecisionPackage"], now);

        var selectedStrategy = NormalizeStrategy(package.SelectedMergeStrategy);
        ValidatePackage(package, request, selectedStrategy, issues);
        ValidateBoundary(issues);
        if (issues.Count > 0)
            return Blocked(package, request, null, Classify(issues), issues.ToArray(), now);

        var preState = await client.ObserveAsync(request, cancellationToken).ConfigureAwait(false);
        ValidatePreState(package, request, preState, issues);
        if (issues.Count > 0)
            return Blocked(package, request, preState, Classify(issues), issues.ToArray(), now);

        if (request.DryRun)
            return Blocked(package, request, preState, MergeExecutionFailureKind.MergeMutationNotAllowed, ["DryRunDoesNotMerge"], now);

        var mutation = await client.MergeAsync(request, cancellationToken).ConfigureAwait(false);
        if (!mutation.Attempted || !mutation.Accepted)
        {
            var mutationIssues = FeedbackText.SafeList(["MergeMutationFailed", mutation.Error ?? mutation.Message ?? string.Empty]);
            return Failed(package, request, preState, preState, mutation, MergeExecutionFailureKind.MergeMutationFailed, mutationIssues, now, postStateVerified: false);
        }

        var mutationIssuesAfterAccept = new List<string>();
        ValidateMutationResult(package, request, mutation, mutationIssuesAfterAccept);
        if (mutationIssuesAfterAccept.Count > 0)
            return Failed(package, request, preState, preState, mutation, MergeExecutionFailureKind.MergeMutationFailed, mutationIssuesAfterAccept.ToArray(), now, postStateVerified: false);

        var postState = await client.ObserveAsync(request, cancellationToken).ConfigureAwait(false);
        var postIssues = new List<string>();
        ValidatePostState(package, request, postState, mutation, postIssues);
        if (postIssues.Count > 0)
            return Failed(package, request, preState, postState, mutation, MergeExecutionFailureKind.PostMergeVerificationFailed, postIssues.ToArray(), now, postStateVerified: false);

        var receipt = BuildReceipt(
            package,
            request,
            preState,
            postState,
            mutation,
            MergeExecutionVerdict.Executed,
            MergeExecutionFailureKind.None,
            [],
            now,
            postStateVerified: true,
            MergeExecutionBoundary.Executor);
        return new MergeExecutionResult
        {
            Verdict = MergeExecutionVerdict.Executed,
            FailureKind = MergeExecutionFailureKind.None,
            Issues = [],
            Receipt = receipt
        };
    }

    private static void ValidatePackage(
        MergeDecisionPackage package,
        MergeExecutionRequest request,
        MergeDecisionStrategy? selectedStrategy,
        List<string> issues)
    {
        if (package.PackageVerdict != MergeDecisionPackageVerdict.PackageReadyForMergeExecutor)
            issues.Add($"PackageNotEligible:{package.PackageVerdict}");
        if (!package.CanMergeForExecutor)
            issues.Add("PackageCannotMergeForExecutor");
        if (package.BlockReasons.Length > 0)
            issues.Add("PackageBlocked");
        if (!package.Boundary.EvidenceOnly)
            issues.Add("PackageBoundaryNotEvidenceOnly");
        if (PackageBoundaryCarriesAuthority(package.Boundary))
            issues.Add("PackageBoundaryAuthorityViolation");
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
        if (package.ReviewEvidence is null)
            issues.Add("MissingReviewEvidence");
        if (package.ValidationEvidence is null)
            issues.Add("MissingValidationEvidence");
        if (package.MergeDecisionRecord is null)
            issues.Add("MissingMergeDecision");
        else if (package.MergeDecisionRecord.Decision != MergeDecision.ApprovedForMergeExecutor)
            issues.Add("MergeDecisionNotApprovedForExecutor");
        if (selectedStrategy is null)
        {
            issues.Add(string.IsNullOrWhiteSpace(package.SelectedMergeStrategy)
                ? "MissingMergeStrategy"
                : "UnsupportedMergeStrategy");
        }
        if (!string.IsNullOrWhiteSpace(request.RequestedMergeStrategy) &&
            !Same(NormalizedStrategyText(request.RequestedMergeStrategy), NormalizedStrategyText(package.SelectedMergeStrategy)))
        {
            issues.Add("MergeStrategyOverrideNotAllowed");
        }
    }

    private static bool PackageBoundaryCarriesAuthority(MergeDecisionPackageBoundary boundary) =>
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
        var boundary = MergeExecutionBoundary.Executor;
        if (!boundary.CanMerge)
            issues.Add("MergeMutationNotAllowed");
        if (boundary.CanAutoMerge)
            issues.Add("AutoMergeNotAllowed");
        if (boundary.CanApprove || boundary.CanSubmitReview)
            issues.Add("ApprovalNotAllowed");
        if (boundary.CanMarkReadyForReview ||
            boundary.CanRemoveReviewers ||
            boundary.CanResolveReviewThreads ||
            boundary.CanReplyToReviewThreads)
        {
            issues.Add("ReviewMutationNotAllowed");
        }

        if (boundary.CanRequestReviewers)
            issues.Add("ReviewerRequestMutationNotAllowed");
        if (boundary.CanRelease || boundary.CanDeploy || boundary.CanTag || boundary.CanPublish)
            issues.Add("ReleaseDeployNotAllowed");
        if (boundary.CanPromoteMemory)
            issues.Add("MemoryPromotionNotAllowed");
        if (boundary.CanContinueWorkflow)
            issues.Add("WorkflowContinuationNotAllowed");
        if (boundary.CanCommit || boundary.CanPush)
            issues.Add("CommitPushNotAllowed");
        if (boundary.CanMutateSource || boundary.CanMutateWorkspace)
            issues.Add("SourceMutationNotAllowed");
    }

    private static void ValidatePreState(
        MergeDecisionPackage package,
        MergeExecutionRequest request,
        MergeExecutionObservedPrState observed,
        List<string> issues)
    {
        if (!observed.ObservationSucceeded)
        {
            issues.Add($"PullRequestNotFound:{observed.ObservationError ?? "observation failed"}");
            return;
        }

        ValidateSharedState(package, request, observed, issues, requireBaseSha: true);
        if (!Same(observed.PullRequestState, "open"))
            issues.Add("PullRequestNotOpen");
        if (observed.PullRequestDraft)
            issues.Add("PullRequestStillDraft");
        if (observed.Merged)
            issues.Add("PullRequestAlreadyMerged");
        if (observed.HasConflicts)
            issues.Add("PullRequestHasConflicts");
        if (!observed.Mergeable)
            issues.Add("PullRequestNotMergeable");
        if (observed.IsBehindBase)
            issues.Add("PullRequestBehindBase");
    }

    private static void ValidatePostState(
        MergeDecisionPackage package,
        MergeExecutionRequest request,
        MergeExecutionObservedPrState observed,
        MergeMutationResult mutation,
        List<string> issues)
    {
        if (!observed.ObservationSucceeded)
        {
            issues.Add($"PostMergeVerificationFailed:{observed.ObservationError ?? "post observation failed"}");
            return;
        }

        ValidateSharedState(package, request, observed, issues, requireBaseSha: false);
        if (!Same(observed.PullRequestState, "closed") && !Same(observed.PullRequestState, "merged"))
            issues.Add("PostMergePullRequestNotClosed");
        if (!observed.Merged)
            issues.Add("PostMergePullRequestNotMerged");
        if (string.IsNullOrWhiteSpace(observed.MergeCommitSha) && string.IsNullOrWhiteSpace(mutation.MergeCommitSha))
            issues.Add("PostMergeCommitMissing");

        var mergeCommit = observed.MergeCommitSha ?? mutation.MergeCommitSha;
        if (!string.IsNullOrWhiteSpace(observed.BaseSha) &&
            !string.IsNullOrWhiteSpace(mergeCommit) &&
            !Same(observed.BaseSha, mergeCommit))
        {
            issues.Add("PostMergeBaseDoesNotReflectMergeCommit");
        }
    }

    private static void ValidateSharedState(
        MergeDecisionPackage package,
        MergeExecutionRequest request,
        MergeExecutionObservedPrState observed,
        List<string> issues,
        bool requireBaseSha)
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
        if (requireBaseSha &&
            !string.IsNullOrWhiteSpace(request.ExpectedBaseSha) &&
            !Same(observed.BaseSha, request.ExpectedBaseSha))
        {
            issues.Add("BaseShaMismatch");
        }
    }

    private static void ValidateMutationResult(
        MergeDecisionPackage package,
        MergeExecutionRequest request,
        MergeMutationResult mutation,
        List<string> issues)
    {
        if (!Same(NormalizedStrategyText(mutation.MergeStrategy), NormalizedStrategyText(package.SelectedMergeStrategy)))
            issues.Add("MergeMutationStrategyMismatch");
        if (!Same(mutation.ExpectedHeadSha, request.ExpectedHeadSha))
            issues.Add("MergeMutationHeadMismatch");
    }

    private static MergeExecutionResult Blocked(
        MergeDecisionPackage? package,
        MergeExecutionRequest request,
        MergeExecutionObservedPrState? preState,
        MergeExecutionFailureKind kind,
        string[] issues,
        DateTimeOffset now)
    {
        var receipt = BuildReceipt(
            package,
            request,
            preState,
            preState,
            null,
            MergeExecutionVerdict.Blocked,
            kind,
            issues,
            now,
            postStateVerified: false,
            MergeExecutionBoundary.Blocked);
        return new MergeExecutionResult
        {
            Verdict = MergeExecutionVerdict.Blocked,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = receipt
        };
    }

    private static MergeExecutionResult Failed(
        MergeDecisionPackage package,
        MergeExecutionRequest request,
        MergeExecutionObservedPrState preState,
        MergeExecutionObservedPrState postState,
        MergeMutationResult mutation,
        MergeExecutionFailureKind kind,
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
            MergeExecutionVerdict.Failed,
            kind,
            issues,
            now,
            postStateVerified,
            MergeExecutionBoundary.Executor);
        return new MergeExecutionResult
        {
            Verdict = MergeExecutionVerdict.Failed,
            FailureKind = kind,
            Issues = FeedbackText.SafeList(issues),
            Receipt = receipt
        };
    }

    private static MergeExecutionReceipt BuildReceipt(
        MergeDecisionPackage? package,
        MergeExecutionRequest request,
        MergeExecutionObservedPrState? preState,
        MergeExecutionObservedPrState? postState,
        MergeMutationResult? mutation,
        MergeExecutionVerdict verdict,
        MergeExecutionFailureKind failureKind,
        string[] issues,
        DateTimeOffset now,
        bool postStateVerified,
        MergeExecutionBoundary boundary)
    {
        var packageId = package?.MergeDecisionPackageId ?? "missing-merge-decision-package";
        return new MergeExecutionReceipt
        {
            MergeExecutionId = $"merge_exec_{AyMergeExecutionHashing.ShortHash($"{packageId}|{request.Repository}|{request.PullRequestNumber}|{verdict}|{now:O}")}",
            MergeDecisionPackageId = packageId,
            Repository = FeedbackText.Safe(request.Repository),
            PullRequestNumber = request.PullRequestNumber,
            PullRequestUrl = FeedbackText.Safe(package?.PullRequestUrl ?? preState?.PullRequestUrl ?? postState?.PullRequestUrl ?? string.Empty),
            PreState = preState,
            PostState = postState,
            ExpectedHeadBranch = FeedbackText.Safe(request.ExpectedHeadBranch),
            ExpectedHeadSha = FeedbackText.Safe(request.ExpectedHeadSha),
            ExpectedBaseBranch = FeedbackText.Safe(request.ExpectedBaseBranch),
            ExpectedBaseSha = FeedbackText.SafeOrNull(request.ExpectedBaseSha),
            SelectedMergeStrategy = FeedbackText.Safe(package?.SelectedMergeStrategy ?? request.RequestedMergeStrategy ?? "missing-merge-strategy"),
            MergeCommitSha = FeedbackText.SafeOrNull(mutation?.MergeCommitSha ?? postState?.MergeCommitSha),
            MergeAttempted = mutation?.Attempted ?? false,
            MergeAccepted = mutation?.Accepted ?? false,
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

    private static MergeExecutionFailureKind Classify(IEnumerable<string> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Contains("MissingMergeDecisionPackage", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.MissingPackage;
            if (issue.Contains("PackageStale", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.PackageStale;
            if (issue.Contains("PackageBlocked", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.PackageBlocked;
            if (issue.Contains("Package", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.PackageNotEligible;
            if (issue.Contains("PullRequestNotFound", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.PullRequestNotFound;
            if (issue.Contains("PullRequestNotOpen", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.PullRequestNotOpen;
            if (issue.Contains("PullRequestStillDraft", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.PullRequestStillDraft;
            if (issue.Contains("PullRequestAlreadyMerged", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.PullRequestAlreadyMerged;
            if (issue.Contains("RepositoryMismatch", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.RepositoryMismatch;
            if (issue.Contains("PullRequestNumberMismatch", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.PullRequestNumberMismatch;
            if (issue.Contains("HeadBranchMismatch", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.HeadBranchMismatch;
            if (issue.Contains("HeadShaMismatch", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.HeadShaMismatch;
            if (issue.Contains("BaseBranchMismatch", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.BaseBranchMismatch;
            if (issue.Contains("BaseShaMismatch", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.BaseShaMismatch;
            if (issue.Contains("PullRequestHasConflicts", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.PullRequestHasConflicts;
            if (issue.Contains("PullRequestNotMergeable", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.PullRequestNotMergeable;
            if (issue.Contains("PullRequestBehindBase", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.PullRequestBehindBase;
            if (issue.Contains("MissingMergeStrategy", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.MissingMergeStrategy;
            if (issue.Contains("UnsupportedMergeStrategy", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.UnsupportedMergeStrategy;
            if (issue.Contains("MergeStrategyOverrideNotAllowed", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.MergeStrategyOverrideNotAllowed;
            if (issue.Contains("MergeMutationNotAllowed", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.MergeMutationNotAllowed;
            if (issue.Contains("AutoMergeNotAllowed", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.AutoMergeNotAllowed;
            if (issue.Contains("ApprovalNotAllowed", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.ApprovalNotAllowed;
            if (issue.Contains("ReviewMutationNotAllowed", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.ReviewMutationNotAllowed;
            if (issue.Contains("ReviewerRequestMutationNotAllowed", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.ReviewerRequestMutationNotAllowed;
            if (issue.Contains("ReleaseDeployNotAllowed", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.ReleaseDeployNotAllowed;
            if (issue.Contains("MemoryPromotionNotAllowed", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.MemoryPromotionNotAllowed;
            if (issue.Contains("WorkflowContinuationNotAllowed", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.WorkflowContinuationNotAllowed;
            if (issue.Contains("CommitPushNotAllowed", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.CommitPushNotAllowed;
            if (issue.Contains("SourceMutationNotAllowed", StringComparison.OrdinalIgnoreCase)) return MergeExecutionFailureKind.SourceMutationNotAllowed;
        }

        return MergeExecutionFailureKind.BoundaryViolation;
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

    public static string? NormalizedStrategyText(string? value) => NormalizeStrategy(value)?.ToString();

    public static string MergeMethodForStrategy(string? value) => NormalizeStrategy(value) switch
    {
        MergeDecisionStrategy.MergeCommit => "merge",
        MergeDecisionStrategy.Squash => "squash",
        MergeDecisionStrategy.Rebase => "rebase",
        _ => string.Empty
    };

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}

public static class MergeExecutionBypassEvaluator
{
    public static bool CanMerge(object? evidence) => false;
    public static bool CanAutoMerge(object? evidence) => false;
    public static bool CanApprove(object? evidence) => false;
    public static bool CanSubmitReview(object? evidence) => false;
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

internal static class AyMergeExecutionHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
