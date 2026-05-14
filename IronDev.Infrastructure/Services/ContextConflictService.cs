using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Detects whether a proposed ticket conflicts with, duplicates, or requires
/// coordination with existing project tickets, decisions, or rules.
///
/// Algorithm:
///   1. Detect domain and approaches from the user request (keyword matching).
///   2. Score each existing ticket/decision for overlap with the request domain.
///   3. Classify the relationship and generate clarification questions.
///   4. Return a structured TicketConflictAssessment.
///
/// This is a deterministic, LLM-free implementation. It uses keyword matching
/// and simple scoring rather than a second LLM call. Future versions may add
/// semantic similarity via the code index, but v1 stays cost-free.
/// </summary>
public sealed class ContextConflictService : IContextConflictService
{
    // ── Domain taxonomy ───────────────────────────────────────────────────────

    /// <summary>
    /// Maps a domain tag to the keywords that suggest a ticket is in that domain.
    /// All matching is case-insensitive substring.
    /// </summary>
    private static readonly (string Domain, string[] Keywords)[] DomainTable =
    [
        ("REST authentication",
         ["oauth", "api key", "apikey", "bearer", "basic auth", "jwt", "rest auth",
          "authentication", "rest layer", "rest api", "endpoint auth"]),
        ("ticket management",
         ["ticket", "archive ticket", "soft archive", "delete ticket", "isdeleted"]),
        ("chat / conversation",
         ["chat", "conversation", "session", "chat history", "chat session"]),
        ("code indexing",
         ["index", "code index", "indexing", "project index"]),
        ("implementation plan",
         ["plan", "implementation plan", "planned steps"]),
        ("llm tracing",
         ["trace", "llm console", "llm trace", "tracing"]),
        ("architecture / design",
         ["architecture", "design decision", "pattern", "spike"]),
        ("testing",
         ["test", "unit test", "integration test", "grounding test"]),
        ("ui / ux",
         ["ui", "ux", "view ", "xaml", " ui ", " ui.", " ux.", "IronDeveloperControls", "UI component", "toggle", "button"]),
        ("database / persistence",
         ["database", "sql", "migration", "schema", "dbo.", "table"]),
    ];

    /// <summary>
    /// Technical approach keywords. Maps approach label to its triggers.
    /// </summary>
    private static readonly (string Label, string[] Triggers)[] ApproachTable =
    [
        ("OAuth",                   ["oauth"]),
        ("API key authentication",  ["api key", "apikey", "api-key"]),
        ("JWT authentication",      ["jwt"]),
        ("Basic Auth",              ["basic auth", "basicauth"]),
        ("Bearer token",            ["bearer token", "bearer"]),
        ("soft archive (IsDeleted)", ["soft archive", "isdeleted", "archive ticket"]),
        ("hard delete",             ["hard delete", "permanent delete", "drop ticket"]),
        ("streaming responses",     ["stream", "streaming"]),
        ("Replace approach",        ["replace", "supersede", "instead of", "switch from"]),
    ];

    // ── IContextConflictService ───────────────────────────────────────────────

