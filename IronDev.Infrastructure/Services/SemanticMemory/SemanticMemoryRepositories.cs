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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.SemanticChunks SET IsStale = 1 WHERE ArtefactId = @ArtefactId;",
            new { ArtefactId = artefactId },
            cancellationToken: ct));

        const string insert = """
            INSERT INTO dbo.SemanticChunks
                (Id, ArtefactId, ProjectId, ChunkIndex, ChunkText, TokenEstimate, ContentHash, IsStale, CreatedUtc)
            VALUES
                (@Id, @ArtefactId, @ProjectId, @ChunkIndex, @ChunkText, @TokenEstimate, @ContentHash, 0, SYSUTCDATETIME());
            """;
        foreach (var chunk in chunks)
            await connection.ExecuteAsync(new CommandDefinition(insert, chunk, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SemanticChunk>> GetChunksAsync(Guid artefactId, bool includeStale = false, CancellationToken ct = default)
    {
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<SemanticChunk>(new CommandDefinition(
            "SELECT * FROM dbo.SemanticChunks WHERE Id = @ChunkId;",
            new { ChunkId = chunkId },
            cancellationToken: ct));
    }

    public async Task MarkProjectStaleAsync(int projectId, CancellationToken ct = default)
    {
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.SemanticChunks SET IsStale = 1 WHERE ProjectId = @ProjectId;",
            new { ProjectId = projectId },
            cancellationToken: ct));
    }

    public async Task MarkArtefactChunksStaleAsync(Guid artefactId, CancellationToken ct = default)
    {
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.SemanticChunks SET IsStale = 1 WHERE ArtefactId = @ArtefactId;",
            new { ArtefactId = artefactId },
            cancellationToken: ct));
    }

    public async Task MarkEmbeddedAsync(Guid chunkId, string weaviateObjectId, string model, CancellationToken ct = default)
    {
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
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

internal static class SemanticMemorySchema
{
    public static async Task EnsureAsync(IDbConnectionFactory connectionFactory, CancellationToken ct)
    {
        using var connection = connectionFactory.CreateConnection();
        const string sql = """
            IF OBJECT_ID('dbo.SemanticSearchTraces', 'U') IS NOT NULL
               AND EXISTS
               (
                   SELECT 1
                   FROM INFORMATION_SCHEMA.COLUMNS
                   WHERE TABLE_SCHEMA = 'dbo'
                     AND TABLE_NAME = 'SemanticSearchTraces'
                     AND COLUMN_NAME = 'Id'
                     AND DATA_TYPE <> 'uniqueidentifier'
               )
            BEGIN
                IF OBJECT_ID('dbo.SemanticSearchTraceResults', 'U') IS NOT NULL
                    DROP TABLE dbo.SemanticSearchTraceResults;
                DROP TABLE dbo.SemanticSearchTraces;
            END

            IF OBJECT_ID('dbo.SemanticArtefacts', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SemanticArtefacts
                (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    TenantId INT NULL,
                    ProjectId INT NOT NULL,
                    SourceEntityType NVARCHAR(100) NOT NULL,
                    SourceEntityId NVARCHAR(100) NOT NULL,
                    SourceVersionId NVARCHAR(100) NULL,
                    ArtefactType NVARCHAR(100) NOT NULL,
                    AuthorityLevel NVARCHAR(50) NOT NULL,
                    Title NVARCHAR(500) NOT NULL,
                    Summary NVARCHAR(MAX) NULL,
                    ContentHash NVARCHAR(128) NOT NULL,
                    IsStale BIT NOT NULL CONSTRAINT DF_SemanticArtefacts_IsStale DEFAULT 0,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_SemanticArtefacts_CreatedUtc DEFAULT SYSUTCDATETIME(),
                    UpdatedUtc DATETIME2 NOT NULL CONSTRAINT DF_SemanticArtefacts_UpdatedUtc DEFAULT SYSUTCDATETIME()
                );
                CREATE INDEX IX_SemanticArtefacts_Project_Source ON dbo.SemanticArtefacts(ProjectId, SourceEntityType, SourceEntityId, SourceVersionId);
            END

            IF OBJECT_ID('dbo.SemanticChunks', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SemanticChunks
                (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    ArtefactId UNIQUEIDENTIFIER NOT NULL,
                    ProjectId INT NOT NULL,
                    ChunkIndex INT NOT NULL,
                    ChunkText NVARCHAR(MAX) NOT NULL,
                    TokenEstimate INT NULL,
                    ContentHash NVARCHAR(128) NOT NULL,
                    WeaviateObjectId NVARCHAR(100) NULL,
                    EmbeddedAtUtc DATETIME2 NULL,
                    EmbeddingModel NVARCHAR(200) NULL,
                    IsStale BIT NOT NULL CONSTRAINT DF_SemanticChunks_IsStale DEFAULT 0,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_SemanticChunks_CreatedUtc DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_SemanticChunks_SemanticArtefacts FOREIGN KEY (ArtefactId) REFERENCES dbo.SemanticArtefacts(Id)
                );
                CREATE INDEX IX_SemanticChunks_Project_Artefact ON dbo.SemanticChunks(ProjectId, ArtefactId, IsStale);
            END

            IF OBJECT_ID('dbo.EmbeddingJobs', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.EmbeddingJobs
                (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    TenantId INT NULL,
                    ProjectId INT NOT NULL,
                    SourceEntityType NVARCHAR(100) NOT NULL,
                    SourceEntityId NVARCHAR(100) NOT NULL,
                    SourceVersionId NVARCHAR(100) NULL,
                    JobType NVARCHAR(50) NOT NULL,
                    Status NVARCHAR(50) NOT NULL,
                    Attempts INT NOT NULL CONSTRAINT DF_EmbeddingJobs_Attempts DEFAULT 0,
                    LastError NVARCHAR(MAX) NULL,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_EmbeddingJobs_CreatedUtc DEFAULT SYSUTCDATETIME(),
                    StartedUtc DATETIME2 NULL,
                    CompletedUtc DATETIME2 NULL
                );
                CREATE INDEX IX_EmbeddingJobs_Status ON dbo.EmbeddingJobs(Status, CreatedUtc);
            END

            IF OBJECT_ID('dbo.SemanticSearchTraces', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SemanticSearchTraces
                (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    ProjectId INT NOT NULL,
                    QueryText NVARCHAR(MAX) NOT NULL,
                    Consumer NVARCHAR(100) NOT NULL,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_SemanticSearchTraces_CreatedUtc DEFAULT SYSUTCDATETIME()
                );
            END

            IF OBJECT_ID('dbo.SemanticSearchTraceResults', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SemanticSearchTraceResults
                (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    SearchTraceId UNIQUEIDENTIFIER NOT NULL,
                    ArtefactId UNIQUEIDENTIFIER NOT NULL,
                    ChunkId UNIQUEIDENTIFIER NOT NULL,
                    VectorSimilarity FLOAT NOT NULL,
                    FinalScore FLOAT NOT NULL,
                    AuthorityBoost FLOAT NOT NULL,
                    RecencyBoost FLOAT NOT NULL,
                    SourceTypeBoost FLOAT NOT NULL,
                    ExplicitLinkBoost FLOAT NOT NULL,
                    StalePenalty FLOAT NOT NULL,
                    MatchReason NVARCHAR(MAX) NULL,
                    CONSTRAINT FK_SemanticSearchTraceResults_SemanticSearchTraces FOREIGN KEY (SearchTraceId) REFERENCES dbo.SemanticSearchTraces(Id)
                );
            END
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }
}
