using System.Text.Json;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.SemanticMemory;
using IronDev.Services;
public static class MemoryWeaviateSqlVersionSmokeCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var requestedProjectName = MemorySmokeCommandSupport.ReadOption(args, "--project") ?? "IronDev";
        var dogfoodRunId = MemorySmokeCommandSupport.ReadOption(args, "--dogfood-run-id") ?? $"memory-weaviate-sql-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var queryText = MemorySmokeCommandSupport.ReadOption(args, "--query") ?? "first Codex goal builder output";
        var connectionString = MemorySmokeCommandSupport.ResolveIronDevConnectionString(args);
        var repoRoot = MemorySmokeCommandSupport.FindRepositoryRoot();
        var connectionFactory = new CliConnectionFactory(connectionString);

        await MemorySmokeCommandSupport.ApplySqlScriptAsync(
            connectionFactory,
            Path.Combine(repoRoot, "Database", "migrate_project_documents.sql"));

        var project = await MemorySmokeCommandSupport.ResolveProjectAsync(connectionFactory, requestedProjectName);
        if (project is null)
        {
            Console.Error.WriteLine($"Project not found: {requestedProjectName}");
            return 1;
        }

        var tenant = new CliTenantContext(project.TenantId);
        var documentService = new ProjectDocumentService(connectionFactory, tenant);
        var artefactRepository = new SemanticArtefactRepository(connectionFactory);
        var chunkRepository = new SemanticChunkRepository(connectionFactory);
        var traceRepository = new SemanticSearchTraceRepository(connectionFactory);
        var ranker = new SemanticRankingService();

        var stamp = dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
        var titleSeed = $"CODEX_GOALS_WEAVIATE_SQL_SPINE_{stamp}_{Guid.NewGuid():N}";
        var title = titleSeed[..Math.Min(120, titleSeed.Length)];
        var oldContent = """
        # Codex Goals Weaviate Spine

        The first Codex goal is to test builder output and patch generation.
        This historical version is intentionally stale but semantically tempting for builder-output queries.
        """;
        var currentContent = """
        # Codex Goals Weaviate Spine

        The first Codex goal is to prove SQL-backed memory retrieval and authority ranking.
        Current authoritative memory must beat stale vector matches before builder context is trusted.
        """;

        var document = await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = project.ProjectId,
            Title = title,
            DocumentType = "Architecture",
            ContentMarkdown = oldContent,
            ChangeSummary = "Historical Weaviate dogfood version",
            CreatedBy = "TestAgent",
            SourceEntityType = "Discussion",
            SourceEntityId = 9101
        });

        var oldVersion = await documentService.GetCurrentVersionAsync(document.Id)
            ?? throw new InvalidOperationException("Initial document version was not created.");

        var currentVersion = await documentService.AddVersionAsync(new AddProjectDocumentVersionRequest
        {
            DocumentId = document.Id,
            ContentMarkdown = currentContent,
            ChangeSummary = "Current Weaviate dogfood version",
            CreatedBy = "TestAgent",
            IncrementMajorVersion = true,
            Status = "Approved"
        });

        await documentService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
        {
            DocumentVersionId = currentVersion.Id,
            LinkedEntityType = "Discussion",
            LinkedEntityId = 9102,
            LinkType = "CurrentGoalSource",
            CreatedBy = "TestAgent"
        });

        var oldArtefactId = Guid.NewGuid();
        var currentArtefactId = Guid.NewGuid();
        await MemorySmokeCommandSupport.IndexDocumentVersionAsync(
            artefactRepository,
            chunkRepository,
            project.TenantId,
            project.ProjectId,
            oldArtefactId,
            document,
            oldVersion,
            authorityLevel: "LowAuthorityNote",
            content: oldContent);
        await MemorySmokeCommandSupport.IndexDocumentVersionAsync(
            artefactRepository,
            chunkRepository,
            project.TenantId,
            project.ProjectId,
            currentArtefactId,
            document,
            currentVersion,
            authorityLevel: "AcceptedArchitecture",
            content: currentContent);

        await artefactRepository.MarkStaleAsync(new SemanticStaleRequest
        {
            ProjectId = project.ProjectId,
            SourceEntityType = "ProjectDocument",
            SourceEntityId = document.Id.ToString(),
            SourceVersionId = oldVersion.Id.ToString()
        });

        var oldArtefact = await artefactRepository.GetArtefactAsync(oldArtefactId)
            ?? throw new InvalidOperationException("Old semantic artefact was not persisted.");
        var currentArtefact = await artefactRepository.GetArtefactAsync(currentArtefactId)
            ?? throw new InvalidOperationException("Current semantic artefact was not persisted.");
        var oldChunk = (await chunkRepository.GetChunksAsync(oldArtefactId, includeStale: true)).Single();
        var currentChunk = (await chunkRepository.GetChunksAsync(currentArtefactId, includeStale: true)).Single();

        var weaviateEndpoint = MemorySmokeCommandSupport.ReadOption(args, "--weaviate-endpoint") ?? MemorySmokeCommandSupport.ResolveWeaviateEndpoint();
        var collectionName = MemorySmokeCommandSupport.BuildWeaviateDogfoodCollectionName(dogfoodRunId);
        var queryVector = new[] { 1.0, 0.0, 0.0, 0.0 };
        var staleVector = new[] { 1.0, 0.0, 0.0, 0.0 };
        var currentVector = new[] { 0.8, 0.6, 0.0, 0.0 };

        using var httpClient = new HttpClient { BaseAddress = new Uri(weaviateEndpoint.TrimEnd('/') + "/") };
        await MemorySmokeCommandSupport.EnsureWeaviateDogfoodCollectionAsync(httpClient, collectionName);
        await MemorySmokeCommandSupport.UpsertWeaviateChunkAsync(
            httpClient,
            collectionName,
            oldChunk.Id,
            oldArtefact,
            oldChunk,
            project.TenantId,
            isStale: true,
            vector: staleVector);
        await MemorySmokeCommandSupport.UpsertWeaviateChunkAsync(
            httpClient,
            collectionName,
            currentChunk.Id,
            currentArtefact,
            currentChunk,
            project.TenantId,
            isStale: false,
            vector: currentVector);

        var rawMatches = await MemorySmokeCommandSupport.QueryWeaviateDogfoodCollectionAsync(httpClient, collectionName, queryVector, limit: 5);
        var rawRelevantMatches = rawMatches
            .Where(match => match.ProjectId == project.ProjectId &&
                            (string.Equals(match.SourceVersionId, oldVersion.Id.ToString(), StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(match.SourceVersionId, currentVersion.Id.ToString(), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(match => match.RawWeaviateRank)
            .ToArray();

        var artefactsById = new Dictionary<Guid, SemanticArtefact>
        {
            [oldArtefact.Id] = oldArtefact,
            [currentArtefact.Id] = currentArtefact
        };
        var chunksById = new Dictionary<Guid, SemanticChunk>
        {
            [oldChunk.Id] = oldChunk,
            [currentChunk.Id] = currentChunk
        };
        var versionsById = new Dictionary<string, ProjectDocumentVersion>(StringComparer.OrdinalIgnoreCase)
        {
            [oldVersion.Id.ToString()] = oldVersion,
            [currentVersion.Id.ToString()] = currentVersion
        };

        var candidates = rawRelevantMatches
            .Where(match => artefactsById.ContainsKey(match.ArtefactId) &&
                            chunksById.ContainsKey(match.ChunkId) &&
                            versionsById.ContainsKey(match.SourceVersionId ?? string.Empty))
            .Select(match =>
            {
                var version = versionsById[match.SourceVersionId!];
                return MemorySmokeCommandSupport.BuildCandidate(
                    project.TenantId,
                    project.ProjectId,
                    document,
                    version,
                    artefactsById[match.ArtefactId],
                    chunksById[match.ChunkId],
                    version.Id == currentVersion.Id ? currentContent : oldContent,
                    vectorSimilarity: match.VectorSimilarity,
                    contentHashMismatch: false);
            })
            .ToArray();

        var query = new SemanticSearchQuery
        {
            ProjectId = project.ProjectId,
            QueryText = queryText,
            Consumer = "MemorySpineWeaviateSmoke",
            Limit = 5,
            IncludeStale = true
        };

        var results = ranker.Rank(query, candidates);
        var traceId = await traceRepository.CreateTraceAsync(query);
        await traceRepository.AddResultsAsync(traceId, results);
        var links = await documentService.GetLinksForVersionAsync(currentVersion.Id);

        var top = results.FirstOrDefault();
        var staleRawRank = rawRelevantMatches.FirstOrDefault(match => match.SourceVersionId == oldVersion.Id.ToString())?.RawWeaviateRank;
        var currentRawRank = rawRelevantMatches.FirstOrDefault(match => match.SourceVersionId == currentVersion.Id.ToString())?.RawWeaviateRank;
        var passed = top is not null &&
                     top.SourceVersionId == currentVersion.Id.ToString() &&
                     !top.IsStale &&
                     staleRawRank.HasValue &&
                     currentRawRank.HasValue &&
                     staleRawRank.Value < currentRawRank.Value &&
                     links.Any(link => link.LinkType == "CurrentGoalSource");

        var result = new MemoryWeaviateSqlVersionSmokeResult
        {
            DogfoodRunId = dogfoodRunId,
            TenantId = project.TenantId,
            ProjectId = project.ProjectId,
            ProjectName = project.ProjectName,
            DocumentId = document.Id,
            CurrentVersionId = currentVersion.Id,
            OldVersionId = oldVersion.Id,
            Query = queryText,
            WeaviateEndpoint = weaviateEndpoint,
            WeaviateCollection = collectionName,
            SemanticTraceId = traceId,
            SourceLinkCount = links.Count,
            Passed = passed,
            RawMatches = rawRelevantMatches,
            Results = results.Select((ranked, index) => new MemoryWeaviateSqlVersionSearchResult
            {
                FinalAuthorityRank = index + 1,
                RawWeaviateRank = rawRelevantMatches.FirstOrDefault(match => match.SourceVersionId == ranked.SourceVersionId)?.RawWeaviateRank,
                Title = ranked.Title,
                SourceEntityType = ranked.SourceEntityType,
                SourceEntityId = ranked.SourceEntityId,
                SourceVersionId = ranked.SourceVersionId,
                FinalScore = ranked.FinalScore,
                VectorSimilarity = ranked.VectorSimilarity,
                AuthorityBoost = ranked.AuthorityBoost,
                SourceTypeBoost = ranked.SourceTypeBoost,
                RecencyBoost = ranked.RecencyBoost,
                StalePenalty = ranked.StalePenalty,
                IsStale = ranked.IsStale,
                MatchReason = ranked.MatchReason
            }).ToArray()
        };

        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return passed ? 0 : 1;
    }


}
