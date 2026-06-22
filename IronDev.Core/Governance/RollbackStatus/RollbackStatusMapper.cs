namespace IronDev.Core.Governance.RollbackStatus;

public static class RollbackStatusMapper
{
    private const string OperationKind = "Rollback";

    private static readonly string[] CommonForbiddenActions =
    [
        "rollback plan is not rollback execution",
        "rollback availability is not rollback authority",
        "rollback request accepted is not rollback execution",
        "rollback status is not source mutation",
        "rollback status is not commit authority",
        "rollback status is not push authority",
        "rollback status is not PR authority",
        "rollback status is not merge authority",
        "rollback status is not release authority",
        "rollback status is not deployment authority",
        "rollback status is not workflow continuation",
        "do not call system stable until rollback is boring"
    ];

    public static RollbackStatusMappingResult Map(RollbackStatusEvaluationRequest? request)
    {
        if (request is null)
            return BuildMissingRequestResult();

        var blocked = new List<string>();
        var missing = new List<string>();
        var failed = new List<string>();
        var issues = new List<string>();

        ValidateEnvelope(request, blocked, missing);
        ValidateAvailability(request.Availability, blocked, missing);
        ValidatePlan(request, blocked, missing);
        ValidateAuthority(request, blocked, missing);
        ValidateRequest(request, blocked, missing);
        ValidateApplyReceipt(request, blocked, missing);
        ValidateWorktree(request, blocked, missing);
        ValidatePostState(request, failed, issues);

        var state = DetermineState(blocked, missing, failed);
        var status = BuildStatus(request, state, blocked, missing, failed);
        var validation = GovernedOperationStatusValidator.Validate(status);
        var allIssues = Clean([.. issues, .. blocked, .. missing, .. failed, .. validation.Issues, .. validation.RedFlags]);

        return new RollbackStatusMappingResult
        {
            Status = status,
            StatusValidation = validation,
            IsRollbackExecutionAllowed = state == GovernedOperationState.Eligible,
            IsRollbackExecuted = false,
            Issues = allIssues
        };
    }

    private static RollbackStatusMappingResult BuildMissingRequestResult()
    {
        var observed = DateTimeOffset.UnixEpoch;
        var status = new GovernedOperationStatus
        {
            OperationId = "rollback-status-request-missing",
            OperationKind = OperationKind,
            Subject = "rollback status mapping",
            State = GovernedOperationState.Blocked,
            BlockedReasons = ["RollbackStatusEvaluationRequestRequired"],
            MissingEvidence = ["rollback-status-evaluation-request"],
            NextSafeActions = ["request rollback status evidence"],
            ForbiddenActions = CommonForbiddenActions,
            EvidenceRefs = [],
            ReceiptRefs = [],
            ExpiresAtUtc = null,
            ObservedAtUtc = observed
        };
        var validation = GovernedOperationStatusValidator.Validate(status);
        return new RollbackStatusMappingResult
        {
            Status = status,
            StatusValidation = validation,
            IsRollbackExecutionAllowed = false,
            IsRollbackExecuted = false,
            Issues = Clean(["RollbackStatusEvaluationRequestRequired", .. validation.Issues, .. validation.RedFlags])
        };
    }

    private static void ValidateEnvelope(
        RollbackStatusEvaluationRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        RequireText(request.EvaluationId, "EvaluationIdRequired", missing);
        ValidateSingleExplicitScope(request.Repository, "Repository", blocked, missing);
        ValidateSingleExplicitScope(request.Branch, "Branch", blocked, missing);
        ValidateSingleExplicitScope(request.RunId, "RunId", blocked, missing);
        if (string.IsNullOrWhiteSpace(request.PatchHash))
            missing.Add("PatchHashRequired");
        else if (!OperationEligibilityPatchHashRules.IsSafePatchHash(request.PatchHash))
            blocked.Add("PatchHashInvalid");
        RequireText(request.SourceApplyReceiptRef, "SourceApplyReceiptRefRequired", missing);
        if (request.ObservedAtUtc == default)
            missing.Add("ObservedAtUtcRequired");
    }

