namespace IronDev.Core.Governance;

public static class OperationEligibilityEvaluator
{
    private static readonly IReadOnlyCollection<RunAuthorityOperationKind> PatchBoundOperationKinds =
    [
        RunAuthorityOperationKind.PatchPackageWrite,
        RunAuthorityOperationKind.ValidationResultPackageWrite
    ];

    public static OperationEligibilityDecision Evaluate(OperationEligibilityRequest? request)
    {
        var blocked = new List<string>();
        var missing = new List<string>();
        var forbidden = new List<string>();
        var checks = new List<string>();

        if (request is null)
        {
            blocked.Add("OperationEligibilityRequestRequired");
            forbidden.Add("do not proceed without operation eligibility request");
            checks.Add("profile and bounded grant are both required");
            return Result(RunAuthorityOperationKind.Unknown, false, blocked, missing, forbidden, checks);
        }

        if (!Enum.IsDefined(request.OperationKind) || request.OperationKind == RunAuthorityOperationKind.Unknown)
        {
            blocked.Add("OperationKindKnownRequired");
            forbidden.Add("do not treat unknown operation as eligible");
        }

        EvaluateProfile(request, blocked, forbidden, checks);
        EvaluateGrant(request, blocked, forbidden, checks);
        ValidatePatchHash(request, blocked, missing, forbidden, checks);
        ValidateAffectedFiles(request, blocked, missing, forbidden, checks);
        ValidateMutationBudget(request, blocked, forbidden, checks);
        ValidateRequiredValidation(request, blocked, missing, forbidden, checks);

        if (blocked.Count == 0 && missing.Count == 0)
        {
            forbidden.Add("do not treat eligibility as approval");
            forbidden.Add("do not treat eligibility as policy satisfaction");
            forbidden.Add("do not treat eligibility as execution authority");
            forbidden.Add("do not treat eligibility as source apply authority");
            forbidden.Add("do not mutate durable source from eligibility");
            checks.Add("operation-specific governance still required");
            checks.Add("profile and grant eligibility is necessary but not sufficient");
            checks.Add("validation evidence authenticity still requires independent verification");
            return Result(request.OperationKind, true, blocked, missing, forbidden, checks);
        }

        return Result(request.OperationKind, false, blocked, missing, forbidden, checks);
    }

    private static void EvaluateProfile(
        OperationEligibilityRequest request,
        ICollection<string> blocked,
        ICollection<string> forbidden,
        ICollection<string> checks)
    {
        var profileDecision = RunAuthorityProfileEvaluator.Evaluate(request.Profile, request.OperationKind);
        if (profileDecision.IsAllowedByProfile)
        {
            AddRange(forbidden, profileDecision.ForbiddenActions);
            AddRange(checks, profileDecision.RequiredIndependentChecks);
            return;
        }

        blocked.Add("RunAuthorityProfileCheckFailed");
        AddRange(blocked, profileDecision.BlockedReasons.Select(reason => $"RunAuthorityProfileCheckFailed:{reason}"));
        forbidden.Add("do not proceed outside run profile");
        AddRange(forbidden, profileDecision.ForbiddenActions);
        checks.Add("valid run authority profile required");
        AddRange(checks, profileDecision.RequiredIndependentChecks);
    }

    private static void EvaluateGrant(
        OperationEligibilityRequest request,
        ICollection<string> blocked,
        ICollection<string> forbidden,
        ICollection<string> checks)
    {
        var validation = BoundedRunAuthorityGrantValidator.Validate(request.Grant, request.ObservedAtUtc);
        if (!validation.IsValid)
        {
            blocked.Add("BoundedRunAuthorityGrantCheckFailed");
            AddRange(blocked, validation.Issues.Select(issue => $"BoundedRunAuthorityGrantCheckFailed:{issue}"));
            forbidden.Add("do not proceed outside bounded grant envelope");
            checks.Add("valid bounded grant required");
            return;
        }

        if (request.Grant is null)
            return;

        if (request.Grant.StopBeforeOperationKinds.Contains(request.OperationKind))
        {
            blocked.Add($"OperationStoppedBefore:{request.OperationKind}");
            forbidden.Add("do not cross stop-before boundary");
        }
    }

