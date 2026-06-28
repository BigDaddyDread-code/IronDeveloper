using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class ForbiddenActionCatalogValidator
{
    public const int MaxSafeTextLength = 360;

    private static readonly string[] UnsafeForbiddenActionMarkers =
    [
        "isallowed = true",
        "allowed = true",
        "permissiongranted = true",
        "actionallowed = true",
        "canexecute = true",
        "caninvoke = true",
        "canapprove = true",
        "cansatisfypolicy = true",
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
        "notforbiddenmeansallowed = true",
        "allowbyomission = true",
        "not forbidden means allowed",
        "forbidden catalog grants permission",
        "role may perform action unless forbidden",
        "absence from forbidden list permits action",
        "forbidden action catalog authorizes execution",
        "role grants action",
        "role grants endpoint access",
        "role grants ui access",
        "role grants mutation",
        "role grants release",
        "permission granted",
        "authorized",
        "may proceed",
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

    public static ForbiddenActionCatalogValidationResult ValidateRequest(
        ForbiddenActionLookupRequest? request)
    {
        if (request is null)
        {
            return Result(["ForbiddenActionLookupRequestRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRequiredSafeTextIssue(request.CorrelationId, "CorrelationId", issues, unsafeRefs);
        AddRoleIdIssue(request.RequestedRoleId, issues, unsafeRefs);
        AddDefinedEnumIssue(request.RequestedActionKind, "RequestedActionKindInvalid", issues);
        AddDefinedEnumIssue(request.AuthoritySourceKind, "AuthoritySourceKindInvalid", issues);
        AddOptionalSafeTextIssue(request.RoleCatalogEvidenceRef, "RoleCatalogEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.ForbiddenActionCatalogEvidenceRef, "ForbiddenActionCatalogEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalPolicyEvidenceRef, "OptionalPolicyEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalApprovalEvidenceRef, "OptionalApprovalEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalExecutionAuthorityRef, "OptionalExecutionAuthorityRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalMutationAuthorityRef, "OptionalMutationAuthorityRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalWorkflowAuthorityRef, "OptionalWorkflowAuthorityRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalReleaseAuthorityRef, "OptionalReleaseAuthorityRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalRedactionDecisionRef, "OptionalRedactionDecisionRef", issues, unsafeRefs);

        return Result(issues, unsafeRefs);
    }

    public static ForbiddenActionCatalogValidationResult ValidateCatalog(
        GovernanceRoleCatalog? roleCatalog,
        ForbiddenActionCatalog? catalog)
    {
        if (catalog is null)
        {
            return Result(["ForbiddenActionCatalogRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();
        var knownRoles = RoleCatalogValidator.ValidateCatalog(roleCatalog).IsValid
            ? roleCatalog!.Entries.ToDictionary(entry => entry.RoleId, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, GovernanceRoleCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        AddRequiredSafeTextIssue(catalog.CatalogId, "ForbiddenActionCatalogId", issues, unsafeRefs);
        AddRequiredSafeTextIssue(catalog.CatalogVersion, "ForbiddenActionCatalogVersion", issues, unsafeRefs);
        AddRequiredSafeTextIssue(catalog.BoundaryStatement, "ForbiddenActionCatalogBoundaryStatement", issues, unsafeRefs);
        AddBoundaryDenialIssue(catalog.BoundaryStatement, "ForbiddenActionCatalogBoundaryStatementMustDenyAuthorization", issues);

        if (knownRoles.Count == 0)
        {
            issues.Add("RoleCatalogInvalid");
        }

        if (catalog.Entries is null || catalog.Entries.Count == 0)
        {
            issues.Add("ForbiddenActionCatalogEntriesRequired");
            return Result(issues, unsafeRefs);
        }

        foreach (var entry in catalog.Entries)
        {
            var entryResult = ValidateEntry(entry);
            issues.AddRange(entryResult.Issues.Select(issue => $"Entry:{entry?.RoleId ?? "unknown"}:{entry?.RoleForbiddenActionKind.ToString() ?? "unknown"}:{issue}"));
            unsafeRefs.AddRange(entryResult.UnsafeRefs);

            if (entry is null)
            {
                continue;
            }

            if (!knownRoles.TryGetValue(entry.RoleId, out var role))
            {
                issues.Add($"ForbiddenActionCatalogUnknownRole:{entry.RoleId}");
                continue;
            }

            if (entry.RoleKind != role.RoleKind)
            {
                issues.Add($"ForbiddenActionCatalogRoleKindMismatch:{entry.RoleId}");
            }
        }

        var duplicateKeys = catalog.Entries
            .Where(static entry => entry is not null)
            .GroupBy(static entry => $"{entry.RoleId}|{entry.RoleForbiddenActionKind}", StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key);

        foreach (var duplicate in duplicateKeys)
        {
            issues.Add($"ForbiddenActionCatalogDuplicateRoleAction:{duplicate}");
        }

        var missingRoles = knownRoles.Keys
            .Where(roleId => !catalog.Entries.Any(entry => string.Equals(entry.RoleId, roleId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        foreach (var missingRole in missingRoles)
        {
            issues.Add($"ForbiddenActionCatalogMissingRole:{missingRole}");
        }

        return Result(issues, unsafeRefs);
    }

    public static ForbiddenActionCatalogValidationResult ValidateEntry(
        ForbiddenActionCatalogEntry? entry)
    {
        if (entry is null)
        {
            return Result(["ForbiddenActionCatalogEntryRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRoleIdIssue(entry.RoleId, issues, unsafeRefs);
        AddKnownEnumIssue(entry.RoleKind, GovernanceRoleKind.Unknown, "RoleKindUnknown", issues);
        AddRequiredSafeTextIssue(entry.RoleDisplayName, "RoleDisplayName", issues, unsafeRefs);
        AddKnownEnumIssue(entry.RoleForbiddenActionKind, RoleForbiddenActionKind.Unknown, "ForbiddenActionKindUnknown", issues);
        AddKnownEnumIssue(entry.ReasonKind, ForbiddenActionReasonKind.Unknown, "ReasonKindUnknown", issues);
        AddRequiredSafeTextIssue(entry.BoundaryStatement, "BoundaryStatement", issues, unsafeRefs);
        AddBoundaryDenialIssue(entry.BoundaryStatement, "BoundaryStatementMustDenyAuthorization", issues);
        AddRequiredEvidenceRefsIssues(entry.RequiredSeparateEvidenceRefs, issues, unsafeRefs);

        if (!entry.AppliesWhenAuthoritySourceIsRoleEvidence)
        {
            issues.Add("AppliesWhenAuthoritySourceIsRoleEvidenceRequired");
        }

        if (!entry.IsForbidden)
        {
            issues.Add("IsForbiddenRequired");
        }

        if (entry.IsAllowed)
        {
            issues.Add("IsAllowedMustBeFalse");
        }

        if (entry.GrantsAuthority)
        {
            issues.Add("GrantsAuthorityMustBeFalse");
        }

        if (entry.GrantsPermission)
        {
            issues.Add("GrantsPermissionMustBeFalse");
        }

        if (entry.SatisfiesPolicy)
        {
            issues.Add("SatisfiesPolicyMustBeFalse");
        }

        if (entry.AllowsExecution)
        {
            issues.Add("AllowsExecutionMustBeFalse");
        }

        if (entry.AllowsMutation)
        {
            issues.Add("AllowsMutationMustBeFalse");
        }

        if (entry.AllowsWorkflowContinuation)
        {
            issues.Add("AllowsWorkflowContinuationMustBeFalse");
        }

        if (entry.AllowsRelease)
        {
            issues.Add("AllowsReleaseMustBeFalse");
        }

        if (entry.AllowsDeployment)
        {
            issues.Add("AllowsDeploymentMustBeFalse");
        }

        if (entry.BypassesRedaction)
        {
            issues.Add("BypassesRedactionMustBeFalse");
        }

        if (entry.DisclosesSecrets)
        {
            issues.Add("DisclosesSecretsMustBeFalse");
        }

        if (entry.DisclosesCredentials)
        {
            issues.Add("DisclosesCredentialsMustBeFalse");
        }

        if (entry.DisclosesRawPayload)
        {
            issues.Add("DisclosesRawPayloadMustBeFalse");
        }

        if (entry.DisclosesPrivateReasoning)
        {
            issues.Add("DisclosesPrivateReasoningMustBeFalse");
        }

        return Result(issues, unsafeRefs);
    }

    public static bool ContainsUnsafeForbiddenActionText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return UnsafeForbiddenActionMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsKnownAction(RoleForbiddenActionKind actionKind) =>
        actionKind != RoleForbiddenActionKind.Unknown && Enum.IsDefined(actionKind);

    public static bool IsKnownAuthoritySource(ForbiddenActionAuthoritySourceKind sourceKind) =>
        sourceKind != ForbiddenActionAuthoritySourceKind.Unknown && Enum.IsDefined(sourceKind);

    private static ForbiddenActionCatalogValidationResult Result(
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

    private static void AddRequiredEvidenceRefsIssues(
        IReadOnlyList<string>? evidenceRefs,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (evidenceRefs is null || evidenceRefs.Count == 0)
        {
            issues.Add("RequiredSeparateEvidenceRefsRequired");
            return;
        }

        foreach (var evidenceRef in evidenceRefs)
        {
            AddRequiredSafeTextIssue(evidenceRef, "RequiredSeparateEvidenceRef", issues, unsafeRefs);
        }
    }

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

        if (ContainsUnsafeForbiddenActionText(roleId))
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
        if (ContainsUnsafeForbiddenActionText(value))
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
        if (!text.Contains("not authorization", StringComparison.Ordinal) &&
            !text.Contains("not a permission", StringComparison.Ordinal) &&
            !text.Contains("not an allow list", StringComparison.Ordinal))
        {
            issues.Add(issue);
        }
    }

    [GeneratedRegex("^role:[a-z0-9][a-z0-9:-]{2,120}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleIdPattern();

    [GeneratedRegex("^[a-zA-Z0-9 ._:/{}?&=,;()'\\[\\]-]{1,360}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextPattern();
}