    private static void ValidateAvailability(
        RollbackAvailabilityEvidence? availability,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        if (availability is null)
        {
            blocked.Add("RollbackAvailabilityEvidenceRequired");
            missing.Add("rollback-availability");
            return;
        }

        RequireText(availability.EvidenceRef, "RollbackAvailabilityEvidenceRefRequired", missing);
        RequireText(availability.AvailabilityReason, "RollbackAvailabilityReasonRequired", missing);
        if (!availability.IsRollbackAvailable)
        {
            blocked.Add("RollbackUnavailable");
            missing.Add("available-rollback-support");
        }
    }

    private static void ValidatePlan(
        RollbackStatusEvaluationRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        var plan = request.Plan;
        if (plan is null)
        {
            blocked.Add("RollbackPlanRequired");
            missing.Add("rollback-plan");
            return;
        }

        RequireText(plan.EvidenceRef, "RollbackPlanEvidenceRefRequired", missing);
        Match(plan.Repository, request.Repository, "RollbackPlanRepositoryMismatch", blocked);
        Match(plan.Branch, request.Branch, "RollbackPlanBranchMismatch", blocked);
        Match(plan.RunId, request.RunId, "RollbackPlanRunIdMismatch", blocked);
        Match(plan.PatchHash, request.PatchHash, "RollbackPlanPatchHashMismatch", blocked);
        MatchApplyReceipt(plan.SourceApplyReceiptRef, request.SourceApplyReceiptRef, blocked, missing);

        if (!plan.HasRollbackPlan)
        {
            blocked.Add("RollbackPlanRequired");
            missing.Add("rollback-plan");
        }

        if (!plan.IsPlanBoundToApplyReceipt)
            blocked.Add("RollbackPlanNotBoundToApplyReceipt");
        if (plan.RequiresPartialRollback || plan.HasPartialRollbackRisk)
            blocked.Add("PartialRollbackRisk");

        if (plan.PlannedRollbackFilePaths is null)
        {
            missing.Add("rollback-plan-file-paths");
            return;
        }

        foreach (var path in plan.PlannedRollbackFilePaths)
        {
            if (!IsSafeRelativePath(path))
                blocked.Add($"UnsafeRollbackPlanPath:{path}");
        }
    }

    private static void ValidateAuthority(
        RollbackStatusEvaluationRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        var authority = request.Authority;
        if (authority is null)
        {
            blocked.Add("RollbackAuthorityRequired");
            missing.Add("rollback-operation-authority");
            return;
        }

        if (string.IsNullOrWhiteSpace(authority.EvidenceRef) ||
            !(authority.EvidenceRef.StartsWith("rollback-operation-authority:", StringComparison.OrdinalIgnoreCase) ||
              authority.EvidenceRef.StartsWith("operation-eligibility-decision:", StringComparison.OrdinalIgnoreCase)))
        {
            blocked.Add("RollbackAuthorityEvidenceRefInvalid");
            missing.Add("rollback-operation-authority");
        }

        Match(authority.Repository, request.Repository, "RollbackAuthorityRepositoryMismatch", blocked);
        Match(authority.Branch, request.Branch, "RollbackAuthorityBranchMismatch", blocked);
        Match(authority.RunId, request.RunId, "RollbackAuthorityRunIdMismatch", blocked);
        Match(authority.PatchHash, request.PatchHash, "RollbackAuthorityPatchHashMismatch", blocked);
        MatchApplyReceipt(authority.SourceApplyReceiptRef, request.SourceApplyReceiptRef, blocked, missing);

        var decision = authority.Decision;
        if (decision is null)
        {
            blocked.Add("RollbackAuthorityDecisionRequired");
            missing.Add("operation-eligibility-decision");
            return;
        }

        if (decision.OperationKind != RunAuthorityOperationKind.Rollback)
        {
            blocked.Add("RollbackAuthorityOperationMismatch");
            missing.Add("rollback-operation-authority");
        }

        if (!decision.IsEligibleUnderProfileAndGrant)
            blocked.Add("RollbackAuthorityNotEligible");
        if (HasValues(decision.BlockedReasons))
            AddRange(blocked, decision.BlockedReasons);
        if (HasValues(decision.MissingEvidence))
            AddRange(missing, decision.MissingEvidence);
    }

