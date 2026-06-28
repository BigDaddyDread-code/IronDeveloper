using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class MissingEvidenceVisibilityValidator
{
    public const int MaxSafeTextLength = 360;

    private static readonly string[] UnsafeMissingEvidenceVisibilityMarkers =
    [
        "isevidencesatisfied = true",
        "evidencesatisfied = true",
        "createsevidence = true",
        "missingevidencecreated = true",
        "overridesmissingevidence = true",
        "waivesevidencerequirement = true",
        "cansatisfymissingevidence = true",
        "cancreateevidence = true",
        "canapprove = true",
        "cansatisfypolicy = true",
        "canrefreshvalidation = true",
        "canprovesourcesafety = true",
        "canrundiagnostic = true",
        "canretry = true",
        "canrollback = true",
        "canrecover = true",
        "canmutate = true",
        "cancontinueworkflow = true",
        "canmerge = true",
        "canrelease = true",
        "candeploy = true",
        "canbypassredaction = true",
        "canviewsecrets = true",
        "canviewcredentials = true",
        "canviewrawpayload = true",
        "canviewprivatereasoning = true",
        "canproceed = true",
        "missing evidence visibility satisfies evidence",
        "visible missing evidence means approved",
        "missing evidence can be supplied by role",
        "role can fix missing evidence",
        "role can override missing evidence",
        "missing evidence visibility allows workflow continuation",
        "missing evidence visibility grants action",
        "missing evidence visibility grants mutation",
        "missing evidence visibility grants release",
        "missing evidence visibility bypasses redaction",
        string.Concat("bear", "er "),
        string.Concat("to", "ken", "="),
        string.Concat("sec", "ret", "="),
        string.Concat("pass", "word", "="),
        string.Concat("private ", "key"),
        "raw provider response",
        "raw source",
        "source file content",
        "raw diff",
        "raw patch",
        "raw log",
        "private reasoning",
        "chain-of-thought",
        "scratchpad"
    ];

    public static MissingEvidenceVisibilityValidationResult ValidateRequest(
        MissingEvidenceVisibilityRequest? request)
    {
        if (request is null)
        {
            return Result(["MissingEvidenceVisibilityRequestRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRequiredSafeTextIssue(request.CorrelationId, "CorrelationId", issues, unsafeRefs);
        AddRoleIdIssue(request.RequestedRoleId, issues, unsafeRefs);
        AddDefinedEnumIssue(request.RequestedMissingEvidenceKind, "RequestedMissingEvidenceKindInvalid", issues);
        AddDefinedEnumIssue(request.RequestedMaterialKind, "RequestedMaterialKindInvalid", issues);
        AddDefinedEnumIssue(request.RequestedIntent, "RequestedIntentInvalid", issues);
        AddOptionalSafeTextIssue(request.RoleCatalogEvidenceRef, "RoleCatalogEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.VisibilityMatrixEvidenceRef, "VisibilityMatrixEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.ForbiddenActionCatalogEvidenceRef, "ForbiddenActionCatalogEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.SourceMissingEvidenceRef, "SourceMissingEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalTenantBoundaryEvidenceRef, "OptionalTenantBoundaryEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalRedactionEvidenceRef, "OptionalRedactionEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalPolicyEvidenceRef, "OptionalPolicyEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalApprovalEvidenceRef, "OptionalApprovalEvidenceRef", issues, unsafeRefs);

        return Result(issues, unsafeRefs);
    }

    public static bool ContainsUnsafeMissingEvidenceVisibilityText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return UnsafeMissingEvidenceVisibilityMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsReadOnlyIntent(MissingEvidenceVisibilityIntent intent) =>
        intent is MissingEvidenceVisibilityIntent.InspectMissingEvidence or
            MissingEvidenceVisibilityIntent.SummariseMissingEvidence or
            MissingEvidenceVisibilityIntent.ListMissingEvidence;

    public static bool IsKnownMissingEvidenceKind(MissingEvidenceKind kind) =>
        kind != MissingEvidenceKind.Unknown && Enum.IsDefined(kind);

    public static bool IsKnownMaterialKind(MissingEvidenceMaterialKind kind) =>
        kind != MissingEvidenceMaterialKind.Unknown && Enum.IsDefined(kind);

    public static bool IsKnownIntent(MissingEvidenceVisibilityIntent intent) =>
        intent != MissingEvidenceVisibilityIntent.Unknown && Enum.IsDefined(intent);

    public static bool IsSafeVisibleMaterial(MissingEvidenceMaterialKind materialKind) =>
        materialKind is MissingEvidenceMaterialKind.PresenceOnly or
            MissingEvidenceMaterialKind.CategoryOnly or
            MissingEvidenceMaterialKind.RedactedSummary or
            MissingEvidenceMaterialKind.RequiredEvidenceReference;

    public static MissingEvidenceVisibilityClassification ClassifyIntentBlock(
        MissingEvidenceVisibilityIntent intent) =>
        intent switch
        {
            MissingEvidenceVisibilityIntent.SatisfyMissingEvidence or
            MissingEvidenceVisibilityIntent.CreateMissingEvidence or
            MissingEvidenceVisibilityIntent.OverrideMissingEvidence or
            MissingEvidenceVisibilityIntent.WaiveEvidenceRequirement => MissingEvidenceVisibilityClassification.BlockedByEvidenceSatisfactionIntent,
            MissingEvidenceVisibilityIntent.AcceptApproval => MissingEvidenceVisibilityClassification.BlockedByApprovalIntent,
            MissingEvidenceVisibilityIntent.SatisfyPolicy => MissingEvidenceVisibilityClassification.BlockedByPolicyIntent,
            MissingEvidenceVisibilityIntent.RefreshValidation => MissingEvidenceVisibilityClassification.BlockedByValidationIntent,
            MissingEvidenceVisibilityIntent.ProveSourceSafety => MissingEvidenceVisibilityClassification.BlockedBySourceSafetyIntent,
            MissingEvidenceVisibilityIntent.RunDiagnostic or
            MissingEvidenceVisibilityIntent.Retry or
            MissingEvidenceVisibilityIntent.Rollback or
            MissingEvidenceVisibilityIntent.Recover => MissingEvidenceVisibilityClassification.BlockedByExecutionIntent,
            MissingEvidenceVisibilityIntent.MutateSource or
            MissingEvidenceVisibilityIntent.ApplyPatch or
            MissingEvidenceVisibilityIntent.Commit or
            MissingEvidenceVisibilityIntent.Push or
            MissingEvidenceVisibilityIntent.CreatePullRequest or
            MissingEvidenceVisibilityIntent.ReadyForReview => MissingEvidenceVisibilityClassification.BlockedByMutationIntent,
            MissingEvidenceVisibilityIntent.ContinueWorkflow => MissingEvidenceVisibilityClassification.BlockedByWorkflowIntent,
            MissingEvidenceVisibilityIntent.Merge or
            MissingEvidenceVisibilityIntent.Release or
            MissingEvidenceVisibilityIntent.Deploy => MissingEvidenceVisibilityClassification.BlockedByReleaseDeployIntent,
            MissingEvidenceVisibilityIntent.BypassRedaction => MissingEvidenceVisibilityClassification.BlockedByRedactionBypassIntent,
            MissingEvidenceVisibilityIntent.DiscloseSecret or
            MissingEvidenceVisibilityIntent.DiscloseCredential or
            MissingEvidenceVisibilityIntent.DiscloseRawPayload or
            MissingEvidenceVisibilityIntent.DisclosePrivateReasoning => MissingEvidenceVisibilityClassification.BlockedByDisclosureIntent,
            _ => MissingEvidenceVisibilityClassification.BlockedByActionIntent
        };

    public static MissingEvidenceVisibilityClassification ClassifyMaterialBlock(
        MissingEvidenceMaterialKind materialKind) =>
        materialKind switch
        {
            MissingEvidenceMaterialKind.RawPayload or
            MissingEvidenceMaterialKind.RawProviderResponse or
            MissingEvidenceMaterialKind.RawSource or
            MissingEvidenceMaterialKind.RawDiff or
            MissingEvidenceMaterialKind.RawPatch or
            MissingEvidenceMaterialKind.RawLog => MissingEvidenceVisibilityClassification.BlockedByRawMaterial,
            MissingEvidenceMaterialKind.CredentialMaterial => MissingEvidenceVisibilityClassification.BlockedByCredentialMaterial,
            MissingEvidenceMaterialKind.SecretMaterial => MissingEvidenceVisibilityClassification.BlockedBySecretMaterial,
            MissingEvidenceMaterialKind.PrivateReasoning => MissingEvidenceVisibilityClassification.BlockedByPrivateReasoningMaterial,
            _ => MissingEvidenceVisibilityClassification.Hidden
        };

    private static MissingEvidenceVisibilityValidationResult Result(
        IReadOnlyList<string> issues,
        IReadOnlyList<string> unsafeRefs) =>
        new()
        {
            IsValid = issues.Count == 0,
            Issues = issues
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static issue => issue, StringComparer.Ordinal)
                .ToArray(),
            UnsafeRefs = unsafeRefs
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

    private static void AddRoleIdIssue(
        string? roleId,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(roleId))
        {
            issues.Add("RoleIdRequired");
            return;
        }

        if (ContainsUnsafeMissingEvidenceVisibilityText(roleId))
        {
            issues.Add("RoleIdUnsafe");
            unsafeRefs.Add(roleId);
            return;
        }

        if (!RoleIdPattern().IsMatch(roleId))
        {
            issues.Add("RoleIdInvalid");
        }
    }

    private static void AddRequiredSafeTextIssue(
        string? value,
        string issuePrefix,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        AddSafeTextIssue(value, issuePrefix, issues, unsafeRefs);
    }

    private static void AddOptionalSafeTextIssue(
        string? value,
        string issuePrefix,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AddSafeTextIssue(value, issuePrefix, issues, unsafeRefs);
    }

    private static void AddSafeTextIssue(
        string value,
        string issuePrefix,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (ContainsUnsafeMissingEvidenceVisibilityText(value))
        {
            issues.Add($"{issuePrefix}Unsafe");
            unsafeRefs.Add(value);
            return;
        }

        if (value.Length > MaxSafeTextLength ||
            value.Any(static ch => char.IsControl(ch)) ||
            !SafeTextPattern().IsMatch(value))
        {
            issues.Add($"{issuePrefix}Invalid");
        }
    }

    private static void AddDefinedEnumIssue<TEnum>(
        TEnum value,
        string issue,
        ICollection<string> issues)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            issues.Add(issue);
        }
    }

    [GeneratedRegex("^role:[a-z0-9][a-z0-9:-]{2,120}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleIdPattern();

    [GeneratedRegex("^[a-zA-Z0-9 ._:/{}?&=,;()'\\[\\]-]{1,360}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextPattern();
}
