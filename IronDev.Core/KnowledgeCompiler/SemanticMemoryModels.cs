using System;
using System.Collections.Generic;
using IronDev.Core.Models;
using IronDev.Data.Models;

namespace IronDev.Core.KnowledgeCompiler;

public sealed record VectorEmbedding
{
    public Guid Id { get; init; }
    public Guid ArtefactId { get; init; }
    public string ArtefactType { get; init; } = string.Empty;
    public int ProjectId { get; init; }
    public int? SourceDocumentVersionId { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public float[] Vector { get; init; } = [];
    public DateTime EmbeddedAtUtc { get; init; }
    public string ModelVersion { get; init; } = string.Empty;
}

public sealed record SemanticSearchResult
{
    public required ProjectContextDocument Document { get; init; }
    public Guid ArtefactId { get; init; }
    public Guid ChunkId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ArtefactType { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public double VectorSimilarity { get; init; }
    public double FinalScore { get; init; }
    public double Similarity { get; init; }
    public double SimilarityScore { get; init; }
    public double AuthorityBoost { get; init; }
    public double FreshnessBoost { get; init; }
    public double RecencyBoost { get; init; }
    public double SourceTypeBoost { get; init; }
    public double DirectLinkBoost { get; init; }
    public double ExplicitLinkBoost { get; init; }
    public double StalePenalty { get; init; }
    public bool IsStale { get; init; }
    public DateTime? IndexedUtc { get; init; }
    public string MatchReason { get; init; } = string.Empty;
    public string AuthorityLevel { get; init; } = string.Empty;
    public string SourceEntityType { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string? SourceVersionId { get; init; }
    public int? SourceDocumentVersionId { get; init; }
}

public sealed record SemanticSearchQuery
{
    public int ProjectId { get; init; }
    public int? TenantId { get; init; }
    public string QueryText { get; init; } = string.Empty;
    public int Limit { get; init; } = 10;
    public IReadOnlyList<string> RequiredArtefactTypes { get; init; } = [];
    public IReadOnlyList<string> PreferredArtefactTypes { get; init; } = [];
    public IReadOnlyList<Guid> BoostedArtefactIds { get; init; } = [];
    public bool IncludeStale { get; init; }
    public string Consumer { get; init; } = "Unknown";
}

public sealed record SemanticIndexRequest
{
    public int ProjectId { get; init; }
    public int? TenantId { get; init; }
    public string SourceEntityType { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string? SourceVersionId { get; init; }
    public string JobType { get; init; } = "CreateOrUpdateArtefact";
}

public sealed record SemanticStaleRequest
{
    public int ProjectId { get; init; }
    public string SourceEntityType { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string? SourceVersionId { get; init; }
}

public sealed record SemanticArtefactDraft
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int? TenantId { get; init; }
    public int ProjectId { get; init; }
    public string SourceEntityType { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string? SourceVersionId { get; init; }
    public string ArtefactType { get; init; } = string.Empty;
    public string AuthorityLevel { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string SearchableText { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
}

public sealed record SemanticArtefact
{
    public Guid Id { get; init; }
    public int? TenantId { get; init; }
    public int ProjectId { get; init; }
    public string SourceEntityType { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string? SourceVersionId { get; init; }
    public string ArtefactType { get; init; } = string.Empty;
    public string AuthorityLevel { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public bool IsStale { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

public sealed record SemanticChunkDraft
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ArtefactId { get; init; }
    public int ProjectId { get; init; }
    public int ChunkIndex { get; init; }
    public string ChunkText { get; init; } = string.Empty;
    public int? TokenEstimate { get; init; }
    public string ContentHash { get; init; } = string.Empty;
}

public sealed record SemanticChunk
{
    public Guid Id { get; init; }
    public Guid ArtefactId { get; init; }
    public int ProjectId { get; init; }
    public int ChunkIndex { get; init; }
    public string ChunkText { get; init; } = string.Empty;
    public int? TokenEstimate { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public string? WeaviateObjectId { get; init; }
    public DateTime? EmbeddedAtUtc { get; init; }
    public string? EmbeddingModel { get; init; }
    public bool IsStale { get; init; }
}

public sealed record EmbeddingJob
{
    public Guid Id { get; init; }
    public int? TenantId { get; init; }
    public int ProjectId { get; init; }
    public string SourceEntityType { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string? SourceVersionId { get; init; }
    public string JobType { get; init; } = string.Empty;
    public string Status { get; init; } = "Pending";
    public int Attempts { get; init; }
    public string? LastError { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime? StartedUtc { get; init; }
    public DateTime? CompletedUtc { get; init; }
}

public sealed record SemanticSearchCandidate
{
    public required ProjectContextDocument Document { get; init; }
    public required SemanticArtefact Artefact { get; init; }
    public required SemanticChunk Chunk { get; init; }
    public double VectorSimilarity { get; init; }
    public bool ContentHashMismatch { get; init; }
}

public sealed record SemanticContextBundle
{
    public int ProjectId { get; init; }
    public string Query { get; init; } = string.Empty;
    public string CallerContext { get; init; } = string.Empty;
    public IReadOnlyList<SemanticSearchResult> Results { get; init; } = [];
    public string PromptContextMarkdown { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record SemanticMemoryHealth
{
    public int ProjectId { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string ProviderStatus { get; init; } = string.Empty;
    public int DocumentCount { get; init; }
    public int EmbeddedCount { get; init; }
    public int StaleEmbeddingCount { get; init; }
    public DateTime? LastEmbeddedAtUtc { get; init; }
    public DateTime? LastRebuildAtUtc { get; init; }
}

public sealed record SemanticRankingOptions
{
    public double SimilarityWeight { get; init; } = 0.60;
    public double AuthorityWeight { get; init; } = 0.25;
    public double FreshnessWeight { get; init; } = 0.10;
    public double DirectLinkWeight { get; init; } = 0.05;
    public double MinimumSimilarity { get; init; } = 0.75;
}

public sealed record SemanticRetrievalTrace
{
    public Guid Id { get; init; }
    public int ProjectId { get; init; }
    public string Query { get; init; } = string.Empty;
    public IReadOnlyList<SemanticSearchResult> Results { get; init; } = [];
    public string CallerContext { get; init; } = string.Empty;
    public DateTime QueriedAtUtc { get; init; }
}

public sealed record SemanticWorkflowNodeRequest
{
    public int ProjectId { get; init; }
    public int? TenantId { get; init; }
    public string Consumer { get; init; } = "Unknown";
    public string Goal { get; init; } = string.Empty;
    public string UserRequest { get; init; } = string.Empty;
    public long? TicketId { get; init; }
    public int? DiscussionDocumentId { get; init; }
    public IReadOnlyList<string> RequiredArtefactTypes { get; init; } = [];
    public IReadOnlyList<string> PreferredArtefactTypes { get; init; } = [];
    public IReadOnlyList<Guid> BoostedArtefactIds { get; init; } = [];
    public int Limit { get; init; } = 8;
    public bool IncludeStale { get; init; }
}

public sealed record SemanticWorkflowMemoryItem
{
    public Guid ArtefactId { get; init; }
    public Guid ChunkId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ArtefactType { get; init; } = string.Empty;
    public string AuthorityLevel { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public double VectorSimilarity { get; init; }
    public double FinalScore { get; init; }
    public bool IsStale { get; init; }
    public string MatchReason { get; init; } = string.Empty;
    public string SourceEntityType { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string? SourceVersionId { get; init; }
}

public sealed record SemanticWorkflowContext
{
    public int ProjectId { get; init; }
    public string Consumer { get; init; } = "Unknown";
    public string QueryText { get; init; } = string.Empty;
    public string PromptContextMarkdown { get; init; } = string.Empty;
    public IReadOnlyList<SemanticWorkflowMemoryItem> Items { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}

public sealed record SemanticIndexRebuildProgress
{
    public int TotalDocuments { get; init; }
    public int ProcessedDocuments { get; init; }
    public string CurrentDocumentTitle { get; init; } = string.Empty;
    public bool IsCompleted { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record EmbeddingResult
{
    public required float[] Vector { get; init; }
    public required string Model { get; init; }
    public int Dimensions => Vector.Length;
}
