namespace IronDev.Core.Builder;

public static class TesterCoverageStatuses
{
    public const string Covered = "Covered";
    public const string Uncovered = "Uncovered";
    public const string NotTestableYet = "NotTestableYet";
    public const string NeedsClarification = "NeedsClarification";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Covered,
        Uncovered,
        NotTestableYet,
        NeedsClarification
    };
}

public sealed record class TesterCriterionCoveragePackage
{
    public string PackageId { get; init; } = string.Empty;

    public long TicketId { get; init; }
    public int ProjectId { get; init; }

    public string ContractId { get; init; } = string.Empty;
    public string ContractHash { get; init; } = string.Empty;

    public string TesterAgentId { get; init; } = string.Empty;
    public string TesterRunId { get; init; } = string.Empty;

    public IReadOnlyList<TesterAcceptanceCriterionRef> Criteria { get; init; } = [];
    public IReadOnlyList<TesterAuthoredTestCase> Tests { get; init; } = [];
    public IReadOnlyList<TesterCriterionCoverageRow> Coverage { get; init; } = [];
    public IReadOnlyList<TesterUncoveredCriterion> UncoveredCriteria { get; init; } = [];

    public IReadOnlyList<string> KnownRisks { get; init; } = [];
    public IReadOnlyList<string> KnownGaps { get; init; } = [];

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A tester coverage package maps acceptance criteria to test intent. " +
        "It is not test execution, not test proof, not approval, not critic review, not policy satisfaction, " +
        "not workflow continuation, not source apply permission, not release readiness, and not deployment readiness.";
}

public sealed record class TesterCriterionCoverageMatrix
{
    public IReadOnlyList<TesterCriterionCoverageRow> Rows { get; init; } = [];

    public string Boundary { get; init; } =
        "A tester coverage matrix maps criterion ids to authored test intent or explicit coverage gaps. It is not proof that tests passed.";
}

public sealed record class TesterAcceptanceCriterionRef
{
    public string CriterionId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Measure { get; init; } = string.Empty;
}

public sealed record class TesterAuthoredTestCase
{
    public string TestId { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string TestName { get; init; } = string.Empty;
    public string Intent { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;

    public IReadOnlyList<string> CoveredCriterionIds { get; init; } = [];

    public bool IsGeneratedFromCriteria { get; init; } = true;
    public bool SawBuilderDiff { get; init; }
    public bool SawBuilderPatch { get; init; }
    public bool SawBuilderReasoning { get; init; }

    public string Boundary { get; init; } =
        "An authored test case is test intent derived from criteria. It is not test execution, not test proof, and not approval.";
}

public sealed record class TesterCriterionCoverageRow
{
    public string CriterionId { get; init; } = string.Empty;
    public IReadOnlyList<string> TestIds { get; init; } = [];

    public string CoverageStatus { get; init; } = TesterCoverageStatuses.Covered;
    public string Notes { get; init; } = string.Empty;
}

public sealed record class TesterUncoveredCriterion
{
    public string CriterionId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string RequiredHumanDecision { get; init; } = string.Empty;

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "An uncovered criterion is an explicit testing gap for human review. Naming the gap is not approval to ignore it.";
}

public sealed class TesterCriterionCoverageValidationResult
{
    public bool IsValid => Issues.Count == 0;
    public List<string> Issues { get; } = [];
    public List<string> Warnings { get; } = [];
}
