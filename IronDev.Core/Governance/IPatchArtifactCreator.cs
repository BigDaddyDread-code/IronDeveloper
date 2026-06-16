namespace IronDev.Core.Governance;

public interface IPatchArtifactCreator
{
    Task<PatchArtifactCreationResult> CreateAsync(
        PatchArtifactCreationRequest request,
        CancellationToken cancellationToken = default);

    Task<PatchArtifactCreationResult> CreateAndStoreAsync(
        PatchArtifactCreationRequest request,
        CancellationToken cancellationToken = default);
}
