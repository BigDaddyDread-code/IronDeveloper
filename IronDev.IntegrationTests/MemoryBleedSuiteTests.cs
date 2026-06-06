using Dapper;
using IronDev.Core.Interfaces;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services.SemanticMemory;
using IronDev.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class MemoryBleedSuiteTests : IntegrationTestBase
{
    [TestMethod]
    public async Task MemoryBleed_ProjectMapDoesNotIncludeOtherProjectItems()
    {
        var projectA = await SeedProjectAsync(name: "Project A");
        var projectB = await SeedProjectAsync(name: "Project B");
        var memory = ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var mapService = ServiceProvider.GetRequiredService<IProjectMemoryMapService>();

        await memory.SaveDecisionAsync(new ProjectDecision
        {
            ProjectId = projectB,
            Title = "Project B secret memory",
            Detail = "This must not appear in Project A memory map.",
            Status = "Accepted"
        });

        var mapA = await mapService.GetMapAsync(projectA);
        var mapB = await mapService.GetMapAsync(projectB);

        Assert.IsNotNull(mapA);
        Assert.IsNotNull(mapB);
        Assert.IsFalse(mapA.Items.Any(entry => entry.Title == "Project B secret memory"));
        Assert.IsTrue(mapB.Items.Any(entry => entry.Title == "Project B secret memory"));
    }

    [TestMethod]
    public async Task MemoryBleed_ProjectMapDoesNotIncludeOtherTenantItems()
    {
        var tenantBProject = await SeedProjectAsync(tenantId: 2, name: "Tenant B Memory Map Project");
        TenantContext.TenantId = 2;
        var memory = ServiceProvider.GetRequiredService<IProjectMemoryService>();
        await memory.SaveDecisionAsync(new ProjectDecision
        {
            ProjectId = tenantBProject,
            Title = "Tenant B secret memory",
            Detail = "Tenant A must not see this memory.",
            Status = "Accepted"
        });

        TenantContext.TenantId = 1;
        var mapService = ServiceProvider.GetRequiredService<IProjectMemoryMapService>();

        var map = await mapService.GetMapAsync(tenantBProject);

        Assert.IsNull(map);
    }

    [TestMethod]
    public async Task MemoryBleed_DocumentsDecisionsTicketsAndRulesAreProjectScoped()
    {
        var projectA = await SeedProjectAsync(name: "IronDev");
        var projectB = await SeedProjectAsync(name: "BookSeller");
        var memory = ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var tickets = ServiceProvider.GetRequiredService<ITicketService>();

        await memory.SaveContextDocumentAsync(new ProjectContextDocument
        {
            ProjectId = projectA,
            Title = "Shared Memory Boundary",
            Content = "IronDev scoped memory.",
            Status = "Active",
            AuthorityLevel = "ObservedFact"
        });
        await memory.SaveContextDocumentAsync(new ProjectContextDocument
        {
            ProjectId = projectB,
            Title = "Shared Memory Boundary",
            Content = "BookSeller scoped memory.",
            Status = "Active",
            AuthorityLevel = "ObservedFact"
        });
        await memory.SaveDecisionAsync(new ProjectDecision { ProjectId = projectB, Title = "Shared Decision", Detail = "BookSeller decision." });
        await tickets.SaveTicketAsync(new ProjectTicket { ProjectId = projectB, SessionId = Guid.NewGuid(), Title = "Shared Ticket", Content = "BookSeller ticket." });
        await memory.SaveProjectRuleAsync(new ProjectRule { ProjectId = projectB, Name = "Shared Rule", Description = "BookSeller rule.", EnforcementLevel = "Required" });

        var docsA = await memory.GetContextDocumentsAsync(projectA, status: null);
        var docsB = await memory.GetContextDocumentsAsync(projectB, status: null);
        var decisionsA = await memory.GetRecentDecisionsAsync(projectA);
        var ticketsA = await tickets.GetRecentTicketsAsync(projectA);
        var rulesA = await memory.GetProjectRulesAsync(projectA);

        Assert.IsTrue(docsA.Any(doc => doc.Content.Contains("IronDev scoped memory", StringComparison.Ordinal)));
        Assert.IsTrue(docsB.Any(doc => doc.Content.Contains("BookSeller scoped memory", StringComparison.Ordinal)));
        Assert.IsFalse(docsA.Any(doc => doc.Content.Contains("BookSeller scoped memory", StringComparison.Ordinal)));
        Assert.IsFalse(decisionsA.Any(decision => decision.Title == "Shared Decision"));
        Assert.IsFalse(ticketsA.Any(ticket => ticket.Title == "Shared Ticket"));
        Assert.IsFalse(rulesA.Any(rule => rule.Name == "Shared Rule"));
    }

    [TestMethod]
    public async Task MemoryBleed_SemanticEvidenceFiltersByProject()
    {
        var projectA = await SeedProjectAsync(name: "IronDev");
        var projectB = await SeedProjectAsync(name: "BookSeller");
        var provider = new SemanticMemoryEvidenceProvider(new ProjectScopedSemanticMemoryService(
        [
            new SemanticSearchResult
            {
                Document = new ProjectContextDocument
                {
                    ProjectId = projectA,
                    Title = "Shared semantic phrase",
                    Content = "IronDev semantic evidence.",
                    Status = "Active",
                    AuthorityLevel = "ObservedFact"
                },
                SourceEntityType = "Document",
                SourceEntityId = "project-a",
                Title = "Shared semantic phrase",
                Snippet = "IronDev semantic evidence.",
                AuthorityLevel = "ObservedFact",
                FinalScore = 0.9
            },
            new SemanticSearchResult
            {
                Document = new ProjectContextDocument
                {
                    ProjectId = projectB,
                    Title = "Shared semantic phrase",
                    Content = "BookSeller semantic evidence.",
                    Status = "Active",
                    AuthorityLevel = "ObservedFact"
                },
                SourceEntityType = "Document",
                SourceEntityId = "project-b",
                Title = "Shared semantic phrase",
                Snippet = "BookSeller semantic evidence.",
                AuthorityLevel = "ObservedFact",
                FinalScore = 0.9
            }
        ]));

        var evidence = await provider.GetEvidenceAsync(projectA, "shared semantic phrase", string.Empty);

        Assert.IsTrue(evidence.Any(item => item.Excerpt.Contains("IronDev semantic evidence", StringComparison.Ordinal)));
        Assert.IsFalse(evidence.Any(item => item.Excerpt.Contains("BookSeller semantic evidence", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task MemoryBleed_AuditLookupRequiresProjectAndSessionScope()
    {
        var projectId = await SeedProjectAsync();
        var otherProjectId = await SeedProjectAsync(name: "Other audit project");
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Scoped audit lookup"
        });
        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Scoped audit evidence.",
            Tags = BuildEnvelopeJson()
        });

        Assert.IsNotNull(await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId));
        Assert.IsNull(await turnPersistence.GetByMessageAsync(otherProjectId, sessionId, messageId));
        Assert.IsNull(await turnPersistence.GetByMessageAsync(projectId, sessionId + 1, messageId));
    }

    [TestMethod]
    public async Task MemoryBleed_TagFallbackCannotBypassScope()
    {
        var projectId = await SeedProjectAsync();
        var otherProjectId = await SeedProjectAsync(name: "Other fallback project");
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Scoped fallback lookup"
        });
        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Fallback scoped evidence.",
            Tags = BuildEnvelopeJson()
        });
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            DELETE FROM dbo.ChatTurnTraces WHERE ChatMessageId = @MessageId;
            DELETE FROM dbo.ChatTurnClarifications WHERE ChatMessageId = @MessageId;
            DELETE FROM dbo.ChatTurnGovernance WHERE ChatMessageId = @MessageId;
            """,
            new { MessageId = messageId });

        var scoped = await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId);

        Assert.IsNotNull(scoped);
        Assert.IsTrue(scoped.IsFallbackEvidence);
        Assert.IsNull(await turnPersistence.GetByMessageAsync(otherProjectId, sessionId, messageId));
        Assert.IsNull(await turnPersistence.GetByMessageAsync(projectId, sessionId + 1, messageId));
    }

    private sealed class ProjectScopedSemanticMemoryService : ISemanticMemoryService
    {
        private readonly IReadOnlyList<SemanticSearchResult> _results;

        public ProjectScopedSemanticMemoryService(IReadOnlyList<SemanticSearchResult> results)
        {
            _results = results;
        }

        public Task QueueIndexAsync(SemanticIndexRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task EmbedAndStoreAsync(ProjectContextDocument document, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
            SemanticSearchQuery query,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SemanticSearchResult>>(
                _results.Where(result => result.Document.ProjectId == query.ProjectId).ToArray());

        public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
            int projectId,
            string query,
            int limit = 8,
            double minSimilarity = 0.75,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SemanticSearchResult>>(
                _results.Where(result => result.Document.ProjectId == projectId).ToArray());

        public Task<SemanticContextBundle> BuildContextBundleAsync(
            int projectId,
            string query,
            string callerContext,
            int limit = 8,
            CancellationToken ct = default) =>
            Task.FromResult(new SemanticContextBundle
            {
                ProjectId = projectId,
                Query = query,
                CallerContext = callerContext,
                Results = _results.Where(result => result.Document.ProjectId == projectId).ToArray()
            });

        public Task RebuildIndexAsync(
            int projectId,
            IProgress<SemanticIndexRebuildProgress>? progress = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RebuildProjectAsync(int projectId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task MarkStaleAsync(SemanticStaleRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteEmbeddingAsync(Guid artefactId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SemanticMemoryHealth> GetHealthAsync(int projectId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static string BuildEnvelopeJson() =>
        """
        {
          "v": 1,
          "mode": "Formalization",
          "modeConfidence": 0.97,
          "modeReason": "The user explicitly asked to save project work.",
          "clarification": {
            "required": true,
            "kind": "GovernanceIntent",
            "questions": [ "Do you want to turn this into a ticket?" ],
            "reason": "The requested artifact is not fully specified."
          },
          "gate": {
            "mode": "Formalization",
            "canSaveDiscussion": true,
            "canCreateTicket": true,
            "canViewSources": true,
            "canCopyMarkdown": true,
            "reason": "The user explicitly asked to save project work.",
            "confidence": 0.97,
            "governanceActions": [ "Save this response as a Discussion.", "Create a Ticket from the saved Discussion." ]
          },
          "routeTraceId": "route-memory-bleed-test",
          "dogfoodTraceId": "dogfood-memory-bleed-test"
        }
        """;
}
