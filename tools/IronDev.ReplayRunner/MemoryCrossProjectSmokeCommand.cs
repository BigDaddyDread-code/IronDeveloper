using System.Text.Json;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.SemanticMemory;
using IronDev.Services;
public static class MemoryCrossProjectSmokeCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var queryProjectName = MemorySmokeCommandSupport.ReadOption(args, "--project") ?? "IronDev";
        var bleedProjectName = MemorySmokeCommandSupport.ReadOption(args, "--bleed-project") ?? "BookSeller";
        var dogfoodRunId = MemorySmokeCommandSupport.ReadOption(args, "--dogfood-run-id") ?? $"memory-cross-project-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var queryText = MemorySmokeCommandSupport.ReadOption(args, "--query") ?? "first Codex goal checkout flow";
        var connectionString = MemorySmokeCommandSupport.ResolveIronDevConnectionString(args);
        var repoRoot = MemorySmokeCommandSupport.FindRepositoryRoot();
        var connectionFactory = new CliConnectionFactory(connectionString);

        await MemorySmokeCommandSupport.ApplySqlScriptAsync(
            connectionFactory,
            Path.Combine(repoRoot, "Database", "migrate_project_documents.sql"));

        var queryProject = await MemorySmokeCommandSupport.ResolveProjectAsync(connectionFactory, queryProjectName);
        var bleedProject = await MemorySmokeCommandSupport.ResolveProjectAsync(connectionFactory, bleedProjectName);
        if (queryProject is null)
        {
            Console.Error.WriteLine($"Project not found: {queryProjectName}");
            return 1;
        }

        if (bleedProject is null)
        {
            Console.Error.WriteLine($"Bleed-test project not found: {bleedProjectName}");
            return 1;
        }

        var queryTenant = new CliTenantContext(queryProject.TenantId);
        var bleedTenant = new CliTenantContext(bleedProject.TenantId);
        var queryDocumentService = new ProjectDocumentService(connectionFactory, queryTenant);
        var bleedDocumentService = new ProjectDocumentService(connectionFactory, bleedTenant);
        var artefactRepository = new SemanticArtefactRepository(connectionFactory);
        var chunkRepository = new SemanticChunkRepository(connectionFactory);
        var traceRepository = new SemanticSearchTraceRepository(connectionFactory);
        var ranker = new SemanticRankingService();

        var stamp = dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
        var ironDevTitleSeed = $"CODEX_GOALS_CROSS_PROJECT_IRONDEV_{stamp}_{Guid.NewGuid():N}";
        var bookSellerTitleSeed = $"CODEX_GOALS_CROSS_PROJECT_BOOKSELLER_{stamp}_{Guid.NewGuid():N}";
        var ironDevTitle = ironDevTitleSeed[..Math.Min(120, ironDevTitleSeed.Length)];
        var bookSellerTitle = bookSellerTitleSeed[..Math.Min(120, bookSellerTitleSeed.Length)];
        var ironDevContent = """
        # IronDev Cross Project Memory Goal

        The first Codex goal is to prove IronDev memory spine retrieval and prevent project bleed.
        IronDev memory is authoritative only inside the IronDev project context.
        """;
        var bookSellerContent = """
        # BookSeller Cross Project Memory Goal

        The first Codex goal is to test BookSeller checkout, cart, payment, and order flow.
        This document is intentionally semantically tempting for checkout-flow queries.
        """;

        var ironDevDocument = await queryDocumentService.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = queryProject.ProjectId,
            Title = ironDevTitle,
            DocumentType = "Architecture",
            ContentMarkdown = ironDevContent,
            ChangeSummary = "IronDev cross-project authority source",
            CreatedBy = "TestAgent",
            SourceEntityType = "Discussion",
            SourceEntityId = 9201
        });
        var ironDevVersion = await queryDocumentService.GetCurrentVersionAsync(ironDevDocument.Id)
            ?? throw new InvalidOperationException("IronDev cross-project document version was not created.");
        await queryDocumentService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
        {
            DocumentVersionId = ironDevVersion.Id,
            LinkedEntityType = "Discussion",
            LinkedEntityId = 9202,
            LinkType = "CurrentGoalSource",
            CreatedBy = "TestAgent"
        });

        var bookSellerDocument = await bleedDocumentService.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = bleedProject.ProjectId,
            Title = bookSellerTitle,
            DocumentType = "Architecture",
            ContentMarkdown = bookSellerContent,
            ChangeSummary = "BookSeller cross-project temptation source",
            CreatedBy = "TestAgent",
            SourceEntityType = "Discussion",
            SourceEntityId = 9301
        });
        var bookSellerVersion = await bleedDocumentService.GetCurrentVersionAsync(bookSellerDocument.Id)
            ?? throw new InvalidOperationException("BookSeller cross-project document version was not created.");

        var ironDevArtefactId = Guid.NewGuid();
        var bookSellerArtefactId = Guid.NewGuid();
        await MemorySmokeCommandSupport.IndexDocumentVersionAsync(
            artefactRepository,
            chunkRepository,
            queryProject.TenantId,
            queryProject.ProjectId,
            ironDevArtefactId,
            ironDevDocument,
            ironDevVersion,
            authorityLevel: "AcceptedArchitecture",
            content: ironDevContent);
        await MemorySmokeCommandSupport.IndexDocumentVersionAsync(
            artefactRepository,
            chunkRepository,
            bleedProject.TenantId,
            bleedProject.ProjectId,
            bookSellerArtefactId,
            bookSellerDocument,
            bookSellerVersion,
            authorityLevel: "AcceptedArchitecture",
            content: bookSellerContent);

        var ironDevArtefact = await artefactRepository.GetArtefactAsync(ironDevArtefactId)
            ?? throw new InvalidOperationException("IronDev semantic artefact was not persisted.");
        var bookSellerArtefact = await artefactRepository.GetArtefactAsync(bookSellerArtefactId)
            ?? throw new InvalidOperationException("BookSeller semantic artefact was not persisted.");
        var ironDevChunk = (await chunkRepository.GetChunksAsync(ironDevArtefactId, includeStale: true)).Single();
        var bookSellerChunk = (await chunkRepository.GetChunksAsync(bookSellerArtefactId, includeStale: true)).Single();

        var weaviateEndpoint = MemorySmokeCommandSupport.ReadOption(args, "--weaviate-endpoint") ?? MemorySmokeCommandSupport.ResolveWeaviateEndpoint();
        var collectionName = MemorySmokeCommandSupport.BuildWeaviateDogfoodCollectionName(dogfoodRunId);
        var queryVector = new[] { 1.0, 0.0, 0.0, 0.0 };
        var bookSellerVector = new[] { 1.0, 0.0, 0.0, 0.0 };
        var ironDevVector = new[] { 0.82, 0.5723635, 0.0, 0.0 };

        using var httpClient = new HttpClient { BaseAddress = new Uri(weaviateEndpoint.TrimEnd('/') + "/") };
        await MemorySmokeCommandSupport.EnsureWeaviateDogfoodCollectionAsync(httpClient, collectionName);
        await MemorySmokeCommandSupport.UpsertWeaviateChunkAsync(
            httpClient,
            collectionName,
            bookSellerChunk.Id,
            bookSellerArtefact,
            bookSellerChunk,
            bleedProject.TenantId,
            isStale: false,
            vector: bookSellerVector);
        await MemorySmokeCommandSupport.UpsertWeaviateChunkAsync(
            httpClient,
            collectionName,
            ironDevChunk.Id,
            ironDevArtefact,
            ironDevChunk,
            queryProject.TenantId,
            isStale: false,
            vector: ironDevVector);

        var rawMatches = await MemorySmokeCommandSupport.QueryWeaviateDogfoodCollectionAsync(httpClient, collectionName, queryVector, limit: 5);
        var rawRelevantMatches = rawMatches
            .Where(match =>
                string.Equals(match.SourceVersionId, ironDevVersion.Id.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(match.SourceVersionId, bookSellerVersion.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(match => match.RawWeaviateRank)
            .ToArray();
        var acceptedMatches = rawRelevantMatches
            .Where(match => match.ProjectId == queryProject.ProjectId)
            .ToArray();

        var candidates = acceptedMatches
            .Where(match => match.ArtefactId == ironDevArtefact.Id && match.ChunkId == ironDevChunk.Id)
            .Select(match => MemorySmokeCommandSupport.BuildCandidate(
                queryProject.TenantId,
                queryProject.ProjectId,
                ironDevDocument,
                ironDevVersion,
                ironDevArtefact,
                ironDevChunk,
                ironDevContent,
                vectorSimilarity: match.VectorSimilarity,
                contentHashMismatch: false))
            .ToArray();

        var query = new SemanticSearchQuery
        {
            ProjectId = queryProject.ProjectId,
            QueryText = queryText,
            Consumer = "MemorySpineCrossProjectSmoke",
            Limit = 5,
            IncludeStale = true
        };

        var results = ranker.Rank(query, candidates);
        var traceId = await traceRepository.CreateTraceAsync(query);
        await traceRepository.AddResultsAsync(traceId, results);
        var links = await queryDocumentService.GetLinksForVersionAsync(ironDevVersion.Id);

        var rawTop = rawRelevantMatches.FirstOrDefault();
        var finalTop = results.FirstOrDefault();
        var passed = rawTop is not null &&
                     rawTop.ProjectId == bleedProject.ProjectId &&
                     finalTop is not null &&
                     finalTop.SourceVersionId == ironDevVersion.Id.ToString() &&
                     finalTop.Document.ProjectId == queryProject.ProjectId &&
                     links.Any(link => link.LinkType == "CurrentGoalSource");

        var decisions = rawRelevantMatches.Select(match =>
        {
            var isQueryProject = match.ProjectId == queryProject.ProjectId;
            var finalRank = results
                .Select((result, index) => new { result.SourceVersionId, Rank = index + 1 })
                .FirstOrDefault(result => string.Equals(result.SourceVersionId, match.SourceVersionId, StringComparison.OrdinalIgnoreCase))
                ?.Rank;

            return new CrossProjectMemoryDecision
            {
                RawWeaviateRank = match.RawWeaviateRank,
                FinalAuthorityRank = finalRank,
                ProjectId = match.ProjectId,
                ProjectName = match.ProjectId == queryProject.ProjectId ? queryProject.ProjectName : bleedProject.ProjectName,
                SourceVersionId = match.SourceVersionId,
                VectorSimilarity = match.VectorSimilarity,
                Decision = isQueryProject ? "accepted_project_authority" : "rejected_cross_project"
            };
        }).ToArray();

        var result = new MemoryCrossProjectSmokeResult
        {
            DogfoodRunId = dogfoodRunId,
            TenantId = queryProject.TenantId,
            QueryProjectId = queryProject.ProjectId,
            QueryProjectName = queryProject.ProjectName,
            BleedProjectId = bleedProject.ProjectId,
            BleedProjectName = bleedProject.ProjectName,
            Query = queryText,
            WeaviateEndpoint = weaviateEndpoint,
            WeaviateCollection = collectionName,
            SemanticTraceId = traceId,
            SourceLinkCount = links.Count,
            Passed = passed,
            RawMatches = rawRelevantMatches,
            Decisions = decisions,
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
