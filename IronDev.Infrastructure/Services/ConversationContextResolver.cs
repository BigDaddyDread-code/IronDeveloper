using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
        "what is standard",
        "what do you recommend",
        "what do yo recommend",
        "what would you recommend",
        "what should i use",
        "which should i use"
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

    private static readonly string[] ArchitectureAddFollowUps =
    [
        "add that architecture",
        "add that artecture",
        "add this architecture",
        "add this artecture",
        "capture that architecture",
        "capture this architecture",
        "record that architecture",
        "record this architecture"
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

        if (IsAny(lower, ArchitectureAddFollowUps))
        {
            var recommendation = FirstUseful(
                snapshot.LastRecommendation,
                snapshot.LastOptionsPresented.LastOrDefault());

            return Resolved(
                original,
                BuildArchitectureAddText(snapshot, recommendation),
                "ArchitectureDecision",
                ContextRequestKind.ArchitectureDecisionExploration,
                snapshot,
                requiresCodeEvidence: false,
                allowsTicketCreation: false,
                ["ActiveTopic", "PendingDecision", "LastRecommendation", "LastOptionsPresented"],
                "Architecture add follow-up resolved against latest topical target.");
        }

        if (IsAny(lower, ConfirmationFollowUps))
        {
            if (IsShortAffirmation(lower) &&
                !LastAssistantAskedForGovernanceCommitment(request.RecentConversationSummary))
            {
                return Resolved(
                    original,
                    BuildExplorationContinuationText(snapshot),
                    "Exploration",
                    ContextRequestKind.GeneralChat,
                    snapshot,
                    requiresCodeEvidence: false,
                    allowsTicketCreation: false,
                    ["TopicKind", "ActiveTopic", "LastOptionsPresented"],
                    "Short affirmation resolved against the active non-governance topic.");
            }

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
                : BuildRecommendationQuestion(snapshot, topic);

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
        return TryInferTopicalSnapshot(request, conversation, haystack);
    }

    private static ConversationContextSnapshot? TryInferTopicalSnapshot(
        ContextAgentRouteRequest request,
        string conversation,
        string haystack)
    {
        var projectName = InferProjectName(conversation);

        if (IsStorageChoice(haystack))
        {
            var options = ExtractStorageOptions(haystack);
            var recommendation = ExtractStorageRecommendation(haystack);
            return new ConversationContextSnapshot
            {
                ProjectId = request.ProjectId,
                SessionId = request.SessionId,
                TopicKind = ConversationTopicKind.StorageChoice,
                ActiveTopic = $"{projectName} storage architecture",
                CurrentGoal = "choose storage approach",
                ContextMode = "ArchitectureAdvice",
                PendingDecision = "Choose storage engine and data access style",
                LastRecommendation = recommendation,
                LastOptionsPresented = options,
                KnownFacts =
                [
                    $"{projectName} is the active project idea",
                    "The latest topical target is storage choice"
                ]
            };
        }

        if (IsPlatformChoice(haystack))
        {
            return new ConversationContextSnapshot
            {
                ProjectId = request.ProjectId,
                SessionId = request.SessionId,
                TopicKind = ConversationTopicKind.PlatformChoice,
                ActiveTopic = $"{projectName} platform choice",
                CurrentGoal = "choose first implementation platform",
                ContextMode = "ArchitectureAdvice",
                PendingDecision = "Choose platform/runtime for the first playable version",
                LastOptionsPresented = ExtractPlatformOptions(haystack),
                KnownFacts =
                [
                    $"{projectName} is the active project idea",
                    "The latest topical target is platform choice"
                ]
            };
        }

        if (ContainsAny(haystack, "rules of the game", "game rules", "rules for"))
        {
            return new ConversationContextSnapshot
            {
                ProjectId = request.ProjectId,
                SessionId = request.SessionId,
                TopicKind = ConversationTopicKind.GameRules,
                ActiveTopic = $"{projectName} game rules",
                CurrentGoal = "capture and refine the game rules",
                ContextMode = "ArchitectureAdvice",
                PendingDecision = "Decide the game rules to preserve",
                KnownFacts =
                [
                    $"{projectName} is the active project idea",
                    "The latest topical target is game rules"
                ]
            };
        }

        if (ContainsAny(haystack, "first slice", "what slice", "smallest playable", "slice 1"))
        {
            return new ConversationContextSnapshot
            {
                ProjectId = request.ProjectId,
                SessionId = request.SessionId,
                TopicKind = ConversationTopicKind.FirstSlice,
                ActiveTopic = $"{projectName} first playable slice",
                CurrentGoal = "choose the first playable slice",
                ContextMode = "ArchitectureAdvice",
                PendingDecision = "Choose the first playable slice",
                KnownFacts =
                [
                    $"{projectName} is the active project idea",
                    "The latest topical target is first slice"
                ]
            };
        }

        return null;
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
            TopicKind = ParseTopicKind(single.GetValueOrDefault("TopicKind")),
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

    private static bool IsShortAffirmation(string lower)
        => lower is "yes" or "y" or "yep" or "yeah" or "ok" or "okay" or "sounds good";

    private static bool LastAssistantAskedForGovernanceCommitment(string recentConversationSummary)
    {
        if (string.IsNullOrWhiteSpace(recentConversationSummary))
            return false;

        var lastAssistant = recentConversationSummary
            .Replace("\r\n", "\n")
            .Split('\n')
            .LastOrDefault(line => line.TrimStart().StartsWith("assistant:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(lastAssistant))
            return false;

        var normalized = lastAssistant.ToLowerInvariant();
        return normalized.Contains("save this", StringComparison.Ordinal) ||
               normalized.Contains("record this", StringComparison.Ordinal) ||
               normalized.Contains("create a ticket", StringComparison.Ordinal) ||
               normalized.Contains("turn this into", StringComparison.Ordinal) ||
               normalized.Contains("commit", StringComparison.Ordinal) ||
               normalized.Contains("architecture decision", StringComparison.Ordinal);
    }

    private static bool ContainsAny(string text, params string[] terms)
        => terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string text)
        => text.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();

    private static string DescribeTopic(ConversationContextSnapshot snapshot)
        => FirstUseful(snapshot.ActiveTopic, snapshot.CurrentGoal, "the active topic");

    private static string BuildPersistenceSubject(ConversationContextSnapshot snapshot)
    {
        var recommendation = FirstUseful(snapshot.LastRecommendation, snapshot.LastOptionsPresented.FirstOrDefault());
        var topic = DescribeTopic(snapshot);
        return string.IsNullOrWhiteSpace(recommendation)
            ? $"{topic} already exists"
            : $"{recommendation} for {topic} already exists";
    }

    private static string BuildTicketWorkText(ConversationContextSnapshot snapshot)
    {
        var recommendation = FirstUseful(snapshot.LastRecommendation, snapshot.LastOptionsPresented.FirstOrDefault());
        var topic = DescribeTopic(snapshot);
        if (!string.IsNullOrWhiteSpace(recommendation))
            return $"add {recommendation} for {topic}.";
        if (!string.IsNullOrWhiteSpace(snapshot.PendingDecision))
            return snapshot.PendingDecision;
        return topic;
    }

    private static string BuildIndustryStandardQuestion(ConversationContextSnapshot snapshot)
    {
        return $"What is the industry-standard approach for {DescribeTopic(snapshot)}?";
    }

    private static string BuildRecommendationQuestion(ConversationContextSnapshot snapshot, string topic)
    {
        if ((IsStorageSnapshot(snapshot) || IsPlatformSnapshot(snapshot)) &&
            snapshot.LastOptionsPresented.Count > 0)
        {
            return $"Recommend between {string.Join(" and ", snapshot.LastOptionsPresented)} for {topic}.";
        }

        return $"What is the recommended architecture approach for {topic}?";
    }

    private static string BuildConfirmationText(
        ConversationContextSnapshot snapshot,
        string recommendation,
        string verb)
    {
        if (!string.IsNullOrWhiteSpace(recommendation))
            return $"{verb} {recommendation} for {DescribeTopic(snapshot)}.";

        return $"{verb} the pending decision for {DescribeTopic(snapshot)}.";
    }

    private static string BuildExplorationContinuationText(ConversationContextSnapshot snapshot)
    {
        var selected = FirstUseful(snapshot.LastRecommendation, snapshot.LastOptionsPresented.LastOrDefault());
        var topic = DescribeTopic(snapshot);
        return string.IsNullOrWhiteSpace(selected)
            ? $"Continue exploring {topic}."
            : $"Continue with {selected} for {topic}.";
    }

    private static string BuildArchitectureAddText(ConversationContextSnapshot snapshot, string recommendation)
    {
        var topic = DescribeTopic(snapshot);
        if (!string.IsNullOrWhiteSpace(recommendation))
            return $"Add {recommendation} as the architecture decision for {topic}.";

        if (!string.IsNullOrWhiteSpace(snapshot.PendingDecision))
            return $"Add the pending architecture decision for {topic}: {snapshot.PendingDecision}.";

        return $"Add the architecture decision for {topic}.";
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

    private static bool IsStorageChoice(string haystack)
        => ContainsAny(haystack, "json or sql server", "sql server", "entity framework", "ef core", "database", "storage");

    private static bool IsPlatformChoice(string haystack)
        => ContainsAny(haystack, "web or forms", "winforms", "windows forms", "web app", "desktop app");

    private static bool IsStorageSnapshot(ConversationContextSnapshot snapshot)
        => snapshot.TopicKind == ConversationTopicKind.StorageChoice ||
           snapshot.TopicKind == ConversationTopicKind.ArchitectureChoice ||
           ContainsAny(
            $"{snapshot.ActiveTopic} {snapshot.CurrentGoal} {snapshot.PendingDecision}",
            "storage",
            "database",
            "persistence");

    private static bool IsPlatformSnapshot(ConversationContextSnapshot snapshot)
        => snapshot.TopicKind == ConversationTopicKind.PlatformChoice ||
           ContainsAny(
            $"{snapshot.ActiveTopic} {snapshot.CurrentGoal} {snapshot.PendingDecision}",
            "platform",
            "runtime",
            "winforms",
            "web");

    private static IReadOnlyList<string> ExtractStorageOptions(string haystack)
    {
        var options = new List<string>();
        AddIfPresent(options, haystack, "json", "JSON");
        AddIfPresent(options, haystack, "sql server", "SQL Server");
        AddIfPresent(options, haystack, "entity framework", "Entity Framework");
        AddIfPresent(options, haystack, "ef core", "EF Core");
        AddIfPresent(options, haystack, "sqlite", "SQLite");
        AddIfPresent(options, haystack, "postgres", "PostgreSQL");
        return CoalesceStorageOptions(options);
    }

    private static IReadOnlyList<string> ExtractPlatformOptions(string haystack)
    {
        var options = new List<string>();
        AddIfPresent(options, haystack, "web", "Web");
        AddIfPresent(options, haystack, "forms", "WinForms");
        AddIfPresent(options, haystack, "winforms", "WinForms");
        AddIfPresent(options, haystack, "windows forms", "WinForms");
        AddIfPresent(options, haystack, "desktop", "Desktop");
        return options.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> CoalesceStorageOptions(IReadOnlyList<string> options)
    {
        var normalized = options.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (normalized.Contains("SQL Server", StringComparer.OrdinalIgnoreCase) &&
            normalized.Contains("Entity Framework", StringComparer.OrdinalIgnoreCase))
        {
            normalized.RemoveAll(option => option.Equals("SQL Server", StringComparison.OrdinalIgnoreCase) ||
                                           option.Equals("Entity Framework", StringComparison.OrdinalIgnoreCase));
            normalized.Add("SQL Server + Entity Framework");
        }

        return normalized;
    }

    private static string ExtractStorageRecommendation(string haystack)
    {
        if (haystack.Contains("sql server", StringComparison.OrdinalIgnoreCase) &&
            (haystack.Contains("entity framework", StringComparison.OrdinalIgnoreCase) ||
             haystack.Contains("ef core", StringComparison.OrdinalIgnoreCase)))
        {
            return "SQL Server + Entity Framework";
        }

        if (haystack.Contains("sql server", StringComparison.OrdinalIgnoreCase) &&
            haystack.Contains("i will", StringComparison.OrdinalIgnoreCase))
        {
            return "SQL Server";
        }

        return string.Empty;
    }

    private static string InferProjectName(string conversation)
    {
        var projectMatch = Regex.Match(
            conversation,
            @"(?<name>[A-Z][A-Za-z0-9]+)\s+project",
            RegexOptions.CultureInvariant);

        if (projectMatch.Success)
            return projectMatch.Groups["name"].Value.Trim();

        var match = Regex.Match(
            conversation,
            @"build\s+(?:a|an)?\s*(?<name>[a-z0-9 '\-]+?game)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (match.Success)
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(match.Groups["name"].Value.Trim().ToLowerInvariant());

        return "the current project";
    }

    private static void AddIfPresent(List<string> options, string haystack, string needle, string label)
    {
        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            options.Add(label);
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

    private static string FirstUseful(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static ConversationTopicKind ParseTopicKind(string? value)
        => Enum.TryParse(value, ignoreCase: true, out ConversationTopicKind parsed) &&
           Enum.IsDefined(parsed)
            ? parsed
            : ConversationTopicKind.Unknown;

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
