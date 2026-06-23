using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data;
using IronDev.Data.Models;
using IronDev.Services;
using Weaviate.Client;
using Weaviate.Client.Models;


namespace IronDev.Infrastructure.Services.SemanticMemory;

public sealed class WeaviateSemanticMemoryService : ISemanticMemoryService
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IEmbeddingContentExtractor _contentExtractor;
    private readonly IProjectMemoryService _projectMemoryService;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ISemanticChunker _chunker;
    private readonly ISemanticRankingService _rankingService;
    private readonly ISemanticArtefactRepository _artefactRepository;
    private readonly ISemanticChunkRepository _chunkRepository;
    private readonly IEmbeddingJobRepository _embeddingJobRepository;
    private readonly ISemanticSearchTraceRepository _traceRepository;
    private readonly WeaviateOptions _options;
    private readonly SemanticRankingOptions _rankingOptions;
    
    private WeaviateClient? _client;
    private bool _initialized;

    public WeaviateSemanticMemoryService(
        IEmbeddingProvider embeddingProvider,
        IEmbeddingContentExtractor contentExtractor,
        IProjectMemoryService projectMemoryService,
        IDbConnectionFactory connectionFactory,
        ISemanticChunker chunker,
        ISemanticRankingService rankingService,
        ISemanticArtefactRepository artefactRepository,
        ISemanticChunkRepository chunkRepository,
        IEmbeddingJobRepository embeddingJobRepository,
        ISemanticSearchTraceRepository traceRepository,
        WeaviateOptions options,
        SemanticRankingOptions? rankingOptions = null)
    {
        _embeddingProvider = embeddingProvider;
        _contentExtractor = contentExtractor;
        _projectMemoryService = projectMemoryService;
        _connectionFactory = connectionFactory;
        _chunker = chunker;
        _rankingService = rankingService;
        _artefactRepository = artefactRepository;
        _chunkRepository = chunkRepository;
        _embeddingJobRepository = embeddingJobRepository;
        _traceRepository = traceRepository;
        _options = options;
        _rankingOptions = rankingOptions ?? new SemanticRankingOptions();
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        try
        {
            if (!_options.Enabled)
            {
                throw new InvalidOperationException("Weaviate is disabled in configuration.");
            }

            var uri = new Uri(_options.Endpoint);
            string host = uri.Host;
            string port = uri.Port.ToString();

            // Setup local or custom client.
            var builder = WeaviateClientBuilder.Custom(
                restEndpoint: host,
                restPort: port,
                grpcEndpoint: host,
                grpcPort: _options.GrpcPort.ToString(),
                useSsl: uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            );

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                builder = builder.WithCredentials(Weaviate.Client.Auth.ApiKey(_options.ApiKey));

            _client = await builder.BuildAsync();

            _initialized = true;
        }
        catch
        {
            throw new InvalidOperationException("Failed to initialize Weaviate client. Check endpoint and authentication configuration.");
        }
    }

    public async Task QueueIndexAsync(SemanticIndexRequest request, CancellationToken ct = default)
    {
        await _embeddingJobRepository.CreateAsync(new EmbeddingJob
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            SourceEntityType = request.SourceEntityType,
            SourceEntityId = request.SourceEntityId,
            SourceVersionId = request.SourceVersionId,
            JobType = request.JobType,
            Status = "Pending",
            CreatedUtc = DateTime.UtcNow
        }, ct);
    }

    public async Task EmbedAndStoreAsync(ProjectContextDocument document, CancellationToken ct = default)
    {
        if (document == null) return;
        await EnsureInitializedAsync();
        await EnsureMetadataSchemaAsync(ct);

        string text = _contentExtractor.Extract(document);
        var artefactId = GuidFromLong(document.Id);
        string collectionName = _options.CollectionPrefix;
        string contentHash = GetContentHash(text);
        var draft = new SemanticArtefactDraft
        {
            Id = artefactId,
            TenantId = document.TenantId == 0 ? null : document.TenantId,
            ProjectId = document.ProjectId,
            SourceEntityType = "ProjectContextDocument",
            SourceEntityId = document.Id.ToString(),
            ArtefactType = MapArtefactType(document.DocumentType),
            AuthorityLevel = MapAuthorityLevel(document.AuthorityLevel),
            Title = document.Title,
            Summary = document.Summary,
            SearchableText = text,
            ContentHash = contentHash
        };

        await _artefactRepository.UpsertArtefactAsync(draft, ct);
        var chunks = _chunker.Chunk(draft);
        await _chunkRepository.ReplaceChunksAsync(draft.Id, chunks, ct);
        var collection = await EnsureCollectionAsync(collectionName, ct);

        foreach (var chunk in chunks)
        {
            var embeddingResult = await _embeddingProvider.EmbedAsync(chunk.ChunkText, ct);
            var dataObject = new Dictionary<string, object>
            {
                { "chunkId", chunk.Id.ToString() },
                { "artefactId", artefactId.ToString() },
                { "tenantId", document.TenantId },
                { "projectId", document.ProjectId },
                { "sourceEntityType", draft.SourceEntityType },
                { "sourceEntityId", draft.SourceEntityId },
                { "sourceVersionId", draft.SourceVersionId ?? string.Empty },
                { "documentId", document.Id },
                { "artefactType", draft.ArtefactType },
                { "authorityLevel", draft.AuthorityLevel },
                { "title", draft.Title },
                { "summary", draft.Summary ?? string.Empty },
                { "chunkText", chunk.ChunkText },
                { "chunkIndex", chunk.ChunkIndex },
                { "contentHash", chunk.ContentHash },
                { "embeddedAtUtc", DateTime.UtcNow },
                { "embeddingModel", embeddingResult.Model },
                { "isStale", false }
            };

            try
            {
                await collection.Data.Update(chunk.Id, dataObject, vectors: embeddingResult.Vector);
            }
            catch
            {
                await collection.Data.Insert(dataObject, uuid: chunk.Id, vectors: embeddingResult.Vector);
            }

            await _chunkRepository.MarkEmbeddedAsync(chunk.Id, chunk.Id.ToString(), embeddingResult.Model, ct);
        }
    }

    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        int projectId,
        string query,
        int limit = 8,
        double minSimilarity = 0.75,
        CancellationToken ct = default)
        => await SearchAsync(new SemanticSearchQuery
        {
            ProjectId = projectId,
            QueryText = query,
            Limit = limit,
            IncludeStale = minSimilarity < 0,
            Consumer = "SearchAsync"
        }, ct);

    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        SemanticSearchQuery query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.QueryText))
            return Array.Empty<SemanticSearchResult>();

        await EnsureInitializedAsync();
        await EnsureMetadataSchemaAsync(ct);

        var queryEmbedding = await _embeddingProvider.EmbedAsync(query.QueryText, ct);
        string collectionName = _options.CollectionPrefix;

        if (!await _client!.Collections.Exists(collectionName, ct))
            return Array.Empty<SemanticSearchResult>();

        var collection = _client.Collections.Use(collectionName);
        var response = await collection.Query
            .NearVector(queryEmbedding.Vector, limit: (uint)Math.Max(query.Limit * 3, query.Limit));

        if (response == null || response.Objects == null)
            return Array.Empty<SemanticSearchResult>();

        var candidates = new List<SemanticSearchCandidate>();

        foreach (var obj in response.Objects)
        {
            if (!obj.Properties.TryGetValue("documentId", out var documentIdValue))
                continue;
            if (!obj.Properties.TryGetValue("projectId", out var objectProjectIdValue) ||
                Convert.ToInt32(objectProjectIdValue) != query.ProjectId)
                continue;

            long documentId = Convert.ToInt64(documentIdValue);
            var doc = await _projectMemoryService.GetContextDocumentByIdAsync(documentId, ct);
            if (doc == null) continue;
            if (doc.ProjectId != query.ProjectId) continue;

            double similarity = 0.85;
            if (obj.Vectors != null && obj.Vectors.Count > 0)
            {
                var objVector = obj.Vectors.Values.FirstOrDefault();
                if (objVector != null)
                    similarity = CosineSimilarity.Compute(queryEmbedding.Vector, objVector);
            }

            if (similarity < _rankingOptions.MinimumSimilarity && !query.IncludeStale)
                continue;

            var artefactId = obj.Properties.TryGetValue("artefactId", out var artefactValue) &&
                             Guid.TryParse(artefactValue?.ToString(), out var parsedArtefactId)
                ? parsedArtefactId
                : GuidFromLong(documentId);
            var chunkId = obj.Properties.TryGetValue("chunkId", out var chunkValue) &&
                          Guid.TryParse(chunkValue?.ToString(), out var parsedChunkId)
                ? parsedChunkId
                : obj.UUID ?? Guid.NewGuid();

            var artefact = await _artefactRepository.GetArtefactAsync(artefactId, ct)
                ?? new SemanticArtefact
                {
                    Id = artefactId,
                    TenantId = doc.TenantId,
                    ProjectId = doc.ProjectId,
                    SourceEntityType = "ProjectContextDocument",
                    SourceEntityId = doc.Id.ToString(),
                    ArtefactType = MapArtefactType(doc.DocumentType),
                    AuthorityLevel = MapAuthorityLevel(doc.AuthorityLevel),
                    Title = doc.Title,
                    Summary = doc.Summary,
                    ContentHash = GetContentHash(_contentExtractor.Extract(doc)),
                    CreatedUtc = doc.CreatedDate,
                    UpdatedUtc = doc.UpdatedDate ?? doc.CreatedDate
                };

            var chunk = await _chunkRepository.GetChunkAsync(chunkId, ct)
                ?? new SemanticChunk
                {
                    Id = chunkId,
                    ArtefactId = artefactId,
                    ProjectId = doc.ProjectId,
                    ChunkIndex = Convert.ToInt32(obj.Properties.TryGetValue("chunkIndex", out var chunkIndexValue) ? chunkIndexValue : 0),
                    ChunkText = obj.Properties.TryGetValue("chunkText", out var chunkTextValue) ? chunkTextValue?.ToString() ?? doc.Content : doc.Content,
                    ContentHash = obj.Properties.TryGetValue("contentHash", out var chunkHashValue) ? chunkHashValue?.ToString() ?? string.Empty : string.Empty,
                    IsStale = obj.Properties.TryGetValue("isStale", out var staleValue) && Convert.ToBoolean(staleValue)
                };

            candidates.Add(new SemanticSearchCandidate
            {
                Document = doc,
                Artefact = artefact,
                Chunk = chunk,
                VectorSimilarity = similarity,
                ContentHashMismatch = !string.Equals(chunk.ContentHash, obj.Properties.TryGetValue("contentHash", out var hash) ? hash?.ToString() : null, StringComparison.OrdinalIgnoreCase)
            });
        }

        var ranked = _rankingService.Rank(query, candidates);

        var traceId = await _traceRepository.CreateTraceAsync(query, ct);
        await _traceRepository.AddResultsAsync(traceId, ranked, ct);
        return ranked;
    }

    public async Task<SemanticContextBundle> BuildContextBundleAsync(
        int projectId,
        string query,
        string callerContext,
        int limit = 8,
        CancellationToken ct = default)
    {
        try
        {
            var results = await SearchAsync(projectId, query, limit, _rankingOptions.MinimumSimilarity, ct);
            return SemanticContextBundleBuilder.Build(projectId, query, callerContext, results);
        }
        catch (Exception ex)
        {
            return SemanticContextBundleBuilder.Build(
                projectId,
                query,
                callerContext,
                [],
                [$"Semantic memory retrieval failed: {ex.Message}"]);
        }
    }

    public async Task RebuildIndexAsync(
        int projectId,
        IProgress<SemanticIndexRebuildProgress>? progress = null,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await EnsureMetadataSchemaAsync(ct);
        string collectionName = _options.CollectionPrefix;
        var runId = await StartIndexRunAsync(projectId, ct);
        int total = 0;
        int processed = 0;

        try
        {
            try
            {
                await _chunkRepository.MarkProjectStaleAsync(projectId, ct);
                await _client!.Collections.Delete(collectionName);
            }
            catch
            {
                // Ignore if missing
            }

            var documents = await _projectMemoryService.GetContextDocumentsAsync(
                projectId: projectId,
                status: "Active",
                take: 1000,
                cancellationToken: ct);

            total = documents.Count;

            foreach (var doc in documents)
            {
                if (ct.IsCancellationRequested)
                    break;

                progress?.Report(new SemanticIndexRebuildProgress
                {
                    TotalDocuments = total,
                    ProcessedDocuments = processed,
                    CurrentDocumentTitle = doc.Title,
                    IsCompleted = false
                });

                await EmbedAndStoreAsync(doc, ct);
                processed++;
            }

            await CompleteIndexRunAsync(runId, "Completed", total, processed, null, ct);
            progress?.Report(new SemanticIndexRebuildProgress
            {
                TotalDocuments = total,
                ProcessedDocuments = processed,
                CurrentDocumentTitle = string.Empty,
                IsCompleted = true
            });
        }
        catch (Exception ex)
        {
            await CompleteIndexRunAsync(runId, "Failed", total, processed, ex.Message, CancellationToken.None);
            progress?.Report(new SemanticIndexRebuildProgress
            {
                TotalDocuments = total,
                ProcessedDocuments = processed,
                CurrentDocumentTitle = string.Empty,
                IsCompleted = true,
                ErrorMessage = ex.Message
            });
            throw;
        }
    }

    public async Task RebuildProjectAsync(int projectId, CancellationToken ct = default)
        => await RebuildIndexAsync(projectId, ct: ct);

    public async Task MarkStaleAsync(SemanticStaleRequest request, CancellationToken ct = default)
    {
        await _artefactRepository.MarkStaleAsync(request, ct);
        var artefact = await _artefactRepository.GetArtefactBySourceAsync(
            request.ProjectId,
            request.SourceEntityType,
            request.SourceEntityId,
            request.SourceVersionId,
            ct);
        if (artefact != null)
            await _chunkRepository.MarkArtefactChunksStaleAsync(artefact.Id, ct);
    }

    public async Task DeleteEmbeddingAsync(Guid artefactId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await EnsureMetadataSchemaAsync(ct);
        try
        {
            var documentId = LongFromGuid(artefactId);
            var doc = await _projectMemoryService.GetContextDocumentByIdAsync(documentId, ct);
            if (doc != null)
            {
                string collectionName = _options.CollectionPrefix;
                if (await _client!.Collections.Exists(collectionName, ct))
                {
                    var collection = _client.Collections.Use(collectionName);
                    await collection.Data.DeleteByID(artefactId, ct);
                }
            }
            await DeleteEmbeddingMetadataAsync(artefactId, ct);
        }
        catch
        {
            // Ignore if error or not found
        }
    }

    public async Task<SemanticMemoryHealth> GetHealthAsync(int projectId, CancellationToken ct = default)
    {
        try
        {
            await EnsureInitializedAsync();
            await EnsureMetadataSchemaAsync(ct);
            var documents = await _projectMemoryService.GetContextDocumentsAsync(
                projectId,
                status: "Active",
                take: 1000,
                cancellationToken: ct);
            var rows = await GetEmbeddingMetadataAsync(projectId, ct);
            var documentHashes = documents.ToDictionary(
                d => d.Id,
                d => GetContentHash(_contentExtractor.Extract(d)));

            int staleCount = rows.Count(row =>
                !documentHashes.TryGetValue(row.DocumentId, out var hash) ||
                !string.Equals(row.ContentHash, hash, StringComparison.OrdinalIgnoreCase));

            return new SemanticMemoryHealth
            {
                ProjectId = projectId,
                ProviderName = "Weaviate",
                ProviderStatus = await _client!.Collections.Exists(_options.CollectionPrefix, ct) ? "Ready" : "Collection missing",
                DocumentCount = documents.Count,
                EmbeddedCount = rows.Count,
                StaleEmbeddingCount = staleCount,
                LastEmbeddedAtUtc = rows.Count == 0 ? null : rows.Max(r => r.EmbeddedAtUtc),
                LastRebuildAtUtc = await GetLastRebuildUtcAsync(projectId, ct)
            };
        }
        catch (Exception ex)
        {
            return new SemanticMemoryHealth
            {
                ProjectId = projectId,
                ProviderName = "Weaviate",
                ProviderStatus = $"Unavailable: {ex.Message}",
                DocumentCount = 0,
                EmbeddedCount = 0,
                StaleEmbeddingCount = 0,
                LastEmbeddedAtUtc = null,
                LastRebuildAtUtc = null
            };
        }
    }

    private async Task<CollectionClient> EnsureCollectionAsync(string collectionName, CancellationToken ct)
    {
        if (!await _client!.Collections.Exists(collectionName, ct))
        {
            await _client.Collections.Create(new CollectionCreateParams
            {
                Name = collectionName
            }, ct);
        }

        return _client.Collections.Use(collectionName);
    }

    private string GetCollectionName(int projectId)
        => SanitizeName(_options.CollectionPrefix);

    private static string SanitizeName(string value)
    {
        var cleaned = new string((string.IsNullOrWhiteSpace(value) ? "IronDevKnowledge" : value)
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "IronDevKnowledge" : cleaned;
    }

    private async Task EnsureMetadataSchemaAsync(CancellationToken ct)
    {
        await SemanticMemorySchema.EnsureAsync(_connectionFactory, ct);
        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            IF OBJECT_ID('dbo.SemanticEmbeddings', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SemanticEmbeddings
                (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    ProjectId INT NOT NULL,
                    ArtefactId UNIQUEIDENTIFIER NOT NULL,
                    ArtefactType NVARCHAR(100) NOT NULL,
                    DocumentId BIGINT NOT NULL,
                    SourceDocumentVersionId INT NULL,
                    ContentHash NVARCHAR(128) NOT NULL,
                    ModelVersion NVARCHAR(100) NOT NULL,
                    VectorDimensions INT NOT NULL,
                    VectorData VARBINARY(MAX) NULL,
                    Provider NVARCHAR(100) NOT NULL,
                    CollectionName NVARCHAR(255) NULL,
                    WeaviateObjectId UNIQUEIDENTIFIER NULL,
                    EmbeddedAtUtc DATETIME2 NOT NULL,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_SemanticEmbeddings_CreatedUtc DEFAULT SYSUTCDATETIME(),
                    UpdatedUtc DATETIME2 NULL
                );
                CREATE UNIQUE INDEX UX_SemanticEmbeddings_ArtefactId ON dbo.SemanticEmbeddings(ArtefactId);
                CREATE INDEX IX_SemanticEmbeddings_ProjectId ON dbo.SemanticEmbeddings(ProjectId, ArtefactType);
            END

            IF OBJECT_ID('dbo.SemanticIndexRuns', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SemanticIndexRuns
                (
                    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    ProjectId INT NOT NULL,
                    StartedAtUtc DATETIME2 NOT NULL,
                    CompletedAtUtc DATETIME2 NULL,
                    Status NVARCHAR(50) NOT NULL,
                    TotalDocuments INT NOT NULL,
                    ProcessedDocuments INT NOT NULL,
                    ErrorMessage NVARCHAR(MAX) NULL
                );
            END
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    private async Task UpsertEmbeddingMetadataAsync(
        ProjectContextDocument document,
        Guid artefactId,
        string collectionName,
        string contentHash,
        EmbeddingResult embedding,
        CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = """
            MERGE dbo.SemanticEmbeddings AS target
            USING (SELECT @ArtefactId AS ArtefactId) AS source
            ON target.ArtefactId = source.ArtefactId
            WHEN MATCHED THEN
                UPDATE SET
                    ProjectId = @ProjectId,
                    ArtefactType = @ArtefactType,
                    DocumentId = @DocumentId,
                    ContentHash = @ContentHash,
                    ModelVersion = @ModelVersion,
                    VectorDimensions = @VectorDimensions,
                    Provider = @Provider,
                    CollectionName = @CollectionName,
                    WeaviateObjectId = @WeaviateObjectId,
                    EmbeddedAtUtc = @EmbeddedAtUtc,
                    UpdatedUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT
                (
                    Id, ProjectId, ArtefactId, ArtefactType, DocumentId, SourceDocumentVersionId,
                    ContentHash, ModelVersion, VectorDimensions, VectorData, Provider, CollectionName,
                    WeaviateObjectId, EmbeddedAtUtc, CreatedUtc
                )
                VALUES
                (
                    @Id, @ProjectId, @ArtefactId, @ArtefactType, @DocumentId, NULL,
                    @ContentHash, @ModelVersion, @VectorDimensions, NULL, @Provider, @CollectionName,
                    @WeaviateObjectId, @EmbeddedAtUtc, SYSUTCDATETIME()
                );
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = Guid.NewGuid(),
            ProjectId = document.ProjectId,
            ArtefactId = artefactId,
            ArtefactType = document.DocumentType,
            DocumentId = document.Id,
            ContentHash = contentHash,
            ModelVersion = embedding.Model,
            VectorDimensions = embedding.Dimensions,
            Provider = "Weaviate",
            CollectionName = collectionName,
            WeaviateObjectId = artefactId,
            EmbeddedAtUtc = DateTime.UtcNow
        }, cancellationToken: ct));
    }

    private async Task DeleteEmbeddingMetadataAsync(Guid artefactId, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.SemanticEmbeddings WHERE ArtefactId = @ArtefactId;",
            new { ArtefactId = artefactId },
            cancellationToken: ct));
    }

    private async Task<List<EmbeddingMetadataRow>> GetEmbeddingMetadataAsync(int projectId, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<EmbeddingMetadataRow>(new CommandDefinition(
            """
            SELECT
                TRY_CONVERT(BIGINT, a.SourceEntityId) AS DocumentId,
                a.ContentHash,
                MAX(c.EmbeddedAtUtc) AS EmbeddedAtUtc
            FROM dbo.SemanticArtefacts a
            INNER JOIN dbo.SemanticChunks c ON c.ArtefactId = a.Id
            WHERE a.ProjectId = @ProjectId
              AND a.SourceEntityType = 'ProjectContextDocument'
              AND c.EmbeddedAtUtc IS NOT NULL
            GROUP BY a.SourceEntityId, a.ContentHash;
            """,
            new { ProjectId = projectId },
            cancellationToken: ct));
        return rows.ToList();
    }

    private async Task<int> StartIndexRunAsync(int projectId, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            INSERT INTO dbo.SemanticIndexRuns
                (ProjectId, StartedAtUtc, Status, TotalDocuments, ProcessedDocuments)
            OUTPUT inserted.Id
            VALUES
                (@ProjectId, SYSUTCDATETIME(), 'Running', 0, 0);
            """,
            new { ProjectId = projectId },
            cancellationToken: ct));
    }

    private async Task CompleteIndexRunAsync(
        int runId,
        string status,
        int totalDocuments,
        int processedDocuments,
        string? errorMessage,
        CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.SemanticIndexRuns
            SET CompletedAtUtc = SYSUTCDATETIME(),
                Status = @Status,
                TotalDocuments = @TotalDocuments,
                ProcessedDocuments = @ProcessedDocuments,
                ErrorMessage = @ErrorMessage
            WHERE Id = @RunId;
            """,
            new { RunId = runId, Status = status, TotalDocuments = totalDocuments, ProcessedDocuments = processedDocuments, ErrorMessage = errorMessage },
            cancellationToken: ct));
    }

    private async Task<DateTime?> GetLastRebuildUtcAsync(int projectId, CancellationToken ct)
    {
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            """
            SELECT TOP (1) CompletedAtUtc
            FROM dbo.SemanticIndexRuns
            WHERE ProjectId = @ProjectId
              AND Status = 'Completed'
            ORDER BY CompletedAtUtc DESC;
            """,
            new { ProjectId = projectId },
            cancellationToken: ct));
    }

    private sealed class EmbeddingMetadataRow
    {
        public long DocumentId { get; init; }
        public string ContentHash { get; init; } = string.Empty;
        public DateTime EmbeddedAtUtc { get; init; }
    }

    private static Guid GuidFromLong(long value)
    {
        byte[] bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private static long LongFromGuid(Guid guid)
    {
        byte[] bytes = guid.ToByteArray();
        return BitConverter.ToInt64(bytes, 0);
    }

    private static string GetContentHash(string text)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private static double GetAuthorityScore(string authorityLevel)
    {
        return authorityLevel switch
        {
            "Binding" => 1.0,
            "StrongGuidance" => 0.8,
            "ObservedFact" => 0.6,
            "Pending" => 0.4,
            "ContextOnly" => 0.2,
            _ => 0.1
        };
    }

    private static string MapArtefactType(string documentType) => documentType switch
    {
        "DiscussionDocument" => "Discussion",
        "ArchitectureDocument" => "Architecture",
        "ArchitectureDecision" => "Decision",
        "Requirement" => "Requirement",
        "Risk" => "Risk",
        "OpenQuestion" => "Discussion",
        _ => string.IsNullOrWhiteSpace(documentType) ? "ProjectDocument" : documentType
    };

    private static string MapAuthorityLevel(string authorityLevel) => authorityLevel switch
    {
        "Binding" => "CommittedDecision",
        "StrongGuidance" => "AcceptedArchitecture",
        "ResolvedKnowledge" => "AcceptedRequirement",
        "DiscussionPrompt" => "GeneratedDraft",
        "OpenQuestion" => "GeneratedDraft",
        "ObservedFact" => "TraceObservation",
        "ContextOnly" => "LowAuthorityNote",
        _ => string.IsNullOrWhiteSpace(authorityLevel) ? "GeneratedDraft" : authorityLevel
    };

    private static double GetFreshnessScore(DateTime date)
    {
        var ageInDays = (DateTime.UtcNow - date).TotalDays;
        if (ageInDays < 0) ageInDays = 0;
        return 1.0 / (1.0 + ageInDays / 30.0);
    }

    private static double GetDirectLinkScore(ProjectContextDocument doc, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0.0;
        
        bool titleMatch = doc.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
        bool tagsMatch = doc.Tags != null && doc.Tags.Contains(query, StringComparison.OrdinalIgnoreCase);

        if (titleMatch && tagsMatch) return 1.0;
        if (titleMatch) return 0.8;
        if (tagsMatch) return 0.5;

        return 0.0;
    }
}
