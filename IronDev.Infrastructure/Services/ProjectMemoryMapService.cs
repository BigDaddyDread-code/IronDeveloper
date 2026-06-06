using IronDev.Core.Chat;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;

namespace IronDev.Services;

public interface IProjectMemoryMapService
{
    Task<ProjectMemoryMap?> GetMapAsync(int projectId, int take = 50, CancellationToken cancellationToken = default);
}

public sealed class ProjectMemoryMapService : IProjectMemoryMapService
{
    private readonly IProjectService _projects;
    private readonly IProjectMemoryService _memory;
    private readonly ITicketService _tickets;
    private readonly ISemanticArtefactRepository? _semanticArtefacts;
    private readonly ISemanticChunkRepository? _semanticChunks;

    public ProjectMemoryMapService(
        IProjectService projects,
        IProjectMemoryService memory,
        ITicketService tickets,
        ISemanticArtefactRepository? semanticArtefacts = null,
        ISemanticChunkRepository? semanticChunks = null)
    {
        _projects = projects;
        _memory = memory;
        _tickets = tickets;
        _semanticArtefacts = semanticArtefacts;
        _semanticChunks = semanticChunks;
    }

    public async Task<ProjectMemoryMap?> GetMapAsync(
        int projectId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var boundedTake = Math.Clamp(take, 1, 200);
        var decisions = await _memory.GetRecentDecisionsAsync(projectId, boundedTake, cancellationToken).ConfigureAwait(false);
        var tickets = await _tickets.GetRecentTicketsAsync(projectId, boundedTake, cancellationToken).ConfigureAwait(false);
        var documents = await _memory.GetContextDocumentsAsync(projectId, status: null, take: boundedTake, cancellationToken: cancellationToken).ConfigureAwait(false);
        var rules = await _memory.GetProjectRulesAsync(projectId, cancellationToken).ConfigureAwait(false);
        var semanticItems = await GetSemanticItemsAsync(projectId, boundedTake, cancellationToken).ConfigureAwait(false);

        var entries = decisions.Select(MapDecision)
            .Concat(tickets.Select(MapTicket))
            .Concat(documents.Select(MapDocument))
            .Concat(rules.Take(boundedTake).Select(MapRule))
            .Concat(semanticItems)
            .OrderBy(entry => entry.SourceType, StringComparer.Ordinal)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var graph = BuildSourceGraph(entries);

        return new ProjectMemoryMap(
            project.Id,
            project.Name,
            DateTimeOffset.UtcNow,
            new ProjectMemoryMapCounts(
                Total: entries.Length,
                Decisions: entries.Count(entry => entry.SourceType == "Decision"),
                Tickets: entries.Count(entry => entry.SourceType == "Ticket"),
                Documents: entries.Count(entry => entry.SourceType == "Document"),
                Rules: entries.Count(entry => entry.SourceType == "Rule"),
                Current: entries.Count(entry => entry.IsCurrent),
                Stale: entries.Count(entry => !entry.IsCurrent)),
            entries,
            graph);
    }

    private async Task<IReadOnlyList<ProjectMemoryMapItem>> GetSemanticItemsAsync(
        int projectId,
        int take,
        CancellationToken cancellationToken)
    {
        if (_semanticArtefacts is null || _semanticChunks is null)
            return Array.Empty<ProjectMemoryMapItem>();

        var items = new List<ProjectMemoryMapItem>();
        var artefacts = await _semanticArtefacts.GetProjectArtefactsAsync(projectId, includeStale: true, cancellationToken).ConfigureAwait(false);
        foreach (var artefact in artefacts.Take(take))
        {
            items.Add(MapSemanticArtefact(artefact));
            var chunks = await _semanticChunks.GetChunksAsync(artefact.Id, includeStale: true, cancellationToken).ConfigureAwait(false);
            items.AddRange(chunks.Take(take).Select(chunk => MapSemanticChunk(artefact, chunk)));
        }

        return items;
    }

