namespace IronDev.Core.Governance;

public static class SourceApplyAuthorityEvaluator
{
    public static SourceApplyAuthorityDecision Evaluate(SourceApplyAuthorityRequest? request)
    {
        var blocked = new List<string>();
        var missing = new List<string>();
        var forbidden = new List<string>();
        var checks = new List<string>();

        if (request is null)
        {
            blocked.Add("SourceApplyAuthorityRequestRequired");
            forbidden.Add("do not infer source apply authority from missing request");
            return BuildDecision(
                isEligible: false,
                SourceApplyAuthorityPath.None,
                blocked,
                missing,
                forbidden,
                checks,
                [],
                []);
        }

        ValidateRequestEnvelope(request, blocked, missing, forbidden);
        var hasAccepted = request.AcceptedApplyRequest is not null;
        var hasGrant = request.BoundedRunAuthorityGrant is not null;

        if (!hasAccepted && !hasGrant)
        {
            missing.Add("AcceptedApplyRequestOrBoundedRunAuthorityGrantRequired");
            forbidden.Add("do not use string refs alone as source apply authority");
        }

        if (hasAccepted && hasGrant && !AuthorityPathsBindSameScope(request.AcceptedApplyRequest!, request.BoundedRunAuthorityGrant!))
            blocked.Add("ConflictingSourceApplyAuthorityPaths");

        var acceptedBlocked = new List<string>();
        var acceptedMissing = new List<string>();
        var acceptedForbidden = new List<string>();
        var acceptedChecks = new List<string>();
        if (hasAccepted)
            ValidateAcceptedApplyRequest(request, acceptedBlocked, acceptedMissing, acceptedForbidden, acceptedChecks);

        var grantBlocked = new List<string>();
        var grantMissing = new List<string>();
        var grantForbidden = new List<string>();
        var grantChecks = new List<string>();
        if (hasGrant)
            SourceApplyBoundedGrantValidator.Validate(request, grantBlocked, grantMissing, grantForbidden, grantChecks);

        AddRange(blocked, acceptedBlocked.Select(reason => $"AcceptedApplyRequest:{reason}"));
        AddRange(missing, acceptedMissing.Select(reason => $"AcceptedApplyRequest:{reason}"));
        AddRange(forbidden, acceptedForbidden);
        AddRange(checks, acceptedChecks);

        AddRange(blocked, grantBlocked.Select(reason => $"BoundedRunAuthority:{reason}"));
        AddRange(missing, grantMissing.Select(reason => $"BoundedRunAuthority:{reason}"));
        AddRange(forbidden, grantForbidden);
        AddRange(checks, grantChecks);

        var acceptedValid = hasAccepted && acceptedBlocked.Count == 0 && acceptedMissing.Count == 0;
        var grantValid = hasGrant && grantBlocked.Count == 0 && grantMissing.Count == 0;
        var canUseAcceptedOnly = acceptedValid && !hasGrant;
        var canUseGrantOnly = grantValid && !hasAccepted;
        var canUseBoth = acceptedValid && grantValid && !blocked.Contains("ConflictingSourceApplyAuthorityPaths", StringComparer.OrdinalIgnoreCase);
        var isEligible = blocked.Count == 0 && missing.Count == 0 && (canUseAcceptedOnly || canUseGrantOnly || canUseBoth);

        if (isEligible)
        {
            forbidden.Add("do not apply source from authority decision alone");
            forbidden.Add("do not commit from source apply authority");
            forbidden.Add("do not push from source apply authority");
            forbidden.Add("do not create PR from source apply authority");
            forbidden.Add("do not mark ready for review from source apply authority");
            forbidden.Add("do not merge from source apply authority");
            forbidden.Add("do not release from source apply authority");
            forbidden.Add("do not deploy from source apply authority");
            forbidden.Add("do not promote memory from source apply authority");
            forbidden.Add("do not continue workflow from source apply authority");
            checks.Add("executor must independently re-check repo/branch/run/patch hash/file scope/expiry/worktree state");
        }

        return BuildDecision(
            isEligible,
            SelectAuthorityPath(canUseAcceptedOnly, canUseGrantOnly, canUseBoth),
            blocked,
            missing,
            forbidden,
            checks,
            BuildEvidenceRefs(request),
            request.ReceiptRefs);
    }

