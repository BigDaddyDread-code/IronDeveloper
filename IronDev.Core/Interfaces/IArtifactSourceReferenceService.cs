using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IArtifactSourceReferenceService
{
    Task RecordReferenceAsync(
        ArtifactSourceReference reference,
        CancellationToken ct = default);

    Task<IReadOnlyList<ArtifactSourceReference>> GetForArtifactAsync(
        int tenantId,
        int projectId,
        string artifactType,
        long artifactId,
        CancellationToken ct = default);

    Task AddAsync(
        ArtifactSourceReference reference,
        CancellationToken ct = default);

    Task AddManyAsync(
        IEnumerable<ArtifactSourceReference> references,
        CancellationToken ct = default);

    Task DeleteForArtifactAsync(
        int tenantId,
        int projectId,
        string artifactType,
        long artifactId,
        CancellationToken ct = default);
}
