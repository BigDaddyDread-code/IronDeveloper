namespace IronDev.Core.Governance;

public static class SourceApplyBoundedGrantValidator
{
    private static readonly RunAuthorityOperationKind[] ForbiddenSourceApplyGrantOperations =
    [
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest,
        RunAuthorityOperationKind.ReadyForReview,
        RunAuthorityOperationKind.Merge,
        RunAuthorityOperationKind.Release,
        RunAuthorityOperationKind.Deployment,
        RunAuthorityOperationKind.MemoryPromotion,
        RunAuthorityOperationKind.WorkflowContinuation,
        RunAuthorityOperationKind.PolicySatisfaction,
        RunAuthorityOperationKind.ProviderMutation,
        RunAuthorityOperationKind.DurableEventWrite
    ];

    private static readonly RunAuthorityOperationKind[] RequiredStopBeforeOperations =
    [
        RunAuthorityOperationKind.Commit,
        RunAuthorityOperationKind.Push,
        RunAuthorityOperationKind.DraftPullRequest,
        RunAuthorityOperationKind.ReadyForReview,
        RunAuthorityOperationKind.Merge,
        RunAuthorityOperationKind.Release,
        RunAuthorityOperationKind.Deployment,
        RunAuthorityOperationKind.MemoryPromotion,
        RunAuthorityOperationKind.WorkflowContinuation
    ];

    public static SourceApplyAuthorityDecision Evaluate(SourceApplyAuthorityRequest request)
    {
        var blocked = new List<string>();
        var missing = new List<string>();
        var forbidden = new List<string>();
        var checks = new List<string>();

        Validate(request, blocked, missing, forbidden, checks);

        return SourceApplyAuthorityEvaluator.BuildDecision(
            isEligible: blocked.Count == 0 && missing.Count == 0,
            path: SourceApplyAuthorityPath.BoundedRunAuthority,
            blocked,
            missing,
            forbidden,
            checks,
            SourceApplyAuthorityEvaluator.BuildEvidenceRefs(request),
            request.ReceiptRefs);
    }

    internal static void Validate(
        SourceApplyAuthorityRequest request,
        ICollection<string> blocked,
        ICollection<string> missing,
        ICollection<string> forbidden,
        ICollection<string> checks)
    {
        var grant = request.BoundedRunAuthorityGrant;
        if (grant is null)
        {
            missing.Add("BoundedRunAuthorityGrantRequired");
            return;
        }

        RequireText(grant.GrantId, "BoundedRunGrantIdRequired", blocked);
        ValidateSingleExplicitScope(grant.Repository, "BoundedRunRepository", blocked);
        ValidateSingleExplicitScope(grant.Branch, "BoundedRunBranch", blocked);
        ValidateSingleExplicitScope(grant.RunId, "BoundedRunRunId", blocked);
        Match(grant.Repository, request.Repository, "BoundedRunRepositoryMismatch", blocked);
        Match(grant.Branch, request.Branch, "BoundedRunBranchMismatch", blocked);
        Match(grant.RunId, request.RunId, "BoundedRunRunIdMismatch", blocked);
        ValidateGrantPatchHash(request, grant, blocked, missing);
        ValidateAllowedOperations(grant, blocked);
        ValidateStopBeforeOperations(grant, blocked);
        ValidateFileGlobs(grant.AllowedFileGlobs, "BoundedRunAllowedFileGlobs", requireNonEmpty: true, blocked);
        ValidateFileGlobs(grant.ForbiddenFileGlobs, "BoundedRunForbiddenFileGlobs", requireNonEmpty: false, blocked);
        ValidateAffectedFiles(request, grant.AllowedFileGlobs, grant.ForbiddenFileGlobs, blocked, missing);
        ValidateExpiry(grant.ExpiresAtUtc, request.ObservedAtUtc, "BoundedRun", blocked);
        ValidateGrantedBy(grant.GrantedBy, blocked);
        ValidateMutationBudget(request, grant, blocked, forbidden);
        ValidateRequiredValidation(request, grant, blocked, missing, forbidden, checks);
        RequireText(grant.HumanReadableIntent, "BoundedRunHumanReadableIntentRequired", blocked);

        forbidden.Add("do not apply source from authority decision alone");
        forbidden.Add("do not commit from source apply authority");
        forbidden.Add("do not push from source apply authority");
        forbidden.Add("do not continue workflow from source apply authority");
        checks.Add("executor must independently re-check repo/branch/run/patch hash/file scope/expiry/worktree state");
    }

