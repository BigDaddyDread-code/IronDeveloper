namespace IronDev.Core.Builder;

public sealed class BuilderPatchPackage
{
    public string PackageId { get; init; } = string.Empty;
    public long TicketId { get; init; }
    public int ProjectId { get; init; }

    public string ContractId { get; init; } = string.Empty;
    public string ContractHash { get; init; } = string.Empty;
    public string ContractTitle { get; init; } = string.Empty;

    public string BuilderAgentId { get; init; } = string.Empty;
    public string BuilderRunId { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;

    public IReadOnlyList<BuilderPatchPackageChange> Changes { get; init; } = [];
    public IReadOnlyList<string> KnownRisks { get; init; } = [];
    public IReadOnlyList<string> KnownGaps { get; init; } = [];
    public IReadOnlyList<string> ValidationIssues { get; init; } = [];
    public IReadOnlyList<string> ValidationWarnings { get; init; } = [];

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A builder patch package is an implementation attempt against a confirmed contract. " +
        "It is not approval, not test proof, not critic review, not policy satisfaction, not workflow continuation, " +
        "not source apply permission, not release readiness, or not deployment readiness.";
}

public sealed class BuilderPatchPackageChange
{
    public string ChangeId { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public string Diff { get; init; } = string.Empty;
    public string? FullContentAfter { get; init; }

    public bool IsNewFile { get; init; }
    public bool IsDeletion { get; init; }

    public IReadOnlyList<string> CoveredAcceptanceCriterionIds { get; init; } = [];
    public IReadOnlyList<string> CoveredScopeItemIds { get; init; } = [];
    public IReadOnlyList<string> SupportReasons { get; init; } = [];

    public string TraceSummary { get; init; } = string.Empty;

    public bool IsValid { get; init; }
    public string ValidationMessage { get; init; } = string.Empty;

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A builder change must trace to the confirmed contract or an explicit support reason. " +
        "A changed file is not proof that the contract is satisfied.";
}

public sealed record BuilderContractReference(
    string ContractId,
    string ContractHash,
    string Title,
    IReadOnlyList<string> AcceptanceCriterionIds,
    IReadOnlyList<string> ScopeItemIds);

public sealed class BuilderPatchPackageValidationResult
{
    public bool IsValid => Issues.Count == 0;
    public List<string> Issues { get; } = [];
    public List<string> Warnings { get; } = [];
}
