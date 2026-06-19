using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum FeedbackCiState
{
    Passed = 0,
    Failed,
    Pending,
    Cancelled,
    Skipped,
    Missing,
    Stale,
    Unknown
}

public enum ReviewFeedbackState
{
    RequestedChanges = 0,
    Commented,
    ApprovedButNonAuthoritative,
    Pending,
    Dismissed,
    Unknown
}

public enum FeedbackCategory
{
    BuildFailure = 0,
    TestFailure,
    DiffCheckFailure,
    StaticAnalysisFailure,
    CiInfrastructureFailure,
    FlakyOrTransientCi,
    ReviewRequestedChange,
    ReviewQuestion,
    ReviewNit,
    DocumentationIssue,
    ArtifactMismatch,
    UnsafeMaterialFinding,
    HeadShaDrift,
    MissingEvidence,
    NonActionable,
    Unknown
}

public enum FeedbackSeverity
{
    Blocker = 0,
    NeedsPatch,
    NeedsHumanTriage,
    Informational,
    Stale
}

public enum FeedbackActionability
{
    RequiresGovernedPatchRun = 0,
    RequiresHumanAnswer,
    RequiresEvidenceRefresh,
    RequiresCiRerunRequestInFutureBlock,
    NoActionRequired,
    Unknown
}

public enum FeedbackReadinessOutcome
{
    NeedsGovernedPatchRun = 0,
    NeedsHumanTriage,
    NeedsEvidenceRefresh,
    NoKnownBlockingFeedback,
    Blocked
}

