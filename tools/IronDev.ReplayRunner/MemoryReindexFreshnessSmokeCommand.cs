using System.Text.Json;
using Dapper;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.SemanticMemory;
using IronDev.Services;

public static class MemoryReindexFreshnessSmokeCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var projectName = MemorySmokeCommandSupport.ReadOption(args, "--project") ?? "IronDev";
        var bleedProjectName = MemorySmokeCommandSupport.ReadOption(args, "--bleed-project") ?? "BookSeller";
        var dogfoodRunId = MemorySmokeCommandSupport.ReadOption(args, "--dogfood-run-id") ?? $"memory-reindex-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var queryText = MemorySmokeCommandSupport.ReadOption(args, "--query") ?? "current reindex freshness rules";
        var connectionString = MemorySmokeCommandSupport.ResolveIronDevConnectionString(args);
        var repoRoot = MemorySmokeCommandSupport.FindRepositoryRoot();
        var connectionFactory = new CliConnectionFactory(connectionString);

        await MemorySmokeCommandSupport.ApplySqlScriptAsync(
            connectionFactory,
            Path.Combine(repoRoot, "Database", "migrate_project_documents.sql"));

        var project = await MemorySmokeCommandSupport.ResolveProjectAsync(connectionFactory, projectName);
        var bleedProject = await MemorySmokeCommandSupport.ResolveProjectAsync(connectionFactory, bleedProjectName);
        if (project is null || bleedProject is null)
        {
            Console.Error.WriteLine($"Required projects not found. project={projectName}; bleedProject={bleedProjectName}");
            return 1;
        }

        var tenant = new CliTenantContext(project.TenantId);
        var bleedTenant = new CliTenantContext(bleedProject.TenantId);
        var documentService = new ProjectDocumentService(connectionFactory, tenant);
        var bleedDocumentService = new ProjectDocumentService(connectionFactory, bleedTenant);
        var artefactRepository = new SemanticArtefactRepository(connectionFactory);
        var chunkRepository = new SemanticChunkRepository(connectionFactory);
        var traceRepository = new SemanticSearchTraceRepository(connectionFactory);
        var ranker = new SemanticRankingService();

        var stamp = dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
        var titleSeed = $"REINDEX_FRESHNESS_CURRENT_{stamp}_{Guid.NewGuid():N}";
        var title = titleSeed[..Math.Min(120, titleSeed.Length)];
        var oldContent = """
        # Reindex Freshness Rules

        The current reindex rule is to ignore stale memory and test only builder output.
        This version is intentionally old, stale, and semantically tempting.
        """;
        var currentContent = """
        # Reindex Freshness Rules

        The current reindex rule is to keep accepted project memory fresh, project-scoped, idempotent, and traceable.
        Repeated reindexing must not create duplicate active chunks, duplicate source records, or duplicate indexed artefacts.
        """;
        var bleedContent = """
        # Reindex Freshness Rules

        BookSeller reindexing talks about checkout flow, basket stock reservation, and customer orders.
        This same title is intentionally tempting but must not become IronDev authority.
        """;

        var document = await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = project.ProjectId,
            Title = title,
            DocumentType = "Architecture",
            ContentMarkdown = oldContent,
            ChangeSummary = "Historical reindex rule",
            CreatedBy = "TestAgent",
            SourceEntityType = "Discussion",
            SourceEntityId = 11601
        });

        var oldVersion = await documentService.GetCurrentVersionAsync(document.Id)
            ?? throw new InvalidOperationException("Initial document version was not created.");
        var currentVersion = await documentService.AddVersionAsync(new AddProjectDocumentVersionRequest
        {
            DocumentId = document.Id,
            ContentMarkdown = currentContent,
            ChangeSummary = "Current accepted reindex rule",
            CreatedBy = "TestAgent",
            IncrementMajorVersion = true,
            Status = "Approved"
        });

        var bleedDocument = await bleedDocumentService.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = bleedProject.ProjectId,
            Title = title,
            DocumentType = "Architecture",
            ContentMarkdown = bleedContent,
            ChangeSummary = "Wrong-project reindex bait",
            CreatedBy = "TestAgent",
            SourceEntityType = "Discussion",
            SourceEntityId = 11602
        });
        var bleedVersion = await bleedDocumentService.GetCurrentVersionAsync(bleedDocument.Id)
            ?? throw new InvalidOperationException("Bleed document version was not created.");

        var oldArtefactId = MemorySmokeCommandSupport.DeterministicGuid($"reindex:{project.ProjectId}:{document.Id}:{oldVersion.Id}:artefact");
        var currentArtefactId = MemorySmokeCommandSupport.DeterministicGuid($"reindex:{project.ProjectId}:{document.Id}:{currentVersion.Id}:artefact");
        var bleedArtefactId = MemorySmokeCommandSupport.DeterministicGuid($"reindex:{bleedProject.ProjectId}:{bleedDocument.Id}:{bleedVersion.Id}:artefact");
        var oldChunkId = MemorySmokeCommandSupport.DeterministicGuid($"reindex:{project.ProjectId}:{document.Id}:{oldVersion.Id}:chunk");
        var currentChunkId = MemorySmokeCommandSupport.DeterministicGuid($"reindex:{project.ProjectId}:{document.Id}:{currentVersion.Id}:chunk");
        var bleedChunkId = MemorySmokeCommandSupport.DeterministicGuid($"reindex:{bleedProject.ProjectId}:{bleedDocument.Id}:{bleedVersion.Id}:chunk");

        for (var i = 0; i < 2; i++)
        {
            await MemorySmokeCommandSupport.IndexDocumentVersionAsync(
                artefactRepository,
                chunkRepository,
                project.TenantId,
                project.ProjectId,
                oldArtefactId,
                document,
                oldVersion,
                authorityLevel: "LowAuthorityNote",
                content: oldContent,
                chunkId: oldChunkId);
            await MemorySmokeCommandSupport.IndexDocumentVersionAsync(
                artefactRepository,
                chunkRepository,
                project.TenantId,
                project.ProjectId,
                currentArtefactId,
                document,
                currentVersion,
                authorityLevel: "AcceptedArchitecture",
                content: currentContent,
                chunkId: currentChunkId);
            await MemorySmokeCommandSupport.IndexDocumentVersionAsync(
                artefactRepository,
                chunkRepository,
                bleedProject.TenantId,
                bleedProject.ProjectId,
                bleedArtefactId,
                bleedDocument,
                bleedVersion,
                authorityLevel: "AcceptedArchitecture",
                content: bleedContent,
                chunkId: bleedChunkId);
        }

        await artefactRepository.MarkStaleAsync(new SemanticStaleRequest
        {
            ProjectId = project.ProjectId,
            SourceEntityType = "ProjectDocument",
            SourceEntityId = document.Id.ToString(),
            SourceVersionId = oldVersion.Id.ToString()
        });
        await chunkRepository.MarkArtefactChunksStaleAsync(oldArtefactId);

        var oldArtefact = await artefactRepository.GetArtefactAsync(oldArtefactId)
            ?? throw new InvalidOperationException("Old artefact missing after reindex.");
        var currentArtefact = await artefactRepository.GetArtefactAsync(currentArtefactId)
            ?? throw new InvalidOperationException("Current artefact missing after reindex.");
        var bleedArtefact = await artefactRepository.GetArtefactAsync(bleedArtefactId)
            ?? throw new InvalidOperationException("Bleed artefact missing after reindex.");
        var oldChunk = (await chunkRepository.GetChunksAsync(oldArtefactId, includeStale: true)).Single();
        var currentChunk = (await chunkRepository.GetChunksAsync(currentArtefactId, includeStale: true)).Single();
        var bleedChunk = (await chunkRepository.GetChunksAsync(bleedArtefactId, includeStale: true)).Single();

        var endpoint = MemorySmokeCommandSupport.ReadOption(args, "--weaviate-endpoint") ?? MemorySmokeCommandSupport.ResolveWeaviateEndpoint();
        var collectionName = MemorySmokeCommandSupport.BuildWeaviateDogfoodCollectionName(dogfoodRunId);
        var queryVector = new[] { 1.0, 0.0, 0.0, 0.0 };
        using var httpClient = new HttpClient { BaseAddress = new Uri(endpoint.TrimEnd('/') + "/") };
        await MemorySmokeCommandSupport.EnsureWeaviateDogfoodCollectionAsync(httpClient, collectionName);

        for (var i = 0; i < 2; i++)
        {
            await MemorySmokeCommandSupport.UpsertWeaviateChunkAsync(httpClient, collectionName, oldChunk.Id, oldArtefact, oldChunk, project.TenantId, isStale: true, vector: new[] { 1.0, 0.0, 0.0, 0.0 });
            await MemorySmokeCommandSupport.UpsertWeaviateChunkAsync(httpClient, collectionName, currentChunk.Id, currentArtefact, currentChunk, project.TenantId, isStale: false, vector: new[] { 0.72, 0.69, 0.0, 0.0 });
            await MemorySmokeCommandSupport.UpsertWeaviateChunkAsync(httpClient, collectionName, bleedChunk.Id, bleedArtefact, bleedChunk, bleedProject.TenantId, isStale: false, vector: new[] { 0.99, 0.01, 0.0, 0.0 });
        }

        var rawMatches = await MemorySmokeCommandSupport.QueryWeaviateDogfoodCollectionAsync(httpClient, collectionName, queryVector, limit: 10);
        var rawRelevant = rawMatches
            .Where(match => match.SourceEntityId == document.Id.ToString() ||
                            match.SourceEntityId == bleedDocument.Id.ToString())
            .OrderBy(match => match.RawWeaviateRank)
            .ToArray();

        var candidates = rawRelevant
            .Where(match => match.ProjectId == project.ProjectId)
            .Select(match =>
            {
                var isCurrent = string.Equals(match.SourceVersionId, currentVersion.Id.ToString(), StringComparison.OrdinalIgnoreCase);
                return MemorySmokeCommandSupport.BuildCandidate(
                    project.TenantId,
                    project.ProjectId,
                    document,
                    isCurrent ? currentVersion : oldVersion,
                    isCurrent ? currentArtefact : oldArtefact,
                    isCurrent ? currentChunk : oldChunk,
                    isCurrent ? currentContent : oldContent,
                    vectorSimilarity: match.VectorSimilarity,
                    contentHashMismatch: false);
            })
            .ToArray();

        var query = new SemanticSearchQuery
        {
            ProjectId = project.ProjectId,
            QueryText = queryText,
            Consumer = "MemoryReindexFreshnessSmoke",
            Limit = 5,
            IncludeStale = true,
            BoostedArtefactIds = [currentArtefactId]
        };

        var results = ranker.Rank(query, candidates);
        var traceId = await traceRepository.CreateTraceAsync(query);
        await traceRepository.AddResultsAsync(traceId, results);
        var duplicateEvidence = await CountDuplicatesAsync(connectionFactory, project.ProjectId, document.Id, oldVersion.Id, currentVersion.Id, oldArtefactId, currentArtefactId);
        var rawOld = rawRelevant.FirstOrDefault(match => match.SourceVersionId == oldVersion.Id.ToString());
        var rawCurrent = rawRelevant.FirstOrDefault(match => match.SourceVersionId == currentVersion.Id.ToString());
        var rawBleed = rawRelevant.FirstOrDefault(match => match.ProjectId == bleedProject.ProjectId);
        var top = results.FirstOrDefault();
        var staleResult = results.FirstOrDefault(result => result.SourceVersionId == oldVersion.Id.ToString());
        var currentResult = results.FirstOrDefault(result => result.SourceVersionId == currentVersion.Id.ToString());
        var wrongProjectRejected = rawBleed is not null && results.All(result => result.SourceEntityId != bleedDocument.Id.ToString());
        var exactAcceptedTitlePromoted = currentResult is not null &&
                                         currentResult.SourceVersionId == currentVersion.Id.ToString() &&
                                         currentResult.DirectLinkBoost > 0;

        var passed = top is not null &&
                     top.SourceVersionId == currentVersion.Id.ToString() &&
                     currentResult is not null &&
                     staleResult is not null &&
                     staleResult.IsStale &&
                     staleResult.FinalScore < currentResult.FinalScore &&
                     rawOld is not null &&
                     rawCurrent is not null &&
                     rawOld.RawWeaviateRank < rawCurrent.RawWeaviateRank &&
                     duplicateEvidence.DuplicateCount == 0 &&
                     rawRelevant.Count(match => match.SourceEntityId == document.Id.ToString()) == 2 &&
                     wrongProjectRejected &&
                     exactAcceptedTitlePromoted;

        var result = new MemoryReindexFreshnessSmokeResult
        {
            DogfoodRunId = dogfoodRunId,
            Project = project.ProjectName,
            ProjectId = project.ProjectId,
            BleedProject = bleedProject.ProjectName,
            BleedProjectId = bleedProject.ProjectId,
            DocumentId = document.Id,
            DocumentTitle = document.Title,
            OldVersionId = oldVersion.Id,
            NewVersionId = currentVersion.Id,
            Query = queryText,
            WeaviateEndpoint = endpoint,
            WeaviateCollection = collectionName,
            SemanticTraceId = traceId,
            Passed = passed,
            RawRank = new ReindexRawRankEvidence
            {
                OldVersionRawRank = rawOld?.RawWeaviateRank,
                NewVersionRawRank = rawCurrent?.RawWeaviateRank,
                WrongProjectRawRank = rawBleed?.RawWeaviateRank
            },
            FinalRank = new ReindexFinalRankEvidence
            {
                OldVersionFinalRank = results.Select((item, index) => (item, index)).FirstOrDefault(pair => pair.item.SourceVersionId == oldVersion.Id.ToString()).index + 1,
                NewVersionFinalRank = results.Select((item, index) => (item, index)).FirstOrDefault(pair => pair.item.SourceVersionId == currentVersion.Id.ToString()).index + 1
            },
            StaleDemotion = new ReindexStaleDemotionEvidence
            {
                OldVersionVisible = staleResult is not null,
                OldVersionIsStale = staleResult?.IsStale ?? false,
                OldVersionStalePenalty = staleResult?.StalePenalty ?? 0,
                CurrentBeatsStale = currentResult is not null && staleResult is not null && currentResult.FinalScore > staleResult.FinalScore
            },
            Duplicates = new ReindexDuplicateEvidence
            {
                DuplicateArtefactSourceRecords = duplicateEvidence.DuplicateArtefactSourceRecords,
                DuplicateActiveChunks = duplicateEvidence.DuplicateActiveChunks,
                ActiveChunkCount = duplicateEvidence.ActiveChunkCount,
                DuplicateCount = duplicateEvidence.DuplicateCount,
                IndexedCandidateCount = rawRelevant.Count(match => match.SourceEntityId == document.Id.ToString()),
                DuplicateIndexedCandidates = Math.Max(0, rawRelevant.Count(match => match.SourceEntityId == document.Id.ToString()) - 2)
            },
            WrongProjectRejection = new ReindexWrongProjectRejectionEvidence
            {
                WrongProjectCandidateVisibleRaw = rawBleed is not null,
                WrongProjectName = bleedProject.ProjectName,
                WrongProjectRejectedFromFinal = wrongProjectRejected
            },
            ExactTitlePromotion = new ReindexExactTitlePromotionEvidence
            {
                ExactTitleQuery = title,
                PromotedAcceptedCurrentVersion = exactAcceptedTitlePromoted,
                DirectLinkBoost = currentResult?.DirectLinkBoost ?? 0
            },
            Results = results.Select((ranked, index) => new MemoryWeaviateSqlVersionSearchResult
            {
                FinalAuthorityRank = index + 1,
                RawWeaviateRank = rawRelevant.FirstOrDefault(match => match.SourceVersionId == ranked.SourceVersionId)?.RawWeaviateRank,
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

    private static async Task<ReindexDuplicateEvidence> CountDuplicatesAsync(
        CliConnectionFactory connectionFactory,
        int projectId,
        long documentId,
        long oldVersionId,
        long currentVersionId,
        Guid oldArtefactId,
        Guid currentArtefactId)
    {
        using var connection = connectionFactory.CreateConnection();
        var duplicateArtefacts = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM (
                SELECT SourceVersionId
                FROM dbo.SemanticArtefacts
                WHERE ProjectId = @ProjectId
                  AND SourceEntityType = 'ProjectDocument'
                  AND SourceEntityId = @DocumentId
                  AND SourceVersionId IN (@OldVersionId, @CurrentVersionId)
                GROUP BY SourceVersionId
                HAVING COUNT(*) > 1
            ) duplicates;
            """,
            new { ProjectId = projectId, DocumentId = documentId.ToString(), OldVersionId = oldVersionId.ToString(), CurrentVersionId = currentVersionId.ToString() });
        var duplicateActiveChunks = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM (
                SELECT ArtefactId, ChunkIndex
                FROM dbo.SemanticChunks
                WHERE ArtefactId IN @ArtefactIds
                  AND IsStale = 0
                GROUP BY ArtefactId, ChunkIndex
                HAVING COUNT(*) > 1
            ) duplicates;
            """,
            new { ArtefactIds = new[] { oldArtefactId, currentArtefactId } });
        var activeChunkCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.SemanticChunks
            WHERE ArtefactId IN @ArtefactIds
              AND IsStale = 0;
            """,
            new { ArtefactIds = new[] { oldArtefactId, currentArtefactId } });

        return new ReindexDuplicateEvidence
        {
            DuplicateArtefactSourceRecords = duplicateArtefacts,
            DuplicateActiveChunks = duplicateActiveChunks,
            ActiveChunkCount = activeChunkCount,
            DuplicateCount = duplicateArtefacts + duplicateActiveChunks
        };
    }
}
