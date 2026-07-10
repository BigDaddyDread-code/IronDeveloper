using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IronDev.Data.Models;

// =====================================================================
// Versioned Markdown Project Documents
//
// ProjectDocument        - Stable document identity (one row per doc)
// ProjectDocumentVersion - Immutable Markdown snapshot
// ProjectDocumentLink    - Polymorphic trace link to other artefacts
// =====================================================================

/// <summary>
/// Stable document identity. CurrentVersionId is a soft pointer to the
/// latest version — it carries no FK constraint in the DB to avoid a
/// circular dependency with ProjectDocumentVersions.
/// </summary>
public sealed class ProjectDocument
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Controlled values: Architecture | DiscussionSummary | BuildPlan | DecisionLog
    /// </summary>
    public string DocumentType { get; set; } = "DiscussionSummary";

    /// <summary>Soft pointer to the current version. Not a FK.</summary>
    public long? CurrentVersionId { get; set; }

    /// <summary>Active | Archived | Deleted</summary>
    public string Status { get; set; } = "Active";

    /// <summary>CreatedInIronDev | Uploaded</summary>
    public string Origin { get; set; } = "CreatedInIronDev";

    /// <summary>Uploading | Processing | Draft | Ready | ProcessingFailed | Unsupported | Superseded | Unavailable</summary>
    public string ProcessingStatus { get; set; } = "Draft";

    public string? Description { get; set; }

    /// <summary>Project | MembersOnly</summary>
    public string Visibility { get; set; } = "Project";

    public string? OriginalFileName { get; set; }
    public string? MediaType { get; set; }
    public long? ByteSize { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Immutable Markdown snapshot. ContentMarkdown must never be updated
/// after the row is inserted. Changes always produce a new version row.
/// </summary>
public sealed class ProjectDocumentVersion
{
    public long Id { get; set; }
    public long DocumentId { get; set; }

    public int VersionMajor { get; set; }
    public int VersionMinor { get; set; }

    /// <summary>Human-readable label, e.g. "v0.1", "v1.0".</summary>
    public string VersionLabel => $"v{VersionMajor}.{VersionMinor}";

    /// <summary>
    /// Canonical content. NEVER modified after creation.
    /// </summary>
    public string ContentMarkdown { get; set; } = string.Empty;

    public string? ChangeSummary { get; set; }
    public long? ParentVersionId { get; set; }

    /// <summary>Draft | Approved | Superseded | Archived</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>SHA-256 hex of ContentMarkdown. Used to detect duplicate saves.</summary>
    public string? ContentHash { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Polymorphic trace link between a document version and any IronDev artefact.
/// Mirrors the pattern used by ArtifactSourceReferences.
/// </summary>
public sealed class ProjectDocumentLink
{
    public long Id { get; set; }
    public long DocumentVersionId { get; set; }

    /// <summary>
    /// e.g. Discussion | ChatMessage | ProjectMemory | Decision | Ticket | BuildTrace | LlmTrace
    /// </summary>
    public string LinkedEntityType { get; set; } = string.Empty;

    public long LinkedEntityId { get; set; }

    /// <summary>
    /// e.g. CreatedFrom | GeneratedTicket | References | Supersedes | RefinedBy | BuildTrace | DecisionSource
    /// </summary>
    public string LinkType { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
}

// =====================================================================
// Request / DTO Models
// =====================================================================

public sealed class CreateProjectDocumentRequest
{
    public int ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Architecture | DiscussionSummary | BuildPlan | DecisionLog</summary>
    public string DocumentType { get; set; } = "DiscussionSummary";

    public string ContentMarkdown { get; set; } = string.Empty;
    public string? ChangeSummary { get; set; }
    public string? CreatedBy { get; set; }
    [JsonIgnore]
    public string Origin { get; set; } = "CreatedInIronDev";

    [JsonIgnore]
    public string ProcessingStatus { get; set; } = "Draft";

    public string? Description { get; set; }

    [JsonIgnore]
    public string Visibility { get; set; } = "Project";

    [JsonIgnore]
    public string? OriginalFileName { get; set; }

    [JsonIgnore]
    public string? MediaType { get; set; }

    [JsonIgnore]
    public long? ByteSize { get; set; }

    /// <summary>Optional source link for the initial version (e.g. a chat session).</summary>
    public string? SourceEntityType { get; set; }
    public long? SourceEntityId { get; set; }
}

public sealed class ProjectDocumentUploadRequest
{
    public int ProjectId { get; set; }
    public string? DisplayName { get; set; }
    public string DocumentType { get; set; } = "DiscussionSummary";
    public string? Description { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string? MediaType { get; set; }
    public long ByteSize { get; set; }
    public Stream Content { get; set; } = Stream.Null;
    public string? CreatedBy { get; set; }
}

public sealed class ProjectDocumentUploadResult
{
    public required ProjectDocument Document { get; init; }
    public required ProjectDocumentVersion Version { get; init; }
    public string ProcessingStatus { get; init; } = "Draft";
    public string Boundary { get; init; } =
        "The uploaded file is an immutable Draft document. It is not attached to Chat, indexed for retrieval, approved, or source-mutation authority.";
}

public enum ProjectDocumentUploadFailureKind
{
    InvalidRequest,
    UnsupportedType,
    TooLarge,
    InvalidEncoding,
    DuplicateTitle,
    ProjectNotFound
}

public sealed class ProjectDocumentUploadException : Exception
{
    public ProjectDocumentUploadException(ProjectDocumentUploadFailureKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public ProjectDocumentUploadFailureKind Kind { get; }
}

public sealed class AddProjectDocumentVersionRequest
{
    public long DocumentId { get; set; }
    public string ContentMarkdown { get; set; } = string.Empty;
    public string? ChangeSummary { get; set; }
    public string? CreatedBy { get; set; }

    /// <summary>
    /// If true, bumps VersionMajor (e.g. v0.x → v1.0 or v1.x → v2.0).
    /// If false, bumps VersionMinor only.
    /// </summary>
    public bool IncrementMajorVersion { get; set; }

    /// <summary>Draft | Approved</summary>
    public string Status { get; set; } = "Draft";
}

public sealed class LinkProjectDocumentVersionRequest
{
    public long DocumentVersionId { get; set; }
    public string LinkedEntityType { get; set; } = string.Empty;
    public long LinkedEntityId { get; set; }
    public string LinkType { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
}

public sealed class GetProjectDocumentsRequest
{
    public int ProjectId { get; set; }

    /// <summary>Null = all types.</summary>
    public string? DocumentType { get; set; }

    /// <summary>Null = Active only. Pass "*" to include all statuses.</summary>
    public string? Status { get; set; } = "Active";
}
