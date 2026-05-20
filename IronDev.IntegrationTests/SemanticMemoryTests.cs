using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services.SemanticMemory;
using IronDev.Infrastructure.Services.KnowledgeCompiler;
using IronDev.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public class SemanticMemoryTests : IntegrationTestBase
{
    private IProjectMemoryService _projectMemoryService = default!;
    private ISemanticMemoryService _semanticMemory = default!;
    private IEmbeddingProvider _embeddingProvider = default!;
    private IEmbeddingContentExtractor _contentExtractor = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        _projectMemoryService = ServiceProvider.GetRequiredService<IProjectMemoryService>();
        
        _embeddingProvider = new FakeEmbeddingProvider(new EmbeddingOptions { Provider = "Fake" });
        _contentExtractor = new ContextDocumentEmbeddingContentExtractor();
        
        _semanticMemory = new InMemorySemanticMemoryService(
            _embeddingProvider,
            _contentExtractor,
            _projectMemoryService,
            new SemanticRankingOptions());
    }

    [TestMethod]
    public async Task EmbedAndStore_ShouldInsertDocumentAndAllowSearch()
    {
        // Arrange
        int projectId = await SeedProjectAsync(name: "SemanticTestProject");
        var doc = new ProjectContextDocument
        {
            TenantId = 1,
            ProjectId = projectId,
            DocumentType = "ArchitectureDecision",
            AuthorityLevel = "Binding",
            Status = "Active",
            Title = "Use Postgres for database storage",
            Content = "We decided to use Postgres because of its reliability.",
            Summary = "Database decision for Postgres",
            Tags = "postgres, db",
            CreatedDate = DateTime.UtcNow
        };

        // Save doc to DB to ensure unique ID
        doc.Id = await _projectMemoryService.SaveContextDocumentAsync(doc);

        // Act
        await _semanticMemory.EmbedAndStoreAsync(doc);
        var query = _contentExtractor.Extract(doc);
        var searchResults = await _semanticMemory.SearchAsync(projectId, query, limit: 3, minSimilarity: -1.0);

        // Assert
        Assert.AreEqual(1, searchResults.Count);
        Assert.AreEqual(doc.Title, searchResults[0].Document.Title);
        Assert.IsTrue(searchResults[0].Similarity > 0.75);
    }

    [TestMethod]
    public async Task Search_ShouldApplyRankingWeightsAndRankAuthorityHigher()
    {
        // Arrange
        int projectId = await SeedProjectAsync(name: "RankingTestProject");

        // Seed low authority document
        var lowAuthDoc = new ProjectContextDocument
        {
            TenantId = 1,
            ProjectId = projectId,
            DocumentType = "DiscussionLog",
            AuthorityLevel = "ContextOnly", // Low Authority
            Status = "Active",
            Title = "Authentication system notes",
            Content = "Notes about using JWT for authentication.",
            CreatedDate = DateTime.UtcNow
        };

        // Seed binding authority document
        var highAuthDoc = new ProjectContextDocument
        {
            TenantId = 1,
            ProjectId = projectId,
            DocumentType = "ArchitectureDecision",
            AuthorityLevel = "Binding", // High Authority
            Status = "Active",
            Title = "Authentication system standard",
            Content = "Standard for using JWT for authentication.",
            CreatedDate = DateTime.UtcNow
        };

        // Save documents to DB to ensure unique IDs
        lowAuthDoc.Id = await _projectMemoryService.SaveContextDocumentAsync(lowAuthDoc);
        highAuthDoc.Id = await _projectMemoryService.SaveContextDocumentAsync(highAuthDoc);

        // Act
        await _semanticMemory.EmbedAndStoreAsync(lowAuthDoc);
        await _semanticMemory.EmbedAndStoreAsync(highAuthDoc);
        var results = await _semanticMemory.SearchAsync(projectId, "JWT authentication", limit: 5, minSimilarity: -1.0);

        // Assert
        Assert.AreEqual(2, results.Count);
        // The high authority document should rank first due to higher authority weighting
        Assert.AreEqual(highAuthDoc.Title, results[0].Document.Title);
    }

    [TestMethod]
    public async Task DeleteEmbedding_ShouldRemoveFromMemory()
    {
        // Arrange
        int projectId = await SeedProjectAsync(name: "DeleteTestProject");
        var doc = new ProjectContextDocument
        {
            TenantId = 1,
            ProjectId = projectId,
            DocumentType = "ObservedPattern",
            AuthorityLevel = "Pending",
            Status = "Active",
            Title = "Error handling pattern",
            Content = "Always use try-catch block for database calls.",
            CreatedDate = DateTime.UtcNow
        };

        // Save doc to DB so it has an ID and can be fetched during deletion lookup
        await _projectMemoryService.SaveContextDocumentAsync(doc);
        var docFromDb = (await _projectMemoryService.GetContextDocumentsAsync(projectId, status: "Active")).First(x => x.Title == "Error handling pattern");

        // Act
        await _semanticMemory.EmbedAndStoreAsync(docFromDb);
        var initialResults = await _semanticMemory.SearchAsync(projectId, "Error handling", limit: 2, minSimilarity: -1.0);
        Assert.AreEqual(1, initialResults.Count);

        // Create the artefactId
        byte[] bytes = new byte[16];
        BitConverter.GetBytes(docFromDb.Id).CopyTo(bytes, 0);
        var artefactId = new Guid(bytes);

        await _semanticMemory.DeleteEmbeddingAsync(artefactId);
        var afterDeleteResults = await _semanticMemory.SearchAsync(projectId, "Error handling", limit: 2, minSimilarity: -1.0);

        // Assert
        Assert.AreEqual(0, afterDeleteResults.Count);
    }

    [TestMethod]
    public async Task RebuildIndex_ShouldIndexAllActiveDocuments()
    {
        // Arrange
        int projectId = await SeedProjectAsync(name: "RebuildTestProject");

        var doc1 = new ProjectContextDocument
        {
            TenantId = 1,
            ProjectId = projectId,
            DocumentType = "ObservedPattern",
            AuthorityLevel = "StrongGuidance",
            Status = "Active",
            Title = "Logging rules",
            Content = "Log all exceptions as errors.",
            CreatedDate = DateTime.UtcNow
        };

        var doc2 = new ProjectContextDocument
        {
            TenantId = 1,
            ProjectId = projectId,
            DocumentType = "ObservedPattern",
            AuthorityLevel = "StrongGuidance",
            Status = "Active",
            Title = "Telemetry rules",
            Content = "Track page view durations.",
            CreatedDate = DateTime.UtcNow
        };

        await _projectMemoryService.SaveContextDocumentAsync(doc1);
        await _projectMemoryService.SaveContextDocumentAsync(doc2);

        var memoryService = new InMemorySemanticMemoryService(_embeddingProvider, _contentExtractor, _projectMemoryService, new SemanticRankingOptions());

        var initialResults = await memoryService.SearchAsync(projectId, "Logging rules", limit: 5, minSimilarity: -1.0);
        Assert.AreEqual(0, initialResults.Count);

        // Act
        var progressTracker = new Progress<SemanticIndexRebuildProgress>();
        await memoryService.RebuildIndexAsync(projectId, progressTracker);

        // Assert
        var results = await memoryService.SearchAsync(projectId, "Logging", limit: 5, minSimilarity: -1.0);
        Assert.IsTrue(results.Count >= 1);
        Assert.IsTrue(results.Any(r => r.Document.Title == "Logging rules"));
    }

    [TestMethod]
    public async Task BuildContextBundle_ShouldReturnPromptMarkdownAndExplainableScores()
    {
        // Arrange
        int projectId = await SeedProjectAsync(name: "BundleTestProject");
        var doc = new ProjectContextDocument
        {
            TenantId = 1,
            ProjectId = projectId,
            DocumentType = "ArchitectureDecision",
            AuthorityLevel = "Binding",
            Status = "Active",
            Title = "Builder changes require approval",
            Content = "Code changes must be reviewed before they are applied.",
            Summary = "Human approval is required for builder changes.",
            Tags = "builder,approval,safety",
            CreatedDate = DateTime.UtcNow
        };

        doc.Id = await _projectMemoryService.SaveContextDocumentAsync(doc);
        await _semanticMemory.EmbedAndStoreAsync(doc);

        // Act
        var bundle = await _semanticMemory.BuildContextBundleAsync(
            projectId,
            _contentExtractor.Extract(doc),
            "DiscussionResolver",
            limit: 3);

        // Assert
        Assert.AreEqual(projectId, bundle.ProjectId);
        Assert.AreEqual("DiscussionResolver", bundle.CallerContext);
        Assert.AreEqual(1, bundle.Results.Count);
        Assert.AreEqual(doc.Title, bundle.Results[0].Document.Title);
        Assert.IsTrue(bundle.Results[0].FinalScore > 0);
        Assert.IsTrue(bundle.Results[0].SimilarityScore > 0);
        Assert.IsTrue(bundle.Results[0].AuthorityBoost > 0);
        Assert.IsTrue(bundle.PromptContextMarkdown.Contains(doc.Title, StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(bundle.PromptContextMarkdown.Contains("Retrieved Project Memory", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SemanticWorkflowMemoryNode_ShouldBuildLangGraphReadyContext()
    {
        // Arrange
        int projectId = await SeedProjectAsync(name: "WorkflowMemoryProject");
        var doc = new ProjectContextDocument
        {
            TenantId = 1,
            ProjectId = projectId,
            DocumentType = "ArchitectureDecision",
            AuthorityLevel = "Binding",
            Status = "Active",
            Title = "Builder nodes must retrieve semantic memory first",
            Content = "Before planning or building, workflow nodes must retrieve authority-aware semantic memory from the Knowledge Compiler.",
            Summary = "Workflow planning is grounded by semantic memory.",
            Tags = "langgraph,workflow,builder,memory",
            CreatedDate = DateTime.UtcNow
        };

        doc.Id = await _projectMemoryService.SaveContextDocumentAsync(doc);
        await _semanticMemory.EmbedAndStoreAsync(doc);

        var node = new SemanticWorkflowMemoryNode(_semanticMemory);

        // Act
        var context = await node.BuildContextAsync(new SemanticWorkflowNodeRequest
        {
            ProjectId = projectId,
            Consumer = "BuildPlanner",
            Goal = _contentExtractor.Extract(doc),
            UserRequest = "Plan a ticket using Knowledge Compiler memory.",
            PreferredArtefactTypes = ["Decision", "Architecture"],
            Limit = 4
        });

        // Assert
        Assert.AreEqual(projectId, context.ProjectId);
        Assert.AreEqual("BuildPlanner", context.Consumer);
        Assert.AreEqual(1, context.Items.Count);
        Assert.AreEqual(doc.Title, context.Items[0].Title);
        Assert.IsTrue(context.Items[0].FinalScore > 0);
        Assert.IsTrue(context.PromptContextMarkdown.Contains("Retrieved Project Memory", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(context.PromptContextMarkdown.Contains(doc.Title, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task SemanticWorkflowMemoryNode_ShouldFailSoftWhenRequestHasNoSearchableText()
    {
        // Arrange
        int projectId = await SeedProjectAsync(name: "WorkflowMemoryEmptyProject");
        var node = new SemanticWorkflowMemoryNode(_semanticMemory);

        // Act
        var context = await node.BuildContextAsync(new SemanticWorkflowNodeRequest
        {
            ProjectId = projectId,
            Consumer = "Validator"
        });

        // Assert
        Assert.AreEqual(projectId, context.ProjectId);
        Assert.AreEqual("Validator", context.Consumer);
        Assert.AreEqual(0, context.Items.Count);
        Assert.AreEqual(1, context.Warnings.Count);
        Assert.IsTrue(context.PromptContextMarkdown.Contains("No semantic memory matches", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task KnowledgeArtefactApply_ShouldEmbedContextDocumentsAfterSave()
    {
        // Arrange
        int projectId = await SeedProjectAsync(name: "ApplyEmbeddingProject");
        var applyService = new KnowledgeArtefactApplyService(
            _projectMemoryService,
            ServiceProvider.GetRequiredService<ITicketService>(),
            ServiceProvider.GetRequiredService<IArtifactSourceReferenceService>(),
            TenantContext,
            _semanticMemory);

        var proposal = new ArtefactProposal
        {
            Id = Guid.NewGuid(),
            Kind = ArtefactProposalKind.Requirement,
            Title = "Semantic memory feeds workflow planning",
            Summary = "Workflow nodes should use semantic context bundles.",
            Detail = "When a workflow plans implementation work, retrieve authority-aware project memory first.",
            Rationale = "This keeps later LangGraph orchestration grounded in Knowledge Compiler memory.",
            Category = "Workflow",
            ConfidenceScore = 90
        };

        // Act
        var applyResult = await applyService.ApplyAsync(new ArtefactApplyRequest
        {
            ProjectId = projectId,
            Proposals = [proposal]
        });

        var searchResults = await _semanticMemory.SearchAsync(
            projectId,
            "workflow nodes semantic context bundles",
            limit: 5,
            minSimilarity: -1.0);

        // Assert
        Assert.AreEqual(1, applyResult.AppliedCount);
        Assert.AreEqual(1, applyResult.Results.Count);
        Assert.IsTrue(applyResult.Results[0].Success);
        Assert.IsTrue(searchResults.Any(r => r.Document.Title == proposal.Title));
    }

    [TestMethod]
    public void MarkdownAwareSemanticChunker_ShouldPreserveHeadingBoundaries()
    {
        // Arrange
        var chunker = new MarkdownAwareSemanticChunker();
        var text = string.Join("\n\n", Enumerable.Range(1, 8).Select(i =>
            $"## Section {i}\n" + new string('x', 520)));
        var artefact = new SemanticArtefactDraft
        {
            ProjectId = 42,
            ArtefactType = "Architecture",
            AuthorityLevel = "AcceptedArchitecture",
            Title = "Chunking strategy",
            SearchableText = text
        };

        // Act
        var chunks = chunker.Chunk(artefact);

        // Assert
        Assert.IsTrue(chunks.Count > 1);
        Assert.IsTrue(chunks.All(c => c.ChunkText.Length <= 2600));
        Assert.IsTrue(chunks[0].ChunkText.Contains("## Section 1", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(0, chunks[0].ChunkIndex);
    }

    [TestMethod]
    public void SemanticRankingService_ShouldPreferCommittedAuthorityWhenSimilarityIsClose()
    {
        // Arrange
        var ranker = new SemanticRankingService();
        var doc = new ProjectContextDocument
        {
            ProjectId = 1,
            Title = "Approval rule",
            Content = "Code changes require approval."
        };

        var committed = new SemanticSearchCandidate
        {
            Document = doc,
            Artefact = new SemanticArtefact
            {
                Id = Guid.NewGuid(),
                ProjectId = 1,
                SourceEntityType = "ProjectDecision",
                SourceEntityId = "1",
                ArtefactType = "Decision",
                AuthorityLevel = "CommittedDecision",
                Title = "Committed approval decision",
                ContentHash = "a",
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            },
            Chunk = new SemanticChunk
            {
                Id = Guid.NewGuid(),
                ProjectId = 1,
                ChunkText = "Code changes require approval.",
                ContentHash = "a"
            },
            VectorSimilarity = 0.82
        };

        var lowAuthority = committed with
        {
            Artefact = committed.Artefact with
            {
                Id = Guid.NewGuid(),
                Title = "Low authority approval note",
                AuthorityLevel = "LowAuthorityNote"
            },
            Chunk = committed.Chunk with { Id = Guid.NewGuid() },
            VectorSimilarity = 0.86
        };

        // Act
        var results = ranker.Rank(new SemanticSearchQuery
        {
            ProjectId = 1,
            QueryText = "approval",
            Consumer = "BuildPlanner",
            Limit = 2
        }, [lowAuthority, committed]);

        // Assert
        Assert.AreEqual(committed.Artefact.Id, results[0].ArtefactId);
        Assert.IsTrue(results[0].FinalScore > results[1].FinalScore);
    }

    [TestMethod]
    public async Task SemanticRepositories_ShouldPersistArtefactsChunksJobsAndTraces()
    {
        // Arrange
        int projectId = await SeedProjectAsync(name: "SemanticRepositoryProject");
        var artefactRepository = new SemanticArtefactRepository(ServiceProvider.GetRequiredService<IronDev.Data.IDbConnectionFactory>());
        var chunkRepository = new SemanticChunkRepository(ServiceProvider.GetRequiredService<IronDev.Data.IDbConnectionFactory>());
        var jobRepository = new EmbeddingJobRepository(ServiceProvider.GetRequiredService<IronDev.Data.IDbConnectionFactory>());
        var traceRepository = new SemanticSearchTraceRepository(ServiceProvider.GetRequiredService<IronDev.Data.IDbConnectionFactory>());

        var artefact = new SemanticArtefactDraft
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            SourceEntityType = "ProjectContextDocument",
            SourceEntityId = "123",
            ArtefactType = "Decision",
            AuthorityLevel = "CommittedDecision",
            Title = "SQL owns memory",
            SearchableText = "SQL is canonical; Weaviate is rebuildable.",
            ContentHash = "hash"
        };
        var chunk = new SemanticChunkDraft
        {
            Id = Guid.NewGuid(),
            ArtefactId = artefact.Id,
            ProjectId = projectId,
            ChunkIndex = 0,
            ChunkText = "SQL is canonical; Weaviate is rebuildable.",
            ContentHash = "chunk"
        };

        // Act
        await artefactRepository.UpsertArtefactAsync(artefact);
        await chunkRepository.ReplaceChunksAsync(artefact.Id, [chunk]);
        await jobRepository.CreateAsync(new EmbeddingJob
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            SourceEntityType = artefact.SourceEntityType,
            SourceEntityId = artefact.SourceEntityId,
            JobType = "CreateOrUpdateArtefact",
            Status = "Pending",
            CreatedUtc = DateTime.UtcNow
        });
        var traceId = await traceRepository.CreateTraceAsync(new SemanticSearchQuery
        {
            ProjectId = projectId,
            QueryText = "canonical memory",
            Consumer = "Test"
        });

        // Assert
        var saved = await artefactRepository.GetArtefactAsync(artefact.Id);
        var chunks = await chunkRepository.GetChunksAsync(artefact.Id);
        var jobs = await jobRepository.GetPendingAsync();
        Assert.IsNotNull(saved);
        Assert.AreEqual(1, chunks.Count);
        Assert.IsTrue(jobs.Any(j => j.ProjectId == projectId));
        Assert.AreNotEqual(Guid.Empty, traceId);
    }

    [TestMethod]
    public void FakeEmbeddingProvider_ShouldGenerateDeterministicNormalizedVectors()
    {
        // Arrange
        var provider1 = new FakeEmbeddingProvider(new EmbeddingOptions { Dimensions = 1536 });
        var provider2 = new FakeEmbeddingProvider(new EmbeddingOptions { Dimensions = 1536 });
        string text = "IronDev vector database semantic search";

        // Act
        var res1 = provider1.EmbedAsync(text).GetAwaiter().GetResult();
        var res2 = provider2.EmbedAsync(text).GetAwaiter().GetResult();

        // Assert
        Assert.AreEqual(1536, res1.Dimensions);
        Assert.AreEqual(res1.Model, res2.Model);
        
        for (int i = 0; i < res1.Vector.Length; i++)
        {
            Assert.AreEqual(res1.Vector[i], res2.Vector[i], 1e-6f);
        }

        double lengthSq = res1.Vector.Select(x => (double)x * x).Sum();
        Assert.AreEqual(1.0, lengthSq, 1e-4);
    }
}
