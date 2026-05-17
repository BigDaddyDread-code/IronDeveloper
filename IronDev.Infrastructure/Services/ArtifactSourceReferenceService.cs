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

    public async Task<IReadOnlyList<ArtifactSourceReference>> GetReferencesForArtifactAsync(
        string artifactType,
        long artifactId,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection, ct);
        var sql = @"
            SELECT *
            FROM dbo.ArtifactSourceReferences
            WHERE ArtifactType = @artifactType
              AND ArtifactId = @artifactId
            ORDER BY CreatedUtc ASC";

        var results = await connection.QueryAsync<ArtifactSourceReference>(sql, new
        {
            artifactType,
            artifactId
        });

        return results.ToList();
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

        return results.ToList();
    }

    public async Task AddAsync(ArtifactSourceReference reference, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection, ct);
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

        await connection.ExecuteAsync(sql, references);
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
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }
}
