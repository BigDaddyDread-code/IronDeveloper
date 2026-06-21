namespace IronDev.Core.Governance;

public static class AuthorityProfileStatusMapper
{
    public static GovernedOperationStatus Map(AuthorityProfileStatusRequest? request)
    {
        if (request is null)
        {
            return BuildStatus(
                operationId: "authority-profile-status-request-missing",
                operationKind: RunAuthorityOperationKind.Unknown,
                subject: "authority profile status request",
                state: GovernedOperationState.Blocked,
                blockedReasons: [Reason(AuthorityProfileStatusReason.AuthorityProfileKnownRequired)],
                missingEvidence: ["authority-profile-status-request"],
                nextSafeActions: ["request authority profile status input"],
                forbiddenActions: ["do not infer authority from missing status input"],
                evidenceRefs: [],
                receiptRefs: [],
                expiresAtUtc: null,
                observedAtUtc: DateTimeOffset.UnixEpoch);
        }

        if (!Enum.IsDefined(request.ProfileKind) || request.ProfileKind == AuthorityProfileKind.Unknown)
        {
            return BuildBlocked(
                request,
                [Reason(AuthorityProfileStatusReason.AuthorityProfileKnownRequired)],
                ["known-authority-profile"],
                ["request known authority profile selection"],
                ["do not infer authority from unknown profile"]);
        }

        if (IsExpired(request))
        {
            return BuildStatus(
                request,
                GovernedOperationState.Expired,
                [Reason(AuthorityProfileStatusReason.BoundedRunGrantExpired)],
                ["fresh bounded run authority grant"],
                ["request fresh bounded grant for this repo/branch/run/scope"],
                ["do not use expired grant"]);
        }

        if (request.ProfileKind == AuthorityProfileKind.ProposalOnly &&
            RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations.Contains(request.OperationKind))
        {
            return BuildBlocked(
                request,
                [
                    Reason(AuthorityProfileStatusReason.ProposalOnlyDoesNotAllowDurableMutation),
                    $"ProposalOnlyOperationBlocked:{request.OperationKind}"
                ],
                [
                    "bounded-run-authority-grant",
                    "accepted-source-apply-authority"
                ],
                [
                    "review patch package",
                    "request bounded mutation authority for this repo/branch/run/scope"
                ],
                [
                    "do not apply source under ProposalOnly",
                    "do not commit under ProposalOnly",
                    "do not push under ProposalOnly",
                    "do not continue workflow from ProposalOnly status"
                ]);
        }

        if (request.ProfileKind == AuthorityProfileKind.AskBeforeMutation &&
            IsMutationOperation(request.OperationKind) &&
            !HasAcceptedApplyApproval(request.EvidenceRefs))
        {
            return BuildBlocked(
                request,
                [Reason(AuthorityProfileStatusReason.MutationRequiresExplicitHumanApproval)],
                [
                    "accepted-apply-approval",
                    "accepted-source-apply-request"
                ],
                ["request human apply approval for this patch hash/scope"],
                [
                    "do not apply source from patch readiness alone",
                    "do not treat validation passed as approval",
                    "do not treat patch package completed as source apply authority"
                ]);
        }

        if (request.EligibilityDecision is null)
        {
            return BuildBlocked(
                request,
                [Reason(AuthorityProfileStatusReason.OperationEligibilityDecisionRequired)],
                ["operation-eligibility-decision"],
                ["inspect pure operation eligibility evaluation output"],
                ["do not infer eligibility from profile/grant text"]);
        }

        if (request.EligibilityDecision.OperationKind != request.OperationKind)
        {
            return BuildBlocked(
                request,
                [Reason(AuthorityProfileStatusReason.OperationEligibilityDecisionOperationMismatch)],
                ["matching operation eligibility decision"],
                ["request operation eligibility evaluation for this operation"],
                ["do not reuse eligibility decision from another operation"]);
        }

        if (HasValues(request.EligibilityDecision.BlockedReasons))
        {
            return BuildBlocked(
                request,
                [
                    Reason(AuthorityProfileStatusReason.OperationEligibilityDecisionBlocked),
                    .. request.EligibilityDecision.BlockedReasons
                ],
                BuildBlockedDecisionMissingEvidence(request),
                BuildBlockedDecisionNextSafeActions(request),
                BuildDecisionForbiddenActions(request));
        }

        if (HasValues(request.EligibilityDecision.MissingEvidence))
        {
            return BuildBlocked(
                request,
                [Reason(AuthorityProfileStatusReason.OperationEligibilityEvidenceMissing)],
                request.EligibilityDecision.MissingEvidence,
                ["collect required validation evidence"],
                BuildDecisionForbiddenActions(request));
        }

        if (request.EligibilityDecision.IsEligibleUnderProfileAndGrant)
        {
            return BuildStatus(
                request,
                GovernedOperationState.Eligible,
                [],
                [],
                ["request controlled executor review for independent authority re-check"],
                [
                    "do not execute from status alone",
                    "do not treat Eligible status as approval",
                    "do not treat Eligible status as policy satisfaction",
                    "do not apply source from status alone",
                    "executor must independently re-check profile/grant/scope/patch hash/validation/mutation budget/worktree state"
                ]);
        }

        return BuildBlocked(
            request,
            [Reason(AuthorityProfileStatusReason.OperationEligibilityDecisionNotEligible)],
            ["eligible operation eligibility decision"],
            ["request operation eligibility evaluation for this repo/branch/run/scope"],
            ["do not treat a non-eligible decision as executable"]);
    }

