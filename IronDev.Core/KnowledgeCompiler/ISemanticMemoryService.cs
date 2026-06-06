using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Chat;
using IronDev.Core.Models;
using IronDev.Data.Models;

namespace IronDev.Core.KnowledgeCompiler;

public interface ISemanticMemoryService
{
    Task QueueIndexAsync(SemanticIndexRequest request, CancellationToken ct = default);
    Task EmbedAndStoreAsync(ProjectContextDocument document, CancellationToken ct = default);
    Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        SemanticSearchQuery query,
        CancellationToken ct = default);
    Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        int projectId,
        string query,
        int limit = 8,
        double minSimilarity = 0.75,
        CancellationToken ct = default);
    Task<SemanticContextBundle> BuildContextBundleAsync(
        int projectId,
        string query,
        string callerContext,
        int limit = 8,
        CancellationToken ct = default);
    Task RebuildIndexAsync(
        int projectId,
        IProgress<SemanticIndexRebuildProgress>? progress = null,
        CancellationToken ct = default);
    Task RebuildProjectAsync(int projectId, CancellationToken ct = default);
    Task MarkStaleAsync(SemanticStaleRequest request, CancellationToken ct = default);
    Task DeleteEmbeddingAsync(Guid artefactId, CancellationToken ct = default);
    Task<SemanticMemoryHealth> GetHealthAsync(int projectId, CancellationToken ct = default);
}

public interface ISemanticMemoryEvidenceProvider
{
    Task<IReadOnlyList<MemoryEvidence>> GetEvidenceAsync(
        int projectId,
        string userMessage,
        string recentConversationSummary,
        CancellationToken cancellationToken = default);
}

public interface ISemanticChunker
{
    IReadOnlyList<SemanticChunkDraft> Chunk(SemanticArtefactDraft artefact);
}

public interface ISemanticRankingService
{
    IReadOnlyList<SemanticSearchResult> Rank(
        SemanticSearchQuery query,
        IReadOnlyList<SemanticSearchCandidate> candidates);
}

public interface ISemanticArtefactRepository
{
    Task UpsertArtefactAsync(SemanticArtefactDraft artefact, CancellationToken ct = default);
    Task<SemanticArtefact?> GetArtefactAsync(Guid artefactId, CancellationToken ct = default);
    Task<SemanticArtefact?> GetArtefactBySourceAsync(
        int projectId,
        string sourceEntityType,
        string sourceEntityId,
        string? sourceVersionId = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<SemanticArtefact>> GetProjectArtefactsAsync(int projectId, bool includeStale = false, CancellationToken ct = default);
    Task MarkStaleAsync(SemanticStaleRequest request, CancellationToken ct = default);
}

public interface ISemanticChunkRepository
{
    Task ReplaceChunksAsync(Guid artefactId, IReadOnlyList<SemanticChunkDraft> chunks, CancellationToken ct = default);
    Task<IReadOnlyList<SemanticChunk>> GetChunksAsync(Guid artefactId, bool includeStale = false, CancellationToken ct = default);
    Task<SemanticChunk?> GetChunkAsync(Guid chunkId, CancellationToken ct = default);
    Task MarkProjectStaleAsync(int projectId, CancellationToken ct = default);
    Task MarkArtefactChunksStaleAsync(Guid artefactId, CancellationToken ct = default);
    Task MarkEmbeddedAsync(Guid chunkId, string weaviateObjectId, string model, CancellationToken ct = default);
}

public interface IEmbeddingJobRepository
{
    Task CreateAsync(EmbeddingJob job, CancellationToken ct = default);
    Task<IReadOnlyList<EmbeddingJob>> GetPendingAsync(int take = 25, CancellationToken ct = default);
    Task MarkProcessingAsync(Guid jobId, CancellationToken ct = default);
    Task MarkCompletedAsync(Guid jobId, CancellationToken ct = default);
    Task MarkFailedAsync(Guid jobId, string error, int maxAttempts = 5, CancellationToken ct = default);
}

public interface ISemanticSearchTraceRepository
{
    Task<Guid> CreateTraceAsync(SemanticSearchQuery query, CancellationToken ct = default);
    Task AddResultsAsync(Guid traceId, IReadOnlyList<SemanticSearchResult> results, CancellationToken ct = default);
}

public interface ISemanticWorkflowMemoryNode
{
    Task<SemanticWorkflowContext> BuildContextAsync(
        SemanticWorkflowNodeRequest request,
        CancellationToken ct = default);
}
