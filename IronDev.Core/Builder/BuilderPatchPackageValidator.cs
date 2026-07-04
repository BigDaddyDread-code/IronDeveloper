using System.IO;
using System.Text.RegularExpressions;

namespace IronDev.Core.Builder;

public static partial class BuilderPatchPackageValidator
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".xaml",
        ".csproj",
        ".sln",
        ".slnx",
        ".json",
        ".md",
        ".config",
        ".xml",
        ".razor",
        ".css",
        ".html",
        ".js",
        ".ts",
        ".sql",
        ".editorconfig",
        ".props",
        ".targets",
        ".txt",
        ".yml",
        ".yaml"
    };

    private static readonly string[] VagueSupportReasons =
    [
        "cleanup",
        "misc",
        "nice to have",
        "while i was here",
        "probably needed",
        "general improvement"
    ];

    private static readonly string[] AuthorityClaimMarkers =
    [
        string.Concat("Tests", "Passed"),
        string.Concat("Critic", "Satisfied"),
        string.Concat("Policy", "Satisfied"),
        string.Concat("Ready", "To", "Apply"),
        string.Concat("Ready", "To", "Release"),
        string.Concat("Ready", "To", "Deploy"),
        "tests passed",
        "approved by builder",
        "policy satisfied",
        "critic satisfied",
        "ready to apply",
        "ready to release",
        "ready to deploy",
        "contract satisfied",
        "acceptance criteria satisfied",
        "workflow continuation authorized",
        "source apply authorized"
    ];

    public static BuilderPatchPackageValidationResult Validate(BuilderPatchPackage? package, bool strict = true)
    {
        var result = new BuilderPatchPackageValidationResult();
        if (package is null)
        {
            AddIssue(result, "Builder patch package is required.");
            return result;
        }

        ValidatePackageIdentity(package, result);
        ValidateBoundary(package.Boundary, BuilderPatchPackage.BoundaryText, "Package boundary", result);
        ValidateAuthorityClaims(result,
            package.Summary,
            package.Rationale,
            package.ContractTitle,
            package.BuilderAgentId,
            package.BuilderRunId);
        ValidateAuthorityClaims(result,
            package.KnownRisks,
            package.KnownGaps,
            package.ValidationIssues,
            package.ValidationWarnings);

        if (package.Changes.Count == 0)
        {
            AddIssue(result, "Builder patch package must include at least one change.");
            return result;
        }

        foreach (var change in package.Changes)
        {
            ValidateChange(change, strict, result);
        }

        return result;
    }

    private static void ValidatePackageIdentity(BuilderPatchPackage package, BuilderPatchPackageValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(package.PackageId))
            AddIssue(result, "PackageId is required.");

        if (package.TicketId <= 0)
            AddIssue(result, "TicketId is required.");

        if (package.ProjectId <= 0)
            AddIssue(result, "ProjectId is required.");

        if (string.IsNullOrWhiteSpace(package.ContractId))
            AddIssue(result, "ContractId is required. The Builder may implement only against a confirmed contract reference, not loose conversation text.");

        if (string.IsNullOrWhiteSpace(package.ContractHash))
            AddIssue(result, "ContractHash is required. Without the contract hash, the package is not bound to the confirmed contract.");

        if (string.IsNullOrWhiteSpace(package.ContractTitle))
            AddIssue(result, "ContractTitle is required.");
    }

    private static void ValidateChange(BuilderPatchPackageChange change, bool strict, BuilderPatchPackageValidationResult result)
    {
        var label = string.IsNullOrWhiteSpace(change.FilePath) ? "<unknown file>" : change.FilePath;

        if (string.IsNullOrWhiteSpace(change.ChangeId))
            AddIssue(result, $"{label}: ChangeId is required.");

        ValidatePath(change.FilePath, result);

        if (change.IsDeletion)
            AddIssue(result, $"{label}: File deletions are not allowed in v1 builder patch packages.");

        if (string.IsNullOrWhiteSpace(change.Diff) && string.IsNullOrWhiteSpace(change.FullContentAfter))
            AddIssue(result, $"{label}: Change must include a diff or full replacement content.");

        if (string.IsNullOrWhiteSpace(change.Description))
            AddWarning(result, $"{label}: missing change description.");

        ValidateCSharpShape(change, result);
        ValidateBoundary(change.Boundary, BuilderPatchPackageChange.BoundaryText, $"{label}: Change boundary", result);
        ValidateAuthorityClaims(result,
            change.Description,
            change.Diff,
            change.FullContentAfter,
            change.TraceSummary,
            change.ValidationMessage);
        ValidateAuthorityClaims(result,
            change.CoveredAcceptanceCriterionIds,
            change.CoveredScopeItemIds,
            change.SupportReasons);
        ValidateTrace(change, strict, result);
    }

    private static void ValidatePath(string filePath, BuilderPatchPackageValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            AddIssue(result, "Change file path is required.");
            return;
        }

        if (Path.IsPathRooted(filePath) ||
            filePath.StartsWith("/", StringComparison.Ordinal) ||
            filePath.StartsWith("\\", StringComparison.Ordinal))
        {
            AddIssue(result, $"{filePath}: Absolute file paths are not allowed.");
            return;
        }

        var normalized = filePath.Replace('\\', '/');
        if (normalized.Equals("..", StringComparison.Ordinal) ||
            normalized.StartsWith("../", StringComparison.Ordinal) ||
            normalized.EndsWith("/..", StringComparison.Ordinal) ||
            normalized.Contains("/../", StringComparison.Ordinal) ||
            normalized.Contains("..", StringComparison.Ordinal))
        {
            AddIssue(result, $"{filePath}: Path traversal (..) is not allowed.");
            return;
        }

        if (filePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            AddIssue(result, $"{filePath}: File path contains invalid characters.");
            return;
        }

        var extension = Path.GetExtension(filePath);
        if (!string.IsNullOrWhiteSpace(extension) && !AllowedExtensions.Contains(extension))
        {
            AddIssue(result, $"{filePath}: File type '{extension}' is not allowed for builder patch packages.");
        }
    }

    private static void ValidateCSharpShape(BuilderPatchPackageChange change, BuilderPatchPackageValidationResult result)
    {
        if (!change.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(change.FullContentAfter))
        {
            return;
        }

        if (change.FullContentAfter.Contains("namespace Sample", StringComparison.Ordinal) ||
            change.FullContentAfter.Contains("class Program", StringComparison.Ordinal) &&
            !change.FilePath.Contains("Program.cs", StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(result, $"{change.FilePath}: Change appears to be generic sample code.");
        }
    }

    private static void ValidateTrace(BuilderPatchPackageChange change, bool strict, BuilderPatchPackageValidationResult result)
    {
        var hasCriterionTrace = HasAnyMeaningfulValue(change.CoveredAcceptanceCriterionIds);
        var hasScopeTrace = HasAnyMeaningfulValue(change.CoveredScopeItemIds);
        var supportReasons = MeaningfulValues(change.SupportReasons).ToList();
        var label = string.IsNullOrWhiteSpace(change.FilePath) ? "<unknown file>" : change.FilePath;

        if (!hasCriterionTrace && !hasScopeTrace && supportReasons.Count == 0)
        {
            AddIssue(result, $"{label}: Change must trace to at least one acceptance criterion, scope item, or explicit implementation-support reason.");
            return;
        }

        foreach (var reason in supportReasons)
        {
            var normalizedReason = Normalize(reason);
            if (VagueSupportReasons.Any(vague => normalizedReason.Equals(vague, StringComparison.OrdinalIgnoreCase)))
            {
                AddIssue(result, $"{label}: Support reason '{reason}' is too vague to bind the change to the contract.");
            }
        }

        if (strict &&
            IsProductionCodePath(change.FilePath) &&
            !hasCriterionTrace &&
            !hasScopeTrace &&
            supportReasons.Count > 0 &&
            !supportReasons.Any(ReferencesCriterionOrScope))
        {
            AddIssue(result, $"{label}: Production code changes using support reasons must reference a criterion or scope id.");
        }
    }

    private static void ValidateBoundary(
        string boundary,
        string expectedBoundary,
        string label,
        BuilderPatchPackageValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(boundary))
        {
            AddIssue(result, $"{label} is required.");
            return;
        }

        foreach (var fragment in RequiredBoundaryFragments(expectedBoundary))
        {
            if (!boundary.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                AddIssue(result, $"{label} must state '{fragment}'.");
            }
        }
    }

    private static IEnumerable<string> RequiredBoundaryFragments(string expectedBoundary)
    {
        if (string.Equals(expectedBoundary, BuilderPatchPackage.BoundaryText, StringComparison.Ordinal))
        {
            return
            [
                "implementation attempt",
                "confirmed contract",
                "not approval",
                "not test proof",
                "critic review",
                "policy satisfaction",
                "source apply permission",
                "release readiness",
                "deployment readiness"
            ];
        }

        return
        [
            "trace to the confirmed contract",
            "explicit support reason",
            "not proof"
        ];
    }

    private static void ValidateAuthorityClaims(BuilderPatchPackageValidationResult result, params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var marker in AuthorityClaimMarkers)
            {
                if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    AddIssue(result, $"Builder patch package text contains authority claim marker '{marker}'.");
                }
            }
        }
    }

    private static void ValidateAuthorityClaims(BuilderPatchPackageValidationResult result, params IReadOnlyList<string>[] valueSets)
    {
        foreach (var valueSet in valueSets)
        {
            ValidateAuthorityClaims(result, valueSet.ToArray());
        }
    }

    private static bool HasAnyMeaningfulValue(IReadOnlyList<string> values) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value));

    private static IEnumerable<string> MeaningfulValues(IReadOnlyList<string> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value));

    private static bool IsProductionCodePath(string filePath) =>
        filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
        !filePath.Contains("test", StringComparison.OrdinalIgnoreCase) &&
        !filePath.Contains("spec", StringComparison.OrdinalIgnoreCase);

    private static bool ReferencesCriterionOrScope(string reason) =>
        CriterionOrScopeReferenceRegex().IsMatch(reason);

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static void AddIssue(BuilderPatchPackageValidationResult result, string issue)
    {
        if (!result.Issues.Contains(issue, StringComparer.OrdinalIgnoreCase))
        {
            result.Issues.Add(issue);
        }
    }

    private static void AddWarning(BuilderPatchPackageValidationResult result, string warning)
    {
        if (!result.Warnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
        {
            result.Warnings.Add(warning);
        }
    }

    [GeneratedRegex(@"\b(ac|criterion|criteria|scope|scope-item|si)[-\s:]*[a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex CriterionOrScopeReferenceRegex();
}
