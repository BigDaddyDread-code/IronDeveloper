using IronDev.Data.Models;

namespace IronDev.Core.Interfaces;

public interface IProjectDocumentUploadService
{
    Task<ProjectDocumentUploadResult> UploadAsync(
        ProjectDocumentUploadRequest request,
        CancellationToken ct = default);
}
