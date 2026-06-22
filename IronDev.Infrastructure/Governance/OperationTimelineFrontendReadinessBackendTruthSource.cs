using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class OperationTimelineFrontendReadinessBackendTruthSource : FrontendReadinessBackendTruthSource
{
    private readonly IOperationTimelineReadRepository _repository;

    public OperationTimelineFrontendReadinessBackendTruthSource(IOperationTimelineReadRepository repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public override string SourceName => "operation-timeline-repository";

    public override FrontendOperationTimelineReadModel? GetOperationTimeline(
        string operationId,
        FrontendReadinessReadScope scope)
    {
        var result = _repository.GetByOperationId(operationId, scope);
        return result.Found ? result.Timeline : null;
    }
}
