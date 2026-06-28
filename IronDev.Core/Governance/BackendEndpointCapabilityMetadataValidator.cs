using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class BackendEndpointCapabilityMetadataValidator
{
    public const int MaxSafeTextLength = 360;

    private static readonly string[] UnsafeEndpointMetadataMarkers =
    [
        "endpointaccessgranted = true",
        "routeaccessgranted = true",
        "caninvokeendpoint = true",
        "cancallendpoint = true",
        "routeguardcreated = true",
        "cancreaterouteguard = true",
        "roleaccessgranted = true",
        "externalaccessgranted = true",
        "cansatisfypolicy = true",
        "policysatisfied = true",
        "approvalaccepted = true",
        "validationrefreshed = true",
        "sourcesafetyproven = true",
        "canrundiagnostic = true",
        "canretry = true",
        "canrollback = true",
        "canrecover = true",
        "canmutate = true",
        "canapplypatch = true",
        "cancommit = true",
        "canpush = true",
        "cancreatepullrequest = true",
        "canreadyforreview = true",
        "canmerge = true",
        "canrelease = true",
        "candeploy = true",
        "cancontinueworkflow = true",
        "canbypassredaction = true",
        "canviewsecrets = true",
        "canviewcredentials = true",
        "canviewrawpayload = true",
        "canviewprivatereasoning = true",
        "endpoint metadata grants access",
        "endpoint capability grants permission",
        "route template can be called",
        "get endpoint is safe to invoke",
        "post endpoint is allowed",
        "capability metadata satisfies policy",
        "capability metadata approves work",
        "capability metadata refreshes validation",
        "capability metadata proves source safety",
        "capability metadata can mutate",
        "capability metadata continues workflow",
        "capability metadata releases deployment",
        "capability metadata bypasses redaction",
        "capability metadata reveals secrets",
        "access granted",
        "permission granted",
        "authorized endpoint",
        "authorized route",
        "approval granted",
        "policy satisfied",
        "validation refreshed",
        "source safety proven",
        "diagnostic executed",
        "retry allowed",
        "rollback allowed",
        "recovery allowed",
        "mutation allowed",
        "workflow continued",
        "merge allowed",
        "release allowed",
        "deploy allowed",
        "redaction bypassed",
        "secret visible",
        "credential visible",
        "raw payload visible",
        "private reasoning visible",
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

    private static readonly string[] UnsafeRouteTemplateMarkers =
    [
        "http://",
        "https://",
        "localhost",
        "127.0.0.1",
        "prod.",
        "production",
        "tenantid=",
        "userid=",
        string.Concat("bear", "er "),
        string.Concat("to", "ken", "="),
        string.Concat("sec", "ret", "="),
        string.Concat("pass", "word", "="),
        string.Concat("private ", "key")
    ];

    public static BackendEndpointCapabilityValidationResult ValidateRequest(
        BackendEndpointCapabilityMetadataRequest? request)
    {
        if (request is null)
        {
            return Result(["BackendEndpointCapabilityMetadataRequestRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRequiredSafeTextIssue(request.CorrelationId, "CorrelationId", issues, unsafeRefs);
        AddEndpointKeyIssue(request.RequestedEndpointKey, issues, unsafeRefs);
        AddDefinedEnumIssue(request.RequestedIntent, "RequestedIntentInvalid", issues);
        AddOptionalSafeTextIssue(request.EndpointMetadataEvidenceRef, "EndpointMetadataEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.RoleCatalogEvidenceRef, "RoleCatalogEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.VisibilityMatrixEvidenceRef, "VisibilityMatrixEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalPolicyEvidenceRef, "OptionalPolicyEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalRedactionEvidenceRef, "OptionalRedactionEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalTenantBoundaryEvidenceRef, "OptionalTenantBoundaryEvidenceRef", issues, unsafeRefs);

        return Result(issues, unsafeRefs);
    }

    public static BackendEndpointCapabilityValidationResult ValidateCatalog(
        BackendEndpointCapabilityMetadataCatalog? catalog)
    {
        if (catalog is null)
        {
            return Result(["BackendEndpointCapabilityMetadataCatalogRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRequiredSafeTextIssue(catalog.CatalogId, "BackendEndpointCapabilityMetadataCatalogId", issues, unsafeRefs);
        AddRequiredSafeTextIssue(catalog.CatalogVersion, "BackendEndpointCapabilityMetadataCatalogVersion", issues, unsafeRefs);
        AddRequiredSafeTextIssue(catalog.BoundaryStatement, "BackendEndpointCapabilityMetadataCatalogBoundaryStatement", issues, unsafeRefs);
        AddBoundaryDenialIssue(catalog.BoundaryStatement, "BackendEndpointCapabilityMetadataCatalogBoundaryStatementMustDenyEndpointAuthority", issues);

        if (catalog.Entries is null || catalog.Entries.Count == 0)
        {
            issues.Add("BackendEndpointCapabilityMetadataCatalogEntriesRequired");
            return Result(issues, unsafeRefs);
        }

        foreach (var entry in catalog.Entries)
        {
            var entryResult = ValidateEntry(entry);
            issues.AddRange(entryResult.Issues.Select(issue => $"Entry:{entry?.EndpointKey ?? "unknown"}:{issue}"));
            unsafeRefs.AddRange(entryResult.UnsafeRefs);
        }

        var duplicateKeys = catalog.Entries
            .Where(static entry => entry is not null)
            .GroupBy(static entry => entry.EndpointKey, StringComparer.OrdinalIgnoreCase)
            .Where(static group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(static group => group.Key);

        foreach (var duplicate in duplicateKeys)
        {
            issues.Add($"BackendEndpointCapabilityMetadataDuplicateEndpointKey:{duplicate}");
        }

        return Result(issues, unsafeRefs);
    }

    public static BackendEndpointCapabilityValidationResult ValidateEntry(
        BackendEndpointCapabilityMetadataEntry? entry)
    {
        if (entry is null)
        {
            return Result(["BackendEndpointCapabilityMetadataEntryRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddEndpointKeyIssue(entry.EndpointKey, issues, unsafeRefs);
        AddRequiredSafeTextIssue(entry.DisplayName, "DisplayName", issues, unsafeRefs);
        AddRouteTemplateIssue(entry.RouteTemplate, issues, unsafeRefs);
        AddKnownEnumIssue(entry.HttpMethod, BackendEndpointHttpMethodKind.Unknown, "HttpMethodUnknown", issues);
        AddKnownEnumIssue(entry.CapabilityKind, BackendEndpointCapabilityKind.Unknown, "CapabilityKindUnknown", issues);
        AddKnownEnumIssue(entry.VisibilitySurface, RoleVisibilitySurface.Unknown, "VisibilitySurfaceUnknown", issues);
        AddKnownEnumIssue(entry.VisibilityMaterialKind, RoleVisibilityMaterialKind.Unknown, "VisibilityMaterialKindUnknown", issues);
        AddKnownEnumIssue(entry.SensitivityKind, BackendEndpointSensitivityKind.Unknown, "SensitivityKindUnknown", issues);
        AddRequiredSafeTextIssue(entry.OwningSubsystem, "OwningSubsystem", issues, unsafeRefs);
        AddRequiredSafeTextIssue(entry.BoundaryStatement, "BoundaryStatement", issues, unsafeRefs);
        AddBoundaryDenialIssue(entry.BoundaryStatement, "BoundaryStatementMustDenyEndpointAuthority", issues);
        AddRequiredEvidenceRefsIssues(entry.RequiredEvidenceRefs, issues, unsafeRefs);

        if (!entry.RequiresSeparateRoleAssignment)
        {
            issues.Add("RequiresSeparateRoleAssignmentRequired");
        }

        if (!entry.RequiresSeparateVisibilityDecision)
        {
            issues.Add("RequiresSeparateVisibilityDecisionRequired");
        }

        if (!entry.RequiresSeparateAccessDecision)
        {
            issues.Add("RequiresSeparateAccessDecisionRequired");
        }

        if (!entry.RequiresSeparatePolicyDecision)
        {
            issues.Add("RequiresSeparatePolicyDecisionRequired");
        }

        if (!entry.RequiresSeparateRedactionDecision)
        {
            issues.Add("RequiresSeparateRedactionDecisionRequired");
        }

        if (!entry.RequiresSeparateApprovalDecision)
        {
            issues.Add("RequiresSeparateApprovalDecisionRequired");
        }

        if (!entry.RequiresSeparateExecutionAuthority)
        {
            issues.Add("RequiresSeparateExecutionAuthorityRequired");
        }

        if (!entry.RequiresSeparateMutationAuthority)
        {
            issues.Add("RequiresSeparateMutationAuthorityRequired");
        }

        if (!entry.RequiresSeparateWorkflowAuthority)
        {
            issues.Add("RequiresSeparateWorkflowAuthorityRequired");
        }

        if (RequiresTenantBoundaryEvidence(entry) && !entry.RequiresTenantBoundaryDecision)
        {
            issues.Add("RequiresTenantBoundaryDecisionRequired");
        }

        return Result(issues, unsafeRefs);
    }

    public static bool ContainsUnsafeEndpointMetadataText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return UnsafeEndpointMetadataMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsMetadataIntent(BackendEndpointCapabilityIntent intent) =>
        intent is BackendEndpointCapabilityIntent.InspectMetadata or
            BackendEndpointCapabilityIntent.ListMetadata or
            BackendEndpointCapabilityIntent.SummariseMetadata;

    public static BackendEndpointCapabilityClassification ClassifyIntentBlock(
        BackendEndpointCapabilityIntent intent) =>
        intent switch
        {
            BackendEndpointCapabilityIntent.AuthorizeRouteAccess or
            BackendEndpointCapabilityIntent.GrantRoleAccess or
            BackendEndpointCapabilityIntent.GrantExternalAccess => BackendEndpointCapabilityClassification.BlockedByAccessIntent,
            BackendEndpointCapabilityIntent.InvokeEndpoint or
            BackendEndpointCapabilityIntent.CreateRouteGuard => BackendEndpointCapabilityClassification.BlockedByInvocationIntent,
            BackendEndpointCapabilityIntent.SatisfyPolicy or
            BackendEndpointCapabilityIntent.RefreshValidation or
            BackendEndpointCapabilityIntent.ProveSourceSafety => BackendEndpointCapabilityClassification.BlockedByPolicyIntent,
            BackendEndpointCapabilityIntent.AcceptApproval => BackendEndpointCapabilityClassification.BlockedByApprovalIntent,
            BackendEndpointCapabilityIntent.MutateSource => BackendEndpointCapabilityClassification.BlockedByMutationIntent,
            BackendEndpointCapabilityIntent.ContinueWorkflow => BackendEndpointCapabilityClassification.BlockedByWorkflowIntent,
            BackendEndpointCapabilityIntent.Merge or
            BackendEndpointCapabilityIntent.Release or
            BackendEndpointCapabilityIntent.Deploy => BackendEndpointCapabilityClassification.BlockedByReleaseDeployIntent,
            BackendEndpointCapabilityIntent.BypassRedaction => BackendEndpointCapabilityClassification.BlockedByRedactionBypassIntent,
            BackendEndpointCapabilityIntent.DiscloseSecrets => BackendEndpointCapabilityClassification.BlockedBySecretDisclosureIntent,
            BackendEndpointCapabilityIntent.DiscloseRawPayload => BackendEndpointCapabilityClassification.BlockedByRawDisclosureIntent,
            BackendEndpointCapabilityIntent.DisclosePrivateReasoning => BackendEndpointCapabilityClassification.BlockedByPrivateReasoningDisclosureIntent,
            _ => BackendEndpointCapabilityClassification.BlockedByActionIntent
        };

    public static bool RequiresTenantBoundaryEvidence(BackendEndpointCapabilityMetadataEntry entry) =>
        entry.SensitivityKind is BackendEndpointSensitivityKind.TenantScopedMetadata or
            BackendEndpointSensitivityKind.ProjectScopedMetadata;

    public static bool RequiresPolicyEvidence(BackendEndpointCapabilityMetadataEntry entry) =>
        entry.SensitivityKind is BackendEndpointSensitivityKind.SensitiveSummary or
            BackendEndpointSensitivityKind.MutationMaterial or
            BackendEndpointSensitivityKind.ReleaseDeployMaterial;

    public static bool RequiresRedactionEvidence(BackendEndpointCapabilityMetadataEntry entry) =>
        entry.CapabilityKind is BackendEndpointCapabilityKind.RedactedSummary or
            BackendEndpointCapabilityKind.ApprovalPackageReadModel or
            BackendEndpointCapabilityKind.PolicyReviewReadModel or
            BackendEndpointCapabilityKind.ValidationReviewReadModel or
            BackendEndpointCapabilityKind.OperationDiagnosticReadModel or
            BackendEndpointCapabilityKind.ReleaseReadinessReadModel ||
        entry.SensitivityKind is BackendEndpointSensitivityKind.RedactedSummary or
            BackendEndpointSensitivityKind.SensitiveSummary;

    public static bool IsBlockedRawSecretOrPrivateCapability(BackendEndpointCapabilityMetadataEntry entry) =>
        entry.SensitivityKind is BackendEndpointSensitivityKind.RawPayload or
            BackendEndpointSensitivityKind.CredentialMaterial or
            BackendEndpointSensitivityKind.SecretMaterial or
            BackendEndpointSensitivityKind.PrivateReasoning;

    private static BackendEndpointCapabilityValidationResult Result(
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

    private static void AddEndpointKeyIssue(
        string? endpointKey,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(endpointKey))
        {
            issues.Add("EndpointKeyRequired");
            return;
        }

        if (ContainsUnsafeEndpointMetadataText(endpointKey))
        {
            issues.Add("EndpointKeyUnsafe");
            unsafeRefs.Add(endpointKey);
            return;
        }

        if (!EndpointKeyPattern().IsMatch(endpointKey))
        {
            issues.Add("EndpointKeyInvalid");
        }
    }

    private static void AddRouteTemplateIssue(
        string? routeTemplate,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(routeTemplate))
        {
            issues.Add("RouteTemplateRequired");
            return;
        }

        var text = routeTemplate.ToLowerInvariant();
        if (UnsafeRouteTemplateMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            ContainsUnsafeEndpointMetadataText(routeTemplate))
        {
            issues.Add("RouteTemplateUnsafe");
            unsafeRefs.Add(routeTemplate);
            return;
        }

        if (routeTemplate.Length > MaxSafeTextLength ||
            routeTemplate.Any(static ch => char.IsControl(ch)) ||
            !RouteTemplatePattern().IsMatch(routeTemplate))
        {
            issues.Add("RouteTemplateInvalid");
        }
    }

    private static void AddRequiredEvidenceRefsIssues(
        IReadOnlyList<string>? evidenceRefs,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (evidenceRefs is null || evidenceRefs.Count == 0)
        {
            issues.Add("RequiredEvidenceRefsRequired");
            return;
        }

        foreach (var evidenceRef in evidenceRefs)
        {
            AddRequiredSafeTextIssue(evidenceRef, "RequiredEvidenceRef", issues, unsafeRefs);
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
        if (ContainsUnsafeEndpointMetadataText(value))
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

    private static void AddBoundaryDenialIssue(
        string? boundaryStatement,
        string issue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(boundaryStatement))
        {
            return;
        }

        var deniesEndpointAuthority = boundaryStatement.Contains("not endpoint authority", StringComparison.OrdinalIgnoreCase) ||
            boundaryStatement.Contains("does not grant endpoint authority", StringComparison.OrdinalIgnoreCase);
        var deniesAccess = boundaryStatement.Contains("not route access", StringComparison.OrdinalIgnoreCase) ||
            boundaryStatement.Contains("does not grant access", StringComparison.OrdinalIgnoreCase) ||
            boundaryStatement.Contains("not access", StringComparison.OrdinalIgnoreCase);

        if (!deniesEndpointAuthority || !deniesAccess)
        {
            issues.Add(issue);
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9:._/-]{2,159}$", RegexOptions.CultureInvariant)]
    private static partial Regex EndpointKeyPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._:/@+,'()-]{1,359}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextPattern();

    [GeneratedRegex("^/[A-Za-z0-9/_{}:.-]{1,240}$", RegexOptions.CultureInvariant)]
    private static partial Regex RouteTemplatePattern();
}
