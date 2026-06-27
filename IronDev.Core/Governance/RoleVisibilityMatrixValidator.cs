using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class RoleVisibilityMatrixValidator
{
    public const int MaxSafeTextLength = 360;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "role visibility matrix is descriptive only",
        "role visibility matrix does not assign users",
        "role visibility matrix does not grant access",
        "role visibility matrix does not grant permissions",
        "role visibility matrix does not bypass redaction",
        "role visibility matrix does not approve work",
        "role visibility matrix does not satisfy policy",
        "role visibility matrix does not authorize execution",
        "role visibility matrix does not authorize mutation",
        "role visibility matrix does not continue workflow",
        "visibility hints require separate role assignment evidence",
        "visibility hints require separate visibility decision evidence",
        "visibility hints require separate policy/redaction enforcement"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "role visibility matrix is not identity",
        "role visibility matrix is not authorization",
        "role visibility matrix is not access control",
        "role visibility matrix is not approval",
        "role visibility matrix is not policy satisfaction",
        "role visibility matrix is not execution authority",
        "role visibility matrix is not mutation authority",
        "role visibility matrix is not merge authority",
        "role visibility matrix is not release authority",
        "role visibility matrix is not deployment authority",
        "role visibility matrix is not workflow continuation authority",
        "role visibility matrix is not redaction bypass",
        "role visibility matrix is not secret disclosure authority",
        "role visibility matrix is not private reasoning disclosure authority"
    ];

    private static readonly string[] UnsafeVisibilityTextMarkers =
    [
        "can view",
        "can read",
        "can access",
        "access granted",
        "permission granted",
        "authority granted",
        "approval granted",
        "policy satisfied",
        "validation satisfied",
        "safe to disclose",
        "safe to expose",
        "authorized to view",
        "authorized to read",
        "authorized to disclose",
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

    public static RoleVisibilityMatrixValidationResult ValidateEntry(
        GovernanceRoleCatalog? catalog,
        RoleVisibilityMatrixEntry? entry)
    {
        if (catalog is null)
        {
            return Result(["RoleVisibilityCatalogRequired"], [], [], [], [], []);
        }

        if (!RoleCatalogValidator.ValidateCatalog(catalog).IsValid)
        {
            return Result(["RoleVisibilityCatalogInvalid"], [], [], [], [], []);
        }

        if (entry is null)
        {
            return Result(["RoleVisibilityMatrixEntryRequired"], [], [], [], [], []);
        }

        var issues = new List<string>();
        var invalidRoleIds = new List<string>();
        var unknownRoleIds = new List<string>();
        var unsafeEntryRefs = new List<string>();
        var overexposedMaterialRefs = new List<string>();

        AddRoleBindingIssues(catalog, entry, issues, invalidRoleIds, unknownRoleIds);
        AddCatalogVersionIssue(entry.CatalogVersion, catalog.CatalogVersion, "RoleVisibilityMatrixEntryCatalogVersion", issues);
        AddKnownEnumIssue(entry.RoleKind, GovernanceRoleKind.Unknown, "RoleVisibilityMatrixEntryRoleKindUnknown", issues);
        AddKnownEnumIssue(entry.RoleScopeKind, GovernanceRoleScopeKind.Unknown, "RoleVisibilityMatrixEntryRoleScopeKindUnknown", issues);
        AddKnownEnumIssue(entry.Surface, RoleVisibilitySurface.Unknown, "RoleVisibilityMatrixEntrySurfaceUnknown", issues);
        AddKnownEnumIssue(entry.MaterialKind, RoleVisibilityMaterialKind.Unknown, "RoleVisibilityMatrixEntryMaterialKindUnknown", issues);
        AddKnownEnumIssue(entry.SensitivityKind, RoleVisibilitySensitivityKind.Unknown, "RoleVisibilityMatrixEntrySensitivityKindUnknown", issues);
        AddKnownEnumIssue(entry.VisibilityLevel, RoleVisibilityLevel.Unknown, "RoleVisibilityMatrixEntryVisibilityLevelUnknown", issues);
        AddRequiredSafeTextIssue(entry.Reason, "RoleVisibilityMatrixEntryReason", entry.RoleId, issues, unsafeEntryRefs);
        AddRequiredSafeTextIssue(entry.BoundaryStatement, "RoleVisibilityMatrixEntryBoundaryStatement", entry.RoleId, issues, unsafeEntryRefs);
        AddBoundaryDenialIssue(entry.BoundaryStatement, "RoleVisibilityMatrixEntryBoundaryStatementMustDenyAccessAndAuthority", issues);

        if (!entry.RequiresSeparateRoleAssignment)
        {
            issues.Add("RoleVisibilityMatrixEntrySeparateRoleAssignmentRequired");
        }

        if (!entry.RequiresSeparateVisibilityDecision)
        {
            issues.Add("RoleVisibilityMatrixEntrySeparateVisibilityDecisionRequired");
        }

        var sensitive = IsSensitive(entry.MaterialKind, entry.SensitivityKind);
        if (sensitive && !entry.RequiresSeparatePolicyDecision)
        {
            issues.Add("RoleVisibilityMatrixEntrySeparatePolicyDecisionRequired");
        }

        if (RequiresRedaction(entry.MaterialKind, entry.SensitivityKind, entry.VisibilityLevel) && !entry.RequiresRedaction)
        {
            issues.Add("RoleVisibilityMatrixEntryRedactionRequired");
        }

        if (IsSecretOrRawMaterial(entry.MaterialKind, entry.SensitivityKind) &&
            entry.VisibilityLevel != RoleVisibilityLevel.NotVisible)
        {
            var materialRef = MaterialRef(entry);
            issues.Add($"RoleVisibilityMatrixEntryOverexposedMaterial:{materialRef}");
            overexposedMaterialRefs.Add(materialRef);
        }

        return Result(issues, invalidRoleIds, unknownRoleIds, [], unsafeEntryRefs, overexposedMaterialRefs);
    }

    public static RoleVisibilityMatrixValidationResult ValidateMatrix(
        GovernanceRoleCatalog? catalog,
        RoleVisibilityMatrix? matrix)
    {
        if (catalog is null)
        {
            return Result(["RoleVisibilityCatalogRequired"], [], [], [], [], []);
        }

        var catalogValidation = RoleCatalogValidator.ValidateCatalog(catalog);
        if (!catalogValidation.IsValid)
        {
            return Result(["RoleVisibilityCatalogInvalid"], catalogValidation.InvalidRoleIds, [], [], [], []);
        }

        if (matrix is null)
        {
            return Result(["RoleVisibilityMatrixRequired"], [], [], [], [], []);
        }

        var issues = new List<string>();
        var invalidRoleIds = new List<string>();
        var unknownRoleIds = new List<string>();
        var duplicateKeys = new List<string>();
        var unsafeEntryRefs = new List<string>();
        var overexposedMaterialRefs = new List<string>();

        AddRequiredSafeTextIssue(matrix.MatrixId, "RoleVisibilityMatrixId", matrix.MatrixId, issues, unsafeEntryRefs);
        if (!string.IsNullOrWhiteSpace(matrix.MatrixId) && !MatrixIdPattern().IsMatch(matrix.MatrixId))
        {
            issues.Add("RoleVisibilityMatrixIdInvalid");
        }

        AddRequiredSafeTextIssue(matrix.CatalogId, "RoleVisibilityMatrixCatalogId", matrix.MatrixId, issues, unsafeEntryRefs);
        if (!string.IsNullOrWhiteSpace(matrix.CatalogId) &&
            !string.Equals(matrix.CatalogId, catalog.CatalogId, StringComparison.Ordinal))
        {
            issues.Add("RoleVisibilityMatrixCatalogIdMismatch");
        }

        AddCatalogVersionIssue(matrix.CatalogVersion, catalog.CatalogVersion, "RoleVisibilityMatrixCatalogVersion", issues);
        AddRequiredSafeTextIssue(matrix.CreatedReason, "RoleVisibilityMatrixCreatedReason", matrix.MatrixId, issues, unsafeEntryRefs);
        AddRequiredSafeTextIssue(matrix.BoundaryStatement, "RoleVisibilityMatrixBoundaryStatement", matrix.MatrixId, issues, unsafeEntryRefs);
        AddBoundaryDenialIssue(matrix.BoundaryStatement, "RoleVisibilityMatrixBoundaryStatementMustDenyAccessAndAuthority", issues);

        if (matrix.Entries is null || matrix.Entries.Count == 0)
        {
            issues.Add("RoleVisibilityMatrixEntriesRequired");
            return Result(issues, invalidRoleIds, unknownRoleIds, duplicateKeys, unsafeEntryRefs, overexposedMaterialRefs);
        }

        foreach (var entry in matrix.Entries)
        {
            var entryResult = ValidateEntry(catalog, entry);
            issues.AddRange(entryResult.Issues.Select(issue => $"Entry:{entry?.RoleId ?? "unknown"}:{issue}"));
            invalidRoleIds.AddRange(entryResult.InvalidRoleIds);
            unknownRoleIds.AddRange(entryResult.UnknownRoleIds);
            unsafeEntryRefs.AddRange(entryResult.UnsafeEntryRefs);
            overexposedMaterialRefs.AddRange(entryResult.OverexposedMaterialRefs);
        }

        duplicateKeys.AddRange(matrix.Entries
            .Where(static entry => entry is not null)
            .GroupBy(static entry => MatrixKey(entry), StringComparer.OrdinalIgnoreCase)
            .Where(static group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(static group => group.Key));

        foreach (var duplicate in duplicateKeys)
        {
            issues.Add($"RoleVisibilityMatrixDuplicateKey:{duplicate}");
        }

        var matrixRoleIds = matrix.Entries
            .Where(static entry => entry is not null)
            .Select(static entry => entry.RoleId)
            .Where(static roleId => !string.IsNullOrWhiteSpace(roleId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var role in catalog.Entries)
        {
            if (!matrixRoleIds.Contains(role.RoleId))
            {
                issues.Add($"RoleVisibilityMatrixMissingDefaultRole:{role.RoleId}");
            }
        }

        return Result(issues, invalidRoleIds, unknownRoleIds, duplicateKeys, unsafeEntryRefs, overexposedMaterialRefs);
    }

    public static bool ContainsUnsafeVisibilityText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return UnsafeVisibilityTextMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsSecretOrRawMaterial(
        RoleVisibilityMaterialKind materialKind,
        RoleVisibilitySensitivityKind sensitivityKind) =>
        materialKind is RoleVisibilityMaterialKind.RawPayload or
            RoleVisibilityMaterialKind.CredentialMaterial or
            RoleVisibilityMaterialKind.PrivateReasoning ||
        sensitivityKind is RoleVisibilitySensitivityKind.SecretLike or
            RoleVisibilitySensitivityKind.CredentialLike or
            RoleVisibilitySensitivityKind.PrivateReasoning or
            RoleVisibilitySensitivityKind.RawPayload;

    public static bool RequiresRedaction(
        RoleVisibilityMaterialKind materialKind,
        RoleVisibilitySensitivityKind sensitivityKind,
        RoleVisibilityLevel level) =>
        level == RoleVisibilityLevel.RedactedDetails ||
        materialKind is RoleVisibilityMaterialKind.SensitiveFindingSummary or
            RoleVisibilityMaterialKind.SecretScanSummary ||
        sensitivityKind is RoleVisibilitySensitivityKind.SecuritySensitive or
            RoleVisibilitySensitivityKind.SecretLike or
            RoleVisibilitySensitivityKind.CredentialLike or
            RoleVisibilitySensitivityKind.PrivateReasoning or
            RoleVisibilitySensitivityKind.RawPayload;

    private static RoleVisibilityMatrixValidationResult Result(
        IReadOnlyList<string> issues,
        IReadOnlyList<string> invalidRoleIds,
        IReadOnlyList<string> unknownRoleIds,
        IReadOnlyList<string> duplicateKeys,
        IReadOnlyList<string> unsafeEntryRefs,
        IReadOnlyList<string> overexposedMaterialRefs)
    {
        var normalizedIssues = issues
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static issue => issue, StringComparer.Ordinal)
            .ToArray();

        return new RoleVisibilityMatrixValidationResult
        {
            IsValid = normalizedIssues.Length == 0,
            Issues = normalizedIssues,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications,
            InvalidRoleIds = Normalize(invalidRoleIds),
            UnknownRoleIds = Normalize(unknownRoleIds),
            DuplicateMatrixKeys = Normalize(duplicateKeys),
            UnsafeEntryRefs = Normalize(unsafeEntryRefs),
            OverexposedMaterialRefs = Normalize(overexposedMaterialRefs)
        };
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> items) =>
        items
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void AddRoleBindingIssues(
        GovernanceRoleCatalog catalog,
        RoleVisibilityMatrixEntry entry,
        ICollection<string> issues,
        ICollection<string> invalidRoleIds,
        ICollection<string> unknownRoleIds)
    {
        if (string.IsNullOrWhiteSpace(entry.RoleId))
        {
            issues.Add("RoleVisibilityMatrixEntryRoleIdRequired");
            return;
        }

        if (!RoleCatalogValidator.IsSafeRoleId(entry.RoleId))
        {
            issues.Add("RoleVisibilityMatrixEntryRoleIdInvalid");
            invalidRoleIds.Add(entry.RoleId);
            return;
        }

        var role = catalog.Entries.FirstOrDefault(catalogEntry =>
            string.Equals(catalogEntry.RoleId, entry.RoleId, StringComparison.OrdinalIgnoreCase));

        if (role is null)
        {
            issues.Add($"RoleVisibilityMatrixEntryUnknownRoleId:{entry.RoleId}");
            unknownRoleIds.Add(entry.RoleId);
            return;
        }

        if (role.RoleKind != entry.RoleKind)
        {
            issues.Add("RoleVisibilityMatrixEntryRoleKindMismatch");
        }

        if (role.ScopeKind != entry.RoleScopeKind)
        {
            issues.Add("RoleVisibilityMatrixEntryRoleScopeKindMismatch");
        }
    }

    private static void AddCatalogVersionIssue(
        string? catalogVersion,
        string expectedCatalogVersion,
        string issuePrefix,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(catalogVersion))
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        if (!string.Equals(catalogVersion, expectedCatalogVersion, StringComparison.Ordinal))
        {
            issues.Add($"{issuePrefix}Mismatch");
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
        string? entryRef,
        ICollection<string> issues,
        ICollection<string> unsafeEntryRefs)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        if (ContainsUnsafeVisibilityText(value))
        {
            issues.Add($"{issuePrefix}Unsafe");
            if (!string.IsNullOrWhiteSpace(entryRef))
            {
                unsafeEntryRefs.Add(entryRef);
            }

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

        var deniesAccess = boundaryStatement.Contains("does not grant access", StringComparison.OrdinalIgnoreCase) ||
            boundaryStatement.Contains("not access", StringComparison.OrdinalIgnoreCase);
        var deniesAuthority = boundaryStatement.Contains("does not grant authority", StringComparison.OrdinalIgnoreCase) ||
            boundaryStatement.Contains("not authority", StringComparison.OrdinalIgnoreCase);

        if (!deniesAccess || !deniesAuthority)
        {
            issues.Add(issue);
        }
    }

    private static bool IsSensitive(
        RoleVisibilityMaterialKind materialKind,
        RoleVisibilitySensitivityKind sensitivityKind) =>
        RequiresRedaction(materialKind, sensitivityKind, RoleVisibilityLevel.NotVisible) ||
        sensitivityKind is RoleVisibilitySensitivityKind.Confidential;

    private static string MaterialRef(RoleVisibilityMatrixEntry entry) =>
        $"{entry.RoleId}:{entry.Surface}:{entry.MaterialKind}";

    private static string MatrixKey(RoleVisibilityMatrixEntry entry) =>
        $"{entry.RoleId}|{entry.Surface}|{entry.MaterialKind}";

    [GeneratedRegex("^role-visibility-matrix:f02$", RegexOptions.CultureInvariant)]
    private static partial Regex MatrixIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._:/@+,'()-]{1,359}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextPattern();
}
