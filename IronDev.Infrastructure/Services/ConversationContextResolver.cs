using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services;

public sealed class ConversationContextResolver : IConversationContextResolver
{
    private static readonly string[] ArchitectureAdviceFollowUps =
    [
        "industry standard",
        "best practice",
        "best practices",
        "best way",
        "recommended approach",
        "standard approach",
        "what's standard",
        "what is standard"
    ];

    private static readonly string[] ConfirmationFollowUps =
    [
        "yes",
        "yep",
        "yeah",
        "ok",
        "okay",
        "sounds good",
        "that one",
        "use that",
        "go with that",
        "do that"
    ];

    private static readonly string[] CodeEvidenceFollowUps =
    [
        "does this already exist",
        "does this exist",
        "is this already implemented",
        "is it already implemented",
        "check this exists",
        "verify this exists"
    ];

    public ConversationContextResolution Resolve(ContextAgentRouteRequest request)
    {
        var snapshot = request.ConversationContextSnapshot
                    ?? TryParseSnapshot(request.RecentConversationSummary)
                    ?? TryInferSnapshot(request);

        var original = request.UserRequest.Trim();
        var lower = Normalize(original);
        if (snapshot is not { HasUsefulState: true })
        {
            if (string.IsNullOrWhiteSpace(request.RecentConversationSummary) &&
                IsAmbiguousContinuation(lower))
            {
                return new ConversationContextResolution
                {
                    OriginalRequest = original,
                    EffectiveRequest = original,
                    ContextMode = "GeneralDiscussion",
                    RequestKind = ContextRequestKind.GeneralChat,
                    NeedsClarification = true,
                    ClarificationQuestions = [$"What topic should I apply '{original}' to?"],
                    Reason = "Short follow-up has no structured active topic."
                };
            }

            return Unresolved(original, snapshot);
        }

        if (IsAny(lower, CodeEvidenceFollowUps))
        {
            var target = BuildPersistenceSubject(snapshot);
            return Resolved(
                original,
                $"Verify whether {target}.",
                "CodeEvidence",
                ContextRequestKind.VerifyImplementation,
                snapshot,
                requiresCodeEvidence: true,
                allowsTicketCreation: false,
                ["ActiveTopic", "LastRecommendation"],
                "Existence follow-up switches to code evidence mode.");
        }

        var createTicketIntent = ChatIntentParser.ParseCreateTicket(original, null);
        if (createTicketIntent != null)
        {
            var work = BuildTicketWorkText(snapshot);
            return Resolved(
                original,
                work,
                "TicketCreation",
                ContextRequestKind.CreateTicket,
                snapshot,
                requiresCodeEvidence: false,
                allowsTicketCreation: true,
                ["ActiveTopic", "PendingDecision", "LastRecommendation", "KnownFacts"],
                "Ticket command inherits the active topic and pending recommendation.");
        }

        if (IsAny(lower, ConfirmationFollowUps))
        {
            var recommendation = FirstUseful(
                ExtractRecommendationFromConfirmation(original),
                snapshot.LastRecommendation,
                snapshot.LastOptionsPresented.FirstOrDefault());
            var verb = lower == "that one" || lower == "use that" || lower == "go with that"
                ? "Select"
                : "Confirm";
            var text = BuildConfirmationText(snapshot, recommendation, verb);

            return Resolved(
                original,
                text,
                "ArchitectureDecision",
                ContextRequestKind.ArchitectureDecisionExploration,
                snapshot,
                requiresCodeEvidence: false,
                allowsTicketCreation: false,
                ["PendingDecision", "LastRecommendation", "LastOptionsPresented"],
                "Short confirmation resolved against pending decision and last recommendation.");
        }

        if (IsAny(lower, ArchitectureAdviceFollowUps))
        {
            var topic = DescribeTopic(snapshot);
            var text = lower.Contains("industry standard")
                ? BuildIndustryStandardQuestion(snapshot)
                : $"What is the recommended architecture approach for {topic}?";

            return Resolved(
                original,
                text,
                "ArchitectureAdvice",
                ContextRequestKind.ArchitectureAdvice,
                snapshot,
                requiresCodeEvidence: false,
                allowsTicketCreation: false,
                ["ActiveTopic", "CurrentGoal", "KnownFacts"],
                "Short architecture follow-up resolved against active topic.");
        }

        return Unresolved(original, snapshot);
    }

