namespace IronDev.Core.Governance;

public interface IGovernedOperationStatusReadRepository
{
    GovernedOperationStatusReadResult GetByOperationId(
        string operationId,
        FrontendReadinessReadScope scope);
}

public sealed record GovernedOperationStatusReadRecord
{
    public required string OperationId { get; init; }
    public required GovernedOperationStatus Status { get; init; }
    public bool IsTenantScoped { get; init; } = true;
    public int? TenantId { get; init; }
}

public sealed record GovernedOperationStatusReadResult
{
    public required bool Found { get; init; }
    public GovernedOperationStatus? Status { get; init; }
    public required IReadOnlyCollection<string> Issues { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required FrontendReadBoundary Boundary { get; init; }

    public static GovernedOperationStatusReadResult NotFound(params string[] issues) =>
        new()
        {
            Found = false,
            Status = null,
            Issues = issues,
            EvidenceRefs = [],
            Boundary = FrontendReadBoundary.ReadOnlyStatus
        };
}
