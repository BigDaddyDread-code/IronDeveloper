namespace IronDev.Core.Governance;

public static class PatchProposalGovernedOperationStatusMapper
{
    public const string OperationKind = "PatchProposal";

    private static readonly string[] UnsafeAuthorityMarkers =
    [
        "patch proposal approves source apply",
        "patch exists so apply is allowed",
        "tests passed so approved",
        "review summary approves apply",
        "known risks accepted by status",
        "patch proposal completion authorizes commit",
        "patch proposal completion authorizes push",
        "patch proposal completion authorizes pr creation",
        "patch proposal completion authorizes workflow continuation",
        "memory says proposal was approved",
        "ui marked proposal approved",
        "old proposal refreshes current authority"
    ];

    public static PatchProposalGovernedOperationStatusMappingResult Map(PatchProposalStatusInput input)
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

        return new PatchProposalGovernedOperationStatusMappingResult
        {
            Status = status,
            CanonicalValidation = canonical,
            Issues = issues,
            RedFlags = redFlags,
            IsValid = canonical.IsValid && issues.Length == 0 && redFlags.Length == 0
        };
    }

    private static GovernedOperationStatus BuildStatus(PatchProposalStatusInput input)
    {
        var state = input.StatusKind switch
        {
            PatchProposalStatusKind.ReadyForReview => GovernedOperationState.Completed,
            PatchProposalStatusKind.Blocked => GovernedOperationState.Blocked,
            PatchProposalStatusKind.Failed => GovernedOperationState.Failed,
            PatchProposalStatusKind.Expired => GovernedOperationState.Expired,
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
            ReceiptRefs = BuildReceiptRefs(input),
            ExpiresAtUtc = input.ExpiresAtUtc,
            ObservedAtUtc = input.ObservedAtUtc
        };
    }

    private static IReadOnlyList<string> BuildEvidenceRefs(PatchProposalStatusInput input) =>
        Clean(
        [
            Ref("patch-proposal", input.ProposalId),
            Ref("patch-hash", input.PatchHash),
            .. ValuesOrEmpty(input.ArtifactRefs),
            .. ValuesOrEmpty(input.ValidationRefs)
        ]);

    private static IReadOnlyList<string> BuildReceiptRefs(PatchProposalStatusInput input)
    {
        if (input.StatusKind != PatchProposalStatusKind.ReadyForReview)
            return [];

        var proposalId = string.IsNullOrWhiteSpace(input.ProposalId) ? "missing-proposal-id" : input.ProposalId.Trim();
        return [$"patch-proposal-status-artifact:{proposalId}"];
    }

    private static IReadOnlyList<string> BuildBlockedReasons(PatchProposalStatusInput input) =>
        input.StatusKind switch
        {
            PatchProposalStatusKind.ReadyForReview => Clean(ValuesOrEmpty(input.BlockedReasons)),
            PatchProposalStatusKind.Blocked => Clean(ValuesOrEmpty(input.BlockedReasons)),
            PatchProposalStatusKind.Failed => CleanOrDefault(
                input.BlockedReasons,
                "Patch proposal failed."),
            PatchProposalStatusKind.Expired => CleanOrDefault(
                input.BlockedReasons,
                "Patch proposal expired or became stale."),
            _ => Clean(ValuesOrEmpty(input.BlockedReasons))
        };

    private static IReadOnlyList<string> BuildMissingEvidence(PatchProposalStatusInput input) =>
        input.StatusKind == PatchProposalStatusKind.Blocked
            ? Clean(ValuesOrEmpty(input.MissingEvidence))
            : Clean(ValuesOrEmpty(input.MissingEvidence));

    private static IReadOnlyList<string> BuildNextSafeActions(PatchProposalStatusInput input) =>
        input.StatusKind switch
        {
            PatchProposalStatusKind.ReadyForReview => Clean(
            [
                $"request controlled source apply for patch hash {DisplayPatchHash(input)}"
            ]),
            PatchProposalStatusKind.Blocked => Clean(
            [
                "prepare bounded patch proposal",
                "collect missing validation evidence",
                "revise patch scope",
                "inspect failed validation"
            ]),
            PatchProposalStatusKind.Failed => Clean(
            [
                "review failure evidence",
                "prepare new governed proposal"
            ]),
            PatchProposalStatusKind.Expired => Clean(
            [
                "request regenerated patch proposal",
                "request validation rerun",
                "request fresh source apply only after current patch exists"
            ]),
            _ => []
        };

    private static IReadOnlyList<string> BuildForbiddenActions(PatchProposalStatusInput input)
    {
        IReadOnlyList<string> defaults = input.StatusKind switch
        {
            PatchProposalStatusKind.ReadyForReview =>
            [
                "do not apply patch proposal directly to source",
                "do not treat patch proposal completion as source apply authority",
                "do not treat tests passed as approval",
                "do not commit, push, create PRs, merge, release, deploy, promote memory, or continue workflow from patch proposal status"
            ],
            PatchProposalStatusKind.Blocked =>
            [
                "do not apply incomplete patch proposal",
                "do not treat partial proposal as authority"
            ],
            PatchProposalStatusKind.Failed =>
            [
                "do not apply failed patch proposal",
                "do not retry source apply from failed proposal"
            ],
            PatchProposalStatusKind.Expired =>
            [
                "do not refresh stale proposal from memory",
                "do not apply stale patch",
                "do not reuse old approval"
            ],
            _ => []
        };

        return Clean([.. ValuesOrEmpty(input.ForbiddenActions), .. defaults]);
    }

    private static IEnumerable<string> ValidateInput(PatchProposalStatusInput input)
    {
        if (!Enum.IsDefined(input.StatusKind))
            yield return "PatchProposalStatusKindRequired";
        if (string.IsNullOrWhiteSpace(input.ProposalId))
            yield return "PatchProposalIdRequired";
        if (string.IsNullOrWhiteSpace(input.PatchHash))
            yield return "PatchHashRequired";
        if (input.StatusKind == PatchProposalStatusKind.ReadyForReview &&
            ValuesOrEmpty(input.BlockedReasons).Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            yield return "ReadyPatchProposalCannotCarryBlockedReasons";
        }
        if (input.StatusKind == PatchProposalStatusKind.ReadyForReview &&
            ValuesOrEmpty(input.MissingEvidence).Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            yield return "ReadyPatchProposalCannotCarryMissingEvidence";
        }
        if (input.StatusKind == PatchProposalStatusKind.Blocked && ValuesOrEmpty(input.BlockedReasons).All(string.IsNullOrWhiteSpace))
            yield return "PatchProposalBlockedReasonRequired";
        if (input.StatusKind == PatchProposalStatusKind.Blocked &&
            ValuesOrEmpty(input.MissingEvidence).All(string.IsNullOrWhiteSpace))
        {
            yield return "PatchProposalBlockedMissingEvidenceRequired";
        }
        if (input.StatusKind == PatchProposalStatusKind.Expired &&
            input.ExpiresAtUtc is null &&
            !ContainsAny(ValuesOrEmpty(input.BlockedReasons), ["expir", "stale"]))
        {
            yield return "PatchProposalExpiryEvidenceRequired";
        }
    }

    private static IEnumerable<string> DetectAuthorityRedFlags(PatchProposalStatusInput input)
    {
        foreach (var marker in UnsafeAuthorityMarkers)
        {
            if (!ContainsAny(AllInputText(input), [marker]))
                continue;

            yield return marker switch
            {
                var value when value.Contains("memory", StringComparison.OrdinalIgnoreCase) =>
                    "MemoryReferenceCannotApprovePatchProposal",
                var value when value.Contains("ui", StringComparison.OrdinalIgnoreCase) =>
                    "UiStateCannotApprovePatchProposal",
                var value when value.Contains("commit", StringComparison.OrdinalIgnoreCase) ||
                               value.Contains("push", StringComparison.OrdinalIgnoreCase) ||
                               value.Contains("pr creation", StringComparison.OrdinalIgnoreCase) ||
                               value.Contains("workflow continuation", StringComparison.OrdinalIgnoreCase) =>
                    "PatchProposalCompletionCannotAuthorizeNextOperation",
                var value when value.Contains("old proposal", StringComparison.OrdinalIgnoreCase) =>
                    "OldPatchProposalCannotRefreshAuthority",
                _ => "PatchProposalEvidenceCannotApprove"
            };
        }
    }

    private static IEnumerable<string?> AllInputText(PatchProposalStatusInput input)
    {
        yield return input.OperationId;
        yield return input.ProposalId;
        yield return input.PatchHash;
        yield return input.Subject;

        foreach (var value in ValuesOrEmpty(input.ArtifactRefs))
            yield return value;
        foreach (var value in ValuesOrEmpty(input.ValidationRefs))
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

    private static string DisplayPatchHash(PatchProposalStatusInput input) =>
        string.IsNullOrWhiteSpace(input.PatchHash) ? "missing-patch-hash" : input.PatchHash.Trim();

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> CleanOrDefault(IReadOnlyList<string>? values, string fallback)
    {
        var clean = Clean(ValuesOrEmpty(values));
        return clean.Count == 0 ? [fallback] : clean;
    }

    private static bool ContainsAny(IEnumerable<string?> values, IReadOnlyList<string> markers) =>
        values.Any(value => markers.Any(marker => value?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true));

    private static IEnumerable<string> ValuesOrEmpty(IReadOnlyList<string>? values) =>
        values ?? [];
}
