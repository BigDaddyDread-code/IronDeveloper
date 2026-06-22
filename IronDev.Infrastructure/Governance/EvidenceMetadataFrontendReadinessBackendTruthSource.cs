using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class EvidenceMetadataFrontendReadinessBackendTruthSource : FrontendReadinessBackendTruthSource
{
    private readonly IEvidenceMetadataReadRepository _repository;

    public EvidenceMetadataFrontendReadinessBackendTruthSource(IEvidenceMetadataReadRepository repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public override string SourceName => "evidence-metadata-repository";

    public override FrontendEvidenceMetadataReadModel? GetEvidenceMetadata(
        string evidenceRef,
        FrontendReadinessReadScope scope)
    {
        var result = _repository.GetByEvidenceRef(evidenceRef, scope);
        return result.Found ? result.Metadata : null;
    }
}
