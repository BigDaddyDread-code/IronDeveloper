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
}
