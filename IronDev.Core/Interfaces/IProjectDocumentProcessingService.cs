using IronDev.Data.Models;

namespace IronDev.Core.Interfaces;

public interface IProjectDocumentProcessingService
{
    Task<ProjectDocumentProcessingResult> ProcessAsync(
        ProjectDocumentProcessingRequest request,
        CancellationToken ct = default);
}

public interface IProjectDocumentProcessingStateStore
{
    Task<ProjectDocument?> TryBeginProcessingAsync(
        long documentId,
        string? updatedBy,
        CancellationToken ct = default);

    Task<ProjectDocument> UpdateProcessingStateAsync(
        ProjectDocumentProcessingStateUpdate update,
        CancellationToken ct = default);
}
