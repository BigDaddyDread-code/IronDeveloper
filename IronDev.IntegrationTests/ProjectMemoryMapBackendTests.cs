using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ProjectMemoryMapBackendTests : IntegrationTestBase
{
    [TestMethod]
    public async Task ProjectMemoryMap_ReturnsDocumentsDecisionsTicketsAndRules()
    {
        var projectId = await SeedProjectAsync(name: "Memory Map Authority Project");
        var memory = ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var tickets = ServiceProvider.GetRequiredService<ITicketService>();
        var artefacts = ServiceProvider.GetRequiredService<ISemanticArtefactRepository>();
        var chunks = ServiceProvider.GetRequiredService<ISemanticChunkRepository>();
        var mapService = ServiceProvider.GetRequiredService<IProjectMemoryMapService>();
        var artefactId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        await memory.SaveDecisionAsync(new ProjectDecision
        {
            ProjectId = projectId,
            Title = "Accepted memory decision",
            Detail = "Accepted project memory should be accepted and current.",
            Status = "Accepted"
        });
        await tickets.SaveTicketAsync(new ProjectTicket
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            Title = "Generated draft memory ticket",
            Content = "Generated tickets are draft memory.",
            Status = "Draft",
            IsGenerated = true
        });
        await memory.SaveContextDocumentAsync(new ProjectContextDocument
        {
            ProjectId = projectId,
            Title = "Archived memory document",
            Content = "Archived documents are stale and deprecated.",
            AuthorityLevel = "Accepted",
            Status = "Archived"
        });
        await memory.SaveProjectRuleAsync(new ProjectRule
        {
            ProjectId = projectId,
            Name = "Required memory rule",
            Description = "Required rules are accepted authority.",
            EnforcementLevel = "Required"
        });
        await artefacts.UpsertArtefactAsync(new SemanticArtefactDraft
        {
            Id = artefactId,
            TenantId = TenantContext.TenantId,
            ProjectId = projectId,
            SourceEntityType = "Document",
            SourceEntityId = "semantic-doc",
            ArtefactType = "Document",
            AuthorityLevel = "ObservedFact",
            Title = "Semantic memory artefact",
            Summary = "Semantic artefact appears in the backend map.",
            ContentHash = "semantic-hash",
            SearchableText = "Semantic artefact appears in the backend map."
        });
        await chunks.ReplaceChunksAsync(artefactId,
        [
            new SemanticChunkDraft
            {
                Id = chunkId,
                ArtefactId = artefactId,
                ProjectId = projectId,
                ChunkIndex = 0,
                ChunkText = "Semantic chunk appears in the backend map.",
                ContentHash = "chunk-hash"
            }
        ]);

        var map = await mapService.GetMapAsync(projectId);

        Assert.IsNotNull(map);
        Assert.AreEqual(projectId, map.ProjectId);
        Assert.IsTrue(map.Counts.Total >= 4);

        var decision = map.Items.Single(entry => entry.Title == "Accepted memory decision");
        Assert.AreEqual("Decision", decision.SourceType);
        Assert.AreEqual("Accepted", decision.AuthorityLevel);
        Assert.IsTrue(decision.IsCurrent);

        var ticket = map.Items.Single(entry => entry.Title == "Generated draft memory ticket");
        Assert.AreEqual("Ticket", ticket.SourceType);
        Assert.AreEqual("Draft", ticket.AuthorityLevel);
        Assert.IsTrue(ticket.IsCurrent);

        var document = map.Items.Single(entry => entry.Title == "Archived memory document");
        Assert.AreEqual("Document", document.SourceType);
        Assert.AreEqual("Deprecated", document.AuthorityLevel);
        Assert.IsFalse(document.IsCurrent);
        Assert.IsFalse(string.IsNullOrWhiteSpace(document.StalenessReason));

        var rule = map.Items.Single(entry => entry.Title == "Required memory rule");
        Assert.AreEqual("Rule", rule.SourceType);
        Assert.AreEqual("Accepted", rule.AuthorityLevel);
        Assert.IsTrue(rule.IsCurrent);

        var semanticArtefact = map.Items.Single(entry => entry.SourceType == "SemanticArtefact");
        Assert.AreEqual("ObservedFact", semanticArtefact.AuthorityLevel);
        Assert.IsTrue(semanticArtefact.IsCurrent);
        Assert.IsTrue(semanticArtefact.Links!.Any(link => link.LinkType == ProjectMemoryLinkTypes.SourceEntity));

        var semanticChunk = map.Items.Single(entry => entry.SourceType == "SemanticChunk");
        Assert.AreEqual("ObservedFact", semanticChunk.AuthorityLevel);
        Assert.IsTrue(semanticChunk.IsCurrent);
        Assert.IsTrue(semanticChunk.Links!.Any(link => link.LinkType == ProjectMemoryLinkTypes.ParentArtefact));
        Assert.IsTrue(map.Graph.Nodes.Any(node => node.SourceId == semanticArtefact.SourceId && node.SourceType == "SemanticArtefact"));
        Assert.IsTrue(map.Graph.Nodes.Any(node => node.SourceId == semanticChunk.SourceId && node.SourceType == "SemanticChunk"));
        Assert.IsTrue(map.Graph.Edges.Any(edge =>
            edge.FromSourceId == semanticArtefact.SourceId &&
            edge.ToSourceId == "Document-semantic-doc" &&
            edge.LinkType == ProjectMemoryLinkTypes.SourceEntity));
        Assert.IsTrue(map.Graph.Edges.Any(edge =>
            edge.FromSourceId == semanticChunk.SourceId &&
            edge.ToSourceId == semanticArtefact.SourceId &&
            edge.LinkType == ProjectMemoryLinkTypes.ParentArtefact));
    }

    [TestMethod]
    public async Task ProjectMemoryMap_SourceGraphCapturesDocumentSupersessionAndSourceChatMessage()
    {
        var projectId = await SeedProjectAsync(name: "Source Graph Document Project");
        var memory = ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var mapService = ServiceProvider.GetRequiredService<IProjectMemoryMapService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Source graph session"
        });
        var sourceMessageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Source graph source message."
        });
        var olderId = await memory.SaveContextDocumentAsync(new ProjectContextDocument
        {
            ProjectId = projectId,
            Title = "Old architecture note",
            Content = "Old memory statement.",
            AuthorityLevel = "Accepted",
            Status = "Superseded"
        });
        var newerId = await memory.SaveContextDocumentAsync(new ProjectContextDocument
        {
            ProjectId = projectId,
            Title = "Current architecture note",
            Content = "Current memory statement.",
            AuthorityLevel = "Accepted",
            Status = "Active",
            SupersedesDocumentId = olderId,
            SourceChatMessageId = sourceMessageId
        });

        var map = await mapService.GetMapAsync(projectId);

        Assert.IsNotNull(map);
        var current = map.Items.Single(entry => entry.SourceId == $"document-{newerId}");
        Assert.IsTrue(current.Links!.Any(link =>
            link.LinkType == ProjectMemoryLinkTypes.SourceChatMessage &&
            link.TargetSourceId == $"chat-message-{sourceMessageId}"));
        Assert.IsTrue(current.Links!.Any(link =>
            link.LinkType == ProjectMemoryLinkTypes.DerivedFrom &&
            link.TargetSourceId == $"chat-message-{sourceMessageId}"));
        Assert.IsTrue(current.Links!.Any(link =>
            link.LinkType == ProjectMemoryLinkTypes.Supersedes &&
            link.TargetSourceId == $"document-{olderId}"));
        Assert.IsTrue(map.Graph.Edges.Any(edge =>
            edge.FromSourceId == $"document-{newerId}" &&
            edge.ToSourceId == $"document-{olderId}" &&
            edge.LinkType == ProjectMemoryLinkTypes.Supersedes));
        Assert.IsTrue(map.Graph.Edges.Any(edge =>
            edge.FromSourceId == $"document-{olderId}" &&
            edge.ToSourceId == $"document-{newerId}" &&
            edge.LinkType == ProjectMemoryLinkTypes.SupersededBy));
    }

    [TestMethod]
    public async Task ProjectMemoryMap_SourceGraphCapturesGeneratedTicketSources()
    {
        var projectId = await SeedProjectAsync(name: "Generated Ticket Source Graph Project");
        var tickets = ServiceProvider.GetRequiredService<ITicketService>();
        var mapService = ServiceProvider.GetRequiredService<IProjectMemoryMapService>();

        await tickets.SaveTicketAsync(new ProjectTicket
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            Title = "Generated source-linked ticket",
            Content = "Generated from source evidence.",
            Status = "Draft",
            IsGenerated = true,
            SourceChatMessageId = 777,
            SourceDocumentVersionId = 888
        });

        var map = await mapService.GetMapAsync(projectId);

        Assert.IsNotNull(map);
        var ticket = map.Items.Single(entry => entry.Title == "Generated source-linked ticket");
        Assert.IsTrue(ticket.Links!.Any(link =>
            link.LinkType == ProjectMemoryLinkTypes.SourceChatMessage &&
            link.TargetSourceId == "chat-message-777"));
        Assert.IsTrue(ticket.Links!.Any(link =>
            link.LinkType == ProjectMemoryLinkTypes.SourceDocumentVersion &&
            link.TargetSourceId == "document-version-888"));
        Assert.IsTrue(ticket.Links!.Any(link =>
            link.LinkType == ProjectMemoryLinkTypes.GeneratedFrom &&
            link.TargetSourceId == "chat-message-777"));
        Assert.IsTrue(ticket.Links!.Any(link =>
            link.LinkType == ProjectMemoryLinkTypes.GeneratedFrom &&
            link.TargetSourceId == "document-version-888"));
        Assert.IsTrue(map.Graph.Nodes.Any(node =>
            node.SourceId == "document-version-888" &&
            node.SourceType == "DocumentVersion" &&
            node.AuthorityLevel == MemoryAuthorityLevels.Unknown &&
            !node.IsCurrent));
    }

    [TestMethod]
    public async Task ProjectMemoryMap_SourceGraphRepresentsUnknownLinkedTargetsSafely()
    {
        var projectId = await SeedProjectAsync(name: "Unknown Source Graph Project");
        var artefacts = ServiceProvider.GetRequiredService<ISemanticArtefactRepository>();
        var mapService = ServiceProvider.GetRequiredService<IProjectMemoryMapService>();

        await artefacts.UpsertArtefactAsync(new SemanticArtefactDraft
        {
            Id = Guid.NewGuid(),
            TenantId = TenantContext.TenantId,
            ProjectId = projectId,
            SourceEntityType = string.Empty,
            SourceEntityId = "orphan-source",
            ArtefactType = "Document",
            AuthorityLevel = "ObservedFact",
            Title = "Orphan semantic artefact",
            Summary = "Linked source type is unknown.",
            ContentHash = "orphan-hash",
            SearchableText = "Linked source type is unknown."
        });

        var map = await mapService.GetMapAsync(projectId);

        Assert.IsNotNull(map);
        Assert.IsTrue(map.Graph.Nodes.Any(node =>
            node.SourceId == "-orphan-source" &&
            node.SourceType == "Unknown" &&
            node.AuthorityLevel == MemoryAuthorityLevels.Unknown &&
            !node.IsCurrent));
    }

    [TestMethod]
    public async Task ProjectMemoryMap_UnknownStatesFailSafeToUnknown()
    {
        var projectId = await SeedProjectAsync(name: "Unknown Memory Map Project");
        var memory = ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var mapService = ServiceProvider.GetRequiredService<IProjectMemoryMapService>();

        await memory.SaveDecisionAsync(new ProjectDecision
        {
            ProjectId = projectId,
            Title = "Mystery decision",
            Detail = "Unknown states fail safe.",
            Status = "MysteryState"
        });

        var map = await mapService.GetMapAsync(projectId);

        Assert.IsNotNull(map);
        var decision = map.Items.Single(entry => entry.Title == "Mystery decision");
        Assert.AreEqual("Unknown", decision.AuthorityLevel);
        Assert.IsFalse(decision.IsCurrent);
        Assert.IsFalse(string.IsNullOrWhiteSpace(decision.StalenessReason));
    }

    [TestMethod]
    public async Task ProjectMemoryMap_DoesNotRequireSemanticSearch()
    {
        var projectId = await SeedProjectAsync(name: "Map Without Semantic Search");
        var mapService = ServiceProvider.GetRequiredService<IProjectMemoryMapService>();

        var map = await mapService.GetMapAsync(projectId);

        Assert.IsNotNull(map);
        Assert.AreEqual(projectId, map.ProjectId);
    }

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
    public async Task MemoryBleed_TenantIsolationBlocksAllMemoryTypes()
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

}