    private static void ValidatePatchHash(
        OperationEligibilityRequest request,
        ICollection<string> blocked,
        ICollection<string> missing,
        ICollection<string> forbidden,
        ICollection<string> checks)
    {
        if (!string.IsNullOrWhiteSpace(request.PatchHash))
            forbidden.Add("do not treat patch hash match as source apply authority");

        if (!IsPatchBound(request.OperationKind))
            return;

        checks.Add("patch hash binding must match the bounded grant");
        if (request.Grant is null || string.IsNullOrWhiteSpace(request.Grant.PatchHash))
            missing.Add("GrantPatchHashRequired");
        if (string.IsNullOrWhiteSpace(request.PatchHash))
            missing.Add("RequestPatchHashRequired");

        if (!string.IsNullOrWhiteSpace(request.Grant?.PatchHash) &&
            !OperationEligibilityPatchHashRules.IsSafePatchHash(request.Grant.PatchHash))
        {
            blocked.Add("GrantPatchHashInvalid");
        }

        if (!string.IsNullOrWhiteSpace(request.PatchHash) &&
            !OperationEligibilityPatchHashRules.IsSafePatchHash(request.PatchHash))
        {
            blocked.Add("RequestPatchHashInvalid");
        }

        if (!string.IsNullOrWhiteSpace(request.Grant?.PatchHash) &&
            !string.IsNullOrWhiteSpace(request.PatchHash) &&
            !string.Equals(request.Grant.PatchHash, request.PatchHash, StringComparison.OrdinalIgnoreCase))
        {
            blocked.Add("PatchHashMismatch");
        }

    }

    private static void ValidateAffectedFiles(
        OperationEligibilityRequest request,
        ICollection<string> blocked,
        ICollection<string> missing,
        ICollection<string> forbidden,
        ICollection<string> checks)
    {
        if (request.AffectedFilePaths is null || request.AffectedFilePaths.Count == 0)
        {
            missing.Add("AffectedFilePathsRequired");
            return;
        }

        if (request.Grant is null)
            return;

        foreach (var filePath in request.AffectedFilePaths)
        {
            var match = BoundedRunAuthorityGrantMatcher.Evaluate(
                request.Grant,
                request.ObservedAtUtc,
                request.Repository,
                request.Branch,
                request.RunId,
                request.OperationKind,
                filePath);

            if (match.IsInsideGrantEnvelope)
            {
                AddRange(forbidden, match.ForbiddenActions);
                AddRange(checks, match.RequiredIndependentChecks);
                continue;
            }

            AddRange(blocked, match.BlockedReasons.Select(reason => $"AffectedFileRejected:{filePath}:{reason}"));
            AddRange(forbidden, match.ForbiddenActions);
            AddRange(checks, match.RequiredIndependentChecks);
        }
    }

    private static void ValidateMutationBudget(
        OperationEligibilityRequest request,
        ICollection<string> blocked,
        ICollection<string> forbidden,
        ICollection<string> checks)
    {
        if (request.MutationsAlreadyConsumed < 0)
            blocked.Add("MutationsAlreadyConsumedCannotBeNegative");
        if (request.RequestedMutationCount < 0)
            blocked.Add("RequestedMutationCountCannotBeNegative");
        if (request.Grant is null)
            return;

        if (request.MutationsAlreadyConsumed >= 0 &&
            request.RequestedMutationCount >= 0)
        {
            var consumed = (long)request.MutationsAlreadyConsumed;
            var requested = (long)request.RequestedMutationCount;
            var max = (long)request.Grant.MaxMutations;

            if (consumed + requested > max)
            {
                blocked.Add("MutationBudgetExceeded");
                forbidden.Add("do not treat zero max mutations as unlimited");
            }
        }

        checks.Add("durable mutation accounting remains a future runner responsibility");
    }