    public Task<TicketConflictAssessment> AssessAsync(
        ConflictAssessmentContext context,
        CancellationToken         ct = default)
    {
        var request = context.UserRequest;

        // ── 1. Detect domains and approaches in the request ───────────────────
        var requestDomains   = DetectDomains(request);
        var requestApproach  = DetectApproach(request);
        var isReplaceIntent  = IsExplicitReplaceIntent(request);

        // ── 2. Score tickets ──────────────────────────────────────────────────
        var relatedTickets = new List<RelatedTicketMatch>();
        foreach (var ticket in context.RecentTickets.Where(t => !t.IsDeleted))
        {
            var ticketText  = $"{ticket.Title} {ticket.Summary} {ticket.Background} {ticket.TechnicalNotes}";
            var ticketDomains = DetectDomains(ticketText);
            var ticketApproach = DetectApproach(ticketText);

            var domainOverlap = requestDomains.Intersect(ticketDomains, StringComparer.OrdinalIgnoreCase).ToList();
            if (domainOverlap.Count == 0) continue;

            var confidence = Math.Min(0.5 + domainOverlap.Count * 0.15 +
                             (ticketApproach != null && requestApproach != null ? 0.2 : 0.0), 1.0);

            var overlapReason = BuildOverlapReason(domainOverlap, requestApproach, ticketApproach, isReplaceIntent);

            relatedTickets.Add(new RelatedTicketMatch
            {
                TicketId     = ticket.Id,
                Title        = ticket.Title,
                Status       = ticket.Status,
                OverlapReason = overlapReason,
                Confidence   = confidence,
            });
        }

        // ── 3. Score decisions ────────────────────────────────────────────────
        var conflictingDecisions = new List<string>();
        foreach (var decision in context.RecentDecisions)
        {
            var decisionText    = $"{decision.Title} {decision.Detail}";
            var decisionDomains = DetectDomains(decisionText);
            var decisionApproach = DetectApproach(decisionText);

            var domainOverlap = requestDomains.Intersect(decisionDomains, StringComparer.OrdinalIgnoreCase).ToList();
            if (domainOverlap.Count == 0) continue;

            // If the decision mandates an approach that conflicts with the request
            bool isContradictory = requestApproach != null
                                && decisionApproach != null
                                && !string.Equals(requestApproach, decisionApproach, StringComparison.OrdinalIgnoreCase);

            if (isContradictory || domainOverlap.Count > 0)
                conflictingDecisions.Add(
                    $"[Decision #{decision.Id}] \"{decision.Title}\" — {decision.Detail?.Truncate(120)}");
        }

        // ── 4. Score rules ────────────────────────────────────────────────────
        foreach (var rule in context.ProjectRules.Where(
                     r => r.EnforcementLevel is "Required" or "Blocking"))
        {
            var ruleDomains = DetectDomains(rule.Description);
            var ruleApproach = DetectApproach(rule.Description);
            var domainOverlap = requestDomains.Intersect(ruleDomains, StringComparer.OrdinalIgnoreCase).ToList();
            if (domainOverlap.Count == 0) continue;

            bool isContradictory = requestApproach != null
                                && ruleApproach != null
                                && !string.Equals(requestApproach, ruleApproach, StringComparison.OrdinalIgnoreCase);

            if (isContradictory)
                conflictingDecisions.Add(
                    $"[Rule] \"{rule.Name}\" ({rule.EnforcementLevel}) — {rule.Description.Truncate(120)}");
        }

        // ── 5. Classify ───────────────────────────────────────────────────────
        var (classification, recommendedAction, questions) = Classify(
            request, requestApproach, isReplaceIntent,
            relatedTickets, conflictingDecisions);

        var hasConflict = classification is not ConflictClassification.Compatible;

        // Determine existingApproach from highest-confidence related ticket
        var topTicket = relatedTickets.OrderByDescending(t => t.Confidence).FirstOrDefault();
        var existingApproach = topTicket != null
            ? DetectApproach($"{topTicket.Title}")
            : (conflictingDecisions.Count > 0
               ? DetectApproach(conflictingDecisions[0])
               : null);

        var assessment = new TicketConflictAssessment
        {
            HasConflict          = hasConflict,
            Classification       = classification,
            Domain               = string.Join(", ", requestDomains.Take(2)),
            ExistingApproach     = existingApproach ?? string.Empty,
            RequestedApproach    = requestApproach  ?? string.Empty,
            RelatedTickets       = relatedTickets,
            ConflictingDecisions = conflictingDecisions,
            RecommendedAction    = recommendedAction,
            Questions            = questions,
        };

        return Task.FromResult(assessment);
    }

    // ── Classification logic ──────────────────────────────────────────────────

