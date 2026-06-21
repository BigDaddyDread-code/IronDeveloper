namespace IronDev.Core.Governance;

public static class ControlledSourceApplyGovernedOperationStatusMapper
{
    public const string OperationKind = "SourceApply";

    private static readonly string[] UnsafeAuthorityMarkers =
    [
        "status approves source apply",
        "eligible status executes source apply",
        "patch proposal approves source apply",
        "patch exists so apply is allowed",
        "dry-run passed so apply is approved",
        "tests passed so apply is approved",
        "policy satisfied by status",
        "memory says source apply was approved",
        "ui marked source apply approved",
        "source apply receipt authorizes commit",
        "source apply receipt authorizes push",
        "source apply receipt authorizes pr creation",
        "source apply receipt authorizes rollback execution",
        "source apply receipt authorizes workflow continuation",
        "old apply request refreshes current authority"
    ];

    public static ControlledSourceApplyGovernedOperationStatusMappingResult Map(ControlledSourceApplyStatusInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var mapperIssues = ValidateInput(input).ToList();
        var mapperRedFlags = DetectAuthorityRedFlags(input).ToList();
        var status = BuildStatus(input);
        var canonical = GovernedOperationStatusValidator.Validate(status);

        var issues = mapperIssues
            .Concat(canonical.Issues)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var redFlags = mapperRedFlags
            .Concat(canonical.RedFlags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ControlledSourceApplyGovernedOperationStatusMappingResult
        {
            Status = status,
            CanonicalValidation = canonical,
            Issues = issues,
            RedFlags = redFlags,
            IsValid = canonical.IsValid && issues.Length == 0 && redFlags.Length == 0
        };
    }

    private static GovernedOperationStatus BuildStatus(ControlledSourceApplyStatusInput input)
    {
        var state = input.StatusKind switch
        {
            ControlledSourceApplyStatusKind.Blocked => GovernedOperationState.Blocked,
            ControlledSourceApplyStatusKind.Eligible => GovernedOperationState.Eligible,
            ControlledSourceApplyStatusKind.Running => GovernedOperationState.Running,
            ControlledSourceApplyStatusKind.Completed => GovernedOperationState.Completed,
            ControlledSourceApplyStatusKind.Failed => GovernedOperationState.Failed,
            ControlledSourceApplyStatusKind.Expired => GovernedOperationState.Expired,
            _ => (GovernedOperationState)0
        };

        return new GovernedOperationStatus
        {
            OperationId = input.OperationId,
            OperationKind = OperationKind,
            Subject = input.Subject,
            State = state,
            BlockedReasons = BuildBlockedReasons(input),
            MissingEvidence = BuildMissingEvidence(input),
            NextSafeActions = BuildNextSafeActions(input),
            ForbiddenActions = BuildForbiddenActions(input),
            EvidenceRefs = BuildEvidenceRefs(input),
            ReceiptRefs = Clean(ValuesOrEmpty(input.ReceiptRefs)),
            ExpiresAtUtc = input.ExpiresAtUtc,
            ObservedAtUtc = input.ObservedAtUtc
        };
    }

    private static IReadOnlyList<string> BuildEvidenceRefs(ControlledSourceApplyStatusInput input) =>
        Clean(
        [
            Ref("source-apply", input.SourceApplyId),
            Ref("repo", input.RepoId),
            Ref("branch", input.Branch),
            Ref("patch-hash", input.PatchHash),
            .. ValuesOrEmpty(input.EvidenceRefs)
        ]);

    private static IReadOnlyList<string> BuildBlockedReasons(ControlledSourceApplyStatusInput input)
    {
        var reasons = Clean(ValuesOrEmpty(input.BlockedReasons));
        if (input.StatusKind == ControlledSourceApplyStatusKind.Failed && reasons.Count == 0)
            return ["Source apply failed."];

        if (input.StatusKind == ControlledSourceApplyStatusKind.Expired)
        {
            if (reasons.Count == 0)
                return ["Source apply expired or became stale."];
            if (input.ExpiresAtUtc is null &&
                !ContainsAny(reasons, ["expir"]) &&
                ContainsAny(reasons, ["stale"]))
            {
                return Clean([.. reasons, "Source apply expired or became stale."]);
            }
        }

        return reasons;
    }

    private static IReadOnlyList<string> BuildMissingEvidence(ControlledSourceApplyStatusInput input) =>
        Clean(ValuesOrEmpty(input.MissingEvidence));

    private static IReadOnlyList<string> BuildNextSafeActions(ControlledSourceApplyStatusInput input) =>
        input.StatusKind switch
        {
            ControlledSourceApplyStatusKind.Blocked => Clean(
            [
                $"request accepted source-apply authority for patch hash {DisplayPatchHash(input)}",
                "request policy satisfaction for source apply",
                $"request controlled dry-run for patch hash {DisplayPatchHash(input)}",
                "prepare current patch artifact",
                "prepare rollback support",
                "inspect dirty worktree state",
                "request regenerated patch proposal"
            ]),
            ControlledSourceApplyStatusKind.Eligible => Clean(
            [
                $"request controlled source apply execution for patch hash {DisplayPatchHash(input)}"
            ]),
            ControlledSourceApplyStatusKind.Running => Clean(
            [
                "observe source apply execution",
                "inspect source apply run state"
            ]),
            ControlledSourceApplyStatusKind.Completed => Clean(
            [
                "review source apply receipt before requesting controlled commit package"
            ]),
            ControlledSourceApplyStatusKind.Failed => Clean(
            [
                "review failure receipt",
                "prepare rollback request if source was partially mutated",
                "prepare new governed proposal"
            ]),
            ControlledSourceApplyStatusKind.Expired => Clean(
            [
                "request fresh source apply authority",
                "request regenerated patch proposal",
                $"request controlled dry-run for current patch hash {DisplayPatchHash(input)}",
                "request refreshed policy satisfaction"
            ]),
            _ => []
        };

    private static IReadOnlyList<string> BuildForbiddenActions(ControlledSourceApplyStatusInput input)
    {
        IReadOnlyList<string> defaults = input.StatusKind switch
        {
            ControlledSourceApplyStatusKind.Blocked =>
            [
                "do not apply blocked source apply status",
                "do not treat partial source apply evidence as authority"
            ],
            ControlledSourceApplyStatusKind.Eligible =>
            [
                "do not treat status as execution authority",
                "do not apply from status alone",
                "do not commit, push, create PRs, merge, release, deploy, promote memory, or continue workflow from source apply status"
            ],
            ControlledSourceApplyStatusKind.Running =>
            [
                "do not start another source apply for the same patch",
                "do not commit or push while source apply is running",
                "do not continue workflow from running status"
            ],
            ControlledSourceApplyStatusKind.Completed =>
            [
                "do not treat source apply completion as commit authority",
                "do not treat source apply completion as push authority",
                "do not treat source apply completion as PR authority",
                "do not treat source apply completion as workflow continuation authority",
                "do not treat source apply receipt as rollback execution authority"
            ],
            ControlledSourceApplyStatusKind.Failed =>
            [
                "do not retry source apply without fresh authority",
                "do not treat failed apply as source state success",
                "do not continue workflow from failed apply"
            ],
            ControlledSourceApplyStatusKind.Expired =>
            [
                "do not refresh stale authority from memory",
                "do not reuse old apply request",
                "do not apply stale patch",
                "do not infer authority from old receipts"
            ],
            _ => []
        };

        return Clean([.. ValuesOrEmpty(input.ForbiddenActions), .. defaults]);
    }

    private static IEnumerable<string> ValidateInput(ControlledSourceApplyStatusInput input)
    {
        if (!Enum.IsDefined(input.StatusKind))
            yield return "ControlledSourceApplyStatusKindRequired";
        if (string.IsNullOrWhiteSpace(input.SourceApplyId))
            yield return "SourceApplyIdRequired";
        if (string.IsNullOrWhiteSpace(input.RepoId))
            yield return "SourceApplyRepoIdRequired";
        if (string.IsNullOrWhiteSpace(input.Branch))
            yield return "SourceApplyBranchRequired";
        if (string.IsNullOrWhiteSpace(input.PatchHash))
            yield return "PatchHashRequired";
        if (IsContradictorySuccessLikeState(input.StatusKind) &&
            ValuesOrEmpty(input.BlockedReasons).Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            yield return $"{input.StatusKind}SourceApplyCannotCarryBlockedReasons";
        }
        if (IsContradictorySuccessLikeState(input.StatusKind) &&
            ValuesOrEmpty(input.MissingEvidence).Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            yield return $"{input.StatusKind}SourceApplyCannotCarryMissingEvidence";
        }
        if (input.StatusKind == ControlledSourceApplyStatusKind.Blocked && ValuesOrEmpty(input.BlockedReasons).All(string.IsNullOrWhiteSpace))
            yield return "SourceApplyBlockedReasonRequired";
        if (input.StatusKind == ControlledSourceApplyStatusKind.Blocked &&
            ValuesOrEmpty(input.MissingEvidence).All(string.IsNullOrWhiteSpace))
        {
            yield return "SourceApplyBlockedMissingEvidenceRequired";
        }
        if (input.StatusKind == ControlledSourceApplyStatusKind.Completed &&
            ValuesOrEmpty(input.ReceiptRefs).All(string.IsNullOrWhiteSpace))
        {
            yield return "SourceApplyCompletedReceiptRequired";
        }
        if (input.StatusKind == ControlledSourceApplyStatusKind.Expired &&
            input.ExpiresAtUtc is null &&
            !ContainsAny(ValuesOrEmpty(input.BlockedReasons), ["expir", "stale"]))
        {
            yield return "SourceApplyExpiryEvidenceRequired";
        }
    }

    private static IEnumerable<string> DetectAuthorityRedFlags(ControlledSourceApplyStatusInput input)
    {
        foreach (var marker in UnsafeAuthorityMarkers)
        {
            if (!ContainsAny(AllInputText(input), [marker]))
                continue;

            yield return marker switch
            {
                var value when value.Contains("memory", StringComparison.OrdinalIgnoreCase) =>
                    "MemoryReferenceCannotApproveSourceApply",
                var value when value.Contains("ui", StringComparison.OrdinalIgnoreCase) =>
                    "UiStateCannotApproveSourceApply",
                var value when value.Contains("policy satisfied by status", StringComparison.OrdinalIgnoreCase) =>
                    "SourceApplyStatusCannotSatisfyPolicy",
                var value when value.Contains("source apply receipt", StringComparison.OrdinalIgnoreCase) =>
                    "SourceApplyReceiptCannotAuthorizeNextOperation",
                var value when value.Contains("old apply request", StringComparison.OrdinalIgnoreCase) =>
                    "OldSourceApplyRequestCannotRefreshAuthority",
                _ => "SourceApplyEvidenceCannotApprove"
            };
        }
    }

    private static bool IsContradictorySuccessLikeState(ControlledSourceApplyStatusKind kind) =>
        kind is ControlledSourceApplyStatusKind.Eligible or
            ControlledSourceApplyStatusKind.Running or
            ControlledSourceApplyStatusKind.Completed;

    private static IEnumerable<string?> AllInputText(ControlledSourceApplyStatusInput input)
    {
        yield return input.OperationId;
        yield return input.SourceApplyId;
        yield return input.Subject;
        yield return input.RepoId;
        yield return input.Branch;
        yield return input.PatchHash;

        foreach (var value in ValuesOrEmpty(input.EvidenceRefs))
            yield return value;
        foreach (var value in ValuesOrEmpty(input.ReceiptRefs))
            yield return value;
        foreach (var value in ValuesOrEmpty(input.BlockedReasons))
            yield return value;
        foreach (var value in ValuesOrEmpty(input.MissingEvidence))
            yield return value;
        foreach (var value in ValuesOrEmpty(input.ForbiddenActions))
            yield return value;
    }

    private static string Ref(string prefix, string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}:{value.Trim()}";

    private static string DisplayPatchHash(ControlledSourceApplyStatusInput input) =>
        string.IsNullOrWhiteSpace(input.PatchHash) ? "missing-patch-hash" : input.PatchHash.Trim();

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool ContainsAny(IEnumerable<string?> values, IReadOnlyList<string> markers) =>
        values.Any(value => markers.Any(marker => value?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true));

    private static IEnumerable<string> ValuesOrEmpty(IReadOnlyList<string>? values) =>
        values ?? [];
}
