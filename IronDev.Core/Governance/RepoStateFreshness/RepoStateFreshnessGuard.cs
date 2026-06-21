using IronDev.Core.Governance;

namespace IronDev.Core.Governance.RepoStateFreshness;

public static class RepoStateFreshnessGuard
{
    public static RepoStateFreshnessResult Evaluate(RepoStateFreshnessRequest? request)
    {
        var issues = new List<RepoStateFreshnessIssueKind>();
        var detailReasons = new List<string>();
        var detailMissingEvidence = new List<string>();

        if (request is null)
        {
            issues.Add(RepoStateFreshnessIssueKind.MissingRequest);
            return Build(issues, detailReasons, detailMissingEvidence);
        }

        ValidateRequest(request, issues, detailReasons, detailMissingEvidence);

        if (request.Expected is null)
            issues.Add(RepoStateFreshnessIssueKind.MissingExpectedState);
        if (request.Observed is null)
            issues.Add(RepoStateFreshnessIssueKind.MissingObservedState);

        if (request.Expected is null || request.Observed is null)
            return Build(issues, detailReasons, detailMissingEvidence);

        ValidateExpected(request.Expected, issues, detailReasons, detailMissingEvidence);
        ValidateObserved(request.Observed, issues, detailReasons, detailMissingEvidence);
        EvaluateWorktree(request.Observed, issues);
        EvaluateBase(request.Expected, request.Observed, issues);
        EvaluateHead(request.Expected, request.Observed, issues);
        EvaluatePatch(request.Observed, issues);
        EvaluateCommitHead(request.Expected, request.Observed, issues);
        EvaluateRemote(request.Expected, request.Observed, issues);
        EvaluateValidation(request, request.Expected, request.Observed, issues);

        return Build(issues, detailReasons, detailMissingEvidence);
    }

    private static void ValidateRequest(
        RepoStateFreshnessRequest request,
        ICollection<RepoStateFreshnessIssueKind> issues,
        ICollection<string> detailReasons,
        ICollection<string> detailMissingEvidence)
    {
        RequireText(request.CheckId, "Request.CheckId", "request-check-id", issues, detailReasons, detailMissingEvidence);
        RequireText(request.Repository, "Request.Repository", "request-repository", issues, detailReasons, detailMissingEvidence);
        RequireText(request.RunId, "Request.RunId", "request-run-id", issues, detailReasons, detailMissingEvidence);
        RequireUtc(request.ObservedAtUtc, "Request.ObservedAtUtc", "request-observed-at-utc", issues, detailReasons, detailMissingEvidence);

        if (!Enum.IsDefined(request.OperationKind) || request.OperationKind == RunAuthorityOperationKind.Unknown)
            issues.Add(RepoStateFreshnessIssueKind.InvalidOperationKind);
    }

    private static void ValidateExpected(
        RepoStateExpectation expected,
        ICollection<RepoStateFreshnessIssueKind> issues,
        ICollection<string> detailReasons,
        ICollection<string> detailMissingEvidence)
    {
        RequireText(expected.BaseBranch, "Expected.BaseBranch", "expected-base-branch", issues, detailReasons, detailMissingEvidence);
        RequireText(expected.BaseSha, "Expected.BaseSha", "expected-base-sha", issues, detailReasons, detailMissingEvidence);
        RequireText(expected.HeadBranch, "Expected.HeadBranch", "expected-head-branch", issues, detailReasons, detailMissingEvidence);
        RequireText(expected.HeadSha, "Expected.HeadSha", "expected-head-sha", issues, detailReasons, detailMissingEvidence);
        RequireText(expected.PatchHash, "Expected.PatchHash", "expected-patch-hash", issues, detailReasons, detailMissingEvidence);
        RequireUtc(expected.ValidationObservedAtUtc, "Expected.ValidationObservedAtUtc", "expected-validation-observed-at-utc", issues, detailReasons, detailMissingEvidence);
        RequireText(expected.ValidationBaseSha, "Expected.ValidationBaseSha", "expected-validation-base-sha", issues, detailReasons, detailMissingEvidence);
        RequireText(expected.ValidationHeadSha, "Expected.ValidationHeadSha", "expected-validation-head-sha", issues, detailReasons, detailMissingEvidence);
        RequireText(expected.ValidationPatchHash, "Expected.ValidationPatchHash", "expected-validation-patch-hash", issues, detailReasons, detailMissingEvidence);
        RequireUtc(expected.ValidationExpiresAtUtc, "Expected.ValidationExpiresAtUtc", "expected-validation-expires-at-utc", issues, detailReasons, detailMissingEvidence);
    }

