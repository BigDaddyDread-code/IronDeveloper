using System.Text;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;
using Microsoft.Data.SqlClient;

namespace IronDev.Services;

public sealed class ProjectDocumentUploadService : IProjectDocumentUploadService
{
    public const long MaximumFileBytes = 1024 * 1024;

    private static readonly HashSet<string> AllowedDocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Architecture",
        "DiscussionSummary",
        "BuildPlan",
        "DecisionLog"
    };

    private readonly IProjectDocumentService _documents;

    public ProjectDocumentUploadService(IProjectDocumentService documents)
    {
        _documents = documents;
    }

    public async Task<ProjectDocumentUploadResult> UploadAsync(
        ProjectDocumentUploadRequest request,
        CancellationToken ct = default)
    {
        var originalFileName = SafeFileName(request.OriginalFileName);
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var mediaType = extension switch
        {
            ".md" or ".markdown" => "text/markdown",
            ".txt" => "text/plain",
            _ => throw Failure(
                ProjectDocumentUploadFailureKind.UnsupportedType,
                "Only UTF-8 Markdown (.md, .markdown) and text (.txt) files are supported.")
        };

        if (request.ByteSize <= 0)
            throw Failure(ProjectDocumentUploadFailureKind.InvalidRequest, "Choose a non-empty document file.");

        if (request.ByteSize > MaximumFileBytes)
            throw Failure(ProjectDocumentUploadFailureKind.TooLarge, "Document files must be 1 MiB or smaller.");

        var title = string.IsNullOrWhiteSpace(request.DisplayName)
            ? Path.GetFileNameWithoutExtension(originalFileName)
            : request.DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw Failure(ProjectDocumentUploadFailureKind.InvalidRequest, "Document display name is required.");
        if (title.Length > 300)
            throw Failure(ProjectDocumentUploadFailureKind.InvalidRequest, "Document display name must be 300 characters or fewer.");

        var documentType = (request.DocumentType ?? string.Empty).Trim();
        if (!AllowedDocumentTypes.Contains(documentType))
            throw Failure(ProjectDocumentUploadFailureKind.InvalidRequest, "Choose a supported document type.");

        var description = request.Description?.Trim();
        if (description?.Length > 1000)
            throw Failure(ProjectDocumentUploadFailureKind.InvalidRequest, "Document description must be 1000 characters or fewer.");

        var existing = await _documents.GetDocumentsAsync(new GetProjectDocumentsRequest
        {
            ProjectId = request.ProjectId,
            Status = "*"
        }, ct);
        if (existing.Any(document => string.Equals(document.Title, title, StringComparison.OrdinalIgnoreCase)))
            throw Failure(ProjectDocumentUploadFailureKind.DuplicateTitle, "A project document with this display name already exists.");

        string content;
        try
        {
            var bytes = new byte[checked((int)request.ByteSize)];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = await request.Content.ReadAsync(bytes.AsMemory(offset, bytes.Length - offset), ct);
                if (read == 0)
                    throw new EndOfStreamException();
                offset += read;
            }
            content = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes)
                .TrimStart('\uFEFF');
        }
        catch (Exception error) when (error is DecoderFallbackException or EndOfStreamException)
        {
            throw Failure(ProjectDocumentUploadFailureKind.InvalidEncoding, "The selected file is not valid UTF-8 text.");
        }

        if (string.IsNullOrWhiteSpace(content) || content.Contains('\0'))
            throw Failure(ProjectDocumentUploadFailureKind.InvalidEncoding, "The selected file does not contain readable UTF-8 text.");

        ProjectDocument document;
        try
        {
            document = await _documents.CreateDocumentAsync(new CreateProjectDocumentRequest
            {
                ProjectId = request.ProjectId,
                Title = title,
                DocumentType = documentType,
                ContentMarkdown = content,
                ChangeSummary = $"Uploaded from {originalFileName}",
                CreatedBy = request.CreatedBy,
                Origin = "Uploaded",
                ProcessingStatus = "Draft",
                Description = description,
                Visibility = "Project",
                OriginalFileName = originalFileName,
                MediaType = mediaType,
                ByteSize = request.ByteSize
            }, ct);
        }
        catch (UnauthorizedAccessException)
        {
            throw Failure(ProjectDocumentUploadFailureKind.ProjectNotFound, "The project is not available for document upload.");
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            throw Failure(ProjectDocumentUploadFailureKind.DuplicateTitle, "A project document with this display name already exists.");
        }

        var version = await _documents.GetCurrentVersionAsync(document.Id, ct)
            ?? throw new InvalidOperationException("The uploaded document was created without an immutable version.");

        return new ProjectDocumentUploadResult
        {
            Document = document,
            Version = version,
            ProcessingStatus = document.ProcessingStatus
        };
    }

    private static string SafeFileName(string fileName)
    {
        var normalized = (fileName ?? string.Empty).Replace('\\', '/');
        var safe = Path.GetFileName(normalized).Trim();
        if (string.IsNullOrWhiteSpace(safe) || safe.Length > 260)
            throw Failure(ProjectDocumentUploadFailureKind.InvalidRequest, "The selected file name is invalid.");
        return safe;
    }

    private static ProjectDocumentUploadException Failure(ProjectDocumentUploadFailureKind kind, string message) =>
        new(kind, message);
}