    public static ConversationContextSnapshot? TryInferSnapshot(ContextAgentRouteRequest request)
    {
        var conversation = request.RecentConversationSummary ?? string.Empty;
        if (string.IsNullOrWhiteSpace(conversation))
            return null;

        var haystack = $"{conversation}\n{request.UserRequest}".ToLowerInvariant();
        var isPersistenceDiscussion =
            ContainsAny(haystack, "persist", "persistence", "save data", "database", "db", "orm", "dapper", "sql server") &&
            ContainsAny(haystack, "bookseller", "bookservice", "book.cs", "books");

        if (!isPersistenceDiscussion)
            return null;

        var projectName = ContainsAny(haystack, "bookseller", "bookservice", "book.cs")
            ? "BookSeller"
            : "the project";

        var recommendation = ExtractPersistenceRecommendation(request.UserRequest);
        if (string.IsNullOrWhiteSpace(recommendation))
            recommendation = ExtractPersistenceRecommendation(conversation);

        return new ConversationContextSnapshot
        {
            ProjectId = request.ProjectId,
            SessionId = request.SessionId,
            ActiveTopic = $"{projectName} persistence architecture",
            CurrentGoal = "choose database and data access approach",
            ContextMode = "ArchitectureAdvice",
            PendingDecision = "Choose persistence engine and data access style",
            LastRecommendation = recommendation,
            LastOptionsPresented = string.IsNullOrWhiteSpace(recommendation) ? [] : [recommendation],
            KnownFacts =
            [
                $"{projectName} currently has no database",
                $"{projectName} needs persisted book data"
            ]
        };
    }

    public static ConversationContextSnapshot? TryParseSnapshot(string text)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !text.Contains("ConversationContextSnapshot", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var single = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentList = null;

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.Equals("ConversationContextSnapshot:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(currentList))
                    lists.GetOrAdd(currentList).Add(line[2..].Trim());
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (IsListKey(key))
            {
                currentList = key;
                if (!lists.ContainsKey(key))
                    lists[key] = [];

                if (!string.IsNullOrWhiteSpace(value) &&
                    !value.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    lists[key].Add(value);
                }
                continue;
            }

            currentList = null;
            single[key] = value;
        }

        return new ConversationContextSnapshot
        {
            SessionId = ParseLong(single.GetValueOrDefault("SessionId")),
            ProjectId = ParseInt(single.GetValueOrDefault("ProjectId")),
            ActiveTopic = single.GetValueOrDefault("ActiveTopic") ?? string.Empty,
            CurrentGoal = single.GetValueOrDefault("CurrentGoal") ?? string.Empty,
            ContextMode = single.GetValueOrDefault("ContextMode") ?? string.Empty,
            PendingDecision = single.GetValueOrDefault("PendingDecision") ?? string.Empty,
            PendingQuestions = lists.GetValueOrDefault("PendingQuestions") ?? [],
            LastRecommendation = single.GetValueOrDefault("LastRecommendation") ?? string.Empty,
            LastOptionsPresented = lists.GetValueOrDefault("LastOptionsPresented") ?? [],
            KnownFacts = lists.GetValueOrDefault("KnownFacts") ?? [],
            UpdatedUtc = ParseDate(single.GetValueOrDefault("UpdatedUtc"))
        };
    }

    private static ConversationContextResolution Resolved(
        string original,
        string effective,
        string contextMode,
        ContextRequestKind kind,
        ConversationContextSnapshot snapshot,
        bool requiresCodeEvidence,
        bool allowsTicketCreation,
        IReadOnlyList<string> evidenceUsed,
        string reason)
        => new()
        {
            OriginalRequest = original,
            EffectiveRequest = effective,
            ContextMode = contextMode,
            RequestKind = kind,
            IsResolved = true,
            RequiresCodeEvidence = requiresCodeEvidence,
            AllowsTicketCreation = allowsTicketCreation,
            EvidenceUsed = evidenceUsed,
            Reason = reason,
            Snapshot = snapshot
        };

    private static ConversationContextResolution Unresolved(string original, ConversationContextSnapshot? snapshot)
        => new()
        {
            OriginalRequest = original,
            EffectiveRequest = original,
            ContextMode = snapshot?.ContextMode ?? string.Empty,
            Snapshot = snapshot
        };

