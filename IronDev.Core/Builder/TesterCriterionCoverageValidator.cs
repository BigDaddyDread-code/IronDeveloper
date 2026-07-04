namespace IronDev.Core.Builder;

public static class TesterCriterionCoverageValidator
{
    public const string PackageRequired = "TESTER_COVERAGE_PACKAGE_REQUIRED";
    public const string IdentityRequired = "TESTER_COVERAGE_IDENTITY_REQUIRED";
    public const string CriteriaRequired = "TESTER_COVERAGE_CRITERIA_REQUIRED";
    public const string TestsRequired = "TESTER_COVERAGE_TESTS_REQUIRED";
    public const string CriterionMissingTrace = "TESTER_COVERAGE_CRITERION_MISSING_TRACE";
    public const string TestMissingCriterion = "TESTER_COVERAGE_TEST_MISSING_CRITERION";
    public const string UnknownCriterion = "TESTER_COVERAGE_UNKNOWN_CRITERION";
    public const string UnknownTest = "TESTER_COVERAGE_UNKNOWN_TEST";
    public const string ConflictingStatus = "TESTER_COVERAGE_CONFLICTING_STATUS";
    public const string UncoveredReasonRequired = "TESTER_COVERAGE_UNCOVERED_REASON_REQUIRED";
    public const string ForbiddenAuthorityClaim = "TESTER_COVERAGE_FORBIDDEN_AUTHORITY_CLAIM";
    public const string BuilderVisibilityForbidden = "TESTER_COVERAGE_BUILDER_VISIBILITY_FORBIDDEN";
    public const string BoundaryMissing = "TESTER_COVERAGE_BOUNDARY_MISSING";

    private static readonly string[] BoundaryFragments =
    [
        "maps acceptance criteria to test intent",
        "not test execution",
        "not test proof",
        "not approval",
        "not critic review",
        "not policy satisfaction",
        "not workflow continuation",
        "not source apply permission",
        "not release readiness",
        "not deployment readiness"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        string.Concat("Tests", "Passed"),
        string.Concat("Test", "Proof"),
        string.Concat("Policy", "Satisfied"),
        string.Concat("Ready", "To", "Apply"),
        string.Concat("Ready", "To", "Release"),
        string.Concat("Ready", "To", "Deploy"),
        string.Concat("Critic", "Satisfied"),
        string.Concat("Contract", "Satisfied"),
        "tests passed",
        "test proof",
        "approved",
        "approval",
        "approved by tester",
        "approval granted",
        "policy satisfied",
        "ready to apply",
        "ready to release",
        "ready to deploy",
        "critic satisfied",
        "contract satisfied",
        "therefore the work is approved"
    ];

    private static readonly string[] VagueUncoveredReasons =
    [
        "skip",
        "later",
        "not needed",
        "too hard",
        "none"
    ];

    public static TesterCriterionCoverageValidationResult Validate(TesterCriterionCoveragePackage? package)
    {
        var result = new TesterCriterionCoverageValidationResult();
        if (package is null)
        {
            AddIssue(result, PackageRequired);
            return result;
        }

        ValidateIdentity(package, result);
        ValidatePackageBoundary(package.Boundary, result);
        ValidateAuthorityClaims(result,
            package.PackageId,
            package.ContractId,
            package.ContractHash,
            package.TesterAgentId,
            package.TesterRunId);
        ValidateAuthorityClaims(result, package.KnownRisks, package.KnownGaps);

        if (package.Criteria.Count == 0)
        {
            AddIssue(result, CriteriaRequired);
            return result;
        }

        var criterionIds = ValidateCriteria(package.Criteria, result);
        var testIds = ValidateTests(package.Tests, criterionIds, result);
        ValidateCoverageRows(package.Coverage, criterionIds, testIds, result);
        ValidateUncoveredCriteria(package.UncoveredCriteria, criterionIds, result);
        ValidateTraceCompleteness(package, criterionIds, result);

        return result;
    }

