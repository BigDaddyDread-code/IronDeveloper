namespace IronDev.Core.Governance.RepoStateFreshness;

public static class RepoStateFreshnessGuard
{
    public static RepoStateFreshnessResult Evaluate(RepoStateFreshnessRequest? request)
    {
        var issues = new List<RepoStateFreshnessIssueKind>();

        if (request is null)
        {
            issues.Add(RepoStateFreshnessIssueKind.MissingRequest);
            return Build(issues);
        }

        if (request.Expected is null)
            issues.Add(RepoStateFreshnessIssueKind.MissingExpectedState);
        if (request.Observed is null)
            issues.Add(RepoStateFreshnessIssueKind.MissingObservedState);

        if (request.Expected is null || request.Observed is null)
            return Build(issues);

        EvaluateWorktree(request.Observed, issues);
        EvaluateBase(request.Expected, request.Observed, issues);
        EvaluateHead(request.Expected, request.Observed, issues);
        EvaluatePatch(request.Observed, issues);
        EvaluateCommitHead(request.Expected, request.Observed, issues);
        EvaluateRemote(request.Expected, request.Observed, issues);
        EvaluateValidation(request, request.Expected, request.Observed, issues);

        return Build(issues);
    }

    private static void EvaluateWorktree(
        RepoStateObservation observed,
        ICollection<RepoStateFreshnessIssueKind> issues)
    {
        if (observed.WorktreeState == RepoWorktreeState.Dirty)
            issues.Add(RepoStateFreshnessIssueKind.DirtyWorktree);
        if (observed.WorktreeState == RepoWorktreeState.Unknown)
            issues.Add(RepoStateFreshnessIssueKind.UnknownWorktree);
    }

    private static void EvaluateBase(
        RepoStateExpectation expected,
        RepoStateObservation observed,
        ICollection<RepoStateFreshnessIssueKind> issues)
    {
        if (!Same(expected.BaseBranch, observed.BaseBranch) || !Same(expected.BaseSha, observed.BaseSha))
            issues.Add(RepoStateFreshnessIssueKind.BaseBranchMoved);
    }

    private static void EvaluateHead(
        RepoStateExpectation expected,
        RepoStateObservation observed,
        ICollection<RepoStateFreshnessIssueKind> issues)
    {
        if (!Same(expected.HeadBranch, observed.HeadBranch) || !Same(expected.HeadSha, observed.HeadSha))
            issues.Add(RepoStateFreshnessIssueKind.HeadBranchMoved);
    }

    private static void EvaluatePatch(
        RepoStateObservation observed,
        ICollection<RepoStateFreshnessIssueKind> issues)
    {
        if (observed.PatchApplicability == PatchApplicability.DoesNotApply)
            issues.Add(RepoStateFreshnessIssueKind.PatchNoLongerApplies);
        if (observed.PatchApplicability == PatchApplicability.Unknown)
            issues.Add(RepoStateFreshnessIssueKind.PatchApplicabilityUnknown);
    }

    private static void EvaluateCommitHead(
        RepoStateExpectation expected,
        RepoStateObservation observed,
        ICollection<RepoStateFreshnessIssueKind> issues)
    {
        if (!HasValue(expected.CommitHeadSha))
            return;

        if (!Same(expected.CommitHeadSha, observed.CommitHeadSha))
            issues.Add(RepoStateFreshnessIssueKind.CommitHeadChanged);
    }

    private static void EvaluateRemote(
        RepoStateExpectation expected,
        RepoStateObservation observed,
        ICollection<RepoStateFreshnessIssueKind> issues)
    {
        if (!HasValue(expected.RemoteBranch) && !HasValue(expected.RemoteSha))
            return;

        if (!Same(expected.RemoteBranch, observed.RemoteBranch) || !Same(expected.RemoteSha, observed.RemoteSha))
            issues.Add(RepoStateFreshnessIssueKind.RemoteChanged);
    }

