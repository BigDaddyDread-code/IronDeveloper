using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class ReceiptMetadataFrontendReadinessBackendTruthSource : FrontendReadinessBackendTruthSource
{
    private readonly IReceiptMetadataReadRepository _repository;

    public ReceiptMetadataFrontendReadinessBackendTruthSource(IReceiptMetadataReadRepository repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public override string SourceName => "receipt-metadata-repository";

    public override FrontendReceiptMetadataReadModel? GetReceiptMetadata(
        string receiptRef,
        FrontendReadinessReadScope scope)
    {
        var result = _repository.GetByReceiptRef(receiptRef, scope);
        return result.Found ? result.Metadata : null;
    }
}
