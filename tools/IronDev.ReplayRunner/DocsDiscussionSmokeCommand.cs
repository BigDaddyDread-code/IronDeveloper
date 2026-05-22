using System.Text.Json;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services.SemanticMemory;
using IronDev.Services;

public static class DocsDiscussionSmokeCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var projectName = MemorySmokeCommandSupport.ReadOption(args, "--project") ?? "BookSeller";
        var dogfoodRunId = MemorySmokeCommandSupport.ReadOption(args, "--dogfood-run-id") ?? $"discussion-doc-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = MemorySmokeCommandSupport.FindRepositoryRoot();
        var connectionFactory = new CliConnectionFactory(MemorySmokeCommandSupport.ResolveIronDevConnectionString(args));

        await MemorySmokeCommandSupport.ApplySqlScriptAsync(
            connectionFactory,
            Path.Combine(repoRoot, "Database", "migrate_project_documents.sql"));

        var project = await MemorySmokeCommandSupport.ResolveProjectAsync(connectionFactory, projectName);
        if (project is null)
        {
            Console.Error.WriteLine($"Project not found: {projectName}");
            return 1;
        }

        var tenant = new CliTenantContext(project.TenantId);
        var documentService = new ProjectDocumentService(connectionFactory, tenant);
        var artefactRepository = new SemanticArtefactRepository(connectionFactory);
        var chunkRepository = new SemanticChunkRepository(connectionFactory);
        var traceRepository = new SemanticSearchTraceRepository(connectionFactory);
        var ranker = new SemanticRankingService();
        var sourceDiscussionId = Math.Abs(dogfoodRunId.GetHashCode());
        var title = BuildTitle(dogfoodRunId);
        var draftMarkdown = """
        # BookSeller Storage Discussion

        Source user statement: I want to persist data.

        Clarification needed: identify the project entities, database choice, and persistence style.
        """;
        var acceptedMarkdown = """
        # BookSeller Storage Discussion

        BookSeller should save books, authors, stock counts, storage locations, and sales history.
        SQL Server is the accepted persistence store for this discussion.
        Dapper is acceptable for the first BookSeller dogfood slice.

        Boundary: this proves discussion-to-document capture only; it does not create tickets or build code.
        """;

        var document = await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = project.ProjectId,
            Title = title,
            DocumentType = "DiscussionSummary",
            ContentMarkdown = draftMarkdown,
            ChangeSummary = "Captured vague storage discussion.",
            CreatedBy = "TestAgent",
            SourceEntityType = "ChatDiscussion",
            SourceEntityId = sourceDiscussionId
        });

        var draftVersion = await documentService.GetCurrentVersionAsync(document.Id)
            ?? throw new InvalidOperationException("Initial discussion document version was not created.");

        var acceptedVersion = await documentService.AddVersionAsync(new AddProjectDocumentVersionRequest
        {
            DocumentId = document.Id,
            ContentMarkdown = acceptedMarkdown,
            ChangeSummary = "Accepted clarified BookSeller storage discussion.",
            CreatedBy = "TestAgent",
            IncrementMajorVersion = true,
            Status = "Approved"
        });

        await documentService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
        {
            DocumentVersionId = acceptedVersion.Id,
            LinkedEntityType = "ChatDiscussion",
            LinkedEntityId = sourceDiscussionId,
            LinkType = "CreatedFrom",
            CreatedBy = "TestAgent"
        });

        var artefactId = Guid.NewGuid();
        await MemorySmokeCommandSupport.IndexDocumentVersionAsync(
            artefactRepository,
            chunkRepository,
            project.TenantId,
            project.ProjectId,
            artefactId,
            document,
            acceptedVersion,
            authorityLevel: "AcceptedDiscussion",
            content: acceptedMarkdown);

        var artefact = await artefactRepository.GetArtefactAsync(artefactId)
            ?? throw new InvalidOperationException("Discussion semantic artefact was not persisted.");
        var chunk = (await chunkRepository.GetChunksAsync(artefactId, includeStale: false)).Single();
        var query = new SemanticSearchQuery
        {
            ProjectId = project.ProjectId,
            QueryText = "BookSeller storage discussion SQL Server Dapper",
            Consumer = "DiscussionToDocumentSmoke",
            Limit = 5,
            IncludeStale = false
        };
        var candidate = MemorySmokeCommandSupport.BuildCandidate(
            project.TenantId,
            project.ProjectId,
            document,
            acceptedVersion,
            artefact,
            chunk,
            acceptedMarkdown,
            vectorSimilarity: 0.92,
            contentHashMismatch: false);
        var results = ranker.Rank(query, [candidate]);
        var traceId = await traceRepository.CreateTraceAsync(query);
        await traceRepository.AddResultsAsync(traceId, results);

        var draftLinks = await documentService.GetLinksForVersionAsync(draftVersion.Id);
        var acceptedLinks = await documentService.GetLinksForVersionAsync(acceptedVersion.Id);
        var top = results.FirstOrDefault();
        var passed = document.ProjectId == project.ProjectId &&
                     acceptedVersion.Status == "Approved" &&
                     draftLinks.Any(link => link.LinkedEntityType == "ChatDiscussion" && link.LinkType == "CreatedFrom") &&
                     acceptedLinks.Any(link => link.LinkedEntityType == "ChatDiscussion" && link.LinkType == "CreatedFrom") &&
                     top?.SourceVersionId == acceptedVersion.Id.ToString();

        var result = new
        {
            dogfoodRunId = dogfoodRunId,
            projectId = project.ProjectId,
            projectName = project.ProjectName,
            sourceDiscussionId,
            documentId = document.Id,
            documentTitle = document.Title,
            documentType = document.DocumentType,
            draftVersionId = draftVersion.Id,
            currentVersionId = acceptedVersion.Id,
            currentVersionStatus = acceptedVersion.Status,
            draftSourceLinkCount = draftLinks.Count,
            currentSourceLinkCount = acceptedLinks.Count,
            semanticTraceId = traceId,
            semanticArtefactId = artefactId,
            topSourceVersionId = top?.SourceVersionId,
            passed,
            boundary = "027 proves discussion-to-document capture and indexing only; it does not generate tickets or build code."
        };

        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return passed ? 0 : 1;
    }

    private static string BuildTitle(string dogfoodRunId)
    {
        var stamp = dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
        var title = $"BOOKSELLER_DISCUSSION_STORAGE_{stamp}_{Guid.NewGuid():N}";
        return title[..Math.Min(120, title.Length)];
    }
}
