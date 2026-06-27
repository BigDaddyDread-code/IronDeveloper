using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class RoleCatalogValidator
{
    public const int MaxSafeTextLength = 320;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "role catalog is descriptive only",
        "role catalog does not assign users",
        "role catalog does not grant permissions",
        "role catalog does not approve work",
        "role catalog does not satisfy policy",
        "role catalog does not authorize execution",
        "role catalog does not authorize mutation",
        "role catalog does not continue workflow",
        "role reference requires separate assignment evidence",
        "role reference requires separate approval profile evidence"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "role catalog is not identity",
        "role catalog is not authorization",
        "role catalog is not approval",
        "role catalog is not policy satisfaction",
        "role catalog is not execution authority",
        "role catalog is not mutation authority",
        "role catalog is not merge authority",
        "role catalog is not release authority",
        "role catalog is not deployment authority",
        "role catalog is not workflow continuation authority"
    ];

    private static readonly string[] UnsafeRoleTextMarkers =
    [
        "can approve",
        "can execute",
        "can mutate",
        "can merge",
        "can release",
        "can deploy",
        "permission granted",
        "authority granted",
        "approval granted",
        "policy satisfied",
        "validation satisfied",
        "safe to execute",
        "safe to mutate",
        "authorized to approve",
        "authorized to execute",
        "authorized to merge",
        "authorized to release",
        "workflow continuation authorized",
        "user assigned",
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

    public static GovernanceRoleCatalogValidationResult ValidateEntry(GovernanceRoleCatalogEntry? entry)
    {
        if (entry is null)
        {
            return Result(["RoleCatalogEntryRequired"], invalidRoleIds: [], duplicateRoleIds: [], unsafeRoleIds: []);
        }

        var issues = new List<string>();
        var invalidRoleIds = new List<string>();
        var unsafeRoleIds = new List<string>();

        AddRoleIdIssues(entry.RoleId, "RoleCatalogEntryRoleId", issues, invalidRoleIds, unsafeRoleIds);
        AddCatalogVersionIssues(entry.CatalogVersion, "RoleCatalogEntryCatalogVersion", issues);
        AddKnownEnumIssue(entry.RoleKind, GovernanceRoleKind.Unknown, "RoleCatalogEntryRoleKindUnknown", issues);
        AddKnownEnumIssue(entry.ScopeKind, GovernanceRoleScopeKind.Unknown, "RoleCatalogEntryScopeKindUnknown", issues);
        AddRequiredSafeTextIssue(entry.DisplayName, "RoleCatalogEntryDisplayName", issues, ref unsafeRoleIds, entry.RoleId);
        AddRequiredSafeTextIssue(entry.Description, "RoleCatalogEntryDescription", issues, ref unsafeRoleIds, entry.RoleId);
        AddRequiredSafeTextIssue(entry.ResponsibilitySummary, "RoleCatalogEntryResponsibilitySummary", issues, ref unsafeRoleIds, entry.RoleId);
        AddRequiredSafeTextIssue(entry.CreatedReason, "RoleCatalogEntryCreatedReason", issues, ref unsafeRoleIds, entry.RoleId);
        AddRequiredSafeTextIssue(entry.BoundaryStatement, "RoleCatalogEntryBoundaryStatement", issues, ref unsafeRoleIds, entry.RoleId);
        AddBoundaryDenialIssue(entry.BoundaryStatement, "RoleCatalogEntryBoundaryStatementMustDenyAuthority", issues);
        AddSurfaceIssues(entry.Surfaces, issues);
        AddDeprecationIssues(entry, issues, invalidRoleIds, unsafeRoleIds);

        return Result(issues, invalidRoleIds, duplicateRoleIds: [], unsafeRoleIds);
    }

    public static GovernanceRoleCatalogValidationResult ValidateCatalog(GovernanceRoleCatalog? catalog)
    {
        if (catalog is null)
        {
            return Result(["RoleCatalogRequired"], invalidRoleIds: [], duplicateRoleIds: [], unsafeRoleIds: []);
        }

        var issues = new List<string>();
        var invalidRoleIds = new List<string>();
        var unsafeRoleIds = new List<string>();
        var duplicateRoleIds = new List<string>();

        AddRequiredSafeTextIssue(catalog.CatalogId, "RoleCatalogCatalogId", issues, ref unsafeRoleIds, catalog.CatalogId);
        AddCatalogVersionIssues(catalog.CatalogVersion, "RoleCatalogCatalogVersion", issues);
        AddRequiredSafeTextIssue(catalog.CreatedReason, "RoleCatalogCreatedReason", issues, ref unsafeRoleIds, catalog.CatalogId);
        AddRequiredSafeTextIssue(catalog.BoundaryStatement, "RoleCatalogBoundaryStatement", issues, ref unsafeRoleIds, catalog.CatalogId);
        AddBoundaryDenialIssue(catalog.BoundaryStatement, "RoleCatalogBoundaryStatementMustDenyAuthority", issues);

        if (catalog.Entries is null || catalog.Entries.Count == 0)
        {
            issues.Add("RoleCatalogEntriesRequired");
            return Result(issues, invalidRoleIds, duplicateRoleIds, unsafeRoleIds);
        }

        foreach (var entry in catalog.Entries)
        {
            var entryValidation = ValidateEntry(entry);
            issues.AddRange(entryValidation.Issues.Select(issue => $"Entry:{entry?.RoleId ?? "unknown"}:{issue}"));
            invalidRoleIds.AddRange(entryValidation.InvalidRoleIds);
            unsafeRoleIds.AddRange(entryValidation.UnsafeRoleIds);
        }

        duplicateRoleIds.AddRange(catalog.Entries
            .Where(static entry => entry is not null)
            .GroupBy(static entry => entry.RoleId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(static group => group.Key));

        foreach (var duplicate in duplicateRoleIds)
        {
            issues.Add($"RoleCatalogDuplicateRoleId:{duplicate}");
        }

        var duplicateDisplayNames = catalog.Entries
            .Where(static entry => entry is not null)
            .GroupBy(static entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(static group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        foreach (var duplicate in duplicateDisplayNames)
        {
            issues.Add($"RoleCatalogDuplicateDisplayName:{duplicate}");
        }

        return Result(issues, invalidRoleIds, duplicateRoleIds, unsafeRoleIds);
    }

    public static bool ContainsUnsafeRoleText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return UnsafeRoleTextMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsSafeRoleId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 96 &&
        RoleIdPattern().IsMatch(value) &&
        !ContainsUnsafeRoleText(value);

    private static GovernanceRoleCatalogValidationResult Result(
        IReadOnlyList<string> issues,
        IReadOnlyList<string> invalidRoleIds,
        IReadOnlyList<string> duplicateRoleIds,
        IReadOnlyList<string> unsafeRoleIds)
    {
        var normalizedIssues = issues
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static issue => issue, StringComparer.Ordinal)
            .ToArray();

        return new GovernanceRoleCatalogValidationResult
        {
            IsValid = normalizedIssues.Length == 0,
            Issues = normalizedIssues,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications,
            InvalidRoleIds = invalidRoleIds
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DuplicateRoleIds = duplicateRoleIds
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            UnsafeRoleIds = unsafeRoleIds
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static void AddRoleIdIssues(
        string? roleId,
        string issuePrefix,
        ICollection<string> issues,
        ICollection<string> invalidRoleIds,
        ICollection<string> unsafeRoleIds)
    {
        if (string.IsNullOrWhiteSpace(roleId))
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        if (ContainsUnsafeRoleText(roleId))
        {
            issues.Add($"{issuePrefix}Unsafe");
            unsafeRoleIds.Add(roleId);
            return;
        }

        if (!IsSafeRoleId(roleId))
        {
            issues.Add($"{issuePrefix}Invalid");
            invalidRoleIds.Add(roleId);
        }
    }

    private static void AddCatalogVersionIssues(
        string? catalogVersion,
        string issuePrefix,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(catalogVersion))
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        if (!CatalogVersionPattern().IsMatch(catalogVersion))
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

    private static void AddRequiredSafeTextIssue(
        string? value,
        string issuePrefix,
        ICollection<string> issues,
        ref List<string> unsafeRoleIds,
        string? roleId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        if (ContainsUnsafeRoleText(value))
        {
            issues.Add($"{issuePrefix}Unsafe");
            if (!string.IsNullOrWhiteSpace(roleId))
            {
                unsafeRoleIds.Add(roleId);
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

        if (!boundaryStatement.Contains("does not grant authority", StringComparison.OrdinalIgnoreCase) &&
            !boundaryStatement.Contains("not authority", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(issue);
        }
    }

    private static void AddSurfaceIssues(
        IReadOnlyList<GovernanceRoleSurface>? surfaces,
        ICollection<string> issues)
    {
        if (surfaces is null || surfaces.Count == 0)
        {
            issues.Add("RoleCatalogEntrySurfacesRequired");
            return;
        }

        if (surfaces.Any(static surface => surface == GovernanceRoleSurface.Unknown || !Enum.IsDefined(surface)))
        {
            issues.Add("RoleCatalogEntrySurfaceUnknown");
        }
    }

    private static void AddDeprecationIssues(
        GovernanceRoleCatalogEntry entry,
        ICollection<string> issues,
        ICollection<string> invalidRoleIds,
        ICollection<string> unsafeRoleIds)
    {
        if (!entry.IsDeprecated)
        {
            if (!string.IsNullOrWhiteSpace(entry.ReplacementRoleId))
            {
                issues.Add("RoleCatalogEntryReplacementRoleIdOnlyForDeprecatedEntry");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(entry.ReplacementRoleId))
        {
            if (!entry.CreatedReason.Contains("terminal", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("RoleCatalogEntryDeprecatedReplacementOrTerminalReasonRequired");
            }

            return;
        }

        AddRoleIdIssues(entry.ReplacementRoleId, "RoleCatalogEntryReplacementRoleId", issues, invalidRoleIds, unsafeRoleIds);
    }

    [GeneratedRegex("^role:f01:[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex RoleIdPattern();

    [GeneratedRegex("^f[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CatalogVersionPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._:/@+,'()-]{1,319}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextPattern();
}
