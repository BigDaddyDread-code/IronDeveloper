using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class PatchPackageMetadataFrontendReadinessBackendTruthSource : FrontendReadinessBackendTruthSource
{
    private readonly IPatchPackageMetadataReadRepository _repository;

    public PatchPackageMetadataFrontendReadinessBackendTruthSource(IPatchPackageMetadataReadRepository repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public override string SourceName => "patch-package-metadata-repository";

    public override FrontendReadinessBackendReadResult<FrontendPatchPackageMetadataReadModel> ReadPatchPackageMetadata(
        string packageId,
        FrontendReadinessReadScope scope)
    {
        var result = _repository.GetByPackageId(packageId, scope);
        return FromRepositoryResult(
            result.Found,
            result.Metadata,
            result.Issues,
            model => FrontendReadinessReadStateClassifier.PatchPackageMetadata(model, packageId),
            "PatchPackageMetadataNotFound");
    }

    public override FrontendPatchPackageMetadataReadModel? GetPatchPackageMetadata(
        string packageId,
        FrontendReadinessReadScope scope)
    {
        return ReadPatchPackageMetadata(packageId, scope).Data;
    }
}
