using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class ValidationResultMetadataFrontendReadinessBackendTruthSource : FrontendReadinessBackendTruthSource
{
    private readonly IValidationResultMetadataReadRepository _repository;

    public ValidationResultMetadataFrontendReadinessBackendTruthSource(IValidationResultMetadataReadRepository repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public override string SourceName => "validation-result-metadata-repository";

    public override FrontendValidationResultMetadataReadModel? GetValidationResultMetadata(
        string validationResultId,
        FrontendReadinessReadScope scope)
    {
        var result = _repository.GetByValidationResultId(validationResultId, scope);
        return result.Found ? result.Metadata : null;
    }
}
