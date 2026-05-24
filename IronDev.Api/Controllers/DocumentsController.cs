using IronDev.Core.Interfaces;
using IronDev.Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
public sealed class DocumentsController : ControllerBase
{
    private readonly IProjectDocumentService _documents;

    public DocumentsController(IProjectDocumentService documents)
    {
        _documents = documents;
    }

    [HttpGet("api/projects/{projectId:int}/documents")]
    public Task<IReadOnlyList<ProjectDocument>> GetDocuments(int projectId, [FromQuery] string? documentType, [FromQuery] string? status, CancellationToken ct) =>
        _documents.GetDocumentsAsync(new GetProjectDocumentsRequest { ProjectId = projectId, DocumentType = documentType, Status = string.IsNullOrWhiteSpace(status) ? "Active" : status }, ct);

    [HttpPost("api/projects/{projectId:int}/documents")]
    public Task<ProjectDocument> CreateDocument(CreateProjectDocumentRequest request, CancellationToken ct) =>
        _documents.CreateDocumentAsync(request, ct);

    [HttpGet("api/documents/{documentId:long}")]
    [HttpGet("api/projects/{projectId:int}/documents/{documentId:long}")]
    public Task<ProjectDocument?> GetDocument(long documentId, CancellationToken ct, int? projectId = null) =>
        _documents.GetDocumentAsync(documentId, ct);

    [HttpPut("api/projects/{projectId:int}/documents/{documentId:long}")]
    public Task<ProjectDocumentVersion> SaveDocumentVersion(long documentId, AddProjectDocumentVersionRequest request, CancellationToken ct)
    {
        request.DocumentId = documentId;
        return _documents.AddVersionAsync(request, ct);
    }

    [HttpPost("api/projects/{projectId:int}/documents/{documentId:long}/resolve")]
    public IActionResult ResolveDocumentComment(int projectId, long documentId) =>
        Ok(new { projectId, documentId, status = "not_implemented" });

    [HttpPost("api/documents/{documentId:long}/versions")]
    public Task<ProjectDocumentVersion> AddVersion(AddProjectDocumentVersionRequest request, CancellationToken ct) =>
        _documents.AddVersionAsync(request, ct);

    [HttpGet("api/documents/{documentId:long}/versions/current")]
    public Task<ProjectDocumentVersion?> GetCurrentVersion(long documentId, CancellationToken ct) =>
        _documents.GetCurrentVersionAsync(documentId, ct);

    [HttpGet("api/document-versions/{versionId:long}")]
    public Task<ProjectDocumentVersion?> GetVersion(long versionId, CancellationToken ct) =>
        _documents.GetVersionAsync(versionId, ct);

    [HttpGet("api/documents/{documentId:long}/versions")]
    public Task<IReadOnlyList<ProjectDocumentVersion>> GetVersions(long documentId, CancellationToken ct) =>
        _documents.GetVersionHistoryAsync(documentId, ct);

    [HttpPost("api/document-versions/{versionId:long}/links")]
    public async Task<IActionResult> LinkVersion(LinkProjectDocumentVersionRequest request, CancellationToken ct)
    {
        await _documents.LinkVersionAsync(request, ct);
        return Ok();
    }

    [HttpGet("api/document-versions/{versionId:long}/links")]
    public Task<IReadOnlyList<ProjectDocumentLink>> GetLinks(long versionId, CancellationToken ct) =>
        _documents.GetLinksForVersionAsync(versionId, ct);

    [HttpDelete("api/documents/{documentId:long}")]
    public async Task<IActionResult> ArchiveDocument(long documentId, CancellationToken ct)
    {
        await _documents.ArchiveDocumentAsync(documentId, ct);
        return NoContent();
    }
}
