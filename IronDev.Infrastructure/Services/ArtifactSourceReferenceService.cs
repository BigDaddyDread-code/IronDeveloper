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

}