    private static void EvaluateValidation(
        RepoStateFreshnessRequest request,
        RepoStateExpectation expected,
        RepoStateObservation observed,
        ICollection<RepoStateFreshnessIssueKind> issues)
    {
        if (expected.ValidationObservedAtUtc == default ||
            expected.ValidationExpiresAtUtc == default ||
            request.ObservedAtUtc == default ||
            observed.ObservedAtUtc == default ||
            expected.ValidationExpiresAtUtc <= request.ObservedAtUtc)
        {
            issues.Add(RepoStateFreshnessIssueKind.StaleValidation);
        }

        if (!Same(expected.ValidationBaseSha, observed.BaseSha) ||
            !Same(expected.ValidationHeadSha, observed.HeadSha) ||
            !Same(expected.ValidationPatchHash, expected.PatchHash))
        {
            issues.Add(RepoStateFreshnessIssueKind.ValidationMismatch);
        }
    }

    private static RepoStateFreshnessResult Build(IEnumerable<RepoStateFreshnessIssueKind> issues)
    {
        var issueList = issues
            .Where(issue => issue != RepoStateFreshnessIssueKind.None)
            .Distinct()
            .ToArray();
        var verdict = DetermineVerdict(issueList);

        return new()
        {
            IsFreshForMutation = issueList.Length == 0,
            Verdict = verdict,
            IssueKinds = issueList,
            BlockingReasons = BuildBlockingReasons(issueList),
            MissingEvidenceRefs = BuildMissingEvidence(issueList),
            NextSafeActions = BuildNextSafeActions(issueList),
            Boundary = RepoStateFreshnessBoundary.GuardOnly
        };
    }

    private static RepoStateFreshnessVerdict DetermineVerdict(
        IReadOnlyCollection<RepoStateFreshnessIssueKind> issues)
    {
        if (issues.Count == 0)
            return RepoStateFreshnessVerdict.Fresh;
        if (issues.Contains(RepoStateFreshnessIssueKind.ContradictoryEvidence))
            return RepoStateFreshnessVerdict.Contradictory;
        if (issues.All(IsStaleIssue))
            return RepoStateFreshnessVerdict.Stale;
        return RepoStateFreshnessVerdict.Blocked;
    }

    private static bool IsStaleIssue(RepoStateFreshnessIssueKind issue) =>
        issue is RepoStateFreshnessIssueKind.BaseBranchMoved
            or RepoStateFreshnessIssueKind.HeadBranchMoved
            or RepoStateFreshnessIssueKind.CommitHeadChanged
            or RepoStateFreshnessIssueKind.RemoteChanged
            or RepoStateFreshnessIssueKind.StaleValidation
            or RepoStateFreshnessIssueKind.ValidationMismatch;

    private static IReadOnlyList<string> BuildBlockingReasons(
        IEnumerable<RepoStateFreshnessIssueKind> issues) =>
        Clean(issues.Select(ReasonFor));

    private static IReadOnlyList<string> BuildMissingEvidence(
        IEnumerable<RepoStateFreshnessIssueKind> issues) =>
        Clean(issues.Select(MissingEvidenceFor));

    private static IReadOnlyList<string> BuildNextSafeActions(
        IReadOnlyCollection<RepoStateFreshnessIssueKind> issues)
    {
        if (issues.Count == 0)
        {
            return
            [
                "freshness guard only explains current state; require the relevant governed authority before mutation"
            ];
        }

        var actions = new List<string>();
        foreach (var issue in issues)
            actions.Add(ActionFor(issue));

        actions.Add("do not treat freshness guard output as approval, policy satisfaction, source apply, commit, push, or pull request authority");
        return Clean(actions);
    }

    private static string ReasonFor(RepoStateFreshnessIssueKind issue) =>
        issue switch
        {
            RepoStateFreshnessIssueKind.MissingRequest => "MissingRequest",
            RepoStateFreshnessIssueKind.MissingExpectedState => "MissingExpectedState",
            RepoStateFreshnessIssueKind.MissingObservedState => "MissingObservedState",
            RepoStateFreshnessIssueKind.DirtyWorktree => "DirtyWorktree",
            RepoStateFreshnessIssueKind.UnknownWorktree => "UnknownWorktree",
            RepoStateFreshnessIssueKind.BaseBranchMoved => "BaseBranchMoved",
            RepoStateFreshnessIssueKind.HeadBranchMoved => "HeadBranchMoved",
            RepoStateFreshnessIssueKind.PatchNoLongerApplies => "PatchNoLongerApplies",
            RepoStateFreshnessIssueKind.PatchApplicabilityUnknown => "PatchApplicabilityUnknown",
            RepoStateFreshnessIssueKind.CommitHeadChanged => "CommitHeadChanged",
            RepoStateFreshnessIssueKind.RemoteChanged => "RemoteChanged",
            RepoStateFreshnessIssueKind.StaleValidation => "StaleValidation",
            RepoStateFreshnessIssueKind.ValidationMismatch => "ValidationMismatch",
            RepoStateFreshnessIssueKind.ContradictoryEvidence => "ContradictoryEvidence",
            _ => "UnknownFreshnessIssue"
        };