    private static void ValidateAllowedOperations(BoundedRunAuthorityGrant grant, ICollection<string> blocked)
    {
        if (grant.AllowedOperationKinds is null || grant.AllowedOperationKinds.Count == 0)
        {
            blocked.Add("BoundedRunAllowedOperationKindsRequired");
            return;
        }

        if (!grant.AllowedOperationKinds.Contains(RunAuthorityOperationKind.SourceApply))
            blocked.Add("BoundedRunSourceApplyOperationRequired");

        foreach (var operation in grant.AllowedOperationKinds)
        {
            if (!Enum.IsDefined(operation) || operation == RunAuthorityOperationKind.Unknown)
                blocked.Add("BoundedRunAllowedOperationKindKnownRequired");
            if (ForbiddenSourceApplyGrantOperations.Contains(operation))
                blocked.Add($"PostSourceApplyAuthorityNotAllowedBySourceApplyGrant:{operation}");
        }
    }

    private static void ValidateStopBeforeOperations(BoundedRunAuthorityGrant grant, ICollection<string> blocked)
    {
        if (grant.StopBeforeOperationKinds is null)
        {
            blocked.Add("BoundedRunStopBeforeOperationKindsRequired");
            return;
        }

        foreach (var operation in grant.StopBeforeOperationKinds)
        {
            if (!Enum.IsDefined(operation) || operation == RunAuthorityOperationKind.Unknown)
                blocked.Add("BoundedRunStopBeforeOperationKindKnownRequired");
        }

        if (grant.StopBeforeOperationKinds.Contains(RunAuthorityOperationKind.SourceApply))
            blocked.Add("OperationStoppedBefore:SourceApply");

        foreach (var operation in RequiredStopBeforeOperations)
        {
            if (!grant.StopBeforeOperationKinds.Contains(operation))
                blocked.Add($"PostSourceApplyStopBeforeRequired:{operation}");
        }
    }

    private static void ValidateGrantPatchHash(
        SourceApplyAuthorityRequest request,
        BoundedRunAuthorityGrant grant,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(grant.PatchHash))
        {
            missing.Add("BoundedRunPatchHashRequired");
            return;
        }

        if (!OperationEligibilityPatchHashRules.IsSafePatchHash(grant.PatchHash))
        {
            blocked.Add("BoundedRunPatchHashInvalid");
            return;
        }