    private static ProjectMemoryMapItem MapDecision(ProjectDecision decision)
    {
        var currentness = MemoryCurrentnessNormalizer.FromDecisionStatus(decision.Status);
        return new ProjectMemoryMapItem(
            SourceId: $"decision-{decision.Id}",
            SourceType: "Decision",
            Title: decision.Title,
            Summary: TruncateText(decision.Detail, 240),
            AuthorityLevel: MemoryAuthorityNormalizer.FromDecisionStatus(decision.Status),
            IsCurrent: currentness.IsCurrent,
            StalenessReason: currentness.StalenessReason,
            SupersededBySourceId: currentness.SupersededBySourceId,
            SourceStatus: decision.Status,
            UsedFor: "ContextOnly",
            CreatedUtc: ToOffset(decision.CreatedDate),
            Links: SourceMessageLink(decision.SourceChatMessageId, includeDerivedFrom: true));
    }

    private static ProjectMemoryMapItem MapTicket(ProjectTicket ticket)
    {
        var currentness = MemoryCurrentnessNormalizer.FromTicketState(ticket.Status, ticket.IsDeleted);
        return new ProjectMemoryMapItem(
            SourceId: $"ticket-{ticket.Id}",
            SourceType: "Ticket",
            Title: ticket.Title,
            Summary: TruncateText(FirstNonEmpty(ticket.Summary, ticket.ContextSummary, ticket.Content), 240),
            AuthorityLevel: MemoryAuthorityNormalizer.FromTicketState(ticket.IsGenerated, ticket.Status),
            IsCurrent: currentness.IsCurrent,
            StalenessReason: currentness.StalenessReason,
            SupersededBySourceId: currentness.SupersededBySourceId,
            SourceStatus: ticket.Status,
            UsedFor: "ContextOnly",
            CreatedUtc: ToOffset(ticket.CreatedDate),
            Links: TicketLinks(ticket));
    }

    private static ProjectMemoryMapItem MapDocument(ProjectContextDocument document)
    {
        var currentness = MemoryCurrentnessNormalizer.FromDocumentStatus(document.Status);
        return new ProjectMemoryMapItem(
            SourceId: $"document-{document.Id}",
            SourceType: "Document",
            Title: document.Title,
            Summary: TruncateText(FirstNonEmpty(document.Summary, document.Content), 240),
            AuthorityLevel: MemoryAuthorityNormalizer.FromDocumentAuthority(document.AuthorityLevel, document.Status),
            IsCurrent: currentness.IsCurrent,
            StalenessReason: currentness.StalenessReason,
            SupersededBySourceId: currentness.SupersededBySourceId,
            SourceStatus: document.Status,
            UsedFor: "ContextOnly",
            CreatedUtc: ToOffset(document.CreatedDate),
            UpdatedUtc: ToOffset(document.UpdatedDate),
            Links: DocumentLinks(document));
    }

    private static ProjectMemoryMapItem MapRule(ProjectRule rule)
    {
        var currentness = MemoryCurrentnessNormalizer.FromRuleEnforcementLevel(rule.EnforcementLevel);
        return new ProjectMemoryMapItem(
            SourceId: $"rule-{rule.Id}",
            SourceType: "Rule",
            Title: rule.Name,
            Summary: TruncateText(rule.Description, 240),
            AuthorityLevel: MemoryAuthorityNormalizer.FromRuleEnforcementLevel(rule.EnforcementLevel),
            IsCurrent: currentness.IsCurrent,
            StalenessReason: currentness.StalenessReason,
            SupersededBySourceId: currentness.SupersededBySourceId,
            SourceStatus: rule.EnforcementLevel,
            UsedFor: "ContextOnly",
            CreatedUtc: ToOffset(rule.CreatedDate),
            UpdatedUtc: ToOffset(rule.UpdatedDate));
    }

