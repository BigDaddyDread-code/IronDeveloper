using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class ScreenContractMetadataValidator
{
    public const int MaxSafeTextLength = 360;

    private static readonly string[] UnsafeScreenContractMetadataMarkers =
    [
        "screen grants access",
        "screen grants permission",
        "screen permission granted",
        "screen access granted",
        "client permission decision",
        "client-side authority",
        "local authority state",
        "grants ui authority",
        "grants frontend authority",
        "caninvokeaction = true",
        "canmutate = true",
        "canapprove = true",
        "cansatisfypolicy = true",
        "cancontinueworkflow = true",
        "canbypassredaction = true",
        "candisplaysecrets = true",
        "candisplayrawpayload = true",
        "action allowed",
        "mutation allowed",
        "workflow continued",
        "approval granted",
        "policy satisfied",
        "validation refreshed",
        "source safety proven",
        "ready to execute",
        "ready to mutate",
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

    private static readonly string[] UnsafeRoutePatternMarkers =
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

    public static ScreenContractMetadataValidationResult ValidateCatalog(
        ScreenContractMetadataCatalog? catalog)
    {
        if (catalog is null)
        {
            return Result(["ScreenContractMetadataCatalogRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRequiredSafeTextIssue(catalog.CatalogId, "ScreenContractMetadataCatalogId", issues, unsafeRefs);
        AddRequiredSafeTextIssue(catalog.CatalogVersion, "ScreenContractMetadataCatalogVersion", issues, unsafeRefs);
        AddRequiredSafeTextIssue(catalog.BoundaryStatement, "ScreenContractMetadataCatalogBoundaryStatement", issues, unsafeRefs);
        AddBoundaryDenialIssue(catalog.BoundaryStatement, "ScreenContractMetadataCatalogBoundaryStatementMustDenyUiAuthority", issues);

        if (catalog.Entries is null || catalog.Entries.Count == 0)
        {
            issues.Add("ScreenContractMetadataCatalogEntriesRequired");
            return Result(issues, unsafeRefs);
        }

        foreach (var entry in catalog.Entries)
        {
            var entryResult = ValidateEntry(entry);
            issues.AddRange(entryResult.Issues.Select(issue => $"Entry:{entry?.ScreenKey ?? "unknown"}:{issue}"));
            unsafeRefs.AddRange(entryResult.UnsafeRefs);
        }

        var duplicateKeys = catalog.Entries
            .Where(static entry => entry is not null)
            .GroupBy(static entry => entry.ScreenKey, StringComparer.OrdinalIgnoreCase)
            .Where(static group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(static group => group.Key);

        foreach (var duplicate in duplicateKeys)
        {
            issues.Add($"ScreenContractMetadataDuplicateScreenKey:{duplicate}");
        }

        return Result(issues, unsafeRefs);
    }

    public static ScreenContractMetadataValidationResult ValidateEntry(
        ScreenContractMetadataEntry? entry)
    {
        if (entry is null)
        {
            return Result(["ScreenContractMetadataEntryRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddScreenKeyIssue(entry.ScreenKey, issues, unsafeRefs);
        AddRequiredSafeTextIssue(entry.DisplayName, "DisplayName", issues, unsafeRefs);
        AddRoutePatternIssue(entry.FrontendRoutePattern, issues, unsafeRefs);
        AddRequiredSafeTextIssue(entry.OwningSubsystem, "OwningSubsystem", issues, unsafeRefs);
        AddKnownEnumIssue(entry.ScreenKind, ScreenContractKind.Unknown, "ScreenKindUnknown", issues);
        AddKnownEnumIssue(entry.VisibilitySurface, RoleVisibilitySurface.Unknown, "VisibilitySurfaceUnknown", issues);
        AddKnownEnumIssue(entry.VisibilityMaterialKind, RoleVisibilityMaterialKind.Unknown, "VisibilityMaterialKindUnknown", issues);
        AddKnownEnumIssue(entry.SensitivityKind, ScreenContractSensitivityKind.Unknown, "SensitivityKindUnknown", issues);
        AddEndpointKeyIssue(entry.PrimaryEndpointKey, "PrimaryEndpointKey", issues, unsafeRefs);
        AddEndpointRefsIssue(entry.RelatedEndpointKeys, "RelatedEndpointKey", issues, unsafeRefs);
        AddRequiredEvidenceRefsIssues(entry.RequiredEvidenceRefs, issues, unsafeRefs);
        AddRequiredSafeTextIssue(entry.BoundaryStatement, "BoundaryStatement", issues, unsafeRefs);
        AddBoundaryDenialIssue(entry.BoundaryStatement, "BoundaryStatementMustDenyUiAuthority", issues);

        if (!entry.IsReadOnly)
        {
            issues.Add("ScreenContractMustBeReadOnly");
        }

        if (entry.IsActionScreen)
        {
            issues.Add("ScreenContractMustNotBeActionScreen");
        }

        if (entry.IsMutationScreen)
        {
            issues.Add("ScreenContractMustNotBeMutationScreen");
        }

        if (entry.IsAdminScreen)
        {
            issues.Add("ScreenContractMustNotBeAdminScreen");
        }

        if (entry.IsReleaseDeployScreen)
        {
            issues.Add("ScreenContractMustNotBeReleaseDeployScreen");
        }

        if (entry.AllowsLocalAuthorityState)
        {
            issues.Add("AllowsLocalAuthorityStateMustBeFalse");
        }

        if (entry.AllowsClientSidePermissionDecision)
        {
            issues.Add("AllowsClientSidePermissionDecisionMustBeFalse");
        }

        if (entry.AllowsActionInvocation)
        {
            issues.Add("AllowsActionInvocationMustBeFalse");
        }

        if (entry.AllowsMutation)
        {
            issues.Add("AllowsMutationMustBeFalse");
        }

        if (entry.AllowsWorkflowContinuation)
        {
            issues.Add("AllowsWorkflowContinuationMustBeFalse");
        }

        if (entry.AllowsApproval)
        {
            issues.Add("AllowsApprovalMustBeFalse");
        }

        if (entry.AllowsPolicySatisfaction)
        {
            issues.Add("AllowsPolicySatisfactionMustBeFalse");
        }

        if (entry.AllowsRedactionBypass)
        {
            issues.Add("AllowsRedactionBypassMustBeFalse");
        }

        if (entry.AllowsRawPayloadDisplay)
        {
            issues.Add("AllowsRawPayloadDisplayMustBeFalse");
        }

        if (entry.AllowsSecretDisplay)
        {
            issues.Add("AllowsSecretDisplayMustBeFalse");
        }

        if (entry.AllowsPrivateReasoningDisplay)
        {
            issues.Add("AllowsPrivateReasoningDisplayMustBeFalse");
        }

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

        if (!entry.RequiresTenantBoundaryDecision)
        {
            issues.Add("RequiresTenantBoundaryDecisionRequired");
        }

        if (!entry.RequiresSeparateActionAuthority)
        {
            issues.Add("RequiresSeparateActionAuthorityRequired");
        }

        if (!entry.RequiresSeparateMutationAuthority)
        {
            issues.Add("RequiresSeparateMutationAuthorityRequired");
        }

        if (!entry.RequiresSeparateWorkflowAuthority)
        {
            issues.Add("RequiresSeparateWorkflowAuthorityRequired");
        }

        if (IsBlockedRawSecretOrPrivateScreen(entry))
        {
            issues.Add("ScreenContractRawSecretOrPrivateMaterialBlocked");
        }

        return Result(issues, unsafeRefs);
    }

    public static bool ContainsUnsafeScreenMetadataText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return UnsafeScreenContractMetadataMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsSafeScreenKey(string? screenKey) =>
        !string.IsNullOrWhiteSpace(screenKey) &&
        screenKey.Length <= MaxSafeTextLength &&
        !ContainsUnsafeScreenMetadataText(screenKey) &&
        ScreenKeyPattern().IsMatch(screenKey);

    public static bool IsBlockedRawSecretOrPrivateScreen(ScreenContractMetadataEntry entry) =>
        entry.SensitivityKind is ScreenContractSensitivityKind.RawPayload or
            ScreenContractSensitivityKind.CredentialMaterial or
            ScreenContractSensitivityKind.SecretMaterial or
            ScreenContractSensitivityKind.PrivateReasoning;

    private static ScreenContractMetadataValidationResult Result(
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

    private static void AddScreenKeyIssue(
        string? screenKey,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(screenKey))
        {
            issues.Add("ScreenKeyRequired");
            return;
        }

        if (ContainsUnsafeScreenMetadataText(screenKey))
        {
            issues.Add("ScreenKeyUnsafe");
            unsafeRefs.Add(screenKey);
            return;
        }

        if (!ScreenKeyPattern().IsMatch(screenKey))
        {
            issues.Add("ScreenKeyInvalid");
        }
    }

    private static void AddEndpointKeyIssue(
        string? endpointKey,
        string issuePrefix,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(endpointKey))
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        if (ContainsUnsafeScreenMetadataText(endpointKey))
        {
            issues.Add($"{issuePrefix}Unsafe");
            unsafeRefs.Add(endpointKey);
            return;
        }

        if (!EndpointKeyPattern().IsMatch(endpointKey))
        {
            issues.Add($"{issuePrefix}Invalid");
        }
    }

    private static void AddEndpointRefsIssue(
        IReadOnlyList<string>? endpointRefs,
        string issuePrefix,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (endpointRefs is null)
        {
            issues.Add($"{issuePrefix}ListRequired");
            return;
        }

        foreach (var endpointRef in endpointRefs)
        {
            AddEndpointKeyIssue(endpointRef, issuePrefix, issues, unsafeRefs);
        }
    }

    private static void AddRoutePatternIssue(
        string? routePattern,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(routePattern))
        {
            issues.Add("FrontendRoutePatternRequired");
            return;
        }

        var text = routePattern.ToLowerInvariant();
        if (UnsafeRoutePatternMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            ContainsUnsafeScreenMetadataText(routePattern))
        {
            issues.Add("FrontendRoutePatternUnsafe");
            unsafeRefs.Add(routePattern);
            return;
        }

        if (routePattern.Length > MaxSafeTextLength ||
            routePattern.Any(static ch => char.IsControl(ch)) ||
            !RoutePattern().IsMatch(routePattern))
        {
            issues.Add("FrontendRoutePatternInvalid");
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

        if (ContainsUnsafeScreenMetadataText(value))
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
        string? value,
        string issue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var text = value.ToLowerInvariant();
        if (!text.Contains("not ui authority", StringComparison.Ordinal) &&
            !text.Contains("not screen permission", StringComparison.Ordinal))
        {
            issues.Add(issue);
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9:-]{2,120}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScreenKeyPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9:-]{2,140}$", RegexOptions.CultureInvariant)]
    private static partial Regex EndpointKeyPattern();

    [GeneratedRegex("^[a-zA-Z0-9 ._:/{}?&=,-]{1,360}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoutePattern();

    [GeneratedRegex("^[a-zA-Z0-9 ._:/{}?&=,;()'\\[\\]-]{1,360}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextPattern();
}
