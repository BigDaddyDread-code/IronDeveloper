using IronDev.Client.Http;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;

namespace IronDev.Client.Documents;

public sealed class DocumentsApiClient : IronDevApiClientBase, IDocumentsApiClient
{
    public DocumentsApiClient(HttpClient http)
        : base(http)
    {
    }

    public Task<ProjectDocument> CreateDocumentAsync(CreateProjectDocumentRequest request, CancellationToken ct = default) =>
        PostAsync<ProjectDocument>($"projects/{request.ProjectId}/documents", request, ct);

    public Task<ProjectDocumentVersion> AddVersionAsync(AddProjectDocumentVersionRequest request, CancellationToken ct = default) =>
        PostAsync<ProjectDocumentVersion>($"documents/{request.DocumentId}/versions", request, ct);

    public Task<IReadOnlyList<ProjectDocument>> GetDocumentsAsync(GetProjectDocumentsRequest request, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ProjectDocument>>($"projects/{request.ProjectId}/documents?documentType={Uri.EscapeDataString(request.DocumentType ?? string.Empty)}&status={Uri.EscapeDataString(request.Status ?? string.Empty)}", ct);

    public Task<ProjectDocument?> GetDocumentAsync(long documentId, CancellationToken ct = default) =>
        GetAsync<ProjectDocument?>($"documents/{documentId}", ct);

    public Task<ProjectDocumentVersion?> GetCurrentVersionAsync(long documentId, CancellationToken ct = default) =>
        GetAsync<ProjectDocumentVersion?>($"documents/{documentId}/versions/current", ct);

    public Task<ProjectDocumentVersion?> GetVersionAsync(long documentVersionId, CancellationToken ct = default) =>
        GetAsync<ProjectDocumentVersion?>($"document-versions/{documentVersionId}", ct);

    public Task<IReadOnlyList<ProjectDocumentVersion>> GetVersionHistoryAsync(long documentId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ProjectDocumentVersion>>($"documents/{documentId}/versions", ct);

    public Task LinkVersionAsync(LinkProjectDocumentVersionRequest request, CancellationToken ct = default) =>
        PostAsync<object>($"document-versions/{request.DocumentVersionId}/links", request, ct);

    public Task<IReadOnlyList<ProjectDocumentLink>> GetLinksForVersionAsync(long documentVersionId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ProjectDocumentLink>>($"document-versions/{documentVersionId}/links", ct);

    public async Task ArchiveDocumentAsync(long documentId, CancellationToken ct = default) =>
        await DeleteAsync($"documents/{documentId}", ct);
}