    private static void ValidateIdentity(TesterCriterionCoveragePackage package, TesterCriterionCoverageValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(package.PackageId) ||
            package.TicketId <= 0 ||
            package.ProjectId <= 0 ||
            string.IsNullOrWhiteSpace(package.ContractId) ||
            string.IsNullOrWhiteSpace(package.ContractHash) ||
            string.IsNullOrWhiteSpace(package.TesterAgentId) ||
            string.IsNullOrWhiteSpace(package.TesterRunId))
        {
            AddIssue(result, IdentityRequired);
        }
    }

    private static HashSet<string> ValidateCriteria(
        IReadOnlyList<TesterAcceptanceCriterionRef> criteria,
        TesterCriterionCoverageValidationResult result)
    {
        var criterionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var criterion in criteria)
        {
            if (string.IsNullOrWhiteSpace(criterion.CriterionId) ||
                string.IsNullOrWhiteSpace(criterion.Description) ||
                string.IsNullOrWhiteSpace(criterion.Measure))
            {
                AddIssue(result, CriteriaRequired);
            }

            if (!string.IsNullOrWhiteSpace(criterion.CriterionId))
                criterionIds.Add(criterion.CriterionId);

            ValidateAuthorityClaims(result, criterion.CriterionId, criterion.Description, criterion.Measure);
        }

        return criterionIds;
    }

    private static HashSet<string> ValidateTests(
        IReadOnlyList<TesterAuthoredTestCase> tests,
        HashSet<string> criterionIds,
        TesterCriterionCoverageValidationResult result)
    {
        var testIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var test in tests)
        {
            if (string.IsNullOrWhiteSpace(test.TestId) ||
                string.IsNullOrWhiteSpace(test.RelativePath) ||
                string.IsNullOrWhiteSpace(test.TestName) ||
                string.IsNullOrWhiteSpace(test.Intent) ||
                string.IsNullOrWhiteSpace(test.Content))
            {
                AddIssue(result, TestsRequired);
            }

            if (!string.IsNullOrWhiteSpace(test.TestId))
                testIds.Add(test.TestId);

            if (test.CoveredCriterionIds.Count == 0)
                AddIssue(result, TestMissingCriterion);

            foreach (var criterionId in test.CoveredCriterionIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                if (!criterionIds.Contains(criterionId))
                    AddIssue(result, UnknownCriterion);
            }

            if (!test.IsGeneratedFromCriteria || test.SawBuilderDiff || test.SawBuilderPatch || test.SawBuilderReasoning)
                AddIssue(result, BuilderVisibilityForbidden);

            ValidateNestedBoundary(test.Boundary, result);
            ValidateAuthorityClaims(result, test.TestId, test.RelativePath, test.TestName, test.Intent, test.Content);
            ValidateAuthorityClaims(result, test.CoveredCriterionIds);
        }

        return testIds;
    }

    private static void ValidateCoverageRows(
        IReadOnlyList<TesterCriterionCoverageRow> rows,
        HashSet<string> criterionIds,
        HashSet<string> testIds,
        TesterCriterionCoverageValidationResult result)
    {
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.CriterionId) || !criterionIds.Contains(row.CriterionId))
                AddIssue(result, UnknownCriterion);

            if (!TesterCoverageStatuses.All.Contains(row.CoverageStatus))
                AddIssue(result, CriterionMissingTrace);

            if (string.Equals(row.CoverageStatus, TesterCoverageStatuses.Covered, StringComparison.Ordinal) &&
                row.TestIds.Count == 0)
            {
                AddIssue(result, CriterionMissingTrace);
            }

            foreach (var testId in row.TestIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                if (!testIds.Contains(testId))
                    AddIssue(result, UnknownTest);
            }

            ValidateAuthorityClaims(result, row.CriterionId, row.CoverageStatus, row.Notes);
            ValidateAuthorityClaims(result, row.TestIds);
        }
    }

    private static void ValidateUncoveredCriteria(
        IReadOnlyList<TesterUncoveredCriterion> uncoveredCriteria,
        HashSet<string> criterionIds,
        TesterCriterionCoverageValidationResult result)
    {
        foreach (var uncovered in uncoveredCriteria)
        {
            if (string.IsNullOrWhiteSpace(uncovered.CriterionId) || !criterionIds.Contains(uncovered.CriterionId))
                AddIssue(result, UnknownCriterion);

            if (string.IsNullOrWhiteSpace(uncovered.Reason) ||
                string.IsNullOrWhiteSpace(uncovered.RequiredHumanDecision) ||
                IsVagueUncoveredReason(uncovered.Reason))
            {
                AddIssue(result, UncoveredReasonRequired);
            }

            ValidateNestedBoundary(uncovered.Boundary, result);
            ValidateAuthorityClaims(result, uncovered.CriterionId, uncovered.Reason, uncovered.RequiredHumanDecision);
        }
    }

    private static void ValidateTraceCompleteness(
        TesterCriterionCoveragePackage package,
        HashSet<string> criterionIds,
        TesterCriterionCoverageValidationResult result)
    {
        var covered = package.Coverage
            .Where(row => string.Equals(row.CoverageStatus, TesterCoverageStatuses.Covered, StringComparison.Ordinal) &&
                row.TestIds.Count > 0)
            .Select(row => row.CriterionId)
            .ToHashSet(StringComparer.Ordinal);
        var uncovered = package.UncoveredCriteria
            .Select(item => item.CriterionId)
            .ToHashSet(StringComparer.Ordinal);

        if (package.Tests.Count == 0 && uncovered.Count < criterionIds.Count)
            AddIssue(result, TestsRequired);

        foreach (var criterionId in criterionIds)
        {
            if (!covered.Contains(criterionId) && !uncovered.Contains(criterionId))
                AddIssue(result, CriterionMissingTrace);

            if (covered.Contains(criterionId) && uncovered.Contains(criterionId))
                AddIssue(result, ConflictingStatus);
        }
    }

    private static void ValidatePackageBoundary(string boundary, TesterCriterionCoverageValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(boundary))
        {
            AddIssue(result, BoundaryMissing);
            return;
        }

        foreach (var fragment in BoundaryFragments)
        {
            if (!boundary.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                AddIssue(result, BoundaryMissing);
        }
    }

    private static void ValidateNestedBoundary(string boundary, TesterCriterionCoverageValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(boundary))
            AddIssue(result, BoundaryMissing);
    }

    private static void ValidateAuthorityClaims(TesterCriterionCoverageValidationResult result, params IReadOnlyList<string>[] valueSets)
    {
        foreach (var valueSet in valueSets)
        {
            ValidateAuthorityClaims(result, valueSet.ToArray());
        }
    }

    private static void ValidateAuthorityClaims(TesterCriterionCoverageValidationResult result, params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var marker in AuthorityMarkers)
            {
                if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    AddIssue(result, ForbiddenAuthorityClaim);
            }
        }
    }

    private static bool IsVagueUncoveredReason(string reason)
    {
        var normalized = string.Join(' ', reason.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return VagueUncoveredReasons.Any(vague => string.Equals(vague, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddIssue(TesterCriterionCoverageValidationResult result, string code)
    {
        if (!result.Issues.Contains(code, StringComparer.Ordinal))
            result.Issues.Add(code);
    }
}