    private static void ValidateRequiredValidation(
        OperationEligibilityRequest request,
        ICollection<string> blocked,
        ICollection<string> missing,
        ICollection<string> forbidden,
        ICollection<string> checks)
    {
        if (request.ValidationEvidence is null)
        {
            missing.Add("ValidationEvidenceRequired");
            forbidden.Add("do not treat missing validation evidence as passed");
            return;
        }

        if (request.Grant?.RequiredValidation is null)
            return;

        foreach (var required in request.Grant.RequiredValidation)
        {
            var candidates = request.ValidationEvidence
                .Where(evidence => string.Equals(evidence.ValidationKind, required.ValidationKind, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Length == 0)
            {
                missing.Add($"RequiredValidationEvidenceMissing:{required.ValidationKind}");
                continue;
            }

            if (!candidates.Any(candidate => EvidenceRefMatches(required, candidate)))
                blocked.Add($"RequiredValidationEvidenceRefPrefixMismatch:{required.ValidationKind}");

            foreach (var candidate in candidates)
                ValidateEvidence(candidate, required, request, blocked, missing);
        }

        forbidden.Add("do not treat validation evidence as approval");
        forbidden.Add("do not treat validation evidence as policy satisfaction");
        forbidden.Add("do not run validation from eligibility evaluation");
        checks.Add("validation evidence references still require resolver verification");
    }

    private static void ValidateEvidence(
        OperationEligibilityValidationEvidence evidence,
        BoundedRunAuthorityRequiredValidation required,
        OperationEligibilityRequest request,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(evidence.ValidationKind))
            blocked.Add("ValidationEvidenceKindRequired");
        if (string.IsNullOrWhiteSpace(evidence.EvidenceRef))
            blocked.Add($"ValidationEvidenceRefRequired:{required.ValidationKind}");
        if (!Enum.IsDefined(evidence.Outcome) || evidence.Outcome == OperationEligibilityValidationOutcome.Unknown)
            blocked.Add($"ValidationEvidenceOutcomeKnownRequired:{required.ValidationKind}");
        if (required.MustPass && evidence.Outcome != OperationEligibilityValidationOutcome.Passed)
            blocked.Add($"RequiredValidationMustPass:{required.ValidationKind}:{evidence.Outcome}");

        if (!IsPatchBound(request.OperationKind))
            return;

        if (string.IsNullOrWhiteSpace(evidence.PatchHash))
        {
            missing.Add($"ValidationEvidencePatchHashRequired:{required.ValidationKind}");
            return;
        }

        if (!OperationEligibilityPatchHashRules.IsSafePatchHash(evidence.PatchHash))
        {
            blocked.Add($"ValidationEvidencePatchHashInvalid:{required.ValidationKind}");
            return;
        }

        if (!string.Equals(evidence.PatchHash, request.PatchHash, StringComparison.OrdinalIgnoreCase))
        {
            blocked.Add($"ValidationEvidencePatchHashMismatch:{required.ValidationKind}");
        }
    }

    private static bool EvidenceRefMatches(
        BoundedRunAuthorityRequiredValidation required,
        OperationEligibilityValidationEvidence evidence) =>
        required.EvidenceRefPrefixes is not null &&
        !string.IsNullOrWhiteSpace(evidence.EvidenceRef) &&
        required.EvidenceRefPrefixes.Any(prefix =>
            !string.IsNullOrWhiteSpace(prefix) &&
            evidence.EvidenceRef.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsPatchBound(RunAuthorityOperationKind operationKind) =>
        PatchBoundOperationKinds.Contains(operationKind);

    private static void AddRange(ICollection<string> target, IEnumerable<string> values)
    {
        foreach (var value in values)
            target.Add(value);
    }

    private static OperationEligibilityDecision Result(
        RunAuthorityOperationKind operationKind,
        bool isEligible,
        IEnumerable<string> blocked,
        IEnumerable<string> missing,
        IEnumerable<string> forbidden,
        IEnumerable<string> checks) =>
        new()
        {
            IsEligibleUnderProfileAndGrant = isEligible,
            OperationKind = operationKind,
            BlockedReasons = Clean(blocked),
            MissingEvidence = Clean(missing),
            ForbiddenActions = Clean(forbidden),
            RequiredIndependentChecks = Clean(checks)
        };

    private static IReadOnlyCollection<string> Clean(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
