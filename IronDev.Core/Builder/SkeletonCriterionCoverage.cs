namespace IronDev.Core.Builder;

/// <summary>One row of the criterion→test matrix: a criterion and the tests that cover it — or its explicit uncovered state.</summary>
public sealed record SkeletonCriterionCoverage
{
    public required string Criterion { get; init; }
    public required bool Covered { get; init; }
    public IReadOnlyList<string> CoveringTests { get; init; } = [];
}

/// <summary>
/// P1-4 — computes the criterion→test coverage record deterministically from the
/// acceptance criteria text and the authored tests. Pure and repeatable by design:
/// the package builder computes it, and the ground-truth verifier RECOMPUTES it —
/// the same inputs must always yield the same record, so a package whose recorded
/// coverage disagrees with recomputation has been tampered with.
///
/// Matching is deliberately simple and inspectable: a test covers a criterion when
/// its coversCriterion text, normalized (trimmed, case-insensitive, trailing
/// punctuation ignored), equals the criterion line or one contains the other.
/// A test whose coversCriterion matches no criterion covers nothing.
/// </summary>
public static class SkeletonCriterionCoverageCalculator
{
    public static IReadOnlyList<SkeletonCriterionCoverage> Compute(
        string? acceptanceCriteria,
        IReadOnlyList<SkeletonAuthoredTest> authoredTests)
    {
        var criteria = ParseCriteria(acceptanceCriteria);

        return criteria
            .Select(criterion =>
            {
                var normalizedCriterion = Normalize(criterion);
                var coveringTests = authoredTests
                    .Where(test => Matches(normalizedCriterion, Normalize(test.CoversCriterion)))
                    .Select(test => test.RelativePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new SkeletonCriterionCoverage
                {
                    Criterion = criterion,
                    Covered = coveringTests.Count > 0,
                    CoveringTests = coveringTests
                };
            })
            .ToList();
    }

    /// <summary>Criteria are lines; list bullets and numbering are presentation, not content.</summary>
    public static IReadOnlyList<string> ParseCriteria(string? acceptanceCriteria) =>
        (acceptanceCriteria ?? string.Empty)
            .Split('\n')
            .Select(line => line.TrimEnd('\r').Trim())
            .Select(StripListMarker)
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string StripListMarker(string line)
    {
        var trimmed = line.TrimStart('-', '*', '•', ' ', '\t');
        var digits = 0;
        while (digits < trimmed.Length && char.IsDigit(trimmed[digits]))
            digits++;
        if (digits > 0 && digits < trimmed.Length && (trimmed[digits] == '.' || trimmed[digits] == ')'))
            trimmed = trimmed[(digits + 1)..];
        return trimmed.Trim();
    }

    private static bool Matches(string normalizedCriterion, string normalizedCoversCriterion) =>
        normalizedCoversCriterion.Length > 0 &&
        (normalizedCriterion.Equals(normalizedCoversCriterion, StringComparison.Ordinal) ||
         normalizedCriterion.Contains(normalizedCoversCriterion, StringComparison.Ordinal) ||
         normalizedCoversCriterion.Contains(normalizedCriterion, StringComparison.Ordinal));

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().TrimEnd('.', '!', ';', ':').ToLowerInvariant();
}
