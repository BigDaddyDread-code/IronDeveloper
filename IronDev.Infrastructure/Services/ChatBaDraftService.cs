using System.Globalization;
using System.Text.RegularExpressions;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class ChatBaDraftService : IChatBaDraftService
{
    private const int SourceMessageLimit = 20;

    private readonly IChatHistoryService _chatHistory;

    public ChatBaDraftService(IChatHistoryService chatHistory)
    {
        _chatHistory = chatHistory;
    }

    public async Task<BaWorkingDraft?> BuildAsync(
        ChatBaDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldShape(request))
            return null;

        var sourceTurns = await LoadSourceTurnsAsync(request, cancellationToken).ConfigureAwait(false);
        if (sourceTurns.Count == 0)
            sourceTurns.Add(new BaSourceTurn(null, request.Prompt));

        var allText = string.Join(Environment.NewLine, sourceTurns.Select(turn => turn.Text));
        var title = DeriveTitle(sourceTurns, request.Prompt);
        var rules = ExtractBusinessRules(allText, title);
        var assumptions = ExtractAssumptions(allText);
        var conflicts = ExtractPotentialConflicts(allText);
        var openQuestions = RankOpenQuestions(allText, rules, conflicts);
        var criteria = BuildAcceptanceCriteria(allText, rules);
        var confidence = CalculateConfidence(sourceTurns.Count, rules.Count, criteria.Count, conflicts.Count);

        return new BaWorkingDraft
        {
            CandidateTitle = title,
            Problem = DeriveProblem(allText, title),
            ProposedChange = DeriveProposedChange(allText, title),
            BusinessRules = rules,
            AcceptanceCriteria = criteria,
            Assumptions = assumptions,
            OpenQuestions = openQuestions,
            SourceMessageIds = sourceTurns
                .Where(turn => turn.Id.HasValue)
                .Select(turn => turn.Id!.Value.ToString(CultureInfo.InvariantCulture))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Confidence = confidence,
            ReadyForConfirmation = confidence >= 0.7 && rules.Count > 0 && criteria.Count > 0 && conflicts.Count == 0,
            PotentialConflicts = conflicts,
            SuggestedArtifact = SuggestArtifact(request.EffectiveRoute, rules)
        };
    }

    private async Task<List<BaSourceTurn>> LoadSourceTurnsAsync(
        ChatBaDraftRequest request,
        CancellationToken cancellationToken)
    {
        var turns = new List<BaSourceTurn>();
        if (request.SessionId is > 0)
        {
            var messages = await _chatHistory.GetRecentMessagesAsync(
                request.ProjectId,
                request.SessionId.Value,
                SourceMessageLimit,
                cancellationToken).ConfigureAwait(false);

            turns.AddRange(messages
                .Where(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
                .Where(message => !string.IsNullOrWhiteSpace(message.Message))
                .Select(message => new BaSourceTurn(message.Id, message.Message.Trim())));
        }

        if (!turns.Any(turn => string.Equals(turn.Text, request.Prompt, StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(request.Prompt))
        {
            turns.Add(new BaSourceTurn(null, request.Prompt.Trim()));
        }

        return turns
            .Where(turn => !IsNonSubstantive(turn.Text))
            .ToList();
    }

    private static bool ShouldShape(ChatBaDraftRequest request)
    {
        var route = request.EffectiveRoute;
        if (route.AllowsTicketDrafting)
            return true;

        if (route.Mode == ChatGovernanceMode.Formalization &&
            route.RequestKind is ContextRequestKind.CreateTicket
                or ContextRequestKind.CreateTicketsFromDiscussion
                or ContextRequestKind.BuildTicket)
        {
            return true;
        }

        return HasBaSignals(request.Prompt) || HasBaSignals(request.RecentConversationSummary);
    }

    private static bool HasBaSignals(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.ToLowerInvariant();
        return lower.StartsWith("need ", StringComparison.Ordinal) ||
            lower.StartsWith("needs ", StringComparison.Ordinal) ||
            lower.Contains(" need ", StringComparison.Ordinal) ||
            lower.Contains(" needs ", StringComparison.Ordinal) ||
            lower.Contains("should ", StringComparison.Ordinal) ||
            lower.Contains("must ", StringComparison.Ordinal) ||
            lower.Contains("only after", StringComparison.Ordinal) ||
            lower.Contains("not delivered", StringComparison.Ordinal) ||
            lower.Contains("delivered", StringComparison.Ordinal) ||
            lower.Contains("audit", StringComparison.Ordinal) ||
            lower.Contains("terminal", StringComparison.Ordinal) ||
            lower.Contains("include ", StringComparison.Ordinal) ||
            lower.Contains("acceptance criteria", StringComparison.Ordinal) ||
            lower.Contains("business rule", StringComparison.Ordinal) ||
            lower.Contains("work item", StringComparison.Ordinal) ||
            lower.Contains("ticket", StringComparison.Ordinal);
    }

    private static string DeriveTitle(IReadOnlyList<BaSourceTurn> sourceTurns, string prompt)
    {
        var explicitTitle = ExtractExplicitTitle(prompt);
        if (!string.IsNullOrWhiteSpace(explicitTitle))
            return NormalizeTitle(explicitTitle);

        var allText = string.Join(" ", sourceTurns.Select(turn => turn.Text));
        if (ContainsParcelLost(allText))
            return "Parcels can be marked Lost";

        var seed = sourceTurns
            .Select(turn => turn.Text)
            .FirstOrDefault(text => text.Length > 20) ?? prompt;

        seed = FirstClause(seed)
            .Replace("we need to ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("we need ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("need to ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("need ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("please ", "", StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '.', ',', ';', ':');

        if (string.IsNullOrWhiteSpace(seed))
            return "Candidate work item";

        return NormalizeTitle(seed);
    }

    private static string? ExtractExplicitTitle(string prompt)
    {
        var match = Regex.Match(
            prompt,
            @"\b(?:ticket|work item)\s+(?:is\s+)?(?:titled|title\s*:)\s*(?<title>[^.;\r\n]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["title"].Value.Trim(' ', '\'', '"')
            : null;
    }

    private static string NormalizeTitle(string title)
    {
        var trimmed = title.Trim();
        return Capitalize(trimmed.Length > 80 ? trimmed[..77].TrimEnd() + "..." : trimmed);
    }

    private static string DeriveProblem(string allText, string title)
    {
        if (ContainsParcelLost(allText))
            return "Parcels need a controlled way to be marked Lost without including delivered parcels or bypassing audit expectations.";

        return $"The conversation is describing a candidate work item: {title}.";
    }

    private static string DeriveProposedChange(string allText, string title)
    {
        if (ContainsParcelLost(allText))
            return "Add a Lost parcel state transition with explicit status rules, terminal behavior, and audit handling.";

        return $"Shape and confirm the work needed for {title}.";
    }

    private static IReadOnlyList<string> ExtractBusinessRules(string allText, string title)
    {
        var rules = new List<string>();
        var lower = allText.ToLowerInvariant();

        if (ContainsParcelLost(allText))
        {
            if (lower.Contains("marked lost", StringComparison.Ordinal) ||
                lower.Contains("mark", StringComparison.Ordinal))
            {
                rules.Add("Parcels can be marked Lost.");
            }

            if (lower.Contains("after dispatch", StringComparison.Ordinal) ||
                lower.Contains("dispatched", StringComparison.Ordinal))
            {
                rules.Add("Only dispatched parcels can be marked Lost.");
            }

            if (lower.Contains("not delivered", StringComparison.Ordinal) ||
                lower.Contains("delivered cannot", StringComparison.Ordinal) ||
                lower.Contains("delivered parcels cannot", StringComparison.Ordinal) ||
                lower.Contains("delivered can't", StringComparison.Ordinal))
            {
                rules.Add("Delivered parcels cannot be marked Lost.");
            }

            if (lower.Contains("terminal", StringComparison.Ordinal))
                rules.Add("Lost parcels are terminal.");

            if (lower.Contains("audit", StringComparison.Ordinal))
                rules.Add("Marking a parcel Lost should be audited.");
        }

        foreach (var clause in SplitClauses(allText))
        {
            if (!LooksLikeRule(clause))
                continue;

            var normalized = NormalizeRule(clause);
            if (!rules.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                rules.Add(normalized);
        }

        if (rules.Count == 0 && !string.IsNullOrWhiteSpace(title))
            rules.Add($"{title} needs explicit confirmation before it becomes implementation work.");

        return rules.Take(8).ToArray();
    }

    private static IReadOnlyList<string> BuildAcceptanceCriteria(string allText, IReadOnlyList<string> rules)
    {
        var criteria = new List<string>();
        var lower = allText.ToLowerInvariant();

        if (ContainsParcelLost(allText))
        {
            if (rules.Any(rule => rule.Contains("dispatched", StringComparison.OrdinalIgnoreCase)))
            {
                criteria.Add("InTransit parcels can be marked Lost.");
                criteria.Add("OutForDelivery parcels can be marked Lost.");
            }

            if (rules.Any(rule => rule.Contains("Delivered parcels cannot", StringComparison.OrdinalIgnoreCase)))
                criteria.Add("Delivered parcels cannot be marked Lost.");

            if (rules.Any(rule => rule.Contains("terminal", StringComparison.OrdinalIgnoreCase)))
                criteria.Add("Lost parcels cannot transition back to active statuses.");

            if (lower.Contains("audit", StringComparison.Ordinal))
                criteria.Add("Marking a parcel Lost records audit evidence.");
        }

        foreach (var rule in rules)
        {
            if (criteria.Any(item => string.Equals(item, rule, StringComparison.OrdinalIgnoreCase)))
                continue;

            criteria.Add($"System enforces: {TrimSentence(rule)}.");
        }

        return criteria.Take(8).ToArray();
    }

    private static IReadOnlyList<string> ExtractAssumptions(string allText)
    {
        return SplitClauses(allText)
            .Where(clause =>
                clause.Contains("probably", StringComparison.OrdinalIgnoreCase) ||
                clause.Contains("maybe", StringComparison.OrdinalIgnoreCase) ||
                clause.Contains("assume", StringComparison.OrdinalIgnoreCase))
            .Select(clause => $"Assumption: {NormalizeRule(clause)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractPotentialConflicts(string allText)
    {
        var lower = allText.ToLowerInvariant();
        var conflicts = new List<string>();
        var deliveredExcluded = lower.Contains("not delivered", StringComparison.Ordinal) ||
            lower.Contains("delivered cannot", StringComparison.Ordinal) ||
            lower.Contains("delivered parcels cannot", StringComparison.Ordinal) ||
            lower.Contains("delivered can't", StringComparison.Ordinal);
        var deliveredIncluded = lower.Contains("include delivered", StringComparison.Ordinal) ||
            lower.Contains("delivered can", StringComparison.Ordinal) ||
            lower.Contains("delivered may", StringComparison.Ordinal) ||
            lower.Contains("delivered too", StringComparison.Ordinal);

        if (deliveredExcluded && deliveredIncluded)
        {
            conflicts.Add(
                "Earlier messages excluded delivered parcels, but a later message sounds like delivered parcels may be included.");
        }

        return conflicts;
    }

    private static IReadOnlyList<string> RankOpenQuestions(
        string allText,
        IReadOnlyList<string> rules,
        IReadOnlyList<string> conflicts)
    {
        if (conflicts.Count > 0)
            return ["Which delivered-parcel rule is correct?"];

        var lower = allText.ToLowerInvariant();
        if (ContainsParcelLost(allText) && lower.Contains("audit", StringComparison.Ordinal))
            return ["Should marking a parcel Lost require a reason/comment?"];

        if (rules.Count == 0)
            return ["What business rule should this draft enforce first?"];

        return ["Should this be confirmed as a ticket, decision, or supporting documentation?"];
    }

    private static double CalculateConfidence(int sourceTurnCount, int ruleCount, int criteriaCount, int conflictCount)
    {
        var score = 0.45 + Math.Min(ruleCount, 4) * 0.08 + Math.Min(criteriaCount, 4) * 0.05 + Math.Min(sourceTurnCount, 4) * 0.03;
        if (conflictCount > 0)
            score -= 0.2;

        return Math.Clamp(Math.Round(score, 2), 0.1, 0.95);
    }

    private static string SuggestArtifact(EffectiveChatRoute route, IReadOnlyList<string> rules)
    {
        if (route.RequestKind == ContextRequestKind.ArchitectureDecisionExploration)
            return "Decision";

        if ((route.RequestKind is ContextRequestKind.CreateTicket
            or ContextRequestKind.CreateTicketsFromDiscussion
            or ContextRequestKind.BuildTicket) ||
            rules.Count > 0)
        {
            return "Ticket";
        }

        return "SupportingDocumentation";
    }

    private static bool ContainsParcelLost(string text)
    {
        var lower = text.ToLowerInvariant();
        return (lower.Contains("parcel", StringComparison.Ordinal) ||
                lower.Contains("parcels", StringComparison.Ordinal)) &&
            lower.Contains("lost", StringComparison.Ordinal);
    }

    private static bool LooksLikeRule(string clause)
    {
        var lower = clause.ToLowerInvariant();
        return lower.Contains("only ", StringComparison.Ordinal) ||
            lower.Contains("cannot", StringComparison.Ordinal) ||
            lower.Contains("can't", StringComparison.Ordinal) ||
            lower.Contains("not ", StringComparison.Ordinal) ||
            lower.Contains("must", StringComparison.Ordinal) ||
            lower.Contains("should", StringComparison.Ordinal) ||
            lower.Contains("needs audit", StringComparison.Ordinal) ||
            lower.Contains("terminal", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> SplitClauses(string text) =>
        text.Split(['\r', '\n', '.', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(RemoveSpeakerPrefix)
            .Where(clause => clause.Length > 0)
            .ToArray();

    private static string FirstClause(string text) =>
        SplitClauses(text).FirstOrDefault() ?? text.Trim();

    private static string NormalizeRule(string clause)
    {
        var normalized = RemoveSpeakerPrefix(clause)
            .Replace("probably ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("maybe ", "", StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '.', ',', ';', ':');

        return $"{Capitalize(TrimSentence(normalized))}.";
    }

    private static string RemoveSpeakerPrefix(string text)
    {
        var trimmed = text.Trim();
        var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (separator > 0 && separator <= 12)
        {
            var prefix = trimmed[..separator];
            if (prefix.Equals("user", StringComparison.OrdinalIgnoreCase) ||
                prefix.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(separator + 1)..].Trim();
            }
        }

        return trimmed;
    }

    private static string TrimSentence(string text) =>
        text.Trim().TrimEnd('.', ',', ';', ':');

    private static string Capitalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var trimmed = text.Trim();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    private static bool IsNonSubstantive(string text)
    {
        var lower = text.Trim().ToLowerInvariant();
        return lower is "ok" or "okay" or "yes" or "no" or "thanks" or "thank you";
    }

    private sealed record BaSourceTurn(long? Id, string Text);
}
