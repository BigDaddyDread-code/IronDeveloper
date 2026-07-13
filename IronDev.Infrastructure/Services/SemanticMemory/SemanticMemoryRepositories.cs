using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Data;

namespace IronDev.Infrastructure.Services.SemanticMemory;

public sealed class SemanticArtefactRepository : ISemanticArtefactRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SemanticArtefactRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task UpsertArtefactAsync(SemanticArtefactDraft artefact, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            MERGE dbo.SemanticArtefacts AS target
            USING (SELECT @ProjectId AS ProjectId, @SourceEntityType AS SourceEntityType, @SourceEntityId AS SourceEntityId, @SourceVersionId AS SourceVersionId) AS source
            ON target.ProjectId = source.ProjectId
               AND target.SourceEntityType = source.SourceEntityType
               AND target.SourceEntityId = source.SourceEntityId
               AND ISNULL(target.SourceVersionId, '') = ISNULL(source.SourceVersionId, '')
            WHEN MATCHED THEN
                UPDATE SET
                    TenantId = @TenantId,
                    ArtefactType = @ArtefactType,
                    AuthorityLevel = @AuthorityLevel,
                    Title = @Title,
                    Summary = @Summary,
                    ContentHash = @ContentHash,
                    IsStale = 0,
                    UpdatedUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT
                (
                    Id, TenantId, ProjectId, SourceEntityType, SourceEntityId, SourceVersionId,
                    ArtefactType, AuthorityLevel, Title, Summary, ContentHash, IsStale, CreatedUtc, UpdatedUtc
                )
                VALUES
                (
                    @Id, @TenantId, @ProjectId, @SourceEntityType, @SourceEntityId, @SourceVersionId,
                    @ArtefactType, @AuthorityLevel, @Title, @Summary, @ContentHash, 0, SYSUTCDATETIME(), SYSUTCDATETIME()
                );
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, artefact, cancellationToken: ct));
    }

    public async Task<SemanticArtefact?> GetArtefactAsync(Guid artefactId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SemanticArtefact>(new CommandDefinition(
            "SELECT * FROM dbo.SemanticArtefacts WHERE Id = @ArtefactId;",
            new { ArtefactId = artefactId },
            cancellationToken: ct));
    }

    public async Task<SemanticArtefact?> GetArtefactBySourceAsync(
        int projectId,
        string sourceEntityType,
        string sourceEntityId,
        string? sourceVersionId = null,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SemanticArtefact>(new CommandDefinition(
            """
            SELECT TOP (1) *
            FROM dbo.SemanticArtefacts
            WHERE ProjectId = @ProjectId
              AND SourceEntityType = @SourceEntityType
              AND SourceEntityId = @SourceEntityId
              AND ISNULL(SourceVersionId, '') = ISNULL(@SourceVersionId, '')
            ORDER BY UpdatedUtc DESC;
            """,
            new { ProjectId = projectId, SourceEntityType = sourceEntityType, SourceEntityId = sourceEntityId, SourceVersionId = sourceVersionId },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SemanticArtefact>> GetProjectArtefactsAsync(int projectId, bool includeStale = false, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SemanticArtefact>(new CommandDefinition(
            """
            SELECT *
            FROM dbo.SemanticArtefacts
            WHERE ProjectId = @ProjectId
              AND (@IncludeStale = 1 OR IsStale = 0)
            ORDER BY UpdatedUtc DESC;
            """,
            new { ProjectId = projectId, IncludeStale = includeStale },
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task MarkStaleAsync(SemanticStaleRequest request, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.SemanticArtefacts
            SET IsStale = 1,
                UpdatedUtc = SYSUTCDATETIME()
            WHERE ProjectId = @ProjectId
              AND SourceEntityType = @SourceEntityType
              AND SourceEntityId = @SourceEntityId
              AND (@SourceVersionId IS NULL OR SourceVersionId = @SourceVersionId);
            """,
            request,
            cancellationToken: ct));
    }
}
public sealed class SemanticChunkRepository : ISemanticChunkRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SemanticChunkRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task ReplaceChunksAsync(Guid artefactId, IReadOnlyList<SemanticChunkDraft> chunks, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.SemanticChunks SET IsStale = 1 WHERE ArtefactId = @ArtefactId;",
            new { ArtefactId = artefactId },
            cancellationToken: ct));

        const string upsert = """
            MERGE dbo.SemanticChunks AS target
            USING (SELECT @Id AS Id) AS source
            ON target.Id = source.Id
            WHEN MATCHED THEN
                UPDATE SET
                    ArtefactId = @ArtefactId,
                    ProjectId = @ProjectId,
                    ChunkIndex = @ChunkIndex,
                    ChunkText = @ChunkText,
                    TokenEstimate = @TokenEstimate,
                    ContentHash = @ContentHash,
                    IsStale = 0
            WHEN NOT MATCHED THEN
                INSERT
                    (Id, ArtefactId, ProjectId, ChunkIndex, ChunkText, TokenEstimate, ContentHash, IsStale, CreatedUtc)
                VALUES
                    (@Id, @ArtefactId, @ProjectId, @ChunkIndex, @ChunkText, @TokenEstimate, @ContentHash, 0, SYSUTCDATETIME());
            """;
        foreach (var chunk in chunks)
            await connection.ExecuteAsync(new CommandDefinition(upsert, chunk, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SemanticChunk>> GetChunksAsync(Guid artefactId, bool includeStale = false, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SemanticChunk>(new CommandDefinition(
            """
            SELECT *
            FROM dbo.SemanticChunks
            WHERE ArtefactId = @ArtefactId
              AND (@IncludeStale = 1 OR IsStale = 0)
            ORDER BY ChunkIndex;
            """,
            new { ArtefactId = artefactId, IncludeStale = includeStale },
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<SemanticChunk?> GetChunkAsync(Guid chunkId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SemanticChunk>(new CommandDefinition(
            "SELECT * FROM dbo.SemanticChunks WHERE Id = @ChunkId;",
            new { ChunkId = chunkId },
            cancellationToken: ct));
    }

    public async Task MarkProjectStaleAsync(int projectId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.SemanticChunks SET IsStale = 1 WHERE ProjectId = @ProjectId;",
            new { ProjectId = projectId },
            cancellationToken: ct));
    }

    public async Task MarkArtefactChunksStaleAsync(Guid artefactId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.SemanticChunks SET IsStale = 1 WHERE ArtefactId = @ArtefactId;",
            new { ArtefactId = artefactId },
            cancellationToken: ct));
    }

    public async Task MarkEmbeddedAsync(Guid chunkId, string weaviateObjectId, string model, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.SemanticChunks
            SET WeaviateObjectId = @WeaviateObjectId,
                EmbeddedAtUtc = SYSUTCDATETIME(),
                EmbeddingModel = @Model,
                IsStale = 0
            WHERE Id = @ChunkId;
            """,
            new { ChunkId = chunkId, WeaviateObjectId = weaviateObjectId, Model = model },
            cancellationToken: ct));
    }
}

public sealed class EmbeddingJobRepository : IEmbeddingJobRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public EmbeddingJobRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(EmbeddingJob job, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            INSERT INTO dbo.EmbeddingJobs
                (Id, TenantId, ProjectId, SourceEntityType, SourceEntityId, SourceVersionId, JobType, Status, Attempts, LastError, CreatedUtc)
            VALUES
                (@Id, @TenantId, @ProjectId, @SourceEntityType, @SourceEntityId, @SourceVersionId, @JobType, @Status, @Attempts, @LastError, SYSUTCDATETIME());
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, job, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<EmbeddingJob>> GetPendingAsync(int take = 25, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<EmbeddingJob>(new CommandDefinition(
            """
            SELECT TOP (@Take) *
            FROM dbo.EmbeddingJobs
            WHERE Status = 'Pending'
            ORDER BY CreatedUtc;
            """,
            new { Take = take },
            cancellationToken: ct));
        return rows.ToList();
    }

    public Task MarkProcessingAsync(Guid jobId, CancellationToken ct = default)
        => UpdateStatusAsync(jobId, "Processing", null, ct);

    public Task MarkCompletedAsync(Guid jobId, CancellationToken ct = default)
        => UpdateStatusAsync(jobId, "Completed", null, ct);

    public async Task MarkFailedAsync(Guid jobId, string error, int maxAttempts = 5, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.EmbeddingJobs
            SET Attempts = Attempts + 1,
                Status = CASE WHEN Attempts + 1 >= @MaxAttempts THEN 'DeadLettered' ELSE 'Failed' END,
                LastError = @Error,
                CompletedUtc = SYSUTCDATETIME()
            WHERE Id = @JobId;
            """,
            new { JobId = jobId, Error = error, MaxAttempts = maxAttempts },
            cancellationToken: ct));
    }

    private async Task UpdateStatusAsync(Guid jobId, string status, string? error, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.EmbeddingJobs
            SET Status = @Status,
                StartedUtc = CASE WHEN @Status = 'Processing' THEN SYSUTCDATETIME() ELSE StartedUtc END,
                CompletedUtc = CASE WHEN @Status = 'Completed' THEN SYSUTCDATETIME() ELSE CompletedUtc END,
                LastError = @Error
            WHERE Id = @JobId;
            """,
            new { JobId = jobId, Status = status, Error = error },
            cancellationToken: ct));
    }
}

public sealed class SemanticSearchTraceRepository : ISemanticSearchTraceRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SemanticSearchTraceRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Guid> CreateTraceAsync(SemanticSearchQuery query, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.SemanticSearchTraces
                (Id, ProjectId, QueryText, Consumer, CreatedUtc)
            VALUES
                (@Id, @ProjectId, @QueryText, @Consumer, SYSUTCDATETIME());
            """,
            new { Id = id, query.ProjectId, QueryText = query.QueryText, query.Consumer },
            cancellationToken: ct));
        return id;
    }

    public async Task AddResultsAsync(Guid traceId, IReadOnlyList<SemanticSearchResult> results, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            INSERT INTO dbo.SemanticSearchTraceResults
                (Id, SearchTraceId, ArtefactId, ChunkId, VectorSimilarity, FinalScore, AuthorityBoost, RecencyBoost, SourceTypeBoost, ExplicitLinkBoost, StalePenalty, MatchReason)
            VALUES
                (@Id, @SearchTraceId, @ArtefactId, @ChunkId, @VectorSimilarity, @FinalScore, @AuthorityBoost, @RecencyBoost, @SourceTypeBoost, @ExplicitLinkBoost, @StalePenalty, @MatchReason);
            """;

        foreach (var result in results.Where(r => r.ArtefactId != Guid.Empty && r.ChunkId != Guid.Empty))
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = Guid.NewGuid(),
                SearchTraceId = traceId,
                result.ArtefactId,
                result.ChunkId,
                result.VectorSimilarity,
                result.FinalScore,
                result.AuthorityBoost,
                result.RecencyBoost,
                result.SourceTypeBoost,
                result.ExplicitLinkBoost,
                result.StalePenalty,
                result.MatchReason
            }, cancellationToken: ct));
        }
    }
}