public sealed record FeedbackLoopBoundary
{
    public bool EvidenceOnly { get; init; } = true;
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanCreatePullRequest { get; init; }
    public bool CanUpdatePullRequest { get; init; }
    public bool CanMarkReadyForReview { get; init; }
    public bool CanRequestReviewers { get; init; }
    public bool CanReplyToReviewComments { get; init; }
    public bool CanResolveReviewThreads { get; init; }
    public bool CanRerunCi { get; init; }
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanMutateWorkspace { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanSatisfyPolicy { get; init; }

    public static FeedbackLoopBoundary Evidence { get; } = new();
}

public static class FeedbackLoopBoundaryText
{
    public const string Boundary = """
        Block AN observes CI and review feedback.
        It does not commit.
        It does not push.
        It does not update PRs.
        It does not reply to comments.
        It does not resolve review threads.
        It does not request reviewers.
        It does not mark PRs ready.
        It does not rerun CI.
        It does not merge.
        It does not release.
        It does not deploy.
        It does not continue workflow.
        """;
}

public sealed record FeedbackLoopRequestInput
{
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string PullRequestCreationReceiptId { get; init; }
    public required string RequestedBy { get; init; }
    public required string Reason { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public DateTimeOffset? RequestedAtUtc { get; init; }
}

public sealed record FeedbackLoopRequest
{
    public required string FeedbackLoopRequestId { get; init; }
    public required string RunId { get; init; }
    public required string ProjectId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string PullRequestUrl { get; init; }
    public required string BaseBranch { get; init; }
    public required string HeadBranch { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public required string PullRequestCreationReceiptId { get; init; }
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required string Reason { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public FeedbackLoopBoundary Boundary { get; init; } = FeedbackLoopBoundary.Evidence;
}

public static class FeedbackLoopRequestWriter
{
    public static FeedbackLoopRequest Create(FeedbackLoopRequestInput input) => new()
    {
        FeedbackLoopRequestId = $"feedback_req_{FeedbackHashing.ShortHash($"{input.RunId}|{input.RepositoryFullName}|{input.PullRequestNumber}|{input.ExpectedHeadSha}")}",
        RunId = FeedbackText.Safe(input.RunId),
        ProjectId = FeedbackText.Safe(input.ProjectId),
        RepositoryFullName = FeedbackText.Safe(input.RepositoryFullName),
        PullRequestNumber = input.PullRequestNumber,
        PullRequestUrl = FeedbackText.Safe(input.PullRequestUrl),
        BaseBranch = FeedbackText.Safe(input.BaseBranch),
        HeadBranch = FeedbackText.Safe(input.HeadBranch),
        ExpectedHeadSha = FeedbackText.Safe(input.ExpectedHeadSha),
        PullRequestCreationReceiptId = FeedbackText.Safe(input.PullRequestCreationReceiptId),
        RequestedBy = FeedbackText.Safe(input.RequestedBy),
        RequestedAtUtc = input.RequestedAtUtc ?? DateTimeOffset.UtcNow,
        Reason = FeedbackText.Safe(input.Reason),
        EvidenceRefs = FeedbackText.SafeList(input.EvidenceRefs),
        Boundary = FeedbackLoopBoundary.Evidence
    };
}

public sealed record CiCheckRunSummary
{
    public required string Name { get; init; }
    public string? Status { get; init; }
    public string? Conclusion { get; init; }
    public string? HeadSha { get; init; }
    public string? Url { get; init; }
}

public sealed record CiWorkflowRunSummary
{
    public required string Name { get; init; }
    public string? Status { get; init; }
    public string? Conclusion { get; init; }
    public string? HeadSha { get; init; }
    public string? Url { get; init; }
}

public sealed record CiJobSummary
{
    public required string Name { get; init; }
    public string? Status { get; init; }
    public string? Conclusion { get; init; }
    public string? Url { get; init; }
}

public sealed record CiFailureExcerpt
{
    public required string Source { get; init; }
    public required string Message { get; init; }
    public string? Excerpt { get; init; }
    public string? Url { get; init; }
}

public sealed record CiObservationSnapshot
{
    public required string CiObservationSnapshotId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public string? ObservedHeadSha { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public CiCheckRunSummary[] CheckRuns { get; init; } = [];
    public CiWorkflowRunSummary[] WorkflowRuns { get; init; } = [];
    public CiJobSummary[] Jobs { get; init; } = [];
    public required FeedbackCiState OverallCiState { get; init; }
    public CiFailureExcerpt[] Failures { get; init; } = [];
    public string[] Warnings { get; init; } = [];
    public string[] StaleObservations { get; init; } = [];
    public FeedbackLoopBoundary Boundary { get; init; } = FeedbackLoopBoundary.Evidence;
}

public static class CiObservationBuilder
{
    public static CiObservationSnapshot Build(
        FeedbackLoopRequest request,
        string? observedHeadSha,
        IEnumerable<CiCheckRunSummary>? checkRuns,
        IEnumerable<CiWorkflowRunSummary>? workflowRuns,
        IEnumerable<CiJobSummary>? jobs = null,
        IEnumerable<CiFailureExcerpt>? failureExcerpts = null,
        DateTimeOffset? now = null)
    {
        var checks = (checkRuns ?? []).ToArray();
        var workflows = (workflowRuns ?? []).ToArray();
        var jobList = (jobs ?? []).ToArray();
        var failures = new List<CiFailureExcerpt>(failureExcerpts ?? []);
        var stale = new List<string>();
        if (!string.IsNullOrWhiteSpace(observedHeadSha) && !string.Equals(observedHeadSha, request.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase))
            stale.Add($"ObservedHeadSha:{observedHeadSha}");
        stale.AddRange(checks.Where(item => IsStale(item.HeadSha, request.ExpectedHeadSha)).Select(item => $"CheckRun:{item.Name}"));
        stale.AddRange(workflows.Where(item => IsStale(item.HeadSha, request.ExpectedHeadSha)).Select(item => $"WorkflowRun:{item.Name}"));

        foreach (var check in checks.Where(IsFailure))
            failures.Add(new CiFailureExcerpt { Source = check.Name, Message = $"{check.Name} concluded {check.Conclusion ?? check.Status ?? "unknown"}.", Url = check.Url });
        foreach (var workflow in workflows.Where(IsFailure))
            failures.Add(new CiFailureExcerpt { Source = workflow.Name, Message = $"{workflow.Name} concluded {workflow.Conclusion ?? workflow.Status ?? "unknown"}.", Url = workflow.Url });
        foreach (var job in jobList.Where(IsFailure))
            failures.Add(new CiFailureExcerpt { Source = job.Name, Message = $"{job.Name} concluded {job.Conclusion ?? job.Status ?? "unknown"}.", Url = job.Url });

        var state = DetermineState(observedHeadSha, request.ExpectedHeadSha, checks, workflows, jobList);
        return new CiObservationSnapshot
        {
            CiObservationSnapshotId = $"ci_obs_{FeedbackHashing.ShortHash($"{request.FeedbackLoopRequestId}|{observedHeadSha}|{state}|{checks.Length}|{workflows.Length}")}",
            RunId = request.RunId,
            RepositoryFullName = request.RepositoryFullName,
            PullRequestNumber = request.PullRequestNumber,
            ExpectedHeadSha = request.ExpectedHeadSha,
            ObservedHeadSha = FeedbackText.SafeOrNull(observedHeadSha),
            ObservedAtUtc = now ?? DateTimeOffset.UtcNow,
            CheckRuns = checks,
            WorkflowRuns = workflows,
            Jobs = jobList,
            OverallCiState = state,
            Failures = failures.ToArray(),
            Warnings = state == FeedbackCiState.Missing ? ["No CI observations were available."] : [],
            StaleObservations = stale.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Boundary = FeedbackLoopBoundary.Evidence
        };
    }

    private static FeedbackCiState DetermineState(string? observedHeadSha, string expectedHeadSha, CiCheckRunSummary[] checks, CiWorkflowRunSummary[] workflows, CiJobSummary[] jobs)
    {
        if (!string.IsNullOrWhiteSpace(observedHeadSha) && !string.Equals(observedHeadSha, expectedHeadSha, StringComparison.OrdinalIgnoreCase))
            return FeedbackCiState.Stale;
        var items = checks.Select(item => (item.Status, item.Conclusion))
            .Concat(workflows.Select(item => (item.Status, item.Conclusion)))
            .Concat(jobs.Select(item => (item.Status, item.Conclusion)))
            .ToArray();
        if (items.Length == 0)
            return FeedbackCiState.Missing;
        if (items.Any(item => IsCancelled(item.Status, item.Conclusion)))
            return FeedbackCiState.Cancelled;
        if (items.Any(item => IsFailure(item.Status, item.Conclusion)))
            return FeedbackCiState.Failed;
        if (items.Any(item => IsPending(item.Status, item.Conclusion)))
            return FeedbackCiState.Pending;
        if (items.All(item => IsSkipped(item.Status, item.Conclusion)))
            return FeedbackCiState.Skipped;
        if (items.All(item => IsSuccess(item.Status, item.Conclusion) || IsSkipped(item.Status, item.Conclusion)))
            return FeedbackCiState.Passed;
        return FeedbackCiState.Unknown;
    }

    private static bool IsStale(string? headSha, string expectedHeadSha) =>
        !string.IsNullOrWhiteSpace(headSha) && !string.Equals(headSha, expectedHeadSha, StringComparison.OrdinalIgnoreCase);

    private static bool IsFailure(CiCheckRunSummary item) => IsFailure(item.Status, item.Conclusion);
    private static bool IsFailure(CiWorkflowRunSummary item) => IsFailure(item.Status, item.Conclusion);
    private static bool IsFailure(CiJobSummary item) => IsFailure(item.Status, item.Conclusion);
    private static bool IsFailure(string? status, string? conclusion) =>
        Any(conclusion, "failure", "failed", "timed_out", "action_required") || Any(status, "failure", "failed");
    private static bool IsCancelled(string? status, string? conclusion) => Any(conclusion, "cancelled", "canceled") || Any(status, "cancelled", "canceled");
    private static bool IsSkipped(string? status, string? conclusion) => Any(conclusion, "skipped") || Any(status, "skipped");
    private static bool IsSuccess(string? status, string? conclusion) => Any(conclusion, "success", "passed") || Any(status, "success", "completed", "passed");
    private static bool IsPending(string? status, string? conclusion) =>
        string.IsNullOrWhiteSpace(conclusion) && Any(status, "queued", "in_progress", "pending", "waiting", "requested", "expected");
    private static bool Any(string? value, params string[] candidates) =>
        !string.IsNullOrWhiteSpace(value) && candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
}

public sealed record ReviewSubmissionSummary
{
    public required string Author { get; init; }
    public required ReviewFeedbackState State { get; init; }
    public string? BodySummary { get; init; }
    public string? HeadSha { get; init; }
    public string? Url { get; init; }
    public DateTimeOffset? SubmittedAtUtc { get; init; }
}

public sealed record ReviewCommentSummary
{
    public required string Author { get; init; }
    public string? Path { get; init; }
    public int? Line { get; init; }
    public required string BodySummary { get; init; }
    public string? HeadSha { get; init; }
    public string? Url { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }
}

public sealed record ReviewThreadSummary
{
    public required string ThreadId { get; init; }
    public bool IsResolved { get; init; }
    public required string Summary { get; init; }
    public string? HeadSha { get; init; }
    public string? Url { get; init; }
}

public sealed record RequestedChangeSummary
{
    public required string Author { get; init; }
    public required string Message { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public string[] AffectedFiles { get; init; } = [];
}

public sealed record ReviewFeedbackSnapshot
{
    public required string ReviewFeedbackSnapshotId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public string? ObservedHeadSha { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public ReviewSubmissionSummary[] ReviewSubmissions { get; init; } = [];
    public ReviewCommentSummary[] InlineComments { get; init; } = [];
    public ReviewCommentSummary[] TopLevelComments { get; init; } = [];
    public RequestedChangeSummary[] RequestedChanges { get; init; } = [];
    public ReviewThreadSummary[] UnresolvedThreads { get; init; } = [];
    public string[] StaleFeedback { get; init; } = [];
    public FeedbackLoopBoundary Boundary { get; init; } = FeedbackLoopBoundary.Evidence;
}

public static class ReviewFeedbackSnapshotBuilder
{
    public static ReviewFeedbackSnapshot Build(
        FeedbackLoopRequest request,
        string? observedHeadSha,
        IEnumerable<ReviewSubmissionSummary>? submissions,
        IEnumerable<ReviewCommentSummary>? inlineComments,
        IEnumerable<ReviewCommentSummary>? topLevelComments,
        IEnumerable<ReviewThreadSummary>? unresolvedThreads = null,
        DateTimeOffset? now = null)
    {
        var reviewSubmissions = (submissions ?? []).ToArray();
        var inline = (inlineComments ?? []).ToArray();
        var topLevel = (topLevelComments ?? []).ToArray();
        var threads = (unresolvedThreads ?? []).Where(item => !item.IsResolved).ToArray();
        var requested = reviewSubmissions
            .Where(item => item.State == ReviewFeedbackState.RequestedChanges)
            .Select(item => new RequestedChangeSummary
            {
                Author = item.Author,
                Message = FeedbackText.Safe(item.BodySummary ?? "Requested changes were submitted."),
                EvidenceRefs = FeedbackText.SafeList([item.Url ?? "review-submission"]),
                AffectedFiles = []
            })
            .Concat(inline.Where(item => LooksLikeRequestedChange(item.BodySummary)).Select(item => new RequestedChangeSummary
            {
                Author = item.Author,
                Message = item.BodySummary,
                EvidenceRefs = FeedbackText.SafeList([item.Url ?? "inline-comment"]),
                AffectedFiles = FeedbackText.SafeList([item.Path ?? string.Empty])
            }))
            .ToArray();
        var stale = new List<string>();
        if (!string.IsNullOrWhiteSpace(observedHeadSha) && !string.Equals(observedHeadSha, request.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase))
            stale.Add($"ObservedHeadSha:{observedHeadSha}");
        stale.AddRange(reviewSubmissions.Where(item => IsStale(item.HeadSha, request.ExpectedHeadSha)).Select(item => $"Review:{item.Author}"));
        stale.AddRange(inline.Where(item => IsStale(item.HeadSha, request.ExpectedHeadSha)).Select(item => $"InlineComment:{item.Author}:{item.Path}"));
        stale.AddRange(topLevel.Where(item => IsStale(item.HeadSha, request.ExpectedHeadSha)).Select(item => $"TopLevelComment:{item.Author}"));
        stale.AddRange(threads.Where(item => IsStale(item.HeadSha, request.ExpectedHeadSha)).Select(item => $"Thread:{item.ThreadId}"));

        return new ReviewFeedbackSnapshot
        {
            ReviewFeedbackSnapshotId = $"review_obs_{FeedbackHashing.ShortHash($"{request.FeedbackLoopRequestId}|{observedHeadSha}|{reviewSubmissions.Length}|{inline.Length}|{topLevel.Length}")}",
            RunId = request.RunId,
            RepositoryFullName = request.RepositoryFullName,
            PullRequestNumber = request.PullRequestNumber,
            ExpectedHeadSha = request.ExpectedHeadSha,
            ObservedHeadSha = FeedbackText.SafeOrNull(observedHeadSha),
            ObservedAtUtc = now ?? DateTimeOffset.UtcNow,
            ReviewSubmissions = reviewSubmissions,
            InlineComments = inline,
            TopLevelComments = topLevel,
            RequestedChanges = requested,
            UnresolvedThreads = threads,
            StaleFeedback = stale.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Boundary = FeedbackLoopBoundary.Evidence
        };
    }

    private static bool LooksLikeRequestedChange(string value) =>
        value.Contains("please change", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("must change", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("requested change", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("blocker", StringComparison.OrdinalIgnoreCase);

    private static bool IsStale(string? headSha, string expectedHeadSha) =>
        !string.IsNullOrWhiteSpace(headSha) && !string.Equals(headSha, expectedHeadSha, StringComparison.OrdinalIgnoreCase);
}

public sealed record FeedbackFinding
{
    public required string FindingId { get; init; }
    public required string Source { get; init; }
    public required FeedbackCategory Category { get; init; }
    public required FeedbackSeverity Severity { get; init; }
    public required FeedbackActionability Actionability { get; init; }
    public required string Message { get; init; }
    public string[] EvidenceRefs { get; init; } = [];
    public string[] AffectedFiles { get; init; } = [];
    public required string SuggestedNextStep { get; init; }
    public FeedbackLoopBoundary Boundary { get; init; } = FeedbackLoopBoundary.Evidence;
}

public sealed record FeedbackClassificationReport
{
    public required string FeedbackClassificationReportId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public FeedbackFinding[] Findings { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public FeedbackLoopBoundary Boundary { get; init; } = FeedbackLoopBoundary.Evidence;
}

public static class FeedbackClassifier
{
    public static FeedbackClassificationReport Classify(FeedbackLoopRequest request, CiObservationSnapshot? ci, ReviewFeedbackSnapshot? review, DateTimeOffset? now = null)
    {
        var findings = new List<FeedbackFinding>();
        if (ci is null)
            findings.Add(Finding(request, "ci-observation", FeedbackCategory.MissingEvidence, FeedbackSeverity.NeedsHumanTriage, FeedbackActionability.RequiresEvidenceRefresh, "CI observation is missing.", [], [], "Refresh CI observation evidence."));
        else
            AddCiFindings(request, ci, findings);

        if (review is null)
            findings.Add(Finding(request, "review-feedback", FeedbackCategory.MissingEvidence, FeedbackSeverity.NeedsHumanTriage, FeedbackActionability.RequiresEvidenceRefresh, "Review feedback observation is missing.", [], [], "Refresh review feedback evidence."));
        else
            AddReviewFindings(request, review, findings);

        if (findings.Count == 0)
            findings.Add(Finding(request, "feedback-loop", FeedbackCategory.NonActionable, FeedbackSeverity.Informational, FeedbackActionability.NoActionRequired, "No known blocking feedback was observed.", [], [], "Keep the PR in human review; no automatic workflow continuation is allowed."));

        return new FeedbackClassificationReport
        {
            FeedbackClassificationReportId = $"feedback_class_{FeedbackHashing.ShortHash($"{request.FeedbackLoopRequestId}|{findings.Count}|{string.Join(",", findings.Select(item => item.Category))}")}",
            RunId = request.RunId,
            RepositoryFullName = request.RepositoryFullName,
            PullRequestNumber = request.PullRequestNumber,
            ExpectedHeadSha = request.ExpectedHeadSha,
            Findings = findings.ToArray(),
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = FeedbackLoopBoundary.Evidence
        };
    }

    private static void AddCiFindings(FeedbackLoopRequest request, CiObservationSnapshot ci, List<FeedbackFinding> findings)
    {
        if (ci.OverallCiState == FeedbackCiState.Stale || ci.StaleObservations.Length > 0)
            findings.Add(Finding(request, "ci-observation", FeedbackCategory.HeadShaDrift, FeedbackSeverity.Stale, FeedbackActionability.RequiresEvidenceRefresh, "CI observation is stale for the expected head SHA.", ["ci-observation-snapshot.json"], [], "Refresh CI evidence for the current head SHA."));
        if (ci.OverallCiState == FeedbackCiState.Missing)
            findings.Add(Finding(request, "ci-observation", FeedbackCategory.MissingEvidence, FeedbackSeverity.NeedsHumanTriage, FeedbackActionability.RequiresEvidenceRefresh, "CI observation is missing.", ["ci-observation-snapshot.json"], [], "Collect CI check evidence before planning remediation."));
        if (ci.OverallCiState == FeedbackCiState.Pending)
            findings.Add(Finding(request, "ci-observation", FeedbackCategory.MissingEvidence, FeedbackSeverity.NeedsHumanTriage, FeedbackActionability.RequiresEvidenceRefresh, "CI is still pending.", ["ci-observation-snapshot.json"], [], "Wait for CI to finish or refresh evidence later."));
        foreach (var failure in ci.Failures)
        {
            var category = CategorizeCiFailure(failure);
            var actionability = category == FeedbackCategory.CiInfrastructureFailure || category == FeedbackCategory.FlakyOrTransientCi
                ? FeedbackActionability.RequiresEvidenceRefresh
                : FeedbackActionability.RequiresGovernedPatchRun;
            findings.Add(Finding(request, failure.Source, category, FeedbackSeverity.Blocker, actionability, failure.Message, ["ci-observation-snapshot.json"], [], actionability == FeedbackActionability.RequiresGovernedPatchRun ? "Route a fix through a governed patch run." : "Refresh CI evidence or request a rerun in a future governed block."));
        }
    }

    private static void AddReviewFindings(FeedbackLoopRequest request, ReviewFeedbackSnapshot review, List<FeedbackFinding> findings)
    {
        foreach (var stale in review.StaleFeedback)
            findings.Add(Finding(request, stale, FeedbackCategory.HeadShaDrift, FeedbackSeverity.Stale, FeedbackActionability.RequiresEvidenceRefresh, "Review feedback appears stale for the expected head SHA.", ["review-feedback-snapshot.json"], [], "Refresh review feedback evidence."));
        foreach (var change in review.RequestedChanges)
            findings.Add(Finding(request, change.Author, FeedbackCategory.ReviewRequestedChange, FeedbackSeverity.Blocker, FeedbackActionability.RequiresGovernedPatchRun, change.Message, change.EvidenceRefs, change.AffectedFiles, "Route requested changes through a governed patch run."));
        foreach (var comment in review.InlineComments.Concat(review.TopLevelComments).Where(item => item.BodySummary.Contains('?')))
            findings.Add(Finding(request, comment.Author, FeedbackCategory.ReviewQuestion, FeedbackSeverity.NeedsHumanTriage, FeedbackActionability.RequiresHumanAnswer, comment.BodySummary, FeedbackText.SafeList([comment.Url ?? "review-comment"]), FeedbackText.SafeList([comment.Path ?? string.Empty]), "Prepare a human answer; AN must not reply."));
    }

    private static FeedbackCategory CategorizeCiFailure(CiFailureExcerpt failure)
    {
        var text = $"{failure.Source} {failure.Message} {failure.Excerpt}";
        if (text.Contains("test", StringComparison.OrdinalIgnoreCase))
            return FeedbackCategory.TestFailure;
        if (text.Contains("build", StringComparison.OrdinalIgnoreCase) || text.Contains("compile", StringComparison.OrdinalIgnoreCase))
            return FeedbackCategory.BuildFailure;
        if (text.Contains("diff", StringComparison.OrdinalIgnoreCase) || text.Contains("whitespace", StringComparison.OrdinalIgnoreCase))
            return FeedbackCategory.DiffCheckFailure;
        if (text.Contains("lint", StringComparison.OrdinalIgnoreCase) || text.Contains("analysis", StringComparison.OrdinalIgnoreCase))
            return FeedbackCategory.StaticAnalysisFailure;
        if (text.Contains("flake", StringComparison.OrdinalIgnoreCase) || text.Contains("transient", StringComparison.OrdinalIgnoreCase))
            return FeedbackCategory.FlakyOrTransientCi;
        if (text.Contains("infrastructure", StringComparison.OrdinalIgnoreCase) || text.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return FeedbackCategory.CiInfrastructureFailure;
        return FeedbackCategory.Unknown;
    }

    private static FeedbackFinding Finding(FeedbackLoopRequest request, string source, FeedbackCategory category, FeedbackSeverity severity, FeedbackActionability actionability, string message, IEnumerable<string> evidenceRefs, IEnumerable<string> affectedFiles, string suggestedNextStep) => new()
    {
        FindingId = $"feedback_find_{FeedbackHashing.ShortHash($"{request.FeedbackLoopRequestId}|{source}|{category}|{message}")}",
        Source = FeedbackText.Safe(source),
        Category = category,
        Severity = severity,
        Actionability = actionability,
        Message = FeedbackText.Safe(message),
        EvidenceRefs = FeedbackText.SafeList(evidenceRefs),
        AffectedFiles = FeedbackText.SafeList(affectedFiles),
        SuggestedNextStep = FeedbackText.Safe(suggestedNextStep),
        Boundary = FeedbackLoopBoundary.Evidence
    };
}

public sealed record FeedbackRemediationPlan
{
    public required string FeedbackRemediationPlanId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public string[] FindingsAddressed { get; init; } = [];
    public string[] ProposedPatchScope { get; init; } = [];
    public string[] SuggestedFilesToInspect { get; init; } = [];
    public string[] SuggestedTests { get; init; } = [];
    public string[] HumanQuestions { get; init; } = [];
    public string[] EvidenceRefreshRequests { get; init; } = [];
    public string[] KnownRisks { get; init; } = [];
    public required string NonAuthorityBoundary { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public FeedbackLoopBoundary Boundary { get; init; } = FeedbackLoopBoundary.Evidence;
}

public sealed record SuggestedFeedbackTestProfile
{
    public required string RunId { get; init; }
    public string[] SuggestedTests { get; init; } = [];
    public FeedbackLoopBoundary Boundary { get; init; } = FeedbackLoopBoundary.Evidence;
}

public static class FeedbackRemediationPlanner
{
    public static FeedbackRemediationPlan Propose(FeedbackLoopRequest request, FeedbackClassificationReport classification, IEnumerable<string>? knownRisks = null, DateTimeOffset? now = null)
    {
        var patchFindings = classification.Findings.Where(item => item.Actionability == FeedbackActionability.RequiresGovernedPatchRun).ToArray();
        var humanFindings = classification.Findings.Where(item => item.Actionability == FeedbackActionability.RequiresHumanAnswer).ToArray();
        var refreshFindings = classification.Findings.Where(item => item.Actionability == FeedbackActionability.RequiresEvidenceRefresh || item.Actionability == FeedbackActionability.RequiresCiRerunRequestInFutureBlock).ToArray();
        return new FeedbackRemediationPlan
        {
            FeedbackRemediationPlanId = $"feedback_plan_{FeedbackHashing.ShortHash($"{classification.FeedbackClassificationReportId}|{classification.Findings.Length}")}",
            RunId = request.RunId,
            RepositoryFullName = request.RepositoryFullName,
            PullRequestNumber = request.PullRequestNumber,
            ExpectedHeadSha = request.ExpectedHeadSha,
            FindingsAddressed = classification.Findings.Select(item => item.FindingId).ToArray(),
            ProposedPatchScope = patchFindings.Length == 0 ? [] : patchFindings.Select(item => $"{item.Category}: {item.Message}").ToArray(),
            SuggestedFilesToInspect = FeedbackText.SafeList(patchFindings.SelectMany(item => item.AffectedFiles).DefaultIfEmpty("changed files from the commit package")),
            SuggestedTests = SuggestTests(patchFindings),
            HumanQuestions = humanFindings.Select(item => item.Message).ToArray(),
            EvidenceRefreshRequests = refreshFindings.Select(item => item.Message).ToArray(),
            KnownRisks = FeedbackText.SafeList(knownRisks),
            NonAuthorityBoundary = """
                This remediation plan is not a patch.
                This remediation plan does not update the PR.
                This remediation plan does not reply to reviewers.
                This remediation plan does not rerun CI.
                This remediation plan does not commit or push.
                This remediation plan does not continue workflow.
                """,
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = FeedbackLoopBoundary.Evidence
        };
    }

    private static string[] SuggestTests(FeedbackFinding[] findings)
    {
        var tests = new List<string>();
        if (findings.Any(item => item.Category is FeedbackCategory.TestFailure))
            tests.Add("Rerun the failing test command inside a future governed patch run.");
        if (findings.Any(item => item.Category is FeedbackCategory.BuildFailure))
            tests.Add("dotnet build IronDev.slnx --no-restore -v:minimal");
        if (findings.Any(item => item.Category is FeedbackCategory.DiffCheckFailure))
            tests.Add("git diff --check");
        if (tests.Count == 0 && findings.Length > 0)
            tests.Add("Run the focused validation named by the feedback after a governed patch.");
        return tests.ToArray();
    }
}

public sealed record FeedbackReadinessReport
{
    public required string FeedbackReadinessReportId { get; init; }
    public required string RunId { get; init; }
    public required string RepositoryFullName { get; init; }
    public required int PullRequestNumber { get; init; }
    public required string ExpectedHeadSha { get; init; }
    public string? ObservedHeadSha { get; init; }
    public required FeedbackReadinessOutcome Outcome { get; init; }
    public string[] Blockers { get; init; } = [];
    public string[] HumanTriageItems { get; init; } = [];
    public string[] EvidenceGaps { get; init; } = [];
    public required string SuggestedNextCommand { get; init; }
    public string[] KnownRisks { get; init; } = [];
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public FeedbackLoopBoundary Boundary { get; init; } = FeedbackLoopBoundary.Evidence;
}

public sealed record FeedbackLoopStatusReport
{
    public required string RunId { get; init; }
    public required string[] ArtifactNames { get; init; }
    public FeedbackLoopBoundary Boundary { get; init; } = FeedbackLoopBoundary.Evidence;
}

public static class FeedbackReadinessReporter
{
    public static FeedbackReadinessReport Report(
        FeedbackLoopRequest request,
        CiObservationSnapshot? ci,
        ReviewFeedbackSnapshot? review,
        FeedbackClassificationReport? classification,
        FeedbackRemediationPlan? plan,
        IEnumerable<string>? knownRisks = null,
        DateTimeOffset? now = null)
    {
        var evidenceGaps = new List<string>();
        if (ci is null) evidenceGaps.Add("MissingCiObservation");
        if (review is null) evidenceGaps.Add("MissingReviewFeedbackSnapshot");
        if (classification is null) evidenceGaps.Add("MissingFeedbackClassification");
        if (plan is null) evidenceGaps.Add("MissingFeedbackRemediationPlan");
        if (ci is not null && !string.IsNullOrWhiteSpace(ci.ObservedHeadSha) && !string.Equals(ci.ObservedHeadSha, request.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase))
            evidenceGaps.Add("CiObservedHeadShaMismatch");
        if (review is not null && !string.IsNullOrWhiteSpace(review.ObservedHeadSha) && !string.Equals(review.ObservedHeadSha, request.ExpectedHeadSha, StringComparison.OrdinalIgnoreCase))
            evidenceGaps.Add("ReviewObservedHeadShaMismatch");

        var blockers = classification?.Findings
            .Where(item => item.Actionability == FeedbackActionability.RequiresGovernedPatchRun && item.Severity != FeedbackSeverity.Stale)
            .Select(item => item.Message)
            .ToArray() ?? [];
        var human = classification?.Findings
            .Where(item => item.Actionability == FeedbackActionability.RequiresHumanAnswer)
            .Select(item => item.Message)
            .ToArray() ?? [];

        var outcome = evidenceGaps.Count > 0 ? FeedbackReadinessOutcome.NeedsEvidenceRefresh :
            blockers.Length > 0 ? FeedbackReadinessOutcome.NeedsGovernedPatchRun :
            human.Length > 0 ? FeedbackReadinessOutcome.NeedsHumanTriage :
            FeedbackReadinessOutcome.NoKnownBlockingFeedback;

        return new FeedbackReadinessReport
        {
            FeedbackReadinessReportId = $"feedback_ready_{FeedbackHashing.ShortHash($"{request.FeedbackLoopRequestId}|{outcome}|{blockers.Length}|{human.Length}|{evidenceGaps.Count}")}",
            RunId = request.RunId,
            RepositoryFullName = request.RepositoryFullName,
            PullRequestNumber = request.PullRequestNumber,
            ExpectedHeadSha = request.ExpectedHeadSha,
            ObservedHeadSha = FeedbackText.SafeOrNull(ci?.ObservedHeadSha ?? review?.ObservedHeadSha),
            Outcome = outcome,
            Blockers = blockers,
            HumanTriageItems = human,
            EvidenceGaps = evidenceGaps.ToArray(),
            SuggestedNextCommand = outcome switch
            {
                FeedbackReadinessOutcome.NeedsGovernedPatchRun => $"irondev patch propose --run {request.RunId}",
                FeedbackReadinessOutcome.NeedsHumanTriage => $"irondev feedback status --run {request.RunId}",
                FeedbackReadinessOutcome.NeedsEvidenceRefresh => $"irondev feedback ci --run {request.RunId}",
                _ => $"irondev feedback status --run {request.RunId}"
            },
            KnownRisks = FeedbackText.SafeList(knownRisks),
            CreatedAtUtc = now ?? DateTimeOffset.UtcNow,
            Boundary = FeedbackLoopBoundary.Evidence
        };
    }
}

public sealed record FeedbackLoopBypassReport
{
    public required string FeedbackLoopBypassReportId { get; init; }
    public required string RunId { get; init; }
    public string[] EvidenceSubjects { get; init; } = [];
    public bool CommitCreated { get; init; }
    public bool PushPerformed { get; init; }
    public bool PullRequestUpdated { get; init; }
    public bool ReviewCommentReplied { get; init; }
    public bool ReviewThreadResolved { get; init; }
    public bool CiRerunRequested { get; init; }
    public bool ReadyForReviewMarked { get; init; }
    public bool Merged { get; init; }
    public bool Released { get; init; }
    public bool Deployed { get; init; }
    public bool WorkflowContinued { get; init; }
    public FeedbackLoopBoundary Boundary { get; init; } = FeedbackLoopBoundary.Evidence;
}

public static class FeedbackLoopBypassEvaluator
{
    public static FeedbackLoopBypassReport Evaluate(string runId, IEnumerable<string> evidenceSubjects) => new()
    {
        FeedbackLoopBypassReportId = $"feedback_bypass_{FeedbackHashing.ShortHash(runId)}",
        RunId = FeedbackText.Safe(runId),
        EvidenceSubjects = FeedbackText.SafeList(evidenceSubjects),
        Boundary = FeedbackLoopBoundary.Evidence
    };

    public static bool CanUpdatePullRequest(object? evidence) => false;
    public static bool CanRerunCi(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
}

public static class FeedbackText
{
    public static string Safe(string? value) => (value ?? string.Empty).Trim();
    public static string? SafeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static string Summary(string? value, int maxLength = 220)
    {
        var safe = Safe(value).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return safe.Length <= maxLength ? safe : safe[..maxLength].TrimEnd();
    }

    public static string[] SafeList(IEnumerable<string>? values) => values?
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Select(item => item.Trim().Replace('\\', '/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];
}

internal static class FeedbackHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
