using Dapper;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectChatDocumentSourceService : IProjectChatDocumentSourceService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    public ProjectChatDocumentSourceService(
        IDbConnectionFactory connectionFactory,
        ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<ChatDocumentSource>> GetAvailableSourcesAsync(
        int projectId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                d.Id AS DocumentId,
                v.Id AS DocumentVersionId,
                d.Title,
                d.DocumentType,
                CONCAT('v', v.VersionMajor, '.', v.VersionMinor) AS VersionLabel,
                d.ProcessingStatus AS Status
            FROM dbo.ProjectDocuments d
            INNER JOIN dbo.ProjectDocumentVersions v ON v.Id = d.CurrentVersionId AND v.DocumentId = d.Id
            WHERE d.TenantId = @TenantId
              AND d.ProjectId = @ProjectId
              AND d.Status = 'Active'
              AND d.ProcessingStatus = 'Ready'
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.ProjectDocumentLinks sourceLink
                  INNER JOIN dbo.ProjectContextDocuments contextDocument
                      ON contextDocument.Id = sourceLink.LinkedEntityId
                     AND contextDocument.TenantId = d.TenantId
                     AND contextDocument.ProjectId = d.ProjectId
                  WHERE sourceLink.DocumentVersionId = v.Id
                    AND sourceLink.LinkedEntityType = 'ProjectContextDocument'
                    AND sourceLink.LinkType = 'IndexedAs'
                    AND contextDocument.Status = 'Active'
                    AND contextDocument.Source = CONCAT('ProjectDocumentVersion:', v.Id)
              )
            ORDER BY d.Title, v.VersionMajor DESC, v.VersionMinor DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SourceRow>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId },
            cancellationToken: cancellationToken));
        return rows.Select(row => row.Source).ToList();
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<ChatDocumentSource>>> GetSourcesForMessagesAsync(
        int projectId,
        long sessionId,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var sourceMessageIds = messages
            .Select(message => string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? message.ReplyToMessageId
                : message.Id)
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        if (sourceMessageIds.Length == 0)
            return new Dictionary<long, IReadOnlyList<ChatDocumentSource>>();

        const string sql = """
            SELECT
                sourceMessage.Id AS SourceMessageId,
                d.Id AS DocumentId,
                v.Id AS DocumentVersionId,
                d.Title,
                d.DocumentType,
                CONCAT('v', v.VersionMajor, '.', v.VersionMinor) AS VersionLabel,
                CASE WHEN d.CurrentVersionId = v.Id AND d.ProcessingStatus = 'Ready' THEN 'Ready' ELSE 'AttachedVersion' END AS Status
            FROM dbo.ChatMessages sourceMessage
            INNER JOIN dbo.ProjectDocumentLinks attachment
                ON attachment.LinkedEntityType = 'ChatMessage'
               AND attachment.LinkedEntityId = sourceMessage.Id
               AND attachment.LinkType = 'ChatContext'
            INNER JOIN dbo.ProjectDocumentVersions v ON v.Id = attachment.DocumentVersionId
            INNER JOIN dbo.ProjectDocuments d ON d.Id = v.DocumentId
            WHERE sourceMessage.Id IN @SourceMessageIds
              AND sourceMessage.TenantId = @TenantId
              AND sourceMessage.ProjectId = @ProjectId
              AND sourceMessage.ChatSessionId = @SessionId
              AND d.TenantId = @TenantId
              AND d.ProjectId = @ProjectId
            ORDER BY sourceMessage.Id, d.Title;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SourceRow>(new CommandDefinition(
            sql,
            new
            {
                SourceMessageIds = sourceMessageIds,
                TenantId = _tenant.TenantId,
                ProjectId = projectId,
                SessionId = sessionId
            },
            cancellationToken: cancellationToken));

        return rows
            .GroupBy(row => row.SourceMessageId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ChatDocumentSource>)group.Select(row => row.Source).ToList());
    }

    public async Task<IReadOnlyList<AttachedChatDocumentContext>> GetAttachedContextsAsync(
        int projectId,
        long sessionId,
        long sourceMessageId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var sourceExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(1)
            FROM dbo.ChatMessages
            WHERE Id = @SourceMessageId
              AND TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND ChatSessionId = @SessionId
              AND Role = 'user';
            """,
            new
            {
                SourceMessageId = sourceMessageId,
                TenantId = _tenant.TenantId,
                ProjectId = projectId,
                SessionId = sessionId
            },
            cancellationToken: cancellationToken));
        if (sourceExists == 0)
            throw new ChatDocumentSourceUnavailableException(
                "The source Chat message is not available in this project session.");

        const string sql = """
            SELECT
                d.Id AS DocumentId,
                v.Id AS DocumentVersionId,
                d.Title,
                d.DocumentType,
                CONCAT('v', v.VersionMajor, '.', v.VersionMinor) AS VersionLabel,
                CASE WHEN d.CurrentVersionId = v.Id AND d.ProcessingStatus = 'Ready' THEN 'Ready' ELSE 'AttachedVersion' END AS Status,
                contextDocument.*
            FROM dbo.ChatMessages sourceMessage
            INNER JOIN dbo.ProjectDocumentLinks attachment
                ON attachment.LinkedEntityType = 'ChatMessage'
               AND attachment.LinkedEntityId = sourceMessage.Id
               AND attachment.LinkType = 'ChatContext'
            INNER JOIN dbo.ProjectDocumentVersions v ON v.Id = attachment.DocumentVersionId
            INNER JOIN dbo.ProjectDocuments d ON d.Id = v.DocumentId
            INNER JOIN dbo.ProjectDocumentLinks sourceLink
                ON sourceLink.DocumentVersionId = v.Id
               AND sourceLink.LinkedEntityType = 'ProjectContextDocument'
               AND sourceLink.LinkType = 'IndexedAs'
            INNER JOIN dbo.ProjectContextDocuments contextDocument
                ON contextDocument.Id = sourceLink.LinkedEntityId
               AND contextDocument.Source = CONCAT('ProjectDocumentVersion:', v.Id)
            WHERE sourceMessage.Id = @SourceMessageId
              AND sourceMessage.TenantId = @TenantId
              AND sourceMessage.ProjectId = @ProjectId
              AND sourceMessage.ChatSessionId = @SessionId
              AND sourceMessage.Role = 'user'
              AND d.TenantId = @TenantId
              AND d.ProjectId = @ProjectId
              AND contextDocument.TenantId = @TenantId
              AND contextDocument.ProjectId = @ProjectId;
            """;

        var rows = await connection.QueryAsync<SourceRow, ProjectContextDocument, AttachedChatDocumentContext>(
            new CommandDefinition(
                sql,
                new
                {
                    SourceMessageId = sourceMessageId,
                    TenantId = _tenant.TenantId,
                    ProjectId = projectId,
                    SessionId = sessionId
                },
                cancellationToken: cancellationToken),
            (source, context) => new AttachedChatDocumentContext(source.Source, context),
            splitOn: "Id");

        return rows
            .GroupBy(row => row.Source.DocumentVersionId)
            .Select(group => group.First())
            .ToList();
    }

    private sealed class SourceRow
    {
        public long SourceMessageId { get; set; }
        public long DocumentId { get; set; }
        public long DocumentVersionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string VersionLabel { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public ChatDocumentSource Source => new(
            DocumentId,
            DocumentVersionId,
            Title,
            DocumentType,
            VersionLabel,
            Status);
    }
}
