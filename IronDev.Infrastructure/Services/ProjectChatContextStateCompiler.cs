using IronDev.Core.Chat;
using IronDev.Core.Models;
using IronDev.Data.Models;
using System.Collections.Generic;
using System.Linq;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectChatContextStateCompiler
{
    public ChatContextState Compile(
        ProjectChatContextPipelineResult context,
        string currentUserMessage,
        string recentConversationSummary)
    {
        return new ChatContextState(
            RequiresClarification: context.ContextAgentResult.IsClarificationRequired || context.RouteDecision.NeedsClarification,
            ClarificationQuestions: context.ContextAgentResult.ClarificationQuestions.Concat(context.RouteDecision.ClarificationQuestions).ToList(),
            ContextSummary: context.ContextAgentResult.ContextSummary,
            CurrentUserMessage: currentUserMessage,
            RecentTurns: BuildRecentTurns(recentConversationSummary),
            ActiveArtifact: BuildActiveArtifact(context),
            SemanticEvidence: BuildSemanticEvidence(context),
            AvailableSkillHints: BuildAvailableSkillHints(context.RouteDecision),
            EpisodicMemoryEnabled: false,
            Origin: ChatContextStateOrigin.ProjectChatResponseCompiler);
    }

    private static IReadOnlyList<RecentChatTurn> BuildRecentTurns(string recentConversationSummary)
    {
        if (string.IsNullOrWhiteSpace(recentConversationSummary))
            return Array.Empty<RecentChatTurn>();

        return recentConversationSummary
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseRecentTurn)
            .Where(turn => turn is not null)
            .Select(turn => turn!)
            .Take(8)
            .ToList();
    }

    private static RecentChatTurn? ParseRecentTurn(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        var separatorIndex = trimmed.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= trimmed.Length - 1)
            return null;

        var role = trimmed[..separatorIndex].Trim();
        var message = trimmed[(separatorIndex + 1)..].Trim();

        return message.Length == 0 ? null : new RecentChatTurn(role, message);
    }

    private static ActiveArtifactContext BuildActiveArtifact(ProjectChatContextPipelineResult context)
    {
        if (context.Decisions.Count > 0)
        {
            var decision = context.Decisions.First();
            return new ActiveArtifactContext(
                ArtifactType: "Decision",
                ArtifactId: decision.Id.ToString(),
                Title: decision.Title,
                Summary: TruncateText(decision.Detail, 240));
        }

        if (context.Tickets.Count > 0)
        {
            var ticket = context.Tickets.First();
            return new ActiveArtifactContext(
                ArtifactType: "Ticket",
                ArtifactId: ticket.Id.ToString(),
                Title: ticket.Title,
                Summary: TruncateText(string.Join(" ", ticket.Summary, ticket.ContextSummary), 240));
        }

        if (context.Documents.Count > 0)
        {
            var document = context.Documents.First();
            return new ActiveArtifactContext(
                ArtifactType: "Document",
                ArtifactId: document.Id.ToString(),
                Title: document.Title,
                Summary: TruncateText(string.Join(" ", document.Summary, document.Content), 240));
        }

        return new ActiveArtifactContext(
            ArtifactType: "Project",
            ArtifactId: context.Project.Id.ToString(),
            Title: context.Project.Name,
            Summary: context.Project.Description);
    }

    private static IReadOnlyList<MemoryEvidence> BuildSemanticEvidence(ProjectChatContextPipelineResult context)
    {
        var evidence = new List<MemoryEvidence>();

        foreach (var decision in context.Decisions.Take(5))
        {
            var currentness = MemoryCurrentnessNormalizer.FromDecisionStatus(decision.Status);
            evidence.Add(new MemoryEvidence(
                SourceId: $"decision-{decision.Id}",
                SourceType: "Decision",
                Title: decision.Title,
                Excerpt: TruncateText(decision.Detail, 260),
                IsCurrent: currentness.IsCurrent,
                RelevanceScore: 0.8,
                AuthorityLevel: MemoryAuthorityNormalizer.FromDecisionStatus(decision.Status),
                UsedFor: "ContextOnly",
                StalenessReason: currentness.StalenessReason,
                SupersededBySourceId: currentness.SupersededBySourceId));
        }

        foreach (var ticket in context.Tickets.Take(5))
        {
            var currentness = MemoryCurrentnessNormalizer.FromTicketState(ticket.Status, ticket.IsDeleted);
            evidence.Add(new MemoryEvidence(
                SourceId: $"ticket-{ticket.Id}",
                SourceType: "Ticket",
                Title: ticket.Title,
                Excerpt: TruncateText(string.Join(" ", ticket.Summary, ticket.Content, ticket.Background, ticket.Problem), 260),
                RelevanceScore: 0.72,
                IsCurrent: currentness.IsCurrent,
                AuthorityLevel: MemoryAuthorityNormalizer.FromTicketState(ticket.IsGenerated, ticket.Status),
                UsedFor: "ContextOnly",
                StalenessReason: currentness.StalenessReason,
                SupersededBySourceId: currentness.SupersededBySourceId));
        }

        foreach (var document in context.Documents.Take(5))
        {
            var currentness = MemoryCurrentnessNormalizer.FromDocumentStatus(document.Status);
            evidence.Add(new MemoryEvidence(
                SourceId: $"document-{document.Id}",
                SourceType: "Document",
                Title: document.Title,
                Excerpt: TruncateText(string.Join(" ", document.Summary, document.Content), 260),
                IsCurrent: currentness.IsCurrent,
                RelevanceScore: 0.7,
                AuthorityLevel: MemoryAuthorityNormalizer.FromDocumentAuthority(document.AuthorityLevel, document.Status),
                UsedFor: "ContextOnly",
                StalenessReason: currentness.StalenessReason,
                SupersededBySourceId: currentness.SupersededBySourceId));
        }

        foreach (var rule in context.Rules.Take(5))
        {
            var currentness = MemoryCurrentnessNormalizer.FromRuleEnforcementLevel(rule.EnforcementLevel);
            evidence.Add(new MemoryEvidence(
                SourceId: $"rule-{rule.Id}",
                SourceType: "Rule",
                Title: rule.Name,
                Excerpt: TruncateText(rule.Description, 260),
                IsCurrent: currentness.IsCurrent,
                RelevanceScore: 0.72,
                AuthorityLevel: MemoryAuthorityNormalizer.FromRuleEnforcementLevel(rule.EnforcementLevel),
                UsedFor: "ContextOnly",
                StalenessReason: currentness.StalenessReason,
                SupersededBySourceId: currentness.SupersededBySourceId));
        }

        foreach (var item in context.SemanticMemoryEvidence.Take(6))
        {
            evidence.Add(item with
            {
                AuthorityLevel = MemoryAuthorityNormalizer.FromSemanticAuthority(item.AuthorityLevel),
                UsedFor = "ContextOnly"
            });
        }

        if (context.RouteDecision.EvidenceUsed.Count > 0)
        {
            var currentness = MemoryCurrentnessNormalizer.RuntimeTrace();
            evidence.Add(new MemoryEvidence(
                SourceId: "route",
                SourceType: "Trace",
                Title: "Route evidence",
                Excerpt: TruncateText(string.Join(" | ", context.RouteDecision.EvidenceUsed), 260),
                IsCurrent: currentness.IsCurrent,
                RelevanceScore: 0.55,
                AuthorityLevel: MemoryAuthorityNormalizer.RuntimeTrace,
                UsedFor: "ContextOnly",
                StalenessReason: currentness.StalenessReason,
                SupersededBySourceId: currentness.SupersededBySourceId));
        }

        return evidence;
    }

    private static IReadOnlyList<AvailableSkillHint> BuildAvailableSkillHints(ContextAgentRouteDecision routeDecision)
    {
        var hints = new List<AvailableSkillHint>();

        if (routeDecision.AllowTicketCreation)
        {
            hints.Add(new AvailableSkillHint(
                SkillId: "CreateTicket",
                DisplayName: "CreateTicket",
                CapabilitySummary: "Ticket capture path is available."));
        }

        if (routeDecision.AllowConflictAssessment)
        {
            hints.Add(new AvailableSkillHint(
                SkillId: "ConflictAssessment",
                DisplayName: "ConflictAssessment",
                CapabilitySummary: "Can run conflict checks when creating ticket-level work."));
        }

        if (routeDecision.AllowDeepLookup)
        {
            hints.Add(new AvailableSkillHint(
                SkillId: "DeepLookup",
                DisplayName: "DeepLookup",
                CapabilitySummary: "Can retrieve additional indexed context for code or document review."));
        }

        if (hints.Count == 0)
        {
            hints.Add(new AvailableSkillHint(
                SkillId: "GeneralDiscussion",
                DisplayName: "GeneralDiscussion",
                CapabilitySummary: "No commit workflow capability advertised from this route."));
        }

        return hints;
    }

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
