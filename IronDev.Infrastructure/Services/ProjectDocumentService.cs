using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Services;

public sealed class ProjectDocumentService : IProjectDocumentService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    public ProjectDocumentService(
        IDbConnectionFactory connectionFactory,
        ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    // ------------------------------------------------------------------
    // Create
    // ------------------------------------------------------------------

    public async Task<ProjectDocument> CreateDocumentAsync(
        CreateProjectDocumentRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Document title is required.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.ContentMarkdown))
            throw new ArgumentException("Initial Markdown content is required.", nameof(request));

        await EnsureProjectOwnershipAsync(request.ProjectId, ct);

        using var connection = _connectionFactory.CreateConnection();

        var slug = GenerateSlug(request.Title);

        // Step 1: Insert document (no CurrentVersionId yet)
        const string insertDocSql = """
            INSERT INTO dbo.ProjectDocuments
                (TenantId, ProjectId, Title, Slug, DocumentType, Status,
                 Origin, ProcessingStatus, Description, Visibility, OriginalFileName, MediaType, ByteSize,
                 CreatedAtUtc, CreatedBy)
            OUTPUT inserted.Id, inserted.TenantId, inserted.ProjectId, inserted.Title,
                   inserted.Slug, inserted.DocumentType, inserted.CurrentVersionId,
                   inserted.Status, inserted.Origin, inserted.ProcessingStatus, inserted.Description,
                   inserted.Visibility, inserted.OriginalFileName, inserted.MediaType, inserted.ByteSize,
                   inserted.CreatedAtUtc, inserted.UpdatedAtUtc,
                   inserted.CreatedBy, inserted.UpdatedBy
            VALUES
                (@TenantId, @ProjectId, @Title, @Slug, @DocumentType, 'Active',
                 @Origin, @ProcessingStatus, @Description, @Visibility, @OriginalFileName, @MediaType, @ByteSize,
                 SYSUTCDATETIME(), @CreatedBy);
            """;

        var doc = await connection.QuerySingleAsync<ProjectDocument>(new CommandDefinition(
            insertDocSql,
            new
            {
                TenantId     = _tenant.TenantId,
                request.ProjectId,
                request.Title,
                Slug         = slug,
                request.DocumentType,
                request.Origin,
                request.ProcessingStatus,
                request.Description,
                request.Visibility,
                request.OriginalFileName,
                request.MediaType,
                request.ByteSize,
                request.CreatedBy
            },
            cancellationToken: ct));

        // Step 2: Insert initial v0.1 Draft version
        var contentHash = ComputeContentHash(request.ContentMarkdown);

        const string insertVersionSql = """
            INSERT INTO dbo.ProjectDocumentVersions
                (DocumentId, VersionMajor, VersionMinor, ContentMarkdown, ChangeSummary,
                 ParentVersionId, Status, ContentHash, CreatedAtUtc, CreatedBy)
            OUTPUT inserted.Id, inserted.DocumentId, inserted.VersionMajor, inserted.VersionMinor,
                   inserted.ContentMarkdown, inserted.ChangeSummary, inserted.ParentVersionId,
                   inserted.Status, inserted.ContentHash, inserted.CreatedAtUtc, inserted.CreatedBy
            VALUES
                (@DocumentId, 0, 1, @ContentMarkdown, @ChangeSummary,
                 NULL, 'Draft', @ContentHash, SYSUTCDATETIME(), @CreatedBy);
            """;

        var version = await connection.QuerySingleAsync<ProjectDocumentVersion>(new CommandDefinition(
            insertVersionSql,
            new
            {
                DocumentId      = doc.Id,
                request.ContentMarkdown,
                ChangeSummary   = request.ChangeSummary ?? "Initial version",
                ContentHash     = contentHash,
                request.CreatedBy
            },
            cancellationToken: ct));

        // Step 3: Update document CurrentVersionId pointer
        const string updateCurrentSql = """
            UPDATE dbo.ProjectDocuments
            SET CurrentVersionId = @VersionId,
                UpdatedAtUtc     = SYSUTCDATETIME()
            WHERE Id = @DocumentId AND TenantId = @TenantId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            updateCurrentSql,
            new { VersionId = version.Id, DocumentId = doc.Id, TenantId = _tenant.TenantId },
            cancellationToken: ct));

        doc.CurrentVersionId = version.Id;

        // Step 4: Record source link if provided
        if (!string.IsNullOrWhiteSpace(request.SourceEntityType) && request.SourceEntityId.HasValue)
        {
            await InsertLinkAsync(
                connection,
                version.Id,
                request.SourceEntityType,
                request.SourceEntityId.Value,
                "CreatedFrom",
                request.CreatedBy,
                ct);
        }

        return doc;
    }

    public async Task<ProjectDocumentVersion> AddVersionAsync(
        AddProjectDocumentVersionRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ContentMarkdown))
            throw new ArgumentException("Markdown content is required.", nameof(request));

        using var connection = _connectionFactory.CreateConnection();

        // Load document (ownership implied by TenantId filter)
        var doc = await GetDocumentInternalAsync(connection, request.DocumentId, ct)
            ?? throw new InvalidOperationException($"Document {request.DocumentId} not found.");

        // Load current version for hash check and parent tracking
        ProjectDocumentVersion? currentVersion = null;
        if (doc.CurrentVersionId.HasValue)
        {
            currentVersion = await GetVersionInternalAsync(connection, doc.CurrentVersionId.Value, ct);
        }

        // Reject duplicate content
        var newHash = ComputeContentHash(request.ContentMarkdown);
        if (currentVersion is not null && currentVersion.ContentHash == newHash)
            throw new InvalidOperationException(
                "Save rejected: the new content is identical to the current version. No changes detected.");

        // Calculate next version number
        int nextMajor, nextMinor;
        if (currentVersion is null)
        {
            nextMajor = 0;
            nextMinor = 1;
        }
        else if (request.IncrementMajorVersion)
        {
            nextMajor = currentVersion.VersionMajor + 1;
            nextMinor = 0;
        }
        else
        {
            nextMajor = currentVersion.VersionMajor;
            nextMinor = currentVersion.VersionMinor + 1;
        }

        const string insertSql = """
            INSERT INTO dbo.ProjectDocumentVersions
                (DocumentId, VersionMajor, VersionMinor, ContentMarkdown, ChangeSummary,
                 ParentVersionId, Status, ContentHash, CreatedAtUtc, CreatedBy)
            OUTPUT inserted.Id, inserted.DocumentId, inserted.VersionMajor, inserted.VersionMinor,
                   inserted.ContentMarkdown, inserted.ChangeSummary, inserted.ParentVersionId,
                   inserted.Status, inserted.ContentHash, inserted.CreatedAtUtc, inserted.CreatedBy
            VALUES
                (@DocumentId, @VersionMajor, @VersionMinor, @ContentMarkdown, @ChangeSummary,
                 @ParentVersionId, @Status, @ContentHash, SYSUTCDATETIME(), @CreatedBy);
            """;

        var version = await connection.QuerySingleAsync<ProjectDocumentVersion>(new CommandDefinition(
            insertSql,
            new
            {
                request.DocumentId,
                VersionMajor    = nextMajor,
                VersionMinor    = nextMinor,
                request.ContentMarkdown,
                request.ChangeSummary,
                ParentVersionId = currentVersion?.Id,
                request.Status,
                ContentHash     = newHash,
                request.CreatedBy
            },
            cancellationToken: ct));

        // Update CurrentVersionId pointer on document
        const string updateDocSql = """
            UPDATE dbo.ProjectDocuments
            SET CurrentVersionId = @VersionId,
                UpdatedAtUtc     = SYSUTCDATETIME(),
                UpdatedBy        = @UpdatedBy
            WHERE Id = @DocumentId AND TenantId = @TenantId;
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            updateDocSql,
            new
            {
                VersionId  = version.Id,
                DocumentId = request.DocumentId,
                UpdatedBy  = request.CreatedBy,
                TenantId   = _tenant.TenantId
            },
            cancellationToken: ct));

        return version;
    }

    // ------------------------------------------------------------------
    // Read — Documents
    // ------------------------------------------------------------------

    public async Task<ProjectDocument?> GetDocumentAsync(
        long documentId,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await GetDocumentInternalAsync(connection, documentId, ct);
    }

    public async Task<IReadOnlyList<ProjectDocument>> GetDocumentsAsync(
        GetProjectDocumentsRequest request,
        CancellationToken ct = default)
    {
        // Build dynamic WHERE clause
        var whereParts = new List<string>
        {
            "TenantId = @TenantId",
            "ProjectId = @ProjectId"
        };

        if (!string.IsNullOrWhiteSpace(request.DocumentType))
            whereParts.Add("DocumentType = @DocumentType");

        if (request.Status != "*" && !string.IsNullOrWhiteSpace(request.Status))
            whereParts.Add("Status = @Status");

        var sql = $"""
            SELECT Id, TenantId, ProjectId, Title, Slug, DocumentType, CurrentVersionId,
                   Status, Origin, ProcessingStatus, Description, Visibility,
                   OriginalFileName, MediaType, ByteSize,
                   CreatedAtUtc, UpdatedAtUtc, CreatedBy, UpdatedBy
            FROM dbo.ProjectDocuments
            WHERE {string.Join(" AND ", whereParts)}
            ORDER BY UpdatedAtUtc DESC, CreatedAtUtc DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProjectDocument>(new CommandDefinition(
            sql,
            new
            {
                TenantId         = _tenant.TenantId,
                request.ProjectId,
                request.DocumentType,
                Status           = request.Status ?? "Active"
            },
            cancellationToken: ct));

        return rows.ToList();
    }

    // ------------------------------------------------------------------
    // Read — Versions
    // ------------------------------------------------------------------

    public async Task<ProjectDocumentVersion?> GetCurrentVersionAsync(
        long documentId,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var doc = await GetDocumentInternalAsync(connection, documentId, ct);
        if (doc?.CurrentVersionId is null)
            return null;

        return await GetVersionInternalAsync(connection, doc.CurrentVersionId.Value, ct);
    }

    public async Task<ProjectDocumentVersion?> GetVersionAsync(
        long documentVersionId,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await GetVersionInternalAsync(connection, documentVersionId, ct);
    }

    public async Task<IReadOnlyList<ProjectDocumentVersion>> GetVersionHistoryAsync(
        long documentId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT v.Id, v.DocumentId, v.VersionMajor, v.VersionMinor, v.ContentMarkdown,
                   v.ChangeSummary, v.ParentVersionId, v.Status, v.ContentHash, v.CreatedAtUtc, v.CreatedBy
            FROM dbo.ProjectDocumentVersions v
            INNER JOIN dbo.ProjectDocuments d ON d.Id = v.DocumentId
            WHERE v.DocumentId = @DocumentId
              AND d.TenantId = @TenantId
            ORDER BY v.VersionMajor DESC, v.VersionMinor DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProjectDocumentVersion>(new CommandDefinition(
            sql,
            new { DocumentId = documentId, TenantId = _tenant.TenantId },
            cancellationToken: ct));

        return rows.ToList();
    }

    // ------------------------------------------------------------------
    // Links
    // ------------------------------------------------------------------

    public async Task LinkVersionAsync(
        LinkProjectDocumentVersionRequest request,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var version = await GetVersionInternalAsync(connection, request.DocumentVersionId, ct);
        if (version == null)
            throw new UnauthorizedAccessException(
                $"Document version {request.DocumentVersionId} does not belong to tenant {_tenant.TenantId}.");

        // Idempotency: skip if identical link already exists
        const string existsSql = """
            SELECT COUNT(1)
            FROM dbo.ProjectDocumentLinks l
            INNER JOIN dbo.ProjectDocumentVersions v ON v.Id = l.DocumentVersionId
            INNER JOIN dbo.ProjectDocuments d ON d.Id = v.DocumentId
            WHERE l.DocumentVersionId = @DocumentVersionId
              AND l.LinkedEntityType  = @LinkedEntityType
              AND l.LinkedEntityId    = @LinkedEntityId
              AND l.LinkType          = @LinkType
              AND d.TenantId          = @TenantId;
            """;

        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            existsSql,
            new
            {
                request.DocumentVersionId,
                request.LinkedEntityType,
                request.LinkedEntityId,
                request.LinkType,
                TenantId = _tenant.TenantId
            },
            cancellationToken: ct));

        if (exists > 0)
            return;

        await InsertLinkAsync(
            connection,
            request.DocumentVersionId,
            request.LinkedEntityType,
            request.LinkedEntityId,
            request.LinkType,
            request.CreatedBy,
            ct);
    }

    public async Task<IReadOnlyList<ProjectDocumentLink>> GetLinksForVersionAsync(
        long documentVersionId,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT l.Id, l.DocumentVersionId, l.LinkedEntityType, l.LinkedEntityId,
                   l.LinkType, l.CreatedAtUtc, l.CreatedBy
            FROM dbo.ProjectDocumentLinks l
            INNER JOIN dbo.ProjectDocumentVersions v ON v.Id = l.DocumentVersionId
            INNER JOIN dbo.ProjectDocuments d ON d.Id = v.DocumentId
            WHERE l.DocumentVersionId = @DocumentVersionId
              AND d.TenantId = @TenantId
            ORDER BY l.CreatedAtUtc ASC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProjectDocumentLink>(new CommandDefinition(
            sql,
            new { DocumentVersionId = documentVersionId, TenantId = _tenant.TenantId },
            cancellationToken: ct));

        return rows.ToList();
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    public async Task ArchiveDocumentAsync(
        long documentId,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dbo.ProjectDocuments
            SET Status       = 'Archived',
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE Id = @DocumentId AND TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { DocumentId = documentId, TenantId = _tenant.TenantId },
            cancellationToken: ct));
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private async Task<ProjectDocument?> GetDocumentInternalAsync(
        System.Data.IDbConnection connection,
        long documentId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT Id, TenantId, ProjectId, Title, Slug, DocumentType, CurrentVersionId,
                   Status, Origin, ProcessingStatus, Description, Visibility,
                   OriginalFileName, MediaType, ByteSize,
                   CreatedAtUtc, UpdatedAtUtc, CreatedBy, UpdatedBy
            FROM dbo.ProjectDocuments
            WHERE Id = @DocumentId AND TenantId = @TenantId;
            """;

        return await connection.QuerySingleOrDefaultAsync<ProjectDocument>(new CommandDefinition(
            sql,
            new { DocumentId = documentId, TenantId = _tenant.TenantId },
            cancellationToken: ct));
    }

    private async Task<ProjectDocumentVersion?> GetVersionInternalAsync(
        System.Data.IDbConnection connection,
        long versionId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT v.Id, v.DocumentId, v.VersionMajor, v.VersionMinor, v.ContentMarkdown,
                   v.ChangeSummary, v.ParentVersionId, v.Status, v.ContentHash, v.CreatedAtUtc, v.CreatedBy
            FROM dbo.ProjectDocumentVersions v
            INNER JOIN dbo.ProjectDocuments d ON d.Id = v.DocumentId
            WHERE v.Id = @VersionId
              AND d.TenantId = @TenantId;
            """;

        return await connection.QuerySingleOrDefaultAsync<ProjectDocumentVersion>(new CommandDefinition(
            sql,
            new { VersionId = versionId, TenantId = _tenant.TenantId },
            cancellationToken: ct));
    }

    private async Task EnsureProjectOwnershipAsync(int projectId, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql,
            new { ProjectId = projectId, TenantId = _tenant.TenantId },
            cancellationToken: ct));

        if (owns == 0)
            throw new UnauthorizedAccessException(
                $"Project {projectId} does not belong to tenant {_tenant.TenantId}.");
    }

    private static async Task InsertLinkAsync(
        System.Data.IDbConnection connection,
        long documentVersionId,
        string linkedEntityType,
        long linkedEntityId,
        string linkType,
        string? createdBy,
        CancellationToken ct)
    {
        const string sql = """
            INSERT INTO dbo.ProjectDocumentLinks
                (DocumentVersionId, LinkedEntityType, LinkedEntityId, LinkType, CreatedAtUtc, CreatedBy)
            VALUES
                (@DocumentVersionId, @LinkedEntityType, @LinkedEntityId, @LinkType, SYSUTCDATETIME(), @CreatedBy);
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { documentVersionId, linkedEntityType, linkedEntityId, linkType, createdBy },
            cancellationToken: ct));
    }

    /// <summary>SHA-256 hex of UTF-8 content for duplicate-save detection.</summary>
    private static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Generates a URL-safe slug from a document title.
    /// Lowercase, hyphens only, max 280 chars.
    /// </summary>
    private static string GenerateSlug(string title)
    {
        var slug = title.ToLowerInvariant();
        var sb = new StringBuilder(slug.Length);

        foreach (var c in slug)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else if (c is ' ' or '-' or '_')
                sb.Append('-');
        }

        // Collapse repeated hyphens
        var result = sb.ToString();
        while (result.Contains("--"))
            result = result.Replace("--", "-");

        return result.Trim('-').Length > 280
            ? result.Trim('-')[..280]
            : result.Trim('-');
    }
}