        if (!string.Equals(grant.PatchHash, request.PatchHash, StringComparison.OrdinalIgnoreCase))
            blocked.Add("BoundedRunPatchHashMismatch");
    }

    private static void ValidateRequiredValidation(
        SourceApplyAuthorityRequest request,
        BoundedRunAuthorityGrant grant,
        ICollection<string> blocked,
        ICollection<string> missing,
        ICollection<string> forbidden,
        ICollection<string> checks)
    {
        if (grant.RequiredValidation is null || grant.RequiredValidation.Count == 0)
        {
            missing.Add("BoundedRunRequiredValidationRequired");
            return;
        }

        if (request.ValidationEvidence is null || request.ValidationEvidence.Count == 0)
        {
            missing.Add("ValidationEvidenceRequired");
            forbidden.Add("do not treat missing validation evidence as passed");
            return;
        }

        foreach (var required in grant.RequiredValidation)
        {
            if (string.IsNullOrWhiteSpace(required.ValidationKind))
            {
                blocked.Add("BoundedRunRequiredValidationKindRequired");
                continue;
            }

            if (required.EvidenceRefPrefixes is null || required.EvidenceRefPrefixes.Count == 0)
            {
                blocked.Add($"RequiredValidationEvidenceRefPrefixesRequired:{required.ValidationKind}");
                continue;
            }

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
                ValidateValidationEvidence(candidate, required, request.PatchHash, blocked, missing);
        }

        forbidden.Add("do not treat validation evidence as source apply authority by itself");
        forbidden.Add("do not run validation from source apply authority evaluation");
        checks.Add("validation evidence references still require resolver verification");
    }

    private static void ValidateValidationEvidence(
        OperationEligibilityValidationEvidence evidence,
        BoundedRunAuthorityRequiredValidation required,
        string requestPatchHash,
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

        if (!string.Equals(evidence.PatchHash, requestPatchHash, StringComparison.OrdinalIgnoreCase))
            blocked.Add($"ValidationEvidencePatchHashMismatch:{required.ValidationKind}");
    }

    private static bool EvidenceRefMatches(
        BoundedRunAuthorityRequiredValidation required,
        OperationEligibilityValidationEvidence evidence) =>
        required.EvidenceRefPrefixes is not null &&
        !string.IsNullOrWhiteSpace(evidence.EvidenceRef) &&
        required.EvidenceRefPrefixes.Any(prefix =>
            !string.IsNullOrWhiteSpace(prefix) &&
            evidence.EvidenceRef.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static void ValidateAffectedFiles(
        SourceApplyAuthorityRequest request,
        IReadOnlyCollection<string>? allowedGlobs,
        IReadOnlyCollection<string>? forbiddenGlobs,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        if (request.AffectedFilePaths is null || request.AffectedFilePaths.Count == 0)
        {
            missing.Add("AffectedFilePathsRequired");
            return;
        }

        if (allowedGlobs is null || forbiddenGlobs is null)
            return;

        foreach (var filePath in request.AffectedFilePaths)
        {
            if (!BoundedRunAuthorityGrantFileScope.IsSafeRelativeGlob(filePath))
                blocked.Add($"AffectedFileUnsafe:{filePath}");
            else if (BoundedRunAuthorityGrantFileScope.IsForbidden(filePath, forbiddenGlobs))
                blocked.Add($"AffectedFileForbidden:{filePath}");
            else if (!BoundedRunAuthorityGrantFileScope.IsAllowed(filePath, allowedGlobs, forbiddenGlobs))
                blocked.Add($"AffectedFileNotAllowed:{filePath}");
        }
    }

    private static void ValidateMutationBudget(
        SourceApplyAuthorityRequest request,
        BoundedRunAuthorityGrant grant,
        ICollection<string> blocked,
        ICollection<string> forbidden)
    {
        if (request.MutationsAlreadyConsumed < 0)
            blocked.Add("MutationsAlreadyConsumedCannotBeNegative");
        if (request.RequestedMutationCount < 0)
            blocked.Add("RequestedMutationCountCannotBeNegative");

        if (request.MutationsAlreadyConsumed < 0 || request.RequestedMutationCount < 0)
            return;

        var consumed = (long)request.MutationsAlreadyConsumed;
        var requested = (long)request.RequestedMutationCount;
        var max = (long)grant.MaxMutations;
        if (max < 0)
        {
            blocked.Add("BoundedRunMaxMutationsCannotBeNegative");
            return;
        }

        if (consumed + requested > max)
        {
            blocked.Add("MutationBudgetExceeded");
            forbidden.Add("do not treat zero max mutations as unlimited");
        }
    }

    internal static void ValidateExpiry(
        DateTimeOffset expiresAtUtc,
        DateTimeOffset observedAtUtc,
        string label,
        ICollection<string> blocked)
    {
        if (expiresAtUtc == default)
        {
            blocked.Add($"{label}ExpiresAtUtcRequired");
            return;
        }

        if (expiresAtUtc.Offset != TimeSpan.Zero)
            blocked.Add($"{label}ExpiresAtUtcMustBeUtc");
        if (expiresAtUtc <= observedAtUtc)
            blocked.Add($"{label}Expired");
    }

    internal static void ValidateFileGlobs(
        IReadOnlyCollection<string>? globs,
        string label,
        bool requireNonEmpty,
        ICollection<string> blocked)
    {
        if (globs is null)
        {
            blocked.Add($"{label}Required");
            return;
        }

        if (requireNonEmpty && globs.Count == 0)
            blocked.Add($"{label}Required");

        foreach (var glob in globs)
        {
            if (!BoundedRunAuthorityGrantFileScope.IsSafeRelativeGlob(glob))
                blocked.Add($"{label}Unsafe:{glob}");
        }
    }

    internal static void ValidateGrantedBy(BoundedRunAuthorityGrantedBy? grantedBy, ICollection<string> blocked)
    {
        if (grantedBy is null)
        {
            blocked.Add("BoundedRunGrantedByRequired");
            return;
        }

        RequireText(grantedBy.PrincipalId, "BoundedRunGrantedByPrincipalIdRequired", blocked);
        RequireText(grantedBy.EvidenceRef, "BoundedRunGrantedByEvidenceRefRequired", blocked);
        ValidatePrincipalKind(grantedBy.PrincipalKind, "BoundedRunGrantedByPrincipalKind", blocked);
    }

    internal static void ValidatePrincipalKind(string? principalKind, string label, ICollection<string> blocked)
    {
        if (string.IsNullOrWhiteSpace(principalKind))
        {
            blocked.Add($"{label}Required");
            return;
        }

        if (!string.Equals(principalKind, "Human", StringComparison.OrdinalIgnoreCase))
            blocked.Add($"{label}Forbidden:{principalKind}");
    }

    internal static void ValidateSingleExplicitScope(string? value, string label, ICollection<string> blocked)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            blocked.Add($"{label}Required");
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

    private static void Match(string actual, string expected, string issue, ICollection<string> blocked)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            blocked.Add(issue);
    }

    private static void RequireText(string? value, string issue, ICollection<string> blocked)
    {
        if (string.IsNullOrWhiteSpace(value))
            blocked.Add(issue);
    }
}
