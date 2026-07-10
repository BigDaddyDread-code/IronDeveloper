using IronDev.Core.Interfaces;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
public sealed class DocumentsController : ControllerBase
{
    private readonly IProjectDocumentService _documents;
    private readonly IProjectDocumentUploadService _uploads;

    public DocumentsController(IProjectDocumentService documents, IProjectDocumentUploadService uploads)
    {
        _documents = documents;
        _uploads = uploads;
    }

    [HttpPost("api/projects/{projectId:int}/documents/upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ProjectDocumentUploadService.MaximumFileBytes + 64 * 1024)]
    public async Task<ActionResult<ProjectDocumentUploadResult>> UploadDocument(
        int projectId,
        [FromForm] ProjectDocumentUploadForm form,
        CancellationToken ct)
    {
        if (form.File is null)
            return BadRequest(new { error = "Choose a document file." });

        try
        {
            await using var content = form.File.OpenReadStream();
            var result = await _uploads.UploadAsync(new ProjectDocumentUploadRequest
            {
                ProjectId = projectId,
                DisplayName = form.DisplayName,
                DocumentType = form.DocumentType ?? "DiscussionSummary",
                Description = form.Description,
                OriginalFileName = form.File.FileName,
                MediaType = form.File.ContentType,
                ByteSize = form.File.Length,
                Content = content,
                CreatedBy = User.FindFirst(ClaimTypes.Email)?.Value
                    ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value
            }, ct);
            return CreatedAtAction(
                nameof(GetDocument),
                new { projectId, documentId = result.Document.Id },
                result);
        }
        catch (ProjectDocumentUploadException error)
        {
            return error.Kind switch
            {
                ProjectDocumentUploadFailureKind.UnsupportedType => StatusCode(StatusCodes.Status415UnsupportedMediaType, new { error = error.Message }),
                ProjectDocumentUploadFailureKind.TooLarge => StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = error.Message }),
                ProjectDocumentUploadFailureKind.DuplicateTitle => Conflict(new { error = error.Message }),
                ProjectDocumentUploadFailureKind.ProjectNotFound => NotFound(new { error = error.Message }),
                _ => BadRequest(new { error = error.Message })
            };
        }
    }

    [HttpGet("api/projects/{projectId:int}/documents")]
    public Task<IReadOnlyList<ProjectDocument>> GetDocuments(int projectId, [FromQuery] string? documentType, [FromQuery] string? status, CancellationToken ct) =>
        _documents.GetDocumentsAsync(new GetProjectDocumentsRequest { ProjectId = projectId, DocumentType = documentType, Status = string.IsNullOrWhiteSpace(status) ? "Active" : status }, ct);

    [HttpPost("api/projects/{projectId:int}/documents")]
    public Task<ProjectDocument> CreateDocument(int projectId, CreateProjectDocumentRequest request, CancellationToken ct)
    {
        request.ProjectId = projectId;
        return _documents.CreateDocumentAsync(request, ct);
    }

    [HttpGet("api/documents/{documentId:long}")]
    [HttpGet("api/projects/{projectId:int}/documents/{documentId:long}")]
    public async Task<ActionResult<ProjectDocument>> GetDocument(long documentId, CancellationToken ct, int? projectId = null)
    {
        var document = await _documents.GetDocumentAsync(documentId, ct);
        if (document is not null && projectId.HasValue && document.ProjectId != projectId.Value)
            return NotFound();

        return document is null ? NotFound() : Ok(document);
    }

    [HttpPut("api/projects/{projectId:int}/documents/{documentId:long}")]
    public async Task<ActionResult<ProjectDocumentVersion>> SaveDocumentVersion(int projectId, long documentId, AddProjectDocumentVersionRequest request, CancellationToken ct)
    {
        if (!await DocumentBelongsToProjectAsync(projectId, documentId, ct))
            return NotFound();

        request.DocumentId = documentId;
        return Ok(await _documents.AddVersionAsync(request, ct));
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

    [HttpGet("api/projects/{projectId:int}/documents/{documentId:long}/versions/current")]
    public async Task<ActionResult<ProjectDocumentVersion>> GetProjectDocumentCurrentVersion(int projectId, long documentId, CancellationToken ct)
    {
        if (!await DocumentBelongsToProjectAsync(projectId, documentId, ct))
            return NotFound();

        var version = await _documents.GetCurrentVersionAsync(documentId, ct);
        return version is null ? NotFound() : Ok(version);
    }

    [HttpGet("api/document-versions/{versionId:long}")]
    public Task<ProjectDocumentVersion?> GetVersion(long versionId, CancellationToken ct) =>
        _documents.GetVersionAsync(versionId, ct);

    [HttpGet("api/documents/{documentId:long}/versions")]
    public Task<IReadOnlyList<ProjectDocumentVersion>> GetVersions(long documentId, CancellationToken ct) =>
        _documents.GetVersionHistoryAsync(documentId, ct);

    [HttpGet("api/projects/{projectId:int}/documents/{documentId:long}/versions")]
    public async Task<ActionResult<IReadOnlyList<ProjectDocumentVersion>>> GetProjectDocumentVersions(int projectId, long documentId, CancellationToken ct)
    {
        if (!await DocumentBelongsToProjectAsync(projectId, documentId, ct))
            return NotFound();

        return Ok(await _documents.GetVersionHistoryAsync(documentId, ct));
    }

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

    [HttpPost("api/projects/{projectId:int}/documents/{documentId:long}/archive")]
    public async Task<IActionResult> ArchiveProjectDocument(int projectId, long documentId, CancellationToken ct)
    {
        if (!await DocumentBelongsToProjectAsync(projectId, documentId, ct))
            return NotFound();

        await _documents.ArchiveDocumentAsync(documentId, ct);
        return NoContent();
    }

    private async Task<bool> DocumentBelongsToProjectAsync(int projectId, long documentId, CancellationToken ct)
    {
        var document = await _documents.GetDocumentAsync(documentId, ct);
        return document is not null && document.ProjectId == projectId;
    }
}

public sealed class ProjectDocumentUploadForm
{
    public IFormFile? File { get; set; }
    public string? DisplayName { get; set; }
    public string? DocumentType { get; set; }
    public string? Description { get; set; }
}