    private static void ValidateRequest(
        RollbackStatusEvaluationRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        var accepted = request.Request;
        if (accepted is null)
        {
            blocked.Add("RollbackRequestRequired");
            missing.Add("accepted-rollback-request");
            return;
        }

        RequireText(accepted.EvidenceRef, "RollbackRequestEvidenceRefRequired", missing);
        Match(accepted.Repository, request.Repository, "RollbackRequestRepositoryMismatch", blocked);
        Match(accepted.Branch, request.Branch, "RollbackRequestBranchMismatch", blocked);
        Match(accepted.RunId, request.RunId, "RollbackRequestRunIdMismatch", blocked);
        Match(accepted.PatchHash, request.PatchHash, "RollbackRequestPatchHashMismatch", blocked);
        MatchApplyReceipt(accepted.SourceApplyReceiptRef, request.SourceApplyReceiptRef, blocked, missing);
        if (!accepted.IsRollbackRequestAccepted)
        {
            blocked.Add("RollbackRequestNotAccepted");
            missing.Add("accepted-rollback-request");
        }

        if (accepted.AcceptedAtUtc == default)
            missing.Add("rollback-request-accepted-at");
    }

    private static void ValidateApplyReceipt(
        RollbackStatusEvaluationRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        var receipt = request.ApplyReceipt;
        if (receipt is null)
        {
            blocked.Add("RollbackApplyReceiptRequired");
            missing.Add("matching-source-apply-receipt");
            return;
        }

        MatchApplyReceipt(receipt.ReceiptRef, request.SourceApplyReceiptRef, blocked, missing);
        Match(receipt.Repository, request.Repository, "RollbackApplyReceiptRepositoryMismatch", blocked);
        Match(receipt.Branch, request.Branch, "RollbackApplyReceiptBranchMismatch", blocked);
        Match(receipt.RunId, request.RunId, "RollbackApplyReceiptRunIdMismatch", blocked);
        Match(receipt.PatchHash, request.PatchHash, "RollbackApplyReceiptPatchHashMismatch", blocked);
        if (!receipt.IsSourceApplyReceipt)
            blocked.Add("RollbackApplyReceiptNotSourceApply");
        if (!receipt.IsApplyReceiptAcceptedForRollback)
            blocked.Add("RollbackApplyReceiptNotAcceptedForRollback");
    }

    private static void ValidateWorktree(
        RollbackStatusEvaluationRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        var worktree = request.Worktree;
        if (worktree is null)
        {
            blocked.Add("WorktreeStateRequired");
            missing.Add("immediate-clean-worktree");
            return;
        }

        RequireText(worktree.EvidenceRef, "RollbackWorktreeEvidenceRefRequired", missing);
        Match(worktree.Repository, request.Repository, "RollbackWorktreeRepositoryMismatch", blocked);
        Match(worktree.Branch, request.Branch, "RollbackWorktreeBranchMismatch", blocked);
        RequireText(worktree.HeadCommitId, "RollbackWorktreeHeadCommitRequired", missing);

        if (worktree.ChangedFilePaths is null || worktree.StagedFilePaths is null || worktree.UntrackedFilePaths is null)
        {
            blocked.Add("WorktreeStateRequired");
            missing.Add("immediate-clean-worktree");
            return;
        }

        if (!worktree.IsObservedImmediatelyBeforeRollback ||
            HasValues(worktree.ChangedFilePaths) ||
            HasValues(worktree.StagedFilePaths) ||
            HasValues(worktree.UntrackedFilePaths))
        {
            blocked.Add("DirtyWorktree");
            missing.Add("clean-immediate-worktree-state");
        }
    }

    private static void ValidatePostState(
        RollbackStatusEvaluationRequest request,
        ICollection<string> failed,
        ICollection<string> issues)
    {
        var post = request.PostState;
        if (post is null)
            return;

        if (string.IsNullOrWhiteSpace(post.EvidenceRef))
            failed.Add("RollbackPostStateMismatch");
        if (!Same(post.Repository, request.Repository) ||
            !Same(post.Branch, request.Branch) ||
            !Same(post.RunId, request.RunId) ||
            !Same(post.PatchHash, request.PatchHash) ||
            !Same(post.SourceApplyReceiptRef, request.SourceApplyReceiptRef))
        {
            failed.Add("RollbackPostStateMismatch");
        }

        if (post.RemainingChangedFilePaths is null ||
            post.RemainingStagedFilePaths is null ||
            post.RemainingUntrackedFilePaths is null)
        {
            failed.Add("RollbackPostStateMismatch");
            issues.Add("RollbackPostStateCollectionsRequired");
            return;
        }

        if (!post.IsObservedAfterRollback ||
            !post.MatchesExpectedPostRollbackState ||
            HasValues(post.RemainingChangedFilePaths) ||
            HasValues(post.RemainingStagedFilePaths) ||
            HasValues(post.RemainingUntrackedFilePaths))
        {
            failed.Add("RollbackPostStateMismatch");
        }
    }

