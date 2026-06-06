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

            return bundle.Results
                .Take(6)
                .Select(ToEvidence)
                .Where(evidence => !string.IsNullOrWhiteSpace(evidence.SourceId) &&
                                   !string.IsNullOrWhiteSpace(evidence.Excerpt))
                .ToList();
        }
        catch
        {
            return Array.Empty<MemoryEvidence>();
        }
    }

    private static MemoryEvidence ToEvidence(SemanticSearchResult result)
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

        return new MemoryEvidence(
            SourceId: $"semantic-{sourceType}-{sourceId}",
            SourceType: sourceType,
            Title: title,
            Excerpt: TruncateText(excerpt, 260),
            IsCurrent: !result.IsStale,
            RelevanceScore: relevance,
            AuthorityLevel: MemoryAuthorityNormalizer.FromSemanticAuthority(
                FirstNonEmpty(result.AuthorityLevel, result.Document.AuthorityLevel, MemoryAuthorityLevels.ObservedFact)),
            UsedFor: ContextOnly);
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
