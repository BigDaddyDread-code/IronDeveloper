using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class ExternalViewerRedactionValidator
{
    public const int MaxSafeTextLength = 360;

    private static readonly string[] UnsafeEvidenceMarkers =
    [
        "externalaccessgranted = true",
        "externalviewergranted = true",
        "externalviewerassigned = true",
        "cancreatesharelink = true",
        "canexportrawdata = true",
        "canviewrawpayload = true",
        "canviewrawsource = true",
        "canviewrawlog = true",
        "canviewsecrets = true",
        "canviewcredentials = true",
        "canviewprivatereasoning = true",
        "canbypassredaction = true",
        "canaccessalltenants = true",
        "canviewplatformdata = true",
        "canapprove = true",
        "satisfiespolicy = true",
        "validationrefreshed = true",
        "sourcesafetyproven = true",
        "canrundiagnostic = true",
        "canretry = true",
        "canrollback = true",
        "canrecover = true",
        "canmutate = true",
        "cancontinueworkflow = true",
        "canmerge = true",
        "canrelease = true",
        "candeploy = true",
        "external viewer may see raw payload",
        "external viewer may see secrets",
        "external viewer may see credentials",
        "external viewer may see private reasoning",
        "external viewer may bypass redaction",
        "external viewer may inspect all tenants",
        "external viewer may access platform data",
        "external viewer may export raw data",
        "external viewer may receive provider response",
        "external viewer may approve policy",
        "external viewer may continue workflow",
        "access granted",
        "permission granted",
        "approval granted",
        "policy satisfied",
        "validation refreshed",
        "source safety proven",
        "redaction bypassed",
        "secret visible",
        "credential visible",
        "private reasoning visible",
        "raw payload visible",
        "raw provider response",
        "raw source",
        "source file content",
        "raw diff",
        "raw patch",
        "raw log",
        "private reasoning",
        "chain-of-thought",
        "scratchpad",
        string.Concat("bear", "er "),
        string.Concat("to", "ken", "="),
        string.Concat("sec", "ret", "="),
        string.Concat("pass", "word", "="),
        string.Concat("connection", "string", "="),
        string.Concat("private ", "key")
    ];

    public static ExternalViewerRedactionValidationResult ValidateRequest(
        ExternalViewerRedactionRequest? request)
    {
        if (request is null)
        {
            return Result(["ExternalViewerRedactionRequestRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRequiredSafeTextIssue(request.CorrelationId, "CorrelationId", issues, unsafeRefs);
        AddRoleKeyIssue(request.RequestedRoleKey, issues, unsafeRefs);
        AddKnownEnumIssue(request.RequestedSurface, RoleVisibilitySurface.Unknown, "RequestedSurfaceUnknown", issues);
        AddDefinedEnumIssue(request.RequestedMaterialKind, "RequestedMaterialKindInvalid", issues);
        AddDefinedEnumIssue(request.RequestedIntent, "RequestedIntentInvalid", issues);
        AddOptionalSafeTextIssue(request.SourceEvidenceRef, "SourceEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.RoleCatalogEvidenceRef, "RoleCatalogEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.VisibilityMatrixEvidenceRef, "VisibilityMatrixEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalPolicyEvidenceRef, "OptionalPolicyEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalRedactionEvidenceRef, "OptionalRedactionEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalTenantBoundaryEvidenceRef, "OptionalTenantBoundaryEvidenceRef", issues, unsafeRefs);

        return Result(issues, unsafeRefs);
    }

    public static bool ContainsUnsafeEvidenceText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return UnsafeEvidenceMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsReadOnlyIntent(ExternalViewerRedactionRequestedIntent intent) =>
        intent is ExternalViewerRedactionRequestedIntent.ReadOnlyInspect or
            ExternalViewerRedactionRequestedIntent.ReadOnlySummarise;

    public static ExternalViewerRedactionClassification ClassifyIntentBlock(
        ExternalViewerRedactionRequestedIntent intent) =>
        intent switch
        {
            ExternalViewerRedactionRequestedIntent.GrantExternalAccess or
            ExternalViewerRedactionRequestedIntent.CreateShareLink or
            ExternalViewerRedactionRequestedIntent.ExportRawData or
            ExternalViewerRedactionRequestedIntent.ViewRawPayload or
            ExternalViewerRedactionRequestedIntent.ViewSecrets or
            ExternalViewerRedactionRequestedIntent.ViewCredentials or
            ExternalViewerRedactionRequestedIntent.ViewPrivateReasoning or
            ExternalViewerRedactionRequestedIntent.BypassRedaction or
            ExternalViewerRedactionRequestedIntent.CrossTenantVisibility or
            ExternalViewerRedactionRequestedIntent.PlatformVisibility => ExternalViewerRedactionClassification.BlockedByAccessIntent,
            _ => ExternalViewerRedactionClassification.BlockedByActionIntent
        };

    public static bool RequiresTenantBoundaryEvidence(
        ExternalViewerRedactionMaterialKind materialKind) =>
        materialKind is ExternalViewerRedactionMaterialKind.TenantScopedMetadata or
            ExternalViewerRedactionMaterialKind.ProjectScopedMetadata;

    public static bool RequiresRedactionEvidence(
        ExternalViewerRedactionMaterialKind materialKind) =>
        materialKind is ExternalViewerRedactionMaterialKind.RedactedOperationStatusSummary or
            ExternalViewerRedactionMaterialKind.RedactedValidationSummary or
            ExternalViewerRedactionMaterialKind.RedactedReviewSummary or
            ExternalViewerRedactionMaterialKind.RedactedApprovalSummary or
            ExternalViewerRedactionMaterialKind.RedactedDiagnosticSummary or
            ExternalViewerRedactionMaterialKind.RedactedAuditSummary or
            ExternalViewerRedactionMaterialKind.RedactedReleaseReadinessSummary or
            ExternalViewerRedactionMaterialKind.RedactedPolicySummary or
            ExternalViewerRedactionMaterialKind.RedactedErrorSummary or
            ExternalViewerRedactionMaterialKind.RedactedLogSummary or
            ExternalViewerRedactionMaterialKind.RedactedReceiptSummary;

    private static ExternalViewerRedactionValidationResult Result(
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

    private static void AddRoleKeyIssue(
        string? roleKey,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(roleKey))
        {
            issues.Add("RequestedRoleKeyRequired");
            return;
        }

        if (ContainsUnsafeEvidenceText(roleKey))
        {
            issues.Add("RequestedRoleKeyUnsafe");
            unsafeRefs.Add(roleKey);
            return;
        }

        if (!RoleCatalogValidator.IsSafeRoleId(roleKey))
        {
            issues.Add("RequestedRoleKeyInvalid");
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
        if (ContainsUnsafeEvidenceText(value))
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