    private static GovernedOperationState DetermineState(
        IReadOnlyCollection<string> blocked,
        IReadOnlyCollection<string> missing,
        IReadOnlyCollection<string> failed)
    {
        if (!HasValues(blocked) && !HasValues(missing) && HasValues(failed))
            return GovernedOperationState.Failed;
        if (HasValues(blocked) || HasValues(missing))
            return GovernedOperationState.Blocked;
        return GovernedOperationState.Eligible;
    }

    private static GovernedOperationStatus BuildStatus(
        RollbackStatusEvaluationRequest request,
        GovernedOperationState state,
        IReadOnlyCollection<string> blocked,
        IReadOnlyCollection<string> missing,
        IReadOnlyCollection<string> failed)
    {
        return new GovernedOperationStatus
        {
            OperationId = CleanText(request.EvaluationId, "rollback-status-evaluation"),
            OperationKind = OperationKind,
            Subject = $"rollback status for {CleanText(request.Repository, "unknown-repository")} {CleanText(request.Branch, "unknown-branch")} {CleanText(request.PatchHash, "unknown-patch")}",
            State = state,
            BlockedReasons = state == GovernedOperationState.Blocked ? Clean(blocked) : [],
            MissingEvidence = state == GovernedOperationState.Blocked ? Clean(missing) : [],
            NextSafeActions = BuildNextSafeActions(state, blocked, missing, failed),
            ForbiddenActions = BuildForbiddenActions(state, blocked, failed),
            EvidenceRefs = BuildEvidenceRefs(request),
            ReceiptRefs = Clean(request.ReceiptRefs),
            ExpiresAtUtc = null,
            ObservedAtUtc = request.ObservedAtUtc == default ? DateTimeOffset.UnixEpoch : request.ObservedAtUtc
        };
    }

    private static IReadOnlyList<string> BuildEvidenceRefs(RollbackStatusEvaluationRequest request) =>
        Clean(
        [
            Ref("rollback-status-evaluation", request.EvaluationId),
            Ref("repo", request.Repository),
            Ref("branch", request.Branch),
            Ref("run", request.RunId),
            Ref("patch-hash", request.PatchHash),
            Ref("source-apply-receipt", request.SourceApplyReceiptRef),
            request.Availability?.EvidenceRef,
            request.Plan?.EvidenceRef,
            request.Authority?.EvidenceRef,
            request.Request?.EvidenceRef,
            request.ApplyReceipt?.ReceiptRef,
            request.Worktree?.EvidenceRef,
            request.PostState?.EvidenceRef,
            .. ValuesOrEmpty(request.EvidenceRefs)
        ]);

    private static IReadOnlyList<string> BuildNextSafeActions(
        GovernedOperationState state,
        IReadOnlyCollection<string> blocked,
        IReadOnlyCollection<string> missing,
        IReadOnlyCollection<string> failed)
    {
        if (state == GovernedOperationState.Eligible)
            return ["request controlled rollback executor review separately"];

        if (state == GovernedOperationState.Failed)
        {
            return
            [
                "inspect rollback status failure",
                "request fresh rollback authority before retry"
            ];
        }

        var actions = new List<string>();
        if (Contains(blocked, "RollbackUnavailable") || Contains(blocked, "RollbackAvailabilityEvidenceRequired"))
        {
            actions.Add("inspect source apply receipt");
            actions.Add("create rollback plan evidence");
        }

        if (Contains(blocked, "RollbackAuthorityRequired") || Contains(missing, "rollback-operation-authority"))
            actions.Add("request rollback operation authority");
        if (Contains(blocked, "RollbackApplyReceiptMismatch"))
            actions.Add("inspect matching source apply receipt evidence");
        if (Contains(blocked, "PartialRollbackRisk"))
            actions.Add("request explicit partial rollback authority in future slice");
        if (Contains(blocked, "DirtyWorktree") || Contains(blocked, "WorktreeStateRequired"))
            actions.Add("request worktree cleanup or preservation before rollback");

        actions.Add("collect missing rollback status evidence");
        return Clean(actions);
    }

