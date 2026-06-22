using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class OperationTimelineFrontendReadinessBackendTruthSource : FrontendReadinessBackendTruthSource
{
    private readonly IOperationTimelineReadRepository _repository;

    public OperationTimelineFrontendReadinessBackendTruthSource(IOperationTimelineReadRepository repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public override string SourceName => "operation-timeline-repository";

    public override FrontendReadinessBackendReadResult<FrontendOperationTimelineReadModel> ReadOperationTimeline(
        string operationId,
        FrontendReadinessReadScope scope)
    {
        var result = _repository.GetByOperationId(operationId, scope);
        return FromRepositoryResult(
            result.Found,
            result.Timeline,
            result.Issues,
            model => FrontendReadinessReadStateClassifier.OperationTimeline(model, operationId),
            "OperationTimelineNotFound");
    }

    public override FrontendOperationTimelineReadModel? GetOperationTimeline(
        string operationId,
        FrontendReadinessReadScope scope)
    {
        return ReadOperationTimeline(operationId, scope).Data;
    }
}