    private static GovernedOperationStatus BuildBlocked(
        AuthorityProfileStatusRequest request,
        IReadOnlyCollection<string> blockedReasons,
        IReadOnlyCollection<string> missingEvidence,
        IReadOnlyCollection<string> nextSafeActions,
        IReadOnlyCollection<string> forbiddenActions) =>
        BuildStatus(
            request,
            GovernedOperationState.Blocked,
            blockedReasons,
            missingEvidence,
            nextSafeActions,
            forbiddenActions);

    private static GovernedOperationStatus BuildStatus(
        AuthorityProfileStatusRequest request,
        GovernedOperationState state,
        IReadOnlyCollection<string> blockedReasons,
        IReadOnlyCollection<string> missingEvidence,
        IReadOnlyCollection<string> nextSafeActions,
        IReadOnlyCollection<string> forbiddenActions) =>
        BuildStatus(
            request.OperationId,
            request.OperationKind,
            request.Subject,
            state,
            blockedReasons,
            missingEvidence,
            nextSafeActions,
            forbiddenActions,
            BuildEvidenceRefs(request),
            request.ReceiptRefs,
            request.GrantExpiresAtUtc,
            request.ObservedAtUtc);

    private static GovernedOperationStatus BuildStatus(
        string operationId,
        RunAuthorityOperationKind operationKind,
        string subject,
        GovernedOperationState state,
        IReadOnlyCollection<string> blockedReasons,
        IReadOnlyCollection<string> missingEvidence,
        IReadOnlyCollection<string> nextSafeActions,
        IReadOnlyCollection<string> forbiddenActions,
        IReadOnlyCollection<string> evidenceRefs,
        IReadOnlyCollection<string> receiptRefs,
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset observedAtUtc) =>
        new()
        {
            OperationId = CleanText(operationId, "authority-profile-status"),
            OperationKind = operationKind.ToString(),
            Subject = CleanText(subject, "authority profile status"),
            State = state,
            BlockedReasons = Clean(blockedReasons),
            MissingEvidence = Clean(missingEvidence),
            NextSafeActions = Clean(nextSafeActions),
            ForbiddenActions = Clean(forbiddenActions),
            EvidenceRefs = Clean(evidenceRefs),
            ReceiptRefs = Clean(receiptRefs),
            ExpiresAtUtc = expiresAtUtc,
            ObservedAtUtc = observedAtUtc == default ? DateTimeOffset.UnixEpoch : observedAtUtc
        };

    private static IReadOnlyCollection<string> BuildEvidenceRefs(AuthorityProfileStatusRequest request) =>
        Clean(
        [
            Ref("authority-profile", request.ProfileKind.ToString()),
            Ref("repo", request.Repository),
            Ref("branch", request.Branch),
            Ref("run", request.RunId),
            Ref("patch-hash", request.PatchHash),
            .. ValuesOrEmpty(request.EvidenceRefs)
        ]);

    private static IReadOnlyCollection<string> BuildBlockedDecisionMissingEvidence(AuthorityProfileStatusRequest request)
    {
        if (request.OperationKind == RunAuthorityOperationKind.Push)
            return ["bounded grant allowing Push for this repo/branch/run/scope"];

        return HasValues(request.EligibilityDecision?.MissingEvidence)
            ? request.EligibilityDecision!.MissingEvidence
            : ["bounded grant allowing requested operation"];
    }

    private static IReadOnlyCollection<string> BuildBlockedDecisionNextSafeActions(AuthorityProfileStatusRequest request)
    {
        if (request.OperationKind == RunAuthorityOperationKind.Push)
            return ["request separate push authority after source apply evidence exists"];

        return ["request bounded run authority for this repo/branch/run/scope"];
    }

    private static IReadOnlyCollection<string> BuildDecisionForbiddenActions(AuthorityProfileStatusRequest request)
    {
        var actions = new List<string>
        {
            "do not execute from status alone",
            "do not treat eligibility evidence as approval",
            "do not treat eligibility evidence as policy satisfaction"
        };

        if (request.OperationKind == RunAuthorityOperationKind.Push)
            actions.Add("do not push without explicit bounded authority");

        AddRange(actions, request.EligibilityDecision?.ForbiddenActions);
        return Clean(actions);
    }

    private static bool IsExpired(AuthorityProfileStatusRequest request) =>
        request.GrantExpiresAtUtc.HasValue &&
        request.GrantExpiresAtUtc.Value <= request.ObservedAtUtc;

    private static bool IsMutationOperation(RunAuthorityOperationKind operationKind) =>
        RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations.Contains(operationKind);

    private static bool HasAcceptedApplyApproval(IReadOnlyCollection<string>? evidenceRefs) =>
        ValuesOrEmpty(evidenceRefs).Any(value =>
            value?.StartsWith("accepted-apply-approval:", StringComparison.OrdinalIgnoreCase) == true ||
            value?.StartsWith("accepted-source-apply-request:", StringComparison.OrdinalIgnoreCase) == true);

    private static string Reason(AuthorityProfileStatusReason reason) => reason.ToString();

    private static string Ref(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}:{value.Trim()}";

    private static string CleanText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool HasValues(IReadOnlyCollection<string>? values) =>
        ValuesOrEmpty(values).Any(value => !string.IsNullOrWhiteSpace(value));

    private static IReadOnlyList<string> Clean(IEnumerable<string?>? values) =>
        ValuesOrEmpty(values)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string?> ValuesOrEmpty(IEnumerable<string?>? values) =>
        values ?? [];

    private static void AddRange(ICollection<string> target, IEnumerable<string>? values)
    {
        foreach (var value in ValuesOrEmpty(values))
        {
            if (!string.IsNullOrWhiteSpace(value))
                target.Add(value);
        }
    }
}
