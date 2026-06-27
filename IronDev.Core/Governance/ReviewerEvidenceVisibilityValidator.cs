using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class ReviewerEvidenceVisibilityValidator
{
    public const int MaxSafeTextLength = 360;

    private static readonly string[] UnsafeEvidenceVisibilityMarkers =
    [
        "access granted",
        "permission granted",
        "authority granted",
        "approval granted",
        "policy satisfied",
        "review approved",
        "review accepted",
        "validation satisfied",
        "safe to execute",
        "safe to mutate",
        "safe to disclose",
        "ready to merge",
        "ready to release",
        "ready to deploy",
        "workflow continuation authorized",
        "redaction bypassed",
        "secret visible",
        "credential visible",
        "private reasoning visible",
        "raw payload visible",
        "raw patch",
        "patch payload",
        "raw diff",
        "raw source",
        "source file content",
        "raw provider response",
        "raw command",
        "raw approval",
        "raw policy",
        "private reasoning",
        "chain-of-thought",
        "scratchpad",
        string.Concat("bear", "er "),
        string.Concat("to", "ken", "="),
        string.Concat("sec", "ret", "="),
        string.Concat("pass", "word", "="),
        string.Concat("private ", "key")
    ];

    public static ReviewerEvidenceVisibilityValidationResult ValidateRequest(
        ReviewerEvidenceVisibilityRequest? request)
    {
        if (request is null)
        {
            return Result(["ReviewerEvidenceVisibilityRequestRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRequiredSafeTextIssue(request.TenantId, "TenantId", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.ProjectId, "ProjectId", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.OperationId, "OperationId", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.CorrelationId, "CorrelationId", issues, unsafeRefs);
        AddRoleIdIssue(request.ReviewerRoleId, issues, unsafeRefs);
        AddKnownEnumIssue(request.ReviewerRoleKind, GovernanceRoleKind.Unknown, "ReviewerRoleKindUnknown", issues);
        AddKnownEnumIssue(request.ReviewerRoleScopeKind, GovernanceRoleScopeKind.Unknown, "ReviewerRoleScopeKindUnknown", issues);
        AddKnownEnumIssue(request.EvidenceSurface, RoleVisibilitySurface.Unknown, "EvidenceSurfaceUnknown", issues);
        AddKnownEnumIssue(request.EvidenceMaterialKind, RoleVisibilityMaterialKind.Unknown, "EvidenceMaterialKindUnknown", issues);
        AddKnownEnumIssue(request.EvidenceSensitivityKind, RoleVisibilitySensitivityKind.Unknown, "EvidenceSensitivityKindUnknown", issues);
        AddKnownEnumIssue(request.EvidenceVisibilityLevel, RoleVisibilityLevel.Unknown, "EvidenceVisibilityLevelUnknown", issues);
        AddKnownEnumIssue(request.IntentKind, ReviewerEvidenceVisibilityIntentKind.Unknown, "IntentKindUnknown", issues);
        AddRequiredSafeTextIssue(request.EvidenceRef, "EvidenceRef", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.EvidenceSubjectRef, "EvidenceSubjectRef", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.RoleCatalogId, "RoleCatalogId", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.RoleCatalogVersion, "RoleCatalogVersion", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.RoleCatalogEntryRef, "RoleCatalogEntryRef", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.VisibilityMatrixId, "VisibilityMatrixId", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.VisibilityMatrixVersion, "VisibilityMatrixVersion", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.VisibilityMatrixEntryRef, "VisibilityMatrixEntryRef", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.ReasonCode, "ReasonCode", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.Source, "Source", issues, unsafeRefs);

        if (request.ObservedAtUtc == default)
        {
            issues.Add("ObservedAtUtcRequired");
        }
        else if (request.ObservedAtUtc.Offset != TimeSpan.Zero)
        {
            issues.Add("ObservedAtUtcMustBeUtc");
        }

        AddOptionalSafeTextIssue(request.ReviewerAssignmentEvidenceRef, "ReviewerAssignmentEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.ReviewerEvidenceRequestRef, "ReviewerEvidenceRequestRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.VisibilityDecisionEvidenceRef, "VisibilityDecisionEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.PolicyDecisionEvidenceRef, "PolicyDecisionEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.RedactionEvidenceRef, "RedactionEvidenceRef", issues, unsafeRefs);

        return Result(issues, unsafeRefs);
    }

    public static bool IsReviewerRole(GovernanceRoleKind roleKind) =>
        roleKind is GovernanceRoleKind.Reviewer or
            GovernanceRoleKind.SecurityReviewer or
            GovernanceRoleKind.ReleaseReviewer or
            GovernanceRoleKind.OperationsReviewer or
            GovernanceRoleKind.RollbackReviewer or
            GovernanceRoleKind.RecoveryReviewer;

    public static bool IsReadIntent(ReviewerEvidenceVisibilityIntentKind intent) =>
        intent is ReviewerEvidenceVisibilityIntentKind.ReadEvidenceSummary or
            ReviewerEvidenceVisibilityIntentKind.ReadEvidenceMetadata or
            ReviewerEvidenceVisibilityIntentKind.ReadEvidenceReference or
            ReviewerEvidenceVisibilityIntentKind.ReadRedactedEvidence or
            ReviewerEvidenceVisibilityIntentKind.ReadReviewContext;

    public static bool IsActionIntent(ReviewerEvidenceVisibilityIntentKind intent) =>
        intent.ToString().StartsWith("Action", StringComparison.Ordinal);

    public static bool ContainsUnsafeEvidenceVisibilityText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return UnsafeEvidenceVisibilityMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static bool RequiresPolicyAndRedaction(
        RoleVisibilityMaterialKind materialKind,
        RoleVisibilitySensitivityKind sensitivityKind,
        RoleVisibilityLevel visibilityLevel) =>
        visibilityLevel is RoleVisibilityLevel.RedactedDetails or RoleVisibilityLevel.DetailEligibilityHint ||
        RoleVisibilityMatrixValidator.RequiresRedaction(materialKind, sensitivityKind, visibilityLevel);

    private static ReviewerEvidenceVisibilityValidationResult Result(
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
            issues.Add("ReviewerRoleIdRequired");
            return;
        }

        if (ContainsUnsafeEvidenceVisibilityText(roleId))
        {
            issues.Add("ReviewerRoleIdUnsafe");
            unsafeRefs.Add(roleId);
            return;
        }

        if (!RoleCatalogValidator.IsSafeRoleId(roleId))
        {
            issues.Add("ReviewerRoleIdInvalid");
        }
    }

    private static void AddKnownEnumIssue<TEnum>(
        TEnum value,
        TEnum unknownValue,
        string issue,
        ICollection<string> issues)
        where TEnum : struct, Enum
    {
        if (EqualityComparer<TEnum>.Default.Equals(value, unknownValue) || !Enum.IsDefined(value))
        {
            issues.Add(issue);
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

        AddTextShapeIssue(value, issuePrefix, issues, unsafeRefs);
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

        AddTextShapeIssue(value, issuePrefix, issues, unsafeRefs);
    }

    private static void AddTextShapeIssue(
        string value,
        string issuePrefix,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (ContainsUnsafeEvidenceVisibilityText(value))
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

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._:/@+,'()-]{1,359}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextPattern();
}