    private static ProjectMemoryMapItem MapSemanticArtefact(SemanticArtefact artefact)
    {
        var sourceCurrentness = artefact.IsStale
            ? new MemoryCurrentness(false, "Semantic artefact is marked stale.")
            : new MemoryCurrentness(true);
        return new ProjectMemoryMapItem(
            SourceId: $"semantic-artefact-{artefact.Id:N}",
            SourceType: "SemanticArtefact",
            Title: artefact.Title,
            Summary: artefact.Summary,
            AuthorityLevel: MemoryAuthorityNormalizer.FromSemanticAuthority(artefact.AuthorityLevel),
            IsCurrent: sourceCurrentness.IsCurrent,
            StalenessReason: sourceCurrentness.StalenessReason,
            SupersededBySourceId: sourceCurrentness.SupersededBySourceId,
            SourceStatus: artefact.IsStale ? "Stale" : "Active",
            UsedFor: "ContextOnly",
            CreatedUtc: ToOffset(artefact.CreatedUtc),
            UpdatedUtc: ToOffset(artefact.UpdatedUtc),
            Links:
            [
                new ProjectMemoryLink(ProjectMemoryLinkTypes.SourceEntity, $"{artefact.SourceEntityType}-{artefact.SourceEntityId}", artefact.SourceEntityType)
            ]);
    }

    private static ProjectMemoryMapItem MapSemanticChunk(SemanticArtefact artefact, SemanticChunk chunk)
    {
        var currentness = MemoryCurrentnessNormalizer.FromSemanticResult(
            chunk.IsStale || artefact.IsStale,
            artefact.IsStale
                ? new MemoryCurrentness(false, "Semantic artefact is marked stale.")
                : new MemoryCurrentness(true));
        return new ProjectMemoryMapItem(
            SourceId: $"semantic-chunk-{chunk.Id:N}",
            SourceType: "SemanticChunk",
            Title: $"{artefact.Title} chunk {chunk.ChunkIndex}",
            Summary: TruncateText(chunk.ChunkText, 240),
            AuthorityLevel: MemoryAuthorityNormalizer.FromSemanticAuthority(artefact.AuthorityLevel),
            IsCurrent: currentness.IsCurrent,
            StalenessReason: currentness.StalenessReason,
            SupersededBySourceId: currentness.SupersededBySourceId,
            SourceStatus: chunk.IsStale ? "Stale" : "Active",
            UsedFor: "ContextOnly",
            UpdatedUtc: ToOffset(chunk.EmbeddedAtUtc),
            Links:
            [
                new ProjectMemoryLink(ProjectMemoryLinkTypes.ParentArtefact, $"semantic-artefact-{artefact.Id:N}", "SemanticArtefact")
            ]);
    }

    private static IReadOnlyList<ProjectMemoryLink> DocumentLinks(ProjectContextDocument document)
    {
        var links = new List<ProjectMemoryLink>();
        if (document.SourceChatMessageId.HasValue)
        {
            links.Add(new ProjectMemoryLink(ProjectMemoryLinkTypes.SourceChatMessage, $"chat-message-{document.SourceChatMessageId.Value}", "ChatMessage"));
            links.Add(new ProjectMemoryLink(ProjectMemoryLinkTypes.DerivedFrom, $"chat-message-{document.SourceChatMessageId.Value}", "ChatMessage"));
        }

        if (document.SupersedesDocumentId.HasValue)
            links.Add(new ProjectMemoryLink(ProjectMemoryLinkTypes.Supersedes, $"document-{document.SupersedesDocumentId.Value}", "Document"));
        return links;
    }

    private static IReadOnlyList<ProjectMemoryLink> TicketLinks(ProjectTicket ticket)
    {
        var links = new List<ProjectMemoryLink>();
        if (ticket.SourceChatMessageId.HasValue)
        {
            links.Add(new ProjectMemoryLink(ProjectMemoryLinkTypes.SourceChatMessage, $"chat-message-{ticket.SourceChatMessageId.Value}", "ChatMessage"));
            if (ticket.IsGenerated)
                links.Add(new ProjectMemoryLink(ProjectMemoryLinkTypes.GeneratedFrom, $"chat-message-{ticket.SourceChatMessageId.Value}", "ChatMessage"));
        }

        if (ticket.SourceDocumentVersionId.HasValue)
        {
            links.Add(new ProjectMemoryLink(ProjectMemoryLinkTypes.SourceDocumentVersion, $"document-version-{ticket.SourceDocumentVersionId.Value}", "DocumentVersion"));
            if (ticket.IsGenerated)
                links.Add(new ProjectMemoryLink(ProjectMemoryLinkTypes.GeneratedFrom, $"document-version-{ticket.SourceDocumentVersionId.Value}", "DocumentVersion"));
        }

        return links;
    }

