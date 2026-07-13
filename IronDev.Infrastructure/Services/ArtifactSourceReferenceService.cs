using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class ArtifactSourceReferenceService : IArtifactSourceReferenceService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ArtifactSourceReferenceService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task RecordReferenceAsync(ArtifactSourceReference reference, CancellationToken ct = default)
    {
        await AddAsync(reference, ct);
    }

    public async Task<IReadOnlyList<ArtifactSourceReference>> GetForArtifactAsync(
        int tenantId,
        int projectId,
        string artifactType,
        long artifactId,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection, ct);
        var sql = @"
            SELECT *
            FROM dbo.ArtifactSourceReferences
            WHERE TenantId = @tenantId
              AND ProjectId = @projectId
              AND ArtifactType = @artifactType
              AND ArtifactId = @artifactId
            ORDER BY CreatedUtc ASC";

        var results = await connection.QueryAsync<ArtifactSourceReference>(sql, new
        {
            tenantId,
            projectId,
            artifactType,
            artifactId
        });

        return await FilterValidAsync(connection, results, ct);
    }

    public async Task AddAsync(ArtifactSourceReference reference, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection, ct);
        await RequireValidAsync(connection, reference, ct);
        var sql = @"
            INSERT INTO dbo.ArtifactSourceReferences
            (
                TenantId, ProjectId, ArtifactType, ArtifactId,
                SourceType, SourceId, SourcePath, SourceSymbol,
                SourceSection, SourceAnchor, ReferenceType, Summary,
                RelevanceScore, IsRequired, CreatedBy, CreatedUtc
            )
            VALUES
            (
                @TenantId, @ProjectId, @ArtifactType, @ArtifactId,
                @SourceType, @SourceId, @SourcePath, @SourceSymbol,
                @SourceSection, @SourceAnchor, @ReferenceType, @Summary,
                @RelevanceScore, @IsRequired, @CreatedBy, SYSUTCDATETIME()
            )";

        await connection.ExecuteAsync(sql, reference);
    }

    public async Task AddManyAsync(IEnumerable<ArtifactSourceReference> references, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection, ct);
        var materialized = references.ToArray();
        foreach (var reference in materialized)
            await RequireValidAsync(connection, reference, ct);
        var sql = @"
            INSERT INTO dbo.ArtifactSourceReferences
            (
                TenantId, ProjectId, ArtifactType, ArtifactId,
                SourceType, SourceId, SourcePath, SourceSymbol,
                SourceSection, SourceAnchor, ReferenceType, Summary,
                RelevanceScore, IsRequired, CreatedBy, CreatedUtc
            )
            VALUES
            (
                @TenantId, @ProjectId, @ArtifactType, @ArtifactId,
                @SourceType, @SourceId, @SourcePath, @SourceSymbol,
                @SourceSection, @SourceAnchor, @ReferenceType, @Summary,
                @RelevanceScore, @IsRequired, @CreatedBy, SYSUTCDATETIME()
            )";

        await connection.ExecuteAsync(sql, materialized);
    }

    public async Task DeleteForArtifactAsync(
        int tenantId,
        int projectId,
        string artifactType,
        long artifactId,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection, ct);
        var sql = @"
            DELETE FROM dbo.ArtifactSourceReferences
            WHERE TenantId = @tenantId
              AND ProjectId = @projectId
              AND ArtifactType = @artifactType
              AND ArtifactId = @artifactId";

        await connection.ExecuteAsync(sql, new
        {
            tenantId,
            projectId,
            artifactType,
            artifactId
        });
    }

    private static async Task<IReadOnlyList<ArtifactSourceReference>> FilterValidAsync(
        System.Data.IDbConnection connection,
        IEnumerable<ArtifactSourceReference> references,
        CancellationToken ct)
    {
        var valid = new List<ArtifactSourceReference>();
        foreach (var reference in references)
        {
            if (await IsValidAsync(connection, reference, ct))
                valid.Add(reference);
        }

        return valid;
    }

    private static async Task RequireValidAsync(
        System.Data.IDbConnection connection,
        ArtifactSourceReference reference,
        CancellationToken ct)
    {
        if (!await IsValidAsync(connection, reference, ct))
            throw new InvalidOperationException("The source reference is malformed or outside the artifact project scope.");
    }

    private static async Task<bool> IsValidAsync(
        System.Data.IDbConnection connection,
        ArtifactSourceReference reference,
        CancellationToken ct)
    {
        if (reference.TenantId <= 0 || reference.ProjectId <= 0 || reference.ArtifactId <= 0 ||
            reference.SourceId is not > 0 || string.IsNullOrWhiteSpace(reference.ReferenceType))
            return false;

        var artifactSql = reference.ArtifactType switch
        {
            "Ticket" or "BuilderProposal" => "SELECT COUNT(1) FROM dbo.ProjectTickets WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@Id AND IsDeleted=0;",
            "ImplementationPlan" => "SELECT COUNT(1) FROM dbo.ProjectImplementationPlans WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@Id;",
            "Decision" => "SELECT COUNT(1) FROM dbo.ProjectDecisions WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@Id;",
            "ProjectContextDocument" => "SELECT COUNT(1) FROM dbo.ProjectContextDocuments WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@Id;",
            _ => null
        };
        var sourceSql = reference.SourceType switch
        {
            "Ticket" => "SELECT COUNT(1) FROM dbo.ProjectTickets WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@Id AND IsDeleted=0;",
            "ChatSession" => "SELECT COUNT(1) FROM dbo.ProjectChatSessions WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@Id;",
            "ChatMessage" => "SELECT COUNT(1) FROM dbo.ChatMessages WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@Id;",
            "ProjectDocumentVersion" => "SELECT COUNT(1) FROM dbo.ProjectDocumentVersions v INNER JOIN dbo.ProjectDocuments d ON d.Id=v.DocumentId WHERE d.TenantId=@TenantId AND d.ProjectId=@ProjectId AND v.Id=@Id;",
            "DiscussionDocument" => "SELECT COUNT(1) FROM dbo.ProjectDocuments WHERE TenantId=@TenantId AND ProjectId=@ProjectId AND Id=@Id;",
            _ => null
        };

        if (artifactSql is null || sourceSql is null)
            return false;

        var scope = new { reference.TenantId, reference.ProjectId };
        var projectExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.Projects WHERE TenantId=@TenantId AND Id=@ProjectId;",
            scope,
            cancellationToken: ct)) == 1;
        if (!projectExists)
            return false;

        var artifactExists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            artifactSql,
            new { reference.TenantId, reference.ProjectId, Id = reference.ArtifactId },
            cancellationToken: ct)) == 1;
        if (!artifactExists)
            return false;

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sourceSql,
            new { reference.TenantId, reference.ProjectId, Id = reference.SourceId.Value },
            cancellationToken: ct)) == 1;
    }

    private static async Task EnsureSchemaAsync(System.Data.IDbConnection connection, CancellationToken ct)
    {
        const string sql = """
            IF OBJECT_ID('dbo.ArtifactSourceReferences', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ArtifactSourceReferences
                (
                    ArtifactSourceReferenceId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    ArtifactType NVARCHAR(100) NOT NULL,
                    ArtifactId BIGINT NOT NULL,
                    SourceType NVARCHAR(100) NOT NULL,
                    SourceId BIGINT NULL,
                    SourcePath NVARCHAR(1000) NULL,
                    SourceSymbol NVARCHAR(500) NULL,
                    SourceSection NVARCHAR(500) NULL,
                    SourceAnchor NVARCHAR(500) NULL,
                    ReferenceType NVARCHAR(100) NOT NULL,
                    Summary NVARCHAR(MAX) NULL,
                    RelevanceScore DECIMAL(9,4) NULL,
                    IsRequired BIT NOT NULL CONSTRAINT DF_ArtifactSourceReferences_IsRequired DEFAULT 0,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ArtifactSourceReferences_CreatedUtc DEFAULT SYSUTCDATETIME(),
                    CreatedBy NVARCHAR(200) NULL
                );

                CREATE INDEX IX_ArtifactSourceReferences_Artifact
                    ON dbo.ArtifactSourceReferences(TenantId, ProjectId, ArtifactType, ArtifactId);

                CREATE INDEX IX_ArtifactSourceReferences_Source
                    ON dbo.ArtifactSourceReferences(TenantId, ProjectId, SourceType, SourceId);
            END

            IF COL_LENGTH('dbo.ArtifactSourceReferences', 'SourcePath') IS NULL
            BEGIN
                ALTER TABLE dbo.ArtifactSourceReferences ADD SourcePath NVARCHAR(1000) NULL;
            END

            IF COL_LENGTH('dbo.ArtifactSourceReferences', 'SourceSymbol') IS NULL
            BEGIN
                ALTER TABLE dbo.ArtifactSourceReferences ADD SourceSymbol NVARCHAR(500) NULL;
            END

            IF COL_LENGTH('dbo.ArtifactSourceReferences', 'SourceSection') IS NULL
            BEGIN
                ALTER TABLE dbo.ArtifactSourceReferences ADD SourceSection NVARCHAR(500) NULL;
            END

            IF COL_LENGTH('dbo.ArtifactSourceReferences', 'SourceAnchor') IS NULL
            BEGIN
                ALTER TABLE dbo.ArtifactSourceReferences ADD SourceAnchor NVARCHAR(500) NULL;
            END

            IF COL_LENGTH('dbo.ArtifactSourceReferences', 'ReferenceType') IS NULL
            BEGIN
                ALTER TABLE dbo.ArtifactSourceReferences ADD ReferenceType NVARCHAR(100) NOT NULL
                    CONSTRAINT DF_ArtifactSourceReferences_ReferenceType DEFAULT 'References';
            END

            IF COL_LENGTH('dbo.ArtifactSourceReferences', 'RelationshipType') IS NOT NULL
               AND NOT EXISTS
               (
                   SELECT 1
                   FROM sys.default_constraints dc
                   INNER JOIN sys.columns c
                       ON c.object_id = dc.parent_object_id
                      AND c.column_id = dc.parent_column_id
                   WHERE dc.parent_object_id = OBJECT_ID('dbo.ArtifactSourceReferences')
                     AND c.name = 'RelationshipType'
               )
            BEGIN
                ALTER TABLE dbo.ArtifactSourceReferences ADD
                    CONSTRAINT DF_ArtifactSourceReferences_RelationshipType DEFAULT 'References' FOR RelationshipType;
            END

            IF COL_LENGTH('dbo.ArtifactSourceReferences', 'Summary') IS NULL
            BEGIN
                ALTER TABLE dbo.ArtifactSourceReferences ADD Summary NVARCHAR(MAX) NULL;
            END

            IF COL_LENGTH('dbo.ArtifactSourceReferences', 'RelevanceScore') IS NULL
            BEGIN
                ALTER TABLE dbo.ArtifactSourceReferences ADD RelevanceScore DECIMAL(9,4) NULL;
            END

            IF COL_LENGTH('dbo.ArtifactSourceReferences', 'IsRequired') IS NULL
            BEGIN
                ALTER TABLE dbo.ArtifactSourceReferences ADD IsRequired BIT NOT NULL
                    CONSTRAINT DF_ArtifactSourceReferences_IsRequired DEFAULT 0;
            END

            IF COL_LENGTH('dbo.ArtifactSourceReferences', 'CreatedBy') IS NULL
            BEGIN
                ALTER TABLE dbo.ArtifactSourceReferences ADD CreatedBy NVARCHAR(200) NULL;
            END
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }
}