    private static IReadOnlyList<string> BuildForbiddenActions(
        GovernedOperationState state,
        IReadOnlyCollection<string> blocked,
        IReadOnlyCollection<string> failed)
    {
        var actions = new List<string>(CommonForbiddenActions);

        if (state == GovernedOperationState.Eligible)
        {
            actions.Add("eligible rollback status is not rollback execution");
            actions.Add("accepted rollback request is not rollback execution");
        }
        if (Contains(blocked, "RollbackUnavailable") || Contains(blocked, "RollbackAvailabilityEvidenceRequired"))
            actions.Add("do not execute rollback from unavailable status");
        if (Contains(blocked, "RollbackAuthorityRequired"))
            actions.Add("rollback plan is not rollback authority");
        if (Contains(blocked, "RollbackApplyReceiptMismatch"))
            actions.Add("do not rollback wrong apply receipt");
        if (Contains(blocked, "PartialRollbackRisk"))
            actions.Add("do not execute partial rollback from general rollback status");
        if (Contains(blocked, "DirtyWorktree") || Contains(blocked, "WorktreeStateRequired"))
            actions.Add("do not rollback over dirty worktree");
        if (state == GovernedOperationState.Failed || Contains(failed, "RollbackPostStateMismatch"))
        {
            actions.Add("do not continue workflow after rollback mismatch");
            actions.Add("do not treat failed rollback as stable");
        }

        return Clean(actions);
    }

    private static void ValidateSingleExplicitScope(
        string? value,
        string label,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add($"{label}Required");
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal) ||
            trimmed.Contains('?', StringComparison.Ordinal) ||
            trimmed.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            blocked.Add($"{label}MustBeSingleExplicitScope");
        }
    }

    private static void MatchApplyReceipt(
        string? actual,
        string? expected,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        if (!Same(actual, expected))
        {
            blocked.Add("RollbackApplyReceiptMismatch");
            missing.Add("matching-source-apply-receipt");
        }
    }

    private static void Match(string? actual, string? expected, string issue, ICollection<string> blocked)
    {
        if (!Same(actual, expected))
            blocked.Add(issue);
    }

    private static void RequireText(string? value, string issue, ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(value))
            missing.Add(issue);
    }

    private static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = path.Trim().Replace('\\', '/');
        if (!string.Equals(normalized, path, StringComparison.Ordinal) && path.Contains('\\', StringComparison.Ordinal))
            return false;
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal) ||
            normalized.StartsWith("~", StringComparison.Ordinal) ||
            normalized.Contains('\0', StringComparison.Ordinal) ||
            normalized.Contains('$', StringComparison.Ordinal) ||
            normalized.Contains('%', StringComparison.Ordinal))
        {
            return false;
        }

        if (normalized.Length >= 2 &&
            char.IsLetter(normalized[0]) &&
            normalized[1] == ':')
        {
            return false;
        }

        if (normalized == "." ||
            normalized == ".." ||
            normalized.StartsWith("../", StringComparison.Ordinal) ||
            normalized.Contains("/../", StringComparison.Ordinal) ||
            normalized.EndsWith("/..", StringComparison.Ordinal))
        {
            return false;
        }

        return !normalized.Any(char.IsControl);
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Contains(IEnumerable<string> values, string expected) =>
        values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase));

    private static string Ref(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}:{value.Trim()}";

    private static string CleanText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool HasValues(IEnumerable<string?>? values) =>
        ValuesOrEmpty(values).Any(value => !string.IsNullOrWhiteSpace(value));

    private static void AddRange(ICollection<string> target, IEnumerable<string>? values)
    {
        foreach (var value in ValuesOrEmpty(values))
        {
            if (!string.IsNullOrWhiteSpace(value))
                target.Add(value);
        }
    }

    private static IReadOnlyList<string> Clean(IEnumerable<string?>? values) =>
        ValuesOrEmpty(values)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string?> ValuesOrEmpty(IEnumerable<string?>? values) =>
        values ?? [];
}
