using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class ViewerReadOnlyEnforcementValidator
{
    public const int MaxSafeTextLength = 360;

    private static readonly string[] UnsafeViewerTextMarkers =
    [
        "can view",
        "can read",
        "can access",
        "access granted",
        "permission granted",
        "read authorized",
        "view authorized",
        "authority granted",
        "approval granted",
        "policy satisfied",
        "validation satisfied",
        "safe to execute",
        "safe to mutate",
        "safe to disclose",
        "safe to expose",
        "authorized to view",
        "authorized to read",
        "authorized to execute",
        "authorized to approve",
        "authorized to merge",
        "authorized to release",
        "workflow continuation authorized",
        "redaction bypassed",
        "secret visible",
        "credential visible",
        "private reasoning visible",
        "raw payload visible",
        "user assigned",
        "group assigned",
        "principal",
        "claims principal",
        string.Concat("bear", "er "),
        string.Concat("to", "ken", "="),
        string.Concat("sec", "ret", "="),
        string.Concat("pass", "word", "="),
        string.Concat("private ", "key"),
        "raw policy",
        "raw approval",
        "raw command",
        "raw provider response",
        "private reasoning",
        "chain-of-thought",
        "scratchpad"
    ];

    public static ViewerReadOnlyRequestValidationResult ValidateRequest(ViewerReadOnlyEnforcementRequest? request)
    {
        if (request is null)
        {
            return Result(["ViewerReadOnlyRequestRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRequiredSafeTextIssue(request.TenantId, "TenantId", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.ProjectId, "ProjectId", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.OperationId, "OperationId", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.CorrelationId, "CorrelationId", issues, unsafeRefs);
        AddRoleIdIssue(request.RoleId, issues, unsafeRefs);
        AddKnownEnumIssue(request.RoleKind, GovernanceRoleKind.Unknown, "RoleKindUnknown", issues);
        AddKnownEnumIssue(request.RoleScopeKind, GovernanceRoleScopeKind.Unknown, "RoleScopeKindUnknown", issues);
        AddKnownEnumIssue(request.ViewerRoleKind, ViewerReadOnlyRoleKind.Unknown, "ViewerRoleKindUnknown", issues);
        AddKnownEnumIssue(request.VisibilitySurface, RoleVisibilitySurface.Unknown, "VisibilitySurfaceUnknown", issues);
        AddKnownEnumIssue(request.VisibilityMaterialKind, RoleVisibilityMaterialKind.Unknown, "VisibilityMaterialKindUnknown", issues);
        AddKnownEnumIssue(request.VisibilityLevel, RoleVisibilityLevel.Unknown, "VisibilityLevelUnknown", issues);
        AddKnownEnumIssue(request.SensitivityKind, RoleVisibilitySensitivityKind.Unknown, "SensitivityKindUnknown", issues);
        AddKnownEnumIssue(request.IntentKind, ViewerReadOnlyIntentKind.Unknown, "IntentKindUnknown", issues);
        AddRequiredSafeTextIssue(request.RequestedSurfaceRef, "RequestedSurfaceRef", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.RequestedMaterialRef, "RequestedMaterialRef", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.RequestedEvidenceRef, "RequestedEvidenceRef", issues, unsafeRefs);
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

        AddOptionalSafeTextIssue(request.RoleAssignmentEvidenceRef, "RoleAssignmentEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.VisibilityDecisionEvidenceRef, "VisibilityDecisionEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.PolicyDecisionEvidenceRef, "PolicyDecisionEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.RedactionEvidenceRef, "RedactionEvidenceRef", issues, unsafeRefs);

        return Result(issues, unsafeRefs);
    }

    public static bool ContainsUnsafeViewerText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return UnsafeViewerTextMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsReadIntent(ViewerReadOnlyIntentKind intent) =>
        intent is ViewerReadOnlyIntentKind.ReadStatus or
            ViewerReadOnlyIntentKind.ReadReceipt or
            ViewerReadOnlyIntentKind.ReadAudit or
            ViewerReadOnlyIntentKind.ReadSummary or
            ViewerReadOnlyIntentKind.ReadMetadata or
            ViewerReadOnlyIntentKind.ReadReference or
            ViewerReadOnlyIntentKind.ReadRedactedDetails or
            ViewerReadOnlyIntentKind.ReadFrontendView;

    public static bool IsActionIntent(ViewerReadOnlyIntentKind intent) =>
        intent.ToString().StartsWith("Action", StringComparison.Ordinal);

    public static bool RequiresPolicyAndRedaction(
        RoleVisibilityMaterialKind materialKind,
        RoleVisibilitySensitivityKind sensitivityKind,
        RoleVisibilityLevel visibilityLevel) =>
        visibilityLevel is RoleVisibilityLevel.RedactedDetails or RoleVisibilityLevel.DetailEligibilityHint ||
        RoleVisibilityMatrixValidator.RequiresRedaction(materialKind, sensitivityKind, visibilityLevel);

    private static ViewerReadOnlyRequestValidationResult Result(
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

        if (ContainsUnsafeViewerText(roleId))
        {
            issues.Add("RoleIdUnsafe");
            unsafeRefs.Add(roleId);
            return;
        }

        if (!RoleCatalogValidator.IsSafeRoleId(roleId))
        {
            issues.Add("RoleIdInvalid");
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
        if (ContainsUnsafeViewerText(value))
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
