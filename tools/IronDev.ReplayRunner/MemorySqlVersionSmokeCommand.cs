using System.Text.Json;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.SemanticMemory;
using IronDev.Services;
public static class MemorySqlVersionSmokeCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var requestedProjectName = MemorySmokeCommandSupport.ReadOption(args, "--project") ?? "IronDev";
        var dogfoodRunId = MemorySmokeCommandSupport.ReadOption(args, "--dogfood-run-id") ?? $"memory-sql-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var queryText = MemorySmokeCommandSupport.ReadOption(args, "--query") ?? "current first goal";
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
        var titleSeed = $"CODEX_GOALS_SQL_SPINE_{stamp}_{Guid.NewGuid():N}";
        var title = titleSeed[..Math.Min(120, titleSeed.Length)];
        var oldContent = """
        # Codex Goals SQL Spine

        Old first goal = test builder output.
        This historical version is intentionally stale and must not outrank the current version.
        """;
        var currentContent = """
        # Codex Goals SQL Spine

        Current first goal = prove memory spine retrieval.
        IronDev must retrieve the current SQL ProjectDocumentVersion as authoritative memory.
        """;

        var document = await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = project.ProjectId,
            Title = title,
            DocumentType = "Architecture",
            ContentMarkdown = oldContent,
            ChangeSummary = "Historical dogfood version",
            CreatedBy = "TestAgent",
            SourceEntityType = "Discussion",
            SourceEntityId = 9001
        });

        var oldVersion = await documentService.GetCurrentVersionAsync(document.Id)
            ?? throw new InvalidOperationException("Initial document version was not created.");

        var currentVersion = await documentService.AddVersionAsync(new AddProjectDocumentVersionRequest
        {
            DocumentId = document.Id,
            ContentMarkdown = currentContent,
            ChangeSummary = "Current dogfood version",
            CreatedBy = "TestAgent",
            IncrementMajorVersion = true,
            Status = "Approved"
        });

        await documentService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
        {
            DocumentVersionId = currentVersion.Id,
            LinkedEntityType = "Discussion",
            LinkedEntityId = 9002,
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

        var query = new SemanticSearchQuery
        {
            ProjectId = project.ProjectId,
            QueryText = queryText,
            Consumer = "MemorySpineSmoke",
            Limit = 5,
            IncludeStale = true
        };

        var candidates = new[]
        {
        MemorySmokeCommandSupport.BuildCandidate(project.TenantId, project.ProjectId, document, oldVersion, oldArtefact, oldChunk, oldContent, vectorSimilarity: 0.88, contentHashMismatch: false),
        MemorySmokeCommandSupport.BuildCandidate(project.TenantId, project.ProjectId, document, currentVersion, currentArtefact, currentChunk, currentContent, vectorSimilarity: 0.82, contentHashMismatch: false)
    };

        var results = ranker.Rank(query, candidates);
        var traceId = await traceRepository.CreateTraceAsync(query);
        await traceRepository.AddResultsAsync(traceId, results);
        var links = await documentService.GetLinksForVersionAsync(currentVersion.Id);

        var top = results.FirstOrDefault();
        var passed = top is not null &&
                     top.SourceVersionId == currentVersion.Id.ToString() &&
                     !top.IsStale &&
                     links.Any(link => link.LinkType == "CurrentGoalSource");

        var result = new MemorySqlVersionSmokeResult
        {
            DogfoodRunId = dogfoodRunId,
            ProjectId = project.ProjectId,
            TenantId = project.TenantId,
            ProjectName = project.ProjectName,
            DocumentId = document.Id,
            CurrentVersionId = currentVersion.Id,
            OldVersionId = oldVersion.Id,
            Query = queryText,
            SemanticTraceId = traceId,
            SourceLinkCount = links.Count,
            Passed = passed,
            Expected = new MemorySqlVersionExpected
            {
                TopSourceVersionId = currentVersion.Id.ToString(),
                OldVersionShouldBeStale = true,
                SourceLinkRequired = true
            },
            Results = results.Select(result => new MemorySqlVersionSearchResult
            {
                Title = result.Title,
                SourceEntityType = result.SourceEntityType,
                SourceEntityId = result.SourceEntityId,
                SourceVersionId = result.SourceVersionId,
                FinalScore = result.FinalScore,
                VectorSimilarity = result.VectorSimilarity,
                AuthorityBoost = result.AuthorityBoost,
                SourceTypeBoost = result.SourceTypeBoost,
                RecencyBoost = result.RecencyBoost,
                StalePenalty = result.StalePenalty,
                IsStale = result.IsStale,
                MatchReason = result.MatchReason
            }).ToArray()
        };

        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return passed ? 0 : 1;
    }


}
