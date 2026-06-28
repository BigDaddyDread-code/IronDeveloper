using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class RolePermissionAuditValidator
{
    public const int MaxSafeTextLength = 360;

    private static readonly string[] AppliedChangeMarkers =
    [
        "role assigned",
        "role granted",
        "role revoked",
        "permission granted",
        "permission revoked",
        "access granted",
        "visibility granted",
        "external access granted",
        "change applied",
        "user permission updated",
        "role membership changed",
        "user updated",
        "group updated",
        "principal updated",
        "authorization succeeded"
    ];

    private static readonly string[] AuthorityGrantMarkers =
    [
        "audit record grants permission",
        "audit trail applies role change",
        "audit record assigns role",
        "audit record grants access",
        "audit record authorizes permission",
        "audit record satisfies policy",
        "audit record accepts approval",
        "audit trail updates user",
        "audit trail changes principal",
        "audit record bypasses redaction",
        "canassignrole = true",
        "cangrantpermission = true",
        "cangrantaccess = true",
        "canauthorize = true",
        "canmutate = true",
        "cancontinueworkflow = true",
        "canapprove = true",
        "cansatisfypolicy = true",
        "canbypassredaction = true",
        "canviewsecrets = true",
        "canviewcredentials = true",
        "canviewrawpayload = true",
        "canviewprivatereasoning = true",
        "roleassigned = true",
        "rolegranted = true",
        "permissiongranted = true",
        "permissionrevoked = true",
        "accessgranted = true",
        "visibilitygranted = true",
        "externalaccessgranted = true",
        "changeapplied = true",
        "userupdated = true",
        "groupupdated = true",
        "principalupdated = true",
        "permissionsatisfied = true"
    ];

    private static readonly string[] RawMaterialMarkers =
    [
        "raw payload",
        "raw provider response",
        "raw source",
        "raw log",
        "source file content",
        "raw diff",
        "raw patch"
    ];

    private static readonly string[] SecretMarkers =
    [
        string.Concat("to", "ken", "="),
        string.Concat("sec", "ret", "="),
        string.Concat("bear", "er "),
        string.Concat("private ", "key")
    ];

    private static readonly string[] CredentialMarkers =
    [
        string.Concat("pass", "word", "="),
        "credential material",
        "connection string"
    ];

    private static readonly string[] PrivateReasoningMarkers =
    [
        "private reasoning",
        "chain-of-thought",
        "scratchpad"
    ];

    public static RolePermissionAuditValidationResult ValidateRequest(
        RolePermissionAuditRequest? request)
    {
        if (request is null)
        {
            return Result(["RolePermissionAuditRequestRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRequiredSafeTextIssue(request.CorrelationId, "CorrelationId", issues, unsafeRefs);
        AddRoleIdIssue(request.RequestedRoleId, "RequestedRoleId", required: true, issues, unsafeRefs);
        AddRoleIdIssue(request.RequestedTargetRoleId, "RequestedTargetRoleId", required: false, issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.RequestedPermissionKey, "RequestedPermissionKey", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.RequestedActorRef, "RequestedActorRef", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.RequestedTenantRef, "RequestedTenantRef", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.RequestedProjectRef, "RequestedProjectRef", issues, unsafeRefs);
        AddRequiredSafeTextIssue(request.RequestedOperationRef, "RequestedOperationRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.RoleCatalogEvidenceRef, "RoleCatalogEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.ForbiddenActionCatalogEvidenceRef, "ForbiddenActionCatalogEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.MissingEvidenceVisibilityEvidenceRef, "MissingEvidenceVisibilityEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.SourceEvidenceRef, "SourceEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalPolicyEvidenceRef, "OptionalPolicyEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalApprovalEvidenceRef, "OptionalApprovalEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalTenantBoundaryEvidenceRef, "OptionalTenantBoundaryEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalRedactionEvidenceRef, "OptionalRedactionEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.PreviousAuditRecordFingerprint, "PreviousAuditRecordFingerprint", issues, unsafeRefs);
        AddDefinedEnumIssue(request.RequestedEventKind, "RequestedEventKindInvalid", issues);
        AddDefinedEnumIssue(request.RequestedSubjectKind, "RequestedSubjectKindInvalid", issues);
        AddDefinedEnumIssue(request.RequestedOutcomeKind, "RequestedOutcomeKindInvalid", issues);
        AddDefinedEnumIssue(request.RequestedAuthoritySourceKind, "RequestedAuthoritySourceKindInvalid", issues);

        return Result(issues, unsafeRefs);
    }

    public static bool ContainsUnsafeAuditText(string? value) =>
        UnsafeCategory(value) is not null;

    public static string? UnsafeCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.ToLowerInvariant();
        if (AppliedChangeMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            return "AppliedChange";
        }

        if (AuthorityGrantMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            return "AuthorityGrant";
        }

        if (RawMaterialMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            return "RawMaterial";
        }

        if (SecretMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            return "SecretMaterial";
        }

        if (CredentialMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
        {
            return "CredentialMaterial";
        }

        return PrivateReasoningMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal))
            ? "PrivateReasoning"
            : null;
    }

    public static bool IsKnownEvent(RolePermissionAuditEventKind eventKind) =>
        eventKind != RolePermissionAuditEventKind.Unknown && Enum.IsDefined(eventKind);

    public static bool IsKnownSubject(RolePermissionAuditSubjectKind subjectKind) =>
        subjectKind != RolePermissionAuditSubjectKind.Unknown && Enum.IsDefined(subjectKind);

    public static bool IsKnownOutcome(RolePermissionAuditOutcomeKind outcomeKind) =>
        outcomeKind != RolePermissionAuditOutcomeKind.Unknown && Enum.IsDefined(outcomeKind);

    public static bool IsKnownAuthoritySource(RolePermissionAuditAuthoritySourceKind sourceKind) =>
        sourceKind != RolePermissionAuditAuthoritySourceKind.Unknown && Enum.IsDefined(sourceKind);

    private static RolePermissionAuditValidationResult Result(
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
        string issuePrefix,
        bool required,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(roleId))
        {
            if (required)
            {
                issues.Add($"{issuePrefix}Required");
            }

            return;
        }

        if (AddUnsafeIssue(roleId, issuePrefix, issues, unsafeRefs))
        {
            return;
        }

        if (!RoleIdPattern().IsMatch(roleId))
        {
            issues.Add($"{issuePrefix}Invalid");
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
        if (AddUnsafeIssue(value, issuePrefix, issues, unsafeRefs))
        {
            return;
        }

        if (value.Length > MaxSafeTextLength ||
            value.Any(static ch => char.IsControl(ch)) ||
            !SafeTextPattern().IsMatch(value))
        {
            issues.Add($"{issuePrefix}Invalid");
        }
    }

    private static bool AddUnsafeIssue(
        string value,
        string issuePrefix,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        var category = UnsafeCategory(value);
        if (category is null)
        {
            return false;
        }

        issues.Add($"{issuePrefix}{category}Unsafe");
        unsafeRefs.Add(value);
        return true;
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
