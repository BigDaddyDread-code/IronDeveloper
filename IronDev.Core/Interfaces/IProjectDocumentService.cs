using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Data.Models;

namespace IronDev.Core.Interfaces;

/// <summary>
/// Core service for creating, versioning, and retrieving Markdown project documents.
/// Enforces the immutability contract: content changes always produce a new version.
/// </summary>
public interface IProjectDocumentService
{
    // ------------------------------------------------------------------
    // Create
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates a new document with an initial v0.1 Draft version.
    /// Optionally records a source link (e.g. CreatedFrom a chat session).
    /// </summary>
    Task<ProjectDocument> CreateDocumentAsync(
        CreateProjectDocumentRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Adds a new immutable version to an existing document.
    /// Rejects saves where content hash matches the current version.
    /// Updates the document's CurrentVersionId pointer.
    /// </summary>
    Task<ProjectDocumentVersion> AddVersionAsync(
        AddProjectDocumentVersionRequest request,
        CancellationToken ct = default);

    // ------------------------------------------------------------------
    // Read — Documents
    // ------------------------------------------------------------------

    Task<ProjectDocument?> GetDocumentAsync(
        long documentId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns documents for a project, with optional type and status filters.
    /// Defaults to Status = "Active".
    /// </summary>
    Task<IReadOnlyList<ProjectDocument>> GetDocumentsAsync(
        GetProjectDocumentsRequest request,
        CancellationToken ct = default);

    // ------------------------------------------------------------------
    // Read — Versions
    // ------------------------------------------------------------------

    /// <summary>Returns the current (latest) version content for a document.</summary>
    Task<ProjectDocumentVersion?> GetCurrentVersionAsync(
        long documentId,
        CancellationToken ct = default);

    Task<ProjectDocumentVersion?> GetVersionAsync(
        long documentVersionId,
        CancellationToken ct = default);

    /// <summary>Returns all versions for a document, newest first.</summary>
    Task<IReadOnlyList<ProjectDocumentVersion>> GetVersionHistoryAsync(
        long documentId,
        CancellationToken ct = default);

    // ------------------------------------------------------------------
    // Links
    // ------------------------------------------------------------------

    /// <summary>
    /// Records a trace link between a document version and another artefact.
    /// Idempotent — will not create duplicate (VersionId + EntityType + EntityId + LinkType) entries.
    /// </summary>
    Task LinkVersionAsync(
        LinkProjectDocumentVersionRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProjectDocumentLink>> GetLinksForVersionAsync(
        long documentVersionId,
        CancellationToken ct = default);

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    /// <summary>Soft-archives a document (Status = "Archived"). Does not delete versions.</summary>
    Task ArchiveDocumentAsync(
        long documentId,
        CancellationToken ct = default);
}
