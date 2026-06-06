using IronDev.Core.Chat;
using IronDev.Core.KnowledgeCompiler;

namespace IronDev.Infrastructure.Services.SemanticMemory;

public sealed class SemanticMemoryEvidenceProvider : ISemanticMemoryEvidenceProvider
{
    public const string ContextOnly = "ContextOnly";

    private readonly ISemanticMemoryService _semanticMemory;

    public SemanticMemoryEvidenceProvider(ISemanticMemoryService semanticMemory)
    {
        _semanticMemory = semanticMemory;
    }

    public async Task<IReadOnlyList<MemoryEvidence>> GetEvidenceAsync(
        int projectId,
        string userMessage,
        string recentConversationSummary,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return Array.Empty<MemoryEvidence>();

        try
        {
            var bundle = await _semanticMemory.BuildContextBundleAsync(
                projectId,
                userMessage,
                recentConversationSummary,
                limit: 6,
                cancellationToken).ConfigureAwait(false);

            var retrievalTraceId = Guid.NewGuid().ToString("N");

            return bundle.Results
                .Take(6)
                .Select((result, index) => ToEvidence(result, userMessage, index + 1, retrievalTraceId))
                .Where(evidence => !string.IsNullOrWhiteSpace(evidence.SourceId) &&
                                   !string.IsNullOrWhiteSpace(evidence.Excerpt))
                .ToList();
        }
        catch
        {
            return Array.Empty<MemoryEvidence>();
        }
    }

    private static MemoryEvidence ToEvidence(
        SemanticSearchResult result,
        string query,
        int rank,
        string retrievalTraceId)
    {
        var sourceType = FirstNonEmpty(result.SourceEntityType, result.ArtefactType, "SemanticMemory");
        var sourceId = FirstNonEmpty(
            result.SourceEntityId,
            result.SourceVersionId,
            result.ArtefactId == Guid.Empty ? string.Empty : result.ArtefactId.ToString("N"),
            result.ChunkId == Guid.Empty ? string.Empty : result.ChunkId.ToString("N"));
        var title = FirstNonEmpty(result.Title, result.Document.Title, sourceType);
        var excerpt = FirstNonEmpty(result.Snippet, result.Document.Summary, result.Document.Content);
        var relevance = result.FinalScore > 0
            ? result.FinalScore
            : result.SimilarityScore > 0
                ? result.SimilarityScore
                : result.Similarity;
        var sourceCurrentness = MemoryCurrentnessNormalizer.FromDocumentStatus(result.Document.Status);
        var currentness = MemoryCurrentnessNormalizer.FromSemanticResult(result.IsStale, sourceCurrentness);

        return new MemoryEvidence(
            SourceId: $"semantic-{sourceType}-{sourceId}",
            SourceType: sourceType,
            Title: title,
            Excerpt: TruncateText(excerpt, 260),
            IsCurrent: currentness.IsCurrent,
            RelevanceScore: relevance,
            AuthorityLevel: MemoryAuthorityNormalizer.FromDocumentAuthority(
                FirstNonEmpty(result.AuthorityLevel, result.Document.AuthorityLevel, MemoryAuthorityLevels.ObservedFact),
                result.Document.Status),
            UsedFor: ContextOnly,
            StalenessReason: currentness.StalenessReason,
            SupersededBySourceId: currentness.SupersededBySourceId,
            RetrievalTraceId: retrievalTraceId,
            RetrievalRank: rank,
            RetrievalQuery: TruncateText(query, 160),
            MatchReason: result.MatchReason,
            VectorSimilarity: result.VectorSimilarity);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd() + "...";
    }
}
