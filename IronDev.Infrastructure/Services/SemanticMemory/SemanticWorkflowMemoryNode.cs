using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.KnowledgeCompiler;

namespace IronDev.Infrastructure.Services.SemanticMemory;

public sealed class SemanticWorkflowMemoryNode : ISemanticWorkflowMemoryNode
{
    private readonly ISemanticMemoryService _semanticMemoryService;

    public SemanticWorkflowMemoryNode(ISemanticMemoryService semanticMemoryService)
        => _semanticMemoryService = semanticMemoryService;

    public async Task<SemanticWorkflowContext> BuildContextAsync(
        SemanticWorkflowNodeRequest request,
        CancellationToken ct = default)
    {
        if (request.ProjectId <= 0)
        {
            return Empty(
                request,
                string.Empty,
                "Semantic memory skipped because no project id was supplied.");
        }

        var queryText = BuildQueryText(request);
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Empty(
                request,
                queryText,
                "Semantic memory skipped because the workflow request did not contain searchable text.");
        }

        try
        {
            var query = new SemanticSearchQuery
            {
                ProjectId = request.ProjectId,
                TenantId = request.TenantId,
                QueryText = queryText,
                Consumer = string.IsNullOrWhiteSpace(request.Consumer) ? "WorkflowNode" : request.Consumer,
                Limit = request.Limit <= 0 ? 8 : request.Limit,
                RequiredArtefactTypes = request.RequiredArtefactTypes,
                PreferredArtefactTypes = request.PreferredArtefactTypes,
                BoostedArtefactIds = request.BoostedArtefactIds,
                IncludeStale = request.IncludeStale
            };

            var warnings = Array.Empty<string>();
            var results = await _semanticMemoryService.SearchAsync(query, ct);
            if (results.Count == 0)
            {
                results = await _semanticMemoryService.SearchAsync(
                    query.ProjectId,
                    query.QueryText,
                    query.Limit,
                    minSimilarity: -1.0,
                    ct);
                if (results.Count > 0)
                    warnings = ["Semantic memory used relaxed similarity matching because the strict workflow query returned no matches."];
            }

            var bundle = SemanticContextBundleBuilder.Build(
                request.ProjectId,
                queryText,
                query.Consumer,
                results,
                warnings);

            return new SemanticWorkflowContext
            {
                ProjectId = request.ProjectId,
                Consumer = query.Consumer,
                QueryText = queryText,
                PromptContextMarkdown = bundle.PromptContextMarkdown,
                Items = results.Select(ToMemoryItem).ToList(),
                Warnings = bundle.Warnings,
                CreatedUtc = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return Empty(
                request,
                queryText,
                $"Semantic memory retrieval failed and the workflow should continue without memory: {ex.Message}");
        }
    }

    private static string BuildQueryText(SemanticWorkflowNodeRequest request)
    {
        var sb = new StringBuilder();

        AppendIfPresent(sb, "Goal", request.Goal);
        AppendIfPresent(sb, "Request", request.UserRequest);

        if (request.TicketId is { } ticketId)
            sb.AppendLine($"TicketId: {ticketId}");

        if (request.DiscussionDocumentId is { } discussionDocumentId)
            sb.AppendLine($"DiscussionDocumentId: {discussionDocumentId}");

        if (request.PreferredArtefactTypes.Count > 0)
            sb.AppendLine($"Preferred memory types: {string.Join(", ", request.PreferredArtefactTypes)}");

        if (request.RequiredArtefactTypes.Count > 0)
            sb.AppendLine($"Required memory types: {string.Join(", ", request.RequiredArtefactTypes)}");

        return sb.ToString().Trim();
    }

    private static void AppendIfPresent(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.AppendLine($"{label}: {value.Trim()}");
    }

    private static SemanticWorkflowMemoryItem ToMemoryItem(SemanticSearchResult result)
        => new()
        {
            ArtefactId = result.ArtefactId,
            ChunkId = result.ChunkId,
            Title = FirstNonEmpty(result.Title, result.Document.Title),
            ArtefactType = FirstNonEmpty(result.ArtefactType, result.Document.DocumentType),
            AuthorityLevel = FirstNonEmpty(result.AuthorityLevel, result.Document.AuthorityLevel),
            Snippet = result.Snippet,
            VectorSimilarity = result.VectorSimilarity == 0 ? result.SimilarityScore : result.VectorSimilarity,
            FinalScore = result.FinalScore == 0 ? result.Similarity : result.FinalScore,
            IsStale = result.IsStale,
            MatchReason = result.MatchReason,
            SourceEntityType = result.SourceEntityType,
            SourceEntityId = result.SourceEntityId,
            SourceVersionId = result.SourceVersionId
        };

    private static string FirstNonEmpty(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first : second ?? string.Empty;

    private static SemanticWorkflowContext Empty(
        SemanticWorkflowNodeRequest request,
        string queryText,
        string warning)
    {
        var consumer = string.IsNullOrWhiteSpace(request.Consumer) ? "WorkflowNode" : request.Consumer;
        return new SemanticWorkflowContext
        {
            ProjectId = request.ProjectId,
            Consumer = consumer,
            QueryText = queryText,
            PromptContextMarkdown = "No semantic memory matches were retrieved.",
            Items = [],
            Warnings = [warning],
            CreatedUtc = DateTime.UtcNow
        };
    }
}
