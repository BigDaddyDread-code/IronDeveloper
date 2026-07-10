using IronDev.Core.Interfaces;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using Microsoft.Extensions.Logging;

namespace IronDev.Services;

public sealed class ProjectDocumentProcessingService : IProjectDocumentProcessingService
{
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(10);
    private const string FailureReason = "Document retrieval processing did not complete.";
    private const string RetryAction = "Retry processing. The immutable document version is still stored.";
    private const string LinkEntityType = "ProjectContextDocument";
    private const string LinkType = "IndexedAs";

    private readonly IProjectDocumentService _documents;
    private readonly IProjectDocumentProcessingStateStore _processingState;
    private readonly IProjectMemoryService _memory;
    private readonly ISemanticMemoryService _semanticMemory;
    private readonly ILogger<ProjectDocumentProcessingService> _logger;

    public ProjectDocumentProcessingService(
        IProjectDocumentService documents,
        IProjectDocumentProcessingStateStore processingState,
        IProjectMemoryService memory,
        ISemanticMemoryService semanticMemory,
        ILogger<ProjectDocumentProcessingService> logger)
    {
        _documents = documents;
        _processingState = processingState;
        _memory = memory;
        _semanticMemory = semanticMemory;
        _logger = logger;
    }

    public async Task<ProjectDocumentProcessingResult> ProcessAsync(
        ProjectDocumentProcessingRequest request,
        CancellationToken ct = default)
    {
        var document = await _documents.GetDocumentAsync(request.DocumentId, ct);
        if (document is null || document.ProjectId != request.ProjectId)
            throw Failure(ProjectDocumentProcessingFailureKind.ProjectNotFound, "The project document is not available for processing.");

        if (!string.Equals(document.Status, "Active", StringComparison.OrdinalIgnoreCase))
            throw Failure(ProjectDocumentProcessingFailureKind.InvalidState, "Only active project documents can be processed.");

        if (string.Equals(document.ProcessingStatus, "Processing", StringComparison.OrdinalIgnoreCase)
            && document.ProcessingStartedAtUtc is { } startedAtUtc
            && startedAtUtc > DateTime.UtcNow.Subtract(ProcessingLease))
            throw Failure(ProjectDocumentProcessingFailureKind.AlreadyProcessing, "Document processing is already in progress.");

        var version = document.CurrentVersionId.HasValue
            ? await _documents.GetVersionAsync(document.CurrentVersionId.Value, ct)
            : null;
        if (version is null || version.DocumentId != document.Id)
            throw Failure(ProjectDocumentProcessingFailureKind.InvalidState, "The document has no current immutable version to process.");

        var currentContext = await FindLinkedContextAsync(version.Id, request.ProjectId, ct);
        if (string.Equals(document.ProcessingStatus, "Ready", StringComparison.OrdinalIgnoreCase)
            && currentContext is not null
            && string.Equals(currentContext.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return Ready(document, version, currentContext.Id);
        }

        document = await _processingState.TryBeginProcessingAsync(document.Id, request.ProcessedBy, ct)
            ?? throw Failure(ProjectDocumentProcessingFailureKind.AlreadyProcessing, "Document processing is already in progress.");

        ProjectContextDocument? contextDocument = null;
        try
        {
            var priorContexts = await GetPriorContextsAsync(document, version.Id, ct);
            contextDocument = currentContext ?? new ProjectContextDocument
            {
                ProjectId = request.ProjectId
            };
            contextDocument.DocumentType = document.DocumentType;
            contextDocument.AuthorityLevel = "ContextOnly";
            contextDocument.Status = "Processing";
            contextDocument.Title = document.Title;
            contextDocument.Content = version.ContentMarkdown;
            contextDocument.Summary = document.Description;
            contextDocument.Tags = "project-document";
            contextDocument.AppliesToArea = document.DocumentType;
            contextDocument.Source = ProjectDocumentContextSource.ForVersion(version.Id);
            contextDocument.SupersedesDocumentId = priorContexts.FirstOrDefault()?.Id;

            contextDocument.Id = await _memory.SaveContextDocumentAsync(contextDocument, ct);
            await _documents.LinkVersionAsync(new LinkProjectDocumentVersionRequest
            {
                DocumentVersionId = version.Id,
                LinkedEntityType = LinkEntityType,
                LinkedEntityId = contextDocument.Id,
                LinkType = LinkType,
                CreatedBy = request.ProcessedBy
            }, ct);

            await _semanticMemory.EmbedAndStoreAsync(contextDocument, ct);

            foreach (var prior in priorContexts)
            {
                prior.Status = "Superseded";
                await _memory.SaveContextDocumentAsync(prior, ct);
                var hasVersionSource = ProjectDocumentContextSource.TryGetVersionId(prior.Source, out var priorVersionId);
                await _semanticMemory.MarkStaleAsync(new SemanticStaleRequest
                {
                    ProjectId = request.ProjectId,
                    SourceEntityType = hasVersionSource ? ProjectDocumentContextSource.EntityType : "ProjectContextDocument",
                    SourceEntityId = hasVersionSource ? priorVersionId.ToString() : prior.Id.ToString(),
                    SourceVersionId = hasVersionSource ? priorVersionId.ToString() : null
                }, ct);
            }

            contextDocument.Status = "Active";
            await _memory.SaveContextDocumentAsync(contextDocument, ct);
            document = await _processingState.UpdateProcessingStateAsync(new ProjectDocumentProcessingStateUpdate
            {
                DocumentId = document.Id,
                Status = "Ready",
                UpdatedBy = request.ProcessedBy
            }, ct);

            return Ready(document, version, contextDocument.Id);
        }
        catch (OperationCanceledException)
        {
            await PersistFailureAsync(document.Id, contextDocument, request.ProcessedBy);
            throw;
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "Document processing failed for project document {DocumentId}.", document.Id);
            document = await PersistFailureAsync(document.Id, contextDocument, request.ProcessedBy);
            return new ProjectDocumentProcessingResult
            {
                Document = document,
                Version = version,
                ContextDocumentId = contextDocument?.Id,
                Succeeded = false,
                Status = "ProcessingFailed",
                FailureReason = FailureReason,
                NextSafeAction = RetryAction
            };
        }
    }