    private static (string classification, string action, IReadOnlyList<string> questions)
        Classify(
            string                     request,
            string?                    requestApproach,
            bool                       isReplaceIntent,
            IReadOnlyList<RelatedTicketMatch> related,
            IReadOnlyList<string>      conflictingDecisions)
    {
        // No related work → compatible, go ahead
        if (related.Count == 0 && conflictingDecisions.Count == 0)
            return (ConflictClassification.Compatible, RecommendedAction.CreateSeparate, []);

        var topMatch = related.OrderByDescending(t => t.Confidence).FirstOrDefault();

        // Explicit replace intent
        if (isReplaceIntent && related.Count > 0)
        {
            var replaceQ = $"You said you want to replace the existing approach. " +
                           $"Do you want to update ticket [{topMatch!.TicketId}] \"{topMatch.Title}\", " +
                           $"archive it and create a replacement, or create a new decision first?";
            return (ConflictClassification.ReplacesExisting, RecommendedAction.ReplaceExisting, [replaceQ]);
        }

        // Decision-level conflict
        if (conflictingDecisions.Count > 0 && related.Count > 0)
        {
            var qs = new List<string>
            {
                $"This conflicts with an existing architecture decision: {conflictingDecisions[0]}",
                "Do you want to revise that decision, create a spike to evaluate options, or cancel this ticket?",
            };
            return (ConflictClassification.Conflicts, RecommendedAction.CreateDecision, qs);
        }

        // Decision conflict but no direct ticket overlap
        if (conflictingDecisions.Count > 0)
        {
            var qs = new List<string>
            {
                $"An existing decision may contradict this request: {conflictingDecisions[0]}",
                "Should we create a spike/decision ticket, or do you want to revise the existing decision first?",
            };
            return (ConflictClassification.NeedsDecision, RecommendedAction.CreateDecision, qs);
        }

        // Top match is high-confidence — check approach conflict
        if (topMatch != null && topMatch.Confidence >= 0.7)
        {
            var topApproach = DetectApproach(topMatch.Title);

            // Different approach in same domain → Conflicts / NeedsDecision
            if (requestApproach != null && topApproach != null
                && !string.Equals(requestApproach, topApproach, StringComparison.OrdinalIgnoreCase))
            {
                var qs = new List<string>
                {
                    $"I found an existing ticket [{topMatch.TicketId}] \"{topMatch.Title}\" proposing \"{topApproach}\" " +
                    $"for the same area. Your request proposes \"{requestApproach}\".",
                    $"Should \"{requestApproach}\" replace \"{topApproach}\", supplement it, or should we create a spike to decide?",
                };
                return (ConflictClassification.Conflicts, RecommendedAction.AskClarification, qs);
            }

            // Same approach in same domain → Duplicate or Overlaps
            if (topMatch.Confidence >= 0.85)
            {
                var qs = new List<string>
                {
                    $"I found an existing ticket [{topMatch.TicketId}] \"{topMatch.Title}\" covering the same area.",
                    "Do you want to update that ticket, or create a separate focused ticket?",
                };
                return (ConflictClassification.Duplicate, RecommendedAction.UpdateExisting, qs);
            }

            var oqs = new List<string>
            {
                $"I found related ticket [{topMatch.TicketId}] \"{topMatch.Title}\".",
                "Is this a separate concern, or should it be linked to that existing ticket?",
            };
            return (ConflictClassification.Overlaps, RecommendedAction.AskClarification, oqs);
        }

        // Low-confidence match — probably compatible in a different area
        if (topMatch != null && topMatch.Confidence >= 0.5)
        {
            var qs = new List<string>
            {
                $"Related ticket [{topMatch.TicketId}] \"{topMatch.Title}\" may be in the same area.",
                "Do you want to create this as a separate ticket, or link it to the existing one?",
            };
            return (ConflictClassification.Compatible, RecommendedAction.CreateSeparate, qs);
        }

        return (ConflictClassification.Compatible, RecommendedAction.CreateSeparate, []);
    }

    // ── Detection helpers (pure / static) ────────────────────────────────────

    public static IReadOnlyList<string> DetectDomains(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var lower = text.ToLowerInvariant();
        foreach (var (domain, keywords) in DomainTable)
            if (keywords.Any(k => lower.Contains(k)))
                result.Add(domain);
        return result;
    }

    public static string? DetectApproach(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lower = text.ToLowerInvariant();
        foreach (var (label, triggers) in ApproachTable)
            if (triggers.Any(t => lower.Contains(t)))
                return label;
        return null;
    }

    public static bool IsExplicitReplaceIntent(string request)
    {
        if (string.IsNullOrWhiteSpace(request)) return false;
        var lower = request.ToLowerInvariant();
        return lower.Contains("replace ") || lower.Contains("switch from ")
            || lower.Contains("instead of ") || lower.Contains("supersede ");
    }

    private static string BuildOverlapReason(
        IReadOnlyList<string> sharedDomains,
        string?               requestApproach,
        string?               ticketApproach,
        bool                  isReplace)
    {
        var domain = string.Join(" and ", sharedDomains.Take(2));

        if (isReplace)
            return $"Both concern {domain}; user intends to replace existing approach.";

        if (requestApproach != null && ticketApproach != null
            && !string.Equals(requestApproach, ticketApproach, StringComparison.OrdinalIgnoreCase))
            return $"Both concern {domain} but propose different approaches ({ticketApproach} vs {requestApproach}).";

        if (requestApproach != null && ticketApproach != null)
            return $"Both propose {requestApproach} for {domain}.";

        return $"Both concern {domain}.";
    }
}

// ── String extension used internally ─────────────────────────────────────────

file static class StringExtensions
{
    public static string? Truncate(this string? s, int maxLength)
        => s == null ? null
         : s.Length <= maxLength ? s
         : s[..maxLength] + "…";
}