    private static IReadOnlyList<ProjectMemoryLink> SourceMessageLink(long? sourceChatMessageId, bool includeDerivedFrom = false)
    {
        if (!sourceChatMessageId.HasValue)
            return [];

        var sourceId = $"chat-message-{sourceChatMessageId.Value}";
        var links = new List<ProjectMemoryLink>
        {
            new(ProjectMemoryLinkTypes.SourceChatMessage, sourceId, "ChatMessage")
        };
        if (includeDerivedFrom)
            links.Add(new ProjectMemoryLink(ProjectMemoryLinkTypes.DerivedFrom, sourceId, "ChatMessage"));
        return links;
    }

    private static ProjectMemorySourceGraph BuildSourceGraph(IReadOnlyList<ProjectMemoryMapItem> entries)
    {
        var nodes = new Dictionary<string, ProjectMemorySourceNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<ProjectMemorySourceEdge>();

        foreach (var entry in entries)
        {
            nodes[entry.SourceId] = new ProjectMemorySourceNode(
                entry.SourceId,
                entry.SourceType,
                entry.Title,
                entry.AuthorityLevel,
                entry.IsCurrent);

            foreach (var link in entry.Links ?? Array.Empty<ProjectMemoryLink>())
            {
                if (!nodes.ContainsKey(link.TargetSourceId))
                {
                    nodes[link.TargetSourceId] = new ProjectMemorySourceNode(
                        link.TargetSourceId,
                        FirstNonEmpty(link.TargetSourceType, InferSourceType(link.TargetSourceId)),
                        link.TargetSourceId,
                        MemoryAuthorityLevels.Unknown,
                        false);
                }

                edges.Add(new ProjectMemorySourceEdge(entry.SourceId, link.TargetSourceId, link.LinkType));
                if (string.Equals(link.LinkType, ProjectMemoryLinkTypes.Supersedes, StringComparison.OrdinalIgnoreCase))
                    edges.Add(new ProjectMemorySourceEdge(link.TargetSourceId, entry.SourceId, ProjectMemoryLinkTypes.SupersededBy));
            }
        }

        return new ProjectMemorySourceGraph(
            nodes.Values.OrderBy(node => node.SourceType, StringComparer.Ordinal).ThenBy(node => node.SourceId, StringComparer.OrdinalIgnoreCase).ToArray(),
            edges.OrderBy(edge => edge.FromSourceId, StringComparer.OrdinalIgnoreCase).ThenBy(edge => edge.LinkType, StringComparer.Ordinal).ToArray());
    }

    private static string InferSourceType(string sourceId)
    {
        if (sourceId.StartsWith("decision-", StringComparison.OrdinalIgnoreCase))
            return "Decision";
        if (sourceId.StartsWith("ticket-", StringComparison.OrdinalIgnoreCase))
            return "Ticket";
        if (sourceId.StartsWith("document-", StringComparison.OrdinalIgnoreCase))
            return "Document";
        if (sourceId.StartsWith("rule-", StringComparison.OrdinalIgnoreCase))
            return "Rule";
        if (sourceId.StartsWith("chat-message-", StringComparison.OrdinalIgnoreCase))
            return "ChatMessage";
        if (sourceId.StartsWith("semantic-artefact-", StringComparison.OrdinalIgnoreCase))
            return "SemanticArtefact";
        if (sourceId.StartsWith("semantic-chunk-", StringComparison.OrdinalIgnoreCase))
            return "SemanticChunk";
        return "Unknown";
    }

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue && value.Value != default
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
            : null;

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