    private async Task<ProjectContextDocument?> FindLinkedContextAsync(long versionId, int projectId, CancellationToken ct)
    {
        var links = await _documents.GetLinksForVersionAsync(versionId, ct);
        foreach (var link in links.Where(link =>
                     string.Equals(link.LinkedEntityType, LinkEntityType, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(link.LinkType, LinkType, StringComparison.OrdinalIgnoreCase)))
        {
            var context = await _memory.GetContextDocumentByIdAsync(link.LinkedEntityId, ct);
            if (context?.ProjectId == projectId)
                return context;
        }

        return null;
    }

    private async Task<IReadOnlyList<ProjectContextDocument>> GetPriorContextsAsync(
        ProjectDocument document,
        long currentVersionId,
        CancellationToken ct)
    {
        var contexts = new Dictionary<long, ProjectContextDocument>();
        foreach (var version in await _documents.GetVersionHistoryAsync(document.Id, ct))
        {
            if (version.Id == currentVersionId)
                continue;

            var context = await FindLinkedContextAsync(version.Id, document.ProjectId, ct);
            if (context is not null && string.Equals(context.Status, "Active", StringComparison.OrdinalIgnoreCase))
                contexts[context.Id] = context;
        }

        return contexts.Values.OrderByDescending(context => context.UpdatedDate ?? context.CreatedDate).ToList();
    }

    private async Task<ProjectDocument> PersistFailureAsync(
        long documentId,
        ProjectContextDocument? contextDocument,
        string? processedBy)
    {
        if (contextDocument is not null && contextDocument.Id > 0)
        {
            try
            {
                contextDocument.Status = "ProcessingFailed";
                await _memory.SaveContextDocumentAsync(contextDocument, CancellationToken.None);
            }
            catch
            {
                // The project document remains the durable owner of processing failure state.
            }
        }

        return await _processingState.UpdateProcessingStateAsync(new ProjectDocumentProcessingStateUpdate
        {
            DocumentId = documentId,
            Status = "ProcessingFailed",
            FailureReason = FailureReason,
            UpdatedBy = processedBy
        }, CancellationToken.None);
    }

    private static ProjectDocumentProcessingResult Ready(
        ProjectDocument document,
        ProjectDocumentVersion version,
        long contextDocumentId) => new()
    {
        Document = document,
        Version = version,
        ContextDocumentId = contextDocumentId,
        Succeeded = true,
        Status = "Ready",
        NextSafeAction = "The exact document version is available for project retrieval."
    };

    private static ProjectDocumentProcessingException Failure(
        ProjectDocumentProcessingFailureKind kind,
        string message) => new(kind, message);
}
