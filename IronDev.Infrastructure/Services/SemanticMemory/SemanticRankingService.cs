using System;
using System.Collections.Generic;
using System.Linq;
using IronDev.Core.KnowledgeCompiler;

namespace IronDev.Infrastructure.Services.SemanticMemory;

public sealed class SemanticRankingService : ISemanticRankingService
{
    public IReadOnlyList<SemanticSearchResult> Rank(
        SemanticSearchQuery query,
        IReadOnlyList<SemanticSearchCandidate> candidates)
    {
        return candidates
            .Where(candidate => query.IncludeStale || (!candidate.Artefact.IsStale && !candidate.Chunk.IsStale && !candidate.ContentHashMismatch))
            .Where(candidate => query.RequiredArtefactTypes.Count == 0 || query.RequiredArtefactTypes.Contains(candidate.Artefact.ArtefactType, StringComparer.OrdinalIgnoreCase))
            .Select(candidate => ToResult(query, candidate))
            .OrderByDescending(result => result.FinalScore)
            .Take(query.Limit)
            .ToList();
    }

    private static SemanticSearchResult ToResult(SemanticSearchQuery query, SemanticSearchCandidate candidate)
    {
        var artefact = candidate.Artefact;
        var chunk = candidate.Chunk;
        var authorityBoost = GetAuthorityBoost(artefact.AuthorityLevel);
        var sourceTypeBoost = GetSourceTypeBoost(query.Consumer, artefact.ArtefactType);
        var recencyBoost = GetRecencyBoost(artefact.UpdatedUtc);
        var explicitLinkBoost = query.BoostedArtefactIds.Contains(artefact.Id) ? 0.35 : 0.0;
        var stalePenalty = artefact.IsStale || chunk.IsStale
            ? 0.40
            : candidate.ContentHashMismatch ? 0.60 : 0.0;

        var finalScore = candidate.VectorSimilarity
            + authorityBoost
            + sourceTypeBoost
            + recencyBoost
            + explicitLinkBoost
            - stalePenalty;

        return new SemanticSearchResult
        {
            Document = candidate.Document,
            ArtefactId = artefact.Id,
            ChunkId = chunk.Id,
            Title = artefact.Title,
            ArtefactType = artefact.ArtefactType,
            Snippet = BuildSnippet(chunk.ChunkText),
            VectorSimilarity = candidate.VectorSimilarity,
            FinalScore = finalScore,
            Similarity = finalScore,
            SimilarityScore = candidate.VectorSimilarity,
            AuthorityBoost = authorityBoost,
            FreshnessBoost = recencyBoost,
            RecencyBoost = recencyBoost,
            SourceTypeBoost = sourceTypeBoost,
            DirectLinkBoost = explicitLinkBoost,
            ExplicitLinkBoost = explicitLinkBoost,
            StalePenalty = stalePenalty,
            IsStale = artefact.IsStale || chunk.IsStale || candidate.ContentHashMismatch,
            IndexedUtc = chunk.EmbeddedAtUtc ?? artefact.UpdatedUtc,
            MatchReason = BuildMatchReason(candidate.VectorSimilarity, authorityBoost, sourceTypeBoost, explicitLinkBoost, stalePenalty),
            AuthorityLevel = artefact.AuthorityLevel,
            SourceEntityType = artefact.SourceEntityType,
            SourceEntityId = artefact.SourceEntityId,
            SourceVersionId = artefact.SourceVersionId
        };
    }

    private static double GetAuthorityBoost(string authorityLevel) => authorityLevel switch
    {
        "CommittedDecision" => 0.35,
        "AcceptedArchitecture" => 0.30,
        "AcceptedRequirement" => 0.25,
        "ReviewedTicket" => 0.20,
        "CodeSummary" => 0.15,
        "TestingCompanionReport" => 0.12,
        "TraceObservation" => 0.08,
        "GeneratedDraft" => 0.02,
        "ChatSummary" => 0.00,
        "LowAuthorityNote" => -0.10,
        "Binding" => 0.35,
        "StrongGuidance" => 0.25,
        "ResolvedKnowledge" => 0.20,
        _ => 0.05
    };

    private static double GetSourceTypeBoost(string consumer, string artefactType)
    {
        var normalizedConsumer = consumer.ToLowerInvariant();
        return normalizedConsumer switch
        {
            var c when c.Contains("build") => artefactType switch
            {
                "Decision" => 0.35,
                "Architecture" => 0.30,
                "Ticket" => 0.25,
                "CodeSummary" => 0.25,
                "TraceSummary" => 0.15,
                "Discussion" => 0.05,
                _ => 0.0
            },
            var c when c.Contains("test") => artefactType switch
            {
                "TraceSummary" => 0.30,
                "TestingCompanionReport" => 0.30,
                "Ticket" => 0.20,
                "CodeSummary" => 0.15,
                "Decision" => 0.10,
                _ => 0.0
            },
            _ => artefactType switch
            {
                "Decision" => 0.30,
                "Requirement" => 0.25,
                "Architecture" => 0.20,
                "Discussion" => 0.10,
                "TraceSummary" => 0.05,
                _ => 0.0
            }
        };
    }

    private static double GetRecencyBoost(DateTime updatedUtc)
    {
        var age = (DateTime.UtcNow - updatedUtc).TotalDays;
        if (age < 0) age = 0;
        return Math.Min(0.10, 0.10 / (1.0 + age / 30.0));
    }

    private static string BuildMatchReason(double similarity, double authority, double sourceType, double explicitLink, double stalePenalty)
        => $"Vector {similarity:F2}; authority {authority:+0.00;-0.00;0.00}; source {sourceType:+0.00;-0.00;0.00}; explicit {explicitLink:+0.00;-0.00;0.00}; stale -{stalePenalty:F2}.";

    private static string BuildSnippet(string text)
    {
        var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= 280 ? normalized : normalized[..280] + "...";
    }
}