    private static bool IsListKey(string key)
        => key.Equals("KnownFacts", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("PendingQuestions", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("LastOptionsPresented", StringComparison.OrdinalIgnoreCase);

    private static bool IsAmbiguousContinuation(string lower)
        => IsAny(lower, ArchitectureAdviceFollowUps) ||
           IsAny(lower, ConfirmationFollowUps) ||
           lower is "why" or "how" or "why though" or "how so";

    private static bool IsAny(string lower, IEnumerable<string> phrases)
        => phrases.Any(p => lower == p || lower.TrimEnd('?') == p || lower.StartsWith(p + " "));

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string text)
        => text.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();

    private static string DescribeTopic(ConversationContextSnapshot snapshot)
        => FirstUseful(snapshot.ActiveTopic, snapshot.CurrentGoal, "the active topic");

    private static string BuildPersistenceSubject(ConversationContextSnapshot snapshot)
    {
        var recommendation = FirstUseful(snapshot.LastRecommendation, snapshot.LastOptionsPresented.FirstOrDefault());
        if (IsBookPersistence(snapshot))
        {
            var project = ExtractProjectName(snapshot);
            return string.IsNullOrWhiteSpace(recommendation)
                ? $"{project} has persistence for books already"
                : $"{project} already has {recommendation} persistence for books";
        }

        var topic = DescribeTopic(snapshot);
        return string.IsNullOrWhiteSpace(recommendation)
            ? $"{topic} already exists"
            : $"{recommendation} for {topic} already exists";
    }

    private static string BuildTicketWorkText(ConversationContextSnapshot snapshot)
    {
        var recommendation = FirstUseful(snapshot.LastRecommendation, snapshot.LastOptionsPresented.FirstOrDefault());
        if (IsBookPersistence(snapshot) && !string.IsNullOrWhiteSpace(recommendation))
            return $"add {recommendation} persistence for {ExtractProjectName(snapshot)} books.";

        var topic = DescribeTopic(snapshot);
        if (!string.IsNullOrWhiteSpace(recommendation))
            return $"add {recommendation} for {topic}.";
        if (!string.IsNullOrWhiteSpace(snapshot.PendingDecision))
            return snapshot.PendingDecision;
        return topic;
    }

    private static string BuildIndustryStandardQuestion(ConversationContextSnapshot snapshot)
    {
        if (IsBookPersistence(snapshot))
            return $"What is the industry-standard persistence approach for {ExtractProjectName(snapshot)}?";

        return $"What is the industry-standard approach for {DescribeTopic(snapshot)}?";
    }

    private static string BuildConfirmationText(
        ConversationContextSnapshot snapshot,
        string recommendation,
        string verb)
    {
        if (!string.IsNullOrWhiteSpace(recommendation) && IsBookPersistence(snapshot))
        {
            var project = ExtractProjectName(snapshot);
            return verb == "Select"
                ? $"Select {recommendation} from the last persistence options presented for {project}."
                : $"Confirm {recommendation} as the persistence recommendation for {project}.";
        }

        if (!string.IsNullOrWhiteSpace(recommendation))
            return $"{verb} {recommendation} for {DescribeTopic(snapshot)}.";

        return $"{verb} the pending decision for {DescribeTopic(snapshot)}.";
    }

    private static string ExtractRecommendationFromConfirmation(string text)
    {
        var recommendation = ExtractPersistenceRecommendation(text);
        if (!string.IsNullOrWhiteSpace(recommendation))
            return recommendation;

        var lower = text.ToLowerInvariant();
        foreach (var marker in new[] { "i will use ", "i'll use ", "use " })
        {
            var index = lower.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
                return NormalizeRecommendation(text[(index + marker.Length)..]);
        }

        return string.Empty;
    }

    private static string ExtractPersistenceRecommendation(string text)
    {
        var lower = text.ToLowerInvariant();
        var hasDapper = lower.Contains("dapper", StringComparison.OrdinalIgnoreCase);
        if (!hasDapper)
            return string.Empty;

        if (lower.Contains("sql server", StringComparison.OrdinalIgnoreCase))
            return "SQL Server + Dapper";
        if (lower.Contains("sqlite", StringComparison.OrdinalIgnoreCase))
            return "SQLite + Dapper";
        if (lower.Contains("postgres", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("postgresql", StringComparison.OrdinalIgnoreCase))
            return "PostgreSQL + Dapper";

        return "Dapper";
    }

    private static string NormalizeRecommendation(string value)
    {
        var cleaned = value.Trim().TrimEnd('.', '!', '?');
        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        return cleaned
            .Replace(" and ", " + ", StringComparison.OrdinalIgnoreCase)
            .Replace("  ", " ")
            .Trim();
    }

    private static bool IsBookPersistence(ConversationContextSnapshot snapshot)
    {
        var haystack = string.Join(" ", new[]
        {
            snapshot.ActiveTopic,
            snapshot.CurrentGoal,
            snapshot.PendingDecision,
            snapshot.LastRecommendation,
            string.Join(" ", snapshot.KnownFacts)
        }).ToLowerInvariant();

        return haystack.Contains("bookseller") &&
               (haystack.Contains("persist") || haystack.Contains("database") || haystack.Contains("storage"));
    }

    private static string ExtractProjectName(ConversationContextSnapshot snapshot)
    {
        var topic = DescribeTopic(snapshot);
        var first = topic.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? "the project" : first;
    }

    private static string FirstUseful(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static long ParseLong(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static int ParseInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
}

internal static class DictionaryExtensions
{
    public static List<string> GetOrAdd(this Dictionary<string, List<string>> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var value))
        {
            value = [];
            dictionary[key] = value;
        }

        return value;
    }
}
