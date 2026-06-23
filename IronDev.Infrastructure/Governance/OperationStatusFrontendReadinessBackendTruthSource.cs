using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class OperationStatusFrontendReadinessBackendTruthSource : FrontendReadinessBackendTruthSource
{
    private readonly IGovernedOperationStatusReadRepository _repository;

    public OperationStatusFrontendReadinessBackendTruthSource(IGovernedOperationStatusReadRepository repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public override string SourceName => "operation-status-repository";

    public override FrontendReadinessBackendReadResult<GovernedOperationStatus> ReadOperationStatus(
        string operationId,
        FrontendReadinessReadScope scope)
    {
        var result = _repository.GetByOperationId(operationId, scope);
        return FromRepositoryResult(
            result.Found,
            result.Status,
            result.Issues,
            _ => result.Issues.Contains("StoredOperationStatusInvalid", StringComparer.OrdinalIgnoreCase)
                ? FrontendReadinessReadState.Invalid("StoredOperationStatusInvalid")
                : FrontendReadinessReadState.Available("OperationStatusAvailable"),
            "OperationStatusNotFound");
    }

    public override GovernedOperationStatus? GetOperationStatus(string operationId, FrontendReadinessReadScope scope)
    {
        return ReadOperationStatus(operationId, scope).Data;
    }
}
