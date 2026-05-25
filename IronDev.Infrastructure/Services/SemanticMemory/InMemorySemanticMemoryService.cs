using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services.SemanticMemory;

public sealed class InMemorySemanticMemoryService : ISemanticMemoryService
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IEmbeddingContentExtractor _contentExtractor;
    private readonly IProjectMemoryService _projectMemoryService;
    private readonly SemanticRankingOptions _rankingOptions;

    // Stores: ArtefactId -> (Embedding, Document)
    private static readonly ConcurrentDictionary<Guid, (VectorEmbedding Embedding, ProjectContextDocument Document)> _store = new();

    public InMemorySemanticMemoryService(
        IEmbeddingProvider embeddingProvider,
        IEmbeddingContentExtractor contentExtractor,
        IProjectMemoryService projectMemoryService,
        SemanticRankingOptions? rankingOptions = null)
    {
        _embeddingProvider = embeddingProvider;
        _contentExtractor = contentExtractor;
        _projectMemoryService = projectMemoryService;
        _rankingOptions = rankingOptions ?? new SemanticRankingOptions();
    }

    public Task QueueIndexAsync(SemanticIndexRequest request, CancellationToken ct = default)
        => Task.CompletedTask;

    public async Task EmbedAndStoreAsync(ProjectContextDocument document, CancellationToken ct = default)
    {
        if (document == null) return;

        string text = _contentExtractor.Extract(document);
        var embeddingResult = await _embeddingProvider.EmbedAsync(text, ct);

        var artefactId = GuidFromLong(document.Id);

        var embedding = new VectorEmbedding
        {
            Id = Guid.NewGuid(),
            ArtefactId = artefactId,
            ArtefactType = document.DocumentType,
            ProjectId = document.ProjectId,
            SourceDocumentVersionId = null,
            ContentHash = GetContentHash(text),
            Vector = embeddingResult.Vector,
            EmbeddedAtUtc = DateTime.UtcNow,
            ModelVersion = embeddingResult.Model
        };

        _store[artefactId] = (embedding, document);
    }

    public async Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        int projectId,
        string query,
        int limit = 8,
        double minSimilarity = 0.75,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SemanticSearchResult>();

        var queryEmbedding = await _embeddingProvider.EmbedAsync(query, ct);

        var candidates = _store.Values
            .Where(x => x.Embedding.ProjectId == projectId)
            .Select(x =>
            {
                double similarity = CosineSimilarity.Compute(queryEmbedding.Vector, x.Embedding.Vector);
                return (x.Embedding, x.Document, Similarity: similarity);
            })
            .Where(x => x.Similarity >= minSimilarity)
            .ToList();

        var results = new List<SemanticSearchResult>();

        foreach (var (embedding, doc, similarity) in candidates)
        {
            double authorityScore = GetAuthorityScore(doc.AuthorityLevel);
            double freshnessScore = GetFreshnessScore(doc.UpdatedDate ?? doc.CreatedDate);
            double directLinkScore = GetDirectLinkScore(doc, query);
            bool isStale = IsStale(doc, embedding);

            double finalScore = (similarity * _rankingOptions.SimilarityWeight) +
                                 (authorityScore * _rankingOptions.AuthorityWeight) +
                                 (freshnessScore * _rankingOptions.FreshnessWeight) +
                                 (directLinkScore * _rankingOptions.DirectLinkWeight);

            results.Add(new SemanticSearchResult
            {
                Document = doc,
                ArtefactId = embedding.ArtefactId,
                ChunkId = embedding.Id,
                Title = doc.Title,
                ArtefactType = doc.DocumentType,
                Snippet = BuildSnippet(doc.Content),
                VectorSimilarity = similarity,
                FinalScore = finalScore,
                Similarity = finalScore,
                SimilarityScore = similarity,
                AuthorityBoost = authorityScore,
                FreshnessBoost = freshnessScore,
                RecencyBoost = freshnessScore,
                SourceTypeBoost = 0,
                DirectLinkBoost = directLinkScore,
                ExplicitLinkBoost = directLinkScore,
                StalePenalty = isStale ? 0.40 : 0,
                IsStale = isStale,
                IndexedUtc = embedding.EmbeddedAtUtc,
                MatchReason = $"Semantic similarity: {similarity:F2}, Authority: {doc.AuthorityLevel}",
                AuthorityLevel = doc.AuthorityLevel,
                SourceDocumentVersionId = null
            });
        }

        return results
            .OrderByDescending(r => r.FinalScore)
            .Take(limit)
            .ToList();
    }

    public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(
        SemanticSearchQuery query,
        CancellationToken ct = default)
        => SearchAsync(
            query.ProjectId,
            query.QueryText,
            query.Limit,
            query.IncludeStale ? -1.0 : _rankingOptions.MinimumSimilarity,
            ct);

    public async Task<SemanticContextBundle> BuildContextBundleAsync(
        int projectId,
        string query,
        string callerContext,
        int limit = 8,
        CancellationToken ct = default)
    {
        var results = await SearchAsync(projectId, query, limit, _rankingOptions.MinimumSimilarity, ct);
        return SemanticContextBundleBuilder.Build(projectId, query, callerContext, results);
    }

    public async Task RebuildIndexAsync(
        int projectId,
        IProgress<SemanticIndexRebuildProgress>? progress = null,
        CancellationToken ct = default)
    {
        foreach (var staleKey in _store
            .Where(x => x.Value.Embedding.ProjectId == projectId)
            .Select(x => x.Key)
            .ToList())
        {
            _store.TryRemove(staleKey, out _);
        }

        var documents = await _projectMemoryService.GetContextDocumentsAsync(
            projectId: projectId,
            status: "Active",
            take: 1000,
            cancellationToken: ct);

        int total = documents.Count;
        int processed = 0;

        foreach (var doc in documents)
        {
            if (ct.IsCancellationRequested)
                break;

            progress?.Report(new SemanticIndexRebuildProgress
            {
                TotalDocuments = total,
                ProcessedDocuments = processed,
                CurrentDocumentTitle = doc.Title,
                IsCompleted = false
            });

            await EmbedAndStoreAsync(doc, ct);
            processed++;
        }

        progress?.Report(new SemanticIndexRebuildProgress
        {
            TotalDocuments = total,
            ProcessedDocuments = processed,
            CurrentDocumentTitle = string.Empty,
            IsCompleted = true
        });
    }

    public Task RebuildProjectAsync(int projectId, CancellationToken ct = default)
        => RebuildIndexAsync(projectId, ct: ct);

    public Task MarkStaleAsync(SemanticStaleRequest request, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteEmbeddingAsync(Guid artefactId, CancellationToken ct = default)
    {
        _store.TryRemove(artefactId, out _);
        return Task.CompletedTask;
    }

    public async Task<SemanticMemoryHealth> GetHealthAsync(int projectId, CancellationToken ct = default)
    {
        var projectEmbeddings = _store.Values.Where(x => x.Embedding.ProjectId == projectId).ToList();
        var documents = await _projectMemoryService.GetContextDocumentsAsync(
            projectId,
            status: "Active",
            take: 1000,
            cancellationToken: ct);
        int count = projectEmbeddings.Count;
        int staleCount = projectEmbeddings.Count(x => IsStale(x.Document, x.Embedding));

        DateTime? lastEmbedded = count > 0 
            ? projectEmbeddings.Max(x => x.Embedding.EmbeddedAtUtc) 
            : null;

        return new SemanticMemoryHealth
        {
            ProjectId = projectId,
            ProviderName = "InMemory",
            ProviderStatus = "Ready",
            DocumentCount = documents.Count,
            EmbeddedCount = count,
            StaleEmbeddingCount = staleCount,
            LastEmbeddedAtUtc = lastEmbedded,
            LastRebuildAtUtc = null
        };
    }

    private static Guid GuidFromLong(long value)
    {
        byte[] bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private static string GetContentHash(string text)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    private bool IsStale(ProjectContextDocument document, VectorEmbedding embedding)
    {
        var currentText = _contentExtractor.Extract(document);
        return !string.Equals(GetContentHash(currentText), embedding.ContentHash, StringComparison.OrdinalIgnoreCase);
    }

    private static double GetAuthorityScore(string authorityLevel)
    {
        return authorityLevel switch
        {
            "Binding" => 1.0,
            "StrongGuidance" => 0.8,
            "ObservedFact" => 0.6,
            "Pending" => 0.4,
            "ContextOnly" => 0.2,
            _ => 0.1
        };
    }

    private static double GetFreshnessScore(DateTime date)
    {
        var ageInDays = (DateTime.UtcNow - date).TotalDays;
        if (ageInDays < 0) ageInDays = 0;
        return 1.0 / (1.0 + ageInDays / 30.0);
    }

    private static double GetDirectLinkScore(ProjectContextDocument doc, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0.0;
        
        bool titleMatch = doc.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
        bool tagsMatch = doc.Tags != null && doc.Tags.Contains(query, StringComparison.OrdinalIgnoreCase);

        if (titleMatch && tagsMatch) return 1.0;
        if (titleMatch) return 0.8;
        if (tagsMatch) return 0.5;

        return 0.0;
    }

    private static string BuildSnippet(string text)
    {
        var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= 280 ? normalized : normalized[..280] + "...";
    }
}