    internal static SourceApplyAuthorityDecision BuildDecision(
        bool isEligible,
        SourceApplyAuthorityPath path,
        IEnumerable<string> blocked,
        IEnumerable<string> missing,
        IEnumerable<string> forbidden,
        IEnumerable<string> checks,
        IEnumerable<string> evidenceRefs,
        IEnumerable<string> receiptRefs) =>
        new()
        {
            IsEligibleForControlledSourceApply = isEligible,
            AuthorityPath = isEligible ? path : SourceApplyAuthorityPath.None,
            BlockedReasons = Clean(blocked),
            MissingEvidence = Clean(missing),
            ForbiddenActions = Clean(forbidden),
            RequiredIndependentChecks = Clean(checks),
            EvidenceRefs = Clean(evidenceRefs),
            ReceiptRefs = Clean(receiptRefs)
        };

    internal static IReadOnlyCollection<string> BuildEvidenceRefs(SourceApplyAuthorityRequest request) =>
        Clean(
        [
            Ref("repo", request.Repository),
            Ref("branch", request.Branch),
            Ref("run", request.RunId),
            Ref("patch-hash", request.PatchHash),
            Ref("accepted-apply-request", request.AcceptedApplyRequest?.RequestId),
            request.AcceptedApplyRequest?.EvidenceRef,
            Ref("bounded-run-authority-grant", request.BoundedRunAuthorityGrant?.GrantId),
            .. ValuesOrEmpty(request.EvidenceRefs)
        ]);

    private static void ValidateRequestEnvelope(
        SourceApplyAuthorityRequest request,
        ICollection<string> blocked,
        ICollection<string> missing,
        ICollection<string> forbidden)
    {
        RequireText(request.Repository, "RepositoryRequired", blocked);
        RequireText(request.Branch, "BranchRequired", blocked);
        RequireText(request.RunId, "RunIdRequired", blocked);

        if (string.IsNullOrWhiteSpace(request.PatchHash))
            missing.Add("PatchHashRequired");
        else if (!OperationEligibilityPatchHashRules.IsSafePatchHash(request.PatchHash))
            blocked.Add("PatchHashInvalid");

        if (request.AffectedFilePaths is null || request.AffectedFilePaths.Count == 0)
            missing.Add("AffectedFilePathsRequired");
        else
        {
            foreach (var filePath in request.AffectedFilePaths)
            {
                if (!BoundedRunAuthorityGrantFileScope.IsSafeRelativeGlob(filePath))
                    blocked.Add($"AffectedFileUnsafe:{filePath}");
            }
        }

        if (request.ObservedAtUtc == default)
            blocked.Add("ObservedAtUtcRequired");

        forbidden.Add("do not treat patch readiness as source apply authority");
        forbidden.Add("do not treat validation success as source apply authority");
        forbidden.Add("do not treat status Eligible as source apply authority");
        forbidden.Add("do not treat memory or UI state as source apply authority");
    }