    private static void ValidateObserved(
        RepoStateObservation observed,
        ICollection<RepoStateFreshnessIssueKind> issues,
        ICollection<string> detailReasons,
        ICollection<string> detailMissingEvidence)
    {
        RequireText(observed.BaseBranch, "Observed.BaseBranch", "observed-base-branch", issues, detailReasons, detailMissingEvidence);
        RequireText(observed.BaseSha, "Observed.BaseSha", "observed-base-sha", issues, detailReasons, detailMissingEvidence);
        RequireText(observed.HeadBranch, "Observed.HeadBranch", "observed-head-branch", issues, detailReasons, detailMissingEvidence);
        RequireText(observed.HeadSha, "Observed.HeadSha", "observed-head-sha", issues, detailReasons, detailMissingEvidence);
        RequireUtc(observed.ObservedAtUtc, "Observed.ObservedAtUtc", "observed-at-utc", issues, detailReasons, detailMissingEvidence);

        if (!Enum.IsDefined(observed.WorktreeState))
            issues.Add(RepoStateFreshnessIssueKind.UnknownWorktree);
        if (!Enum.IsDefined(observed.PatchApplicability))
            issues.Add(RepoStateFreshnessIssueKind.PatchApplicabilityUnknown);
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
        if (!HasValue(expected.CommitHeadSha) && !HasValue(observed.CommitHeadSha))
            return;

        if (!Same(expected.CommitHeadSha, observed.CommitHeadSha))
            issues.Add(RepoStateFreshnessIssueKind.CommitHeadChanged);
    }

    private static void EvaluateRemote(
        RepoStateExpectation expected,
        RepoStateObservation observed,
        ICollection<RepoStateFreshnessIssueKind> issues)
    {
        if (!HasValue(expected.RemoteBranch) &&
            !HasValue(expected.RemoteSha) &&
            !HasValue(observed.RemoteBranch) &&
            !HasValue(observed.RemoteSha))
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

    private static RepoStateFreshnessResult Build(
        IEnumerable<RepoStateFreshnessIssueKind> issues,
        IEnumerable<string> detailReasons,
        IEnumerable<string> detailMissingEvidence)
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
            BlockingReasons = BuildBlockingReasons(issueList, detailReasons),
            MissingEvidenceRefs = BuildMissingEvidence(issueList, detailMissingEvidence),
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
        IEnumerable<RepoStateFreshnessIssueKind> issues,
        IEnumerable<string> detailReasons) =>
        Clean(issues.Select(ReasonFor).Concat(detailReasons));

    private static IReadOnlyList<string> BuildMissingEvidence(
        IEnumerable<RepoStateFreshnessIssueKind> issues,
        IEnumerable<string> detailMissingEvidence) =>
        Clean(issues.Select(MissingEvidenceFor).Concat(detailMissingEvidence));

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
            RepoStateFreshnessIssueKind.MissingRequiredStateEvidence => "MissingRequiredStateEvidence",
            RepoStateFreshnessIssueKind.InvalidOperationKind => "InvalidOperationKind",
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
            RepoStateFreshnessIssueKind.MissingRequiredStateEvidence => "required-repo-state-evidence",
            RepoStateFreshnessIssueKind.InvalidOperationKind => "valid-operation-kind",
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
            RepoStateFreshnessIssueKind.MissingRequiredStateEvidence => "collect complete expected and observed repo state evidence",
            RepoStateFreshnessIssueKind.InvalidOperationKind => "use a known governed operation kind before freshness evaluation",
            _ => "request human review of repo state freshness"
        };

    private static void RequireText(
        string? value,
        string field,
        string missingEvidence,
        ICollection<RepoStateFreshnessIssueKind> issues,
        ICollection<string> detailReasons,
        ICollection<string> detailMissingEvidence)
    {
        if (HasValue(value))
            return;

        issues.Add(RepoStateFreshnessIssueKind.MissingRequiredStateEvidence);
        detailReasons.Add($"MissingRequiredStateEvidence:{field}");
        detailMissingEvidence.Add(missingEvidence);
    }

    private static void RequireUtc(
        DateTimeOffset value,
        string field,
        string missingEvidence,
        ICollection<RepoStateFreshnessIssueKind> issues,
        ICollection<string> detailReasons,
        ICollection<string> detailMissingEvidence)
    {
        if (value != default)
            return;

        issues.Add(RepoStateFreshnessIssueKind.MissingRequiredStateEvidence);
        detailReasons.Add($"MissingRequiredStateEvidence:{field}");
        detailMissingEvidence.Add(missingEvidence);
    }

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