    private static string MissingEvidenceFor(RepoStateFreshnessIssueKind issue) =>
        issue switch
        {
            RepoStateFreshnessIssueKind.MissingRequest => "repo-state-freshness-request",
            RepoStateFreshnessIssueKind.MissingExpectedState => "expected-repo-state",
            RepoStateFreshnessIssueKind.MissingObservedState => "observed-repo-state",
            RepoStateFreshnessIssueKind.DirtyWorktree => "clean-worktree-observation",
            RepoStateFreshnessIssueKind.UnknownWorktree => "worktree-state-observation",
            RepoStateFreshnessIssueKind.BaseBranchMoved => "fresh-base-branch-evidence",
            RepoStateFreshnessIssueKind.HeadBranchMoved => "fresh-head-branch-evidence",
            RepoStateFreshnessIssueKind.PatchNoLongerApplies => "applicable-patch-evidence",
            RepoStateFreshnessIssueKind.PatchApplicabilityUnknown => "patch-applicability-evidence",
            RepoStateFreshnessIssueKind.CommitHeadChanged => "fresh-commit-package-evidence",
            RepoStateFreshnessIssueKind.RemoteChanged => "fresh-remote-branch-evidence",
            RepoStateFreshnessIssueKind.StaleValidation => "fresh-validation-result",
            RepoStateFreshnessIssueKind.ValidationMismatch => "matching-validation-result",
            RepoStateFreshnessIssueKind.ContradictoryEvidence => "consistent-repo-state-evidence",
            _ => "freshness-evidence"
        };

    private static string ActionFor(RepoStateFreshnessIssueKind issue) =>
        issue switch
        {
            RepoStateFreshnessIssueKind.MissingRequest => "collect the repo state freshness request and retry diagnosis only",
            RepoStateFreshnessIssueKind.MissingExpectedState => "collect expected repo state evidence before mutation",
            RepoStateFreshnessIssueKind.MissingObservedState => "collect observed current repo state evidence before mutation",
            RepoStateFreshnessIssueKind.DirtyWorktree => "inspect and clean the worktree under governed authority",
            RepoStateFreshnessIssueKind.UnknownWorktree => "collect fresh worktree state observation",
            RepoStateFreshnessIssueKind.BaseBranchMoved => "request fresh proposal, validation, and authority because the base moved",
            RepoStateFreshnessIssueKind.HeadBranchMoved => "request human review or fresh authority because the head moved",
            RepoStateFreshnessIssueKind.PatchNoLongerApplies => "create a revised governed patch proposal and validation",
            RepoStateFreshnessIssueKind.PatchApplicabilityUnknown => "collect explicit patch applicability evidence",
            RepoStateFreshnessIssueKind.CommitHeadChanged => "request fresh commit package or human review",
            RepoStateFreshnessIssueKind.RemoteChanged => "request fresh push or pull request decision",
            RepoStateFreshnessIssueKind.StaleValidation => "run fresh governed validation before mutation",
            RepoStateFreshnessIssueKind.ValidationMismatch => "run validation against the current base, head, and patch hash",
            RepoStateFreshnessIssueKind.ContradictoryEvidence => "inspect contradictory repo state evidence and request human review",
            _ => "request human review of repo state freshness"
        };

    private static bool Same(string? left, string? right) =>
        string.Equals(CleanText(left), CleanText(right), StringComparison.OrdinalIgnoreCase);

    private static bool HasValue(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    private static string CleanText(string? value) =>
        value?.Trim() ?? string.Empty;

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