    private static void ValidateAcceptedApplyRequest(
        SourceApplyAuthorityRequest request,
        ICollection<string> blocked,
        ICollection<string> missing,
        ICollection<string> forbidden,
        ICollection<string> checks)
    {
        var accepted = request.AcceptedApplyRequest;
        if (accepted is null)
        {
            missing.Add("AcceptedApplyRequestRequired");
            return;
        }

        RequireText(accepted.RequestId, "RequestIdRequired", blocked);
        RequireText(accepted.EvidenceRef, "EvidenceRefRequired", blocked);
        Match(accepted.Repository, request.Repository, "RepositoryMismatch", blocked);
        Match(accepted.Branch, request.Branch, "BranchMismatch", blocked);
        Match(accepted.RunId, request.RunId, "RunIdMismatch", blocked);
        ValidateAcceptedPatchHash(request, accepted, blocked, missing);
        SourceApplyBoundedGrantValidator.ValidateFileGlobs(accepted.AllowedFileGlobs, "AcceptedApplyAllowedFileGlobs", requireNonEmpty: true, blocked);
        SourceApplyBoundedGrantValidator.ValidateFileGlobs(accepted.ForbiddenFileGlobs, "AcceptedApplyForbiddenFileGlobs", requireNonEmpty: false, blocked);
        ValidateAffectedFiles(request, accepted.AllowedFileGlobs, accepted.ForbiddenFileGlobs, blocked, missing);

        if (accepted.AcceptedAtUtc == default)
            blocked.Add("AcceptedAtUtcRequired");
        SourceApplyBoundedGrantValidator.ValidateExpiry(accepted.ExpiresAtUtc, request.ObservedAtUtc, "AcceptedApplyRequest", blocked);

        RequireText(accepted.AcceptedByPrincipalId, "AcceptedByPrincipalIdRequired", blocked);
        SourceApplyBoundedGrantValidator.ValidatePrincipalKind(accepted.AcceptedByPrincipalKind, "AcceptedByPrincipalKind", blocked);

        forbidden.Add("do not treat accepted apply request evidence as execution authority");
        checks.Add("executor must resolve and validate accepted apply request record before source apply");
    }

    private static void ValidateAcceptedPatchHash(
        SourceApplyAuthorityRequest request,
        AcceptedSourceApplyRequestEvidence accepted,
        ICollection<string> blocked,
        ICollection<string> missing)
    {
        if (string.IsNullOrWhiteSpace(accepted.PatchHash))
        {
            missing.Add("PatchHashRequired");
            return;
        }

        if (!OperationEligibilityPatchHashRules.IsSafePatchHash(accepted.PatchHash))
        {
            blocked.Add("PatchHashInvalid");
            return;
        }

        if (!string.Equals(accepted.PatchHash, request.PatchHash, StringComparison.OrdinalIgnoreCase))
            blocked.Add("PatchHashMismatch");
    }

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

    private static bool AuthorityPathsBindSameScope(
        AcceptedSourceApplyRequestEvidence accepted,
        BoundedRunAuthorityGrant grant) =>
        string.Equals(accepted.Repository, grant.Repository, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(accepted.Branch, grant.Branch, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(accepted.RunId, grant.RunId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(accepted.PatchHash, grant.PatchHash, StringComparison.OrdinalIgnoreCase) &&
        SameSet(accepted.AllowedFileGlobs, grant.AllowedFileGlobs) &&
        SameSet(accepted.ForbiddenFileGlobs, grant.ForbiddenFileGlobs);

    private static bool SameSet(IReadOnlyCollection<string>? left, IReadOnlyCollection<string>? right)
    {
        var leftSet = Clean(left);
        var rightSet = Clean(right);
        return leftSet.Count == rightSet.Count && !leftSet.Except(rightSet, StringComparer.OrdinalIgnoreCase).Any();
    }

    private static SourceApplyAuthorityPath SelectAuthorityPath(
        bool acceptedOnly,
        bool grantOnly,
        bool both) =>
        grantOnly || both
            ? SourceApplyAuthorityPath.BoundedRunAuthority
            : acceptedOnly
                ? SourceApplyAuthorityPath.AcceptedApplyRequest
                : SourceApplyAuthorityPath.None;

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

    private static string Ref(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}:{value.Trim()}";

    private static void AddRange(ICollection<string> target, IEnumerable<string> values)
    {
        foreach (var value in values)
            target.Add(value);
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
