using IronDev.Core.Interfaces;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ProjectDocumentProcessingServiceTests
{
    [TestMethod]
    public async Task ProcessAsync_DraftPublishesExactVersionAndBecomesReady()
    {
        var fixture = Fixture();
        string? statusAtEmbedding = null;
        fixture.Semantic
            .Setup(service => service.EmbedAndStoreAsync(It.IsAny<ProjectContextDocument>(), It.IsAny<CancellationToken>()))
            .Callback<ProjectContextDocument, CancellationToken>((context, _) => statusAtEmbedding = context.Status)
            .Returns(Task.CompletedTask);

        var result = await fixture.Service.ProcessAsync(Request());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Ready", result.Status);
        Assert.AreEqual("Ready", result.Document.ProcessingStatus);
        Assert.IsNotNull(result.Document.ProcessingStartedAtUtc);
        Assert.IsNotNull(result.Document.ProcessingCompletedAtUtc);
        Assert.AreEqual("Processing", statusAtEmbedding);
        Assert.AreEqual("Active", fixture.SavedContexts.Last().Status);
        Assert.AreEqual(ProjectDocumentContextSource.ForVersion(fixture.Version.Id), fixture.SavedContexts.Last().Source);
        fixture.Documents.Verify(service => service.LinkVersionAsync(
            It.Is<LinkProjectDocumentVersionRequest>(link =>
                link.DocumentVersionId == fixture.Version.Id
                && link.LinkedEntityType == "ProjectContextDocument"
                && link.LinkedEntityId == 50
                && link.LinkType == "IndexedAs"),
            It.IsAny<CancellationToken>()), Times.Once);
        CollectionAssert.AreEqual(new[] { "Processing", "Ready" }, fixture.StateUpdates.Select(update => update.Status).ToArray());
    }

    [TestMethod]
    public async Task ProcessAsync_EmbeddingFailurePersistsFailureAndSupportsRetry()
    {
        var fixture = Fixture();
        fixture.Semantic
            .SetupSequence(service => service.EmbedAndStoreAsync(It.IsAny<ProjectContextDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding unavailable"))
            .Returns(Task.CompletedTask);

        var failed = await fixture.Service.ProcessAsync(Request());

        Assert.IsFalse(failed.Succeeded);
        Assert.AreEqual("ProcessingFailed", failed.Document.ProcessingStatus);
        Assert.AreEqual("Document retrieval processing did not complete.", failed.FailureReason);

        var retried = await fixture.Service.ProcessAsync(Request());

        Assert.IsTrue(retried.Succeeded);
        Assert.AreEqual("Ready", retried.Document.ProcessingStatus);
        CollectionAssert.AreEqual(
            new[] { "Processing", "ProcessingFailed", "Processing", "Ready" },
            fixture.StateUpdates.Select(update => update.Status).ToArray());
    }

    [TestMethod]
    public async Task ProcessAsync_ReadyExactVersionIsIdempotent()
    {
        var fixture = Fixture(initialStatus: "Ready", linkedContext: new ProjectContextDocument
        {
            Id = 50,
            ProjectId = 7,
            Status = "Active",
            Source = ProjectDocumentContextSource.ForVersion(84)
        });

        var result = await fixture.Service.ProcessAsync(Request());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(50L, result.ContextDocumentId);
        fixture.ProcessingState.VerifyNoOtherCalls();
        fixture.Semantic.Verify(service => service.EmbedAndStoreAsync(It.IsAny<ProjectContextDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessAsync_NewVersionSupersedesPriorRetrievalContext()
    {
        var priorVersion = new ProjectDocumentVersion { Id = 83, DocumentId = 42, VersionMajor = 0, VersionMinor = 1 };
        var priorContext = new ProjectContextDocument
        {
            Id = 49,
            ProjectId = 7,
            Status = "Active",
            Title = "Prior",
            Content = "Prior content",
            Source = ProjectDocumentContextSource.ForVersion(83),
            CreatedDate = DateTime.UtcNow.AddMinutes(-5)
        };
        var fixture = Fixture(priorVersion: priorVersion, priorContext: priorContext);

        var result = await fixture.Service.ProcessAsync(Request());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("Superseded", priorContext.Status);
        Assert.AreEqual(49L, fixture.SavedContexts.Last().SupersedesDocumentId);
        fixture.Semantic.Verify(service => service.MarkStaleAsync(
            It.Is<SemanticStaleRequest>(request =>
                request.SourceEntityType == ProjectDocumentContextSource.EntityType
                && request.SourceEntityId == "83"
                && request.SourceVersionId == "83"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessAsync_CrossProjectDocumentIsNotDisclosed()
    {
        var fixture = Fixture(projectId: 8);

        var error = await Assert.ThrowsExactlyAsync<ProjectDocumentProcessingException>(() =>
            fixture.Service.ProcessAsync(Request()));

        Assert.AreEqual(ProjectDocumentProcessingFailureKind.ProjectNotFound, error.Kind);
        fixture.ProcessingState.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ProcessAsync_AtomicStartRefusesAConcurrentProcessor()
    {
        var fixture = Fixture(canBegin: false);

        var error = await Assert.ThrowsExactlyAsync<ProjectDocumentProcessingException>(() =>
            fixture.Service.ProcessAsync(Request()));

        Assert.AreEqual(ProjectDocumentProcessingFailureKind.AlreadyProcessing, error.Kind);
        fixture.Semantic.Verify(service => service.EmbedAndStoreAsync(
            It.IsAny<ProjectContextDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessAsync_FreshProcessingLeaseIsRefusedButInterruptedLeaseCanRecover()
    {
        var fresh = Fixture(initialStatus: "Processing", processingStartedAtUtc: DateTime.UtcNow);
        var freshError = await Assert.ThrowsExactlyAsync<ProjectDocumentProcessingException>(() =>
            fresh.Service.ProcessAsync(Request()));
        Assert.AreEqual(ProjectDocumentProcessingFailureKind.AlreadyProcessing, freshError.Kind);
        fresh.ProcessingState.VerifyNoOtherCalls();

        var interrupted = Fixture(initialStatus: "Processing", processingStartedAtUtc: DateTime.UtcNow.AddMinutes(-11));
        var recovered = await interrupted.Service.ProcessAsync(Request());
        Assert.IsTrue(recovered.Succeeded);
        Assert.AreEqual("Ready", recovered.Status);
    }

    private static ProcessingFixture Fixture(
        string initialStatus = "Draft",
        int projectId = 7,
        ProjectContextDocument? linkedContext = null,
        ProjectDocumentVersion? priorVersion = null,
        ProjectContextDocument? priorContext = null,
        bool canBegin = true,
        DateTime? processingStartedAtUtc = null)
    {
        var document = new ProjectDocument
        {
            Id = 42,
            ProjectId = projectId,
            Title = "Architecture Notes",
            DocumentType = "Architecture",
            Status = "Active",
            ProcessingStatus = initialStatus,
            Description = "Backend boundaries.",
            CurrentVersionId = 84,
            ProcessingStartedAtUtc = processingStartedAtUtc
        };
        var version = new ProjectDocumentVersion
        {
            Id = 84,
            DocumentId = 42,
            VersionMajor = priorVersion is null ? 0 : 0,
            VersionMinor = priorVersion is null ? 1 : 2,
            ContentMarkdown = "# Architecture\n\nKeep the client thin.",
            Status = "Draft"
        };
        var documents = new Mock<IProjectDocumentService>();
        var processingState = new Mock<IProjectDocumentProcessingStateStore>();
        var memory = new Mock<IProjectMemoryService>();
        var semantic = new Mock<ISemanticMemoryService>();
        var stateUpdates = new List<ProjectDocumentProcessingStateUpdate>();
        var savedContexts = new List<ProjectContextDocument>();
        var linksByVersion = new Dictionary<long, List<ProjectDocumentLink>>();
        var contextsById = new Dictionary<long, ProjectContextDocument>();
        long nextContextId = 50;

        documents.Setup(service => service.GetDocumentAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        documents.Setup(service => service.GetVersionAsync(84, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        documents.Setup(service => service.GetVersionHistoryAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorVersion is null ? [version] : [version, priorVersion]);
        linksByVersion[84] = linkedContext is null ? [] : [Link(84, linkedContext.Id)];
        if (priorVersion is not null && priorContext is not null)
        {
            linksByVersion[priorVersion.Id] = [Link(priorVersion.Id, priorContext.Id)];
            contextsById[priorContext.Id] = priorContext;
        }
        if (linkedContext is not null)
        {
            contextsById[linkedContext.Id] = linkedContext;
        }
        documents
            .Setup(service => service.GetLinksForVersionAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long versionId, CancellationToken _) =>
                linksByVersion.TryGetValue(versionId, out var links) ? links : []);
        documents
            .Setup(service => service.LinkVersionAsync(It.IsAny<LinkProjectDocumentVersionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LinkProjectDocumentVersionRequest, CancellationToken>((request, _) =>
            {
                if (!linksByVersion.TryGetValue(request.DocumentVersionId, out var links))
                {
                    links = [];
                    linksByVersion[request.DocumentVersionId] = links;
                }
                links.Add(Link(request.DocumentVersionId, request.LinkedEntityId));
            })
            .Returns(Task.CompletedTask);
        memory
            .Setup(service => service.GetContextDocumentByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long contextId, CancellationToken _) =>
                contextsById.TryGetValue(contextId, out var context) ? context : null);

        processingState
            .Setup(service => service.TryBeginProcessingAsync(42, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long _, string? _, CancellationToken _) =>
            {
                if (!canBegin) return null;
                stateUpdates.Add(new ProjectDocumentProcessingStateUpdate { DocumentId = 42, Status = "Processing" });
                document.ProcessingStatus = "Processing";
                document.ProcessingStartedAtUtc = DateTime.UtcNow;
                document.ProcessingCompletedAtUtc = null;
                return document;
            });
        processingState
            .Setup(service => service.UpdateProcessingStateAsync(It.IsAny<ProjectDocumentProcessingStateUpdate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectDocumentProcessingStateUpdate update, CancellationToken _) =>
            {
                stateUpdates.Add(update);
                document.ProcessingStatus = update.Status;
                document.ProcessingFailureReason = update.Status == "ProcessingFailed" ? update.FailureReason : null;
                if (update.Status == "Processing")
                {
                    document.ProcessingStartedAtUtc = DateTime.UtcNow;
                    document.ProcessingCompletedAtUtc = null;
                }
                else if (update.Status is "Ready" or "ProcessingFailed")
                {
                    document.ProcessingCompletedAtUtc = DateTime.UtcNow;
                }
                return document;
            });
        memory
            .Setup(service => service.SaveContextDocumentAsync(It.IsAny<ProjectContextDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectContextDocument context, CancellationToken _) =>
            {
                if (context.Id == 0) context.Id = nextContextId++;
                contextsById[context.Id] = context;
                savedContexts.Add(context);
                return context.Id;
            });
        semantic.Setup(service => service.EmbedAndStoreAsync(It.IsAny<ProjectContextDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        semantic.Setup(service => service.MarkStaleAsync(It.IsAny<SemanticStaleRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new ProcessingFixture(
            new ProjectDocumentProcessingService(
                documents.Object,
                processingState.Object,
                memory.Object,
                semantic.Object,
                NullLogger<ProjectDocumentProcessingService>.Instance),
            documents,
            processingState,
            semantic,
            version,
            stateUpdates,
            savedContexts);
    }

    private static ProjectDocumentLink Link(long versionId, long contextId) => new()
    {
        DocumentVersionId = versionId,
        LinkedEntityType = "ProjectContextDocument",
        LinkedEntityId = contextId,
        LinkType = "IndexedAs"
    };

    private static ProjectDocumentProcessingRequest Request() => new()
    {
        ProjectId = 7,
        DocumentId = 42,
        ProcessedBy = "bob@irondev.local"
    };

    private sealed record ProcessingFixture(
        ProjectDocumentProcessingService Service,
        Mock<IProjectDocumentService> Documents,
        Mock<IProjectDocumentProcessingStateStore> ProcessingState,
        Mock<ISemanticMemoryService> Semantic,
        ProjectDocumentVersion Version,
        List<ProjectDocumentProcessingStateUpdate> StateUpdates,
        List<ProjectContextDocument> SavedContexts);
}
