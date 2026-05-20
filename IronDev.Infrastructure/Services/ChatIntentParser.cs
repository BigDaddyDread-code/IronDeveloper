using System;
using System.Collections.Generic;
using System.Linq;
using IronDev.Core.Models;
using IronDev.Data.Models;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Parses chat intents, specifically for detecting ticket creation
/// without polluting domain/conflict assessment with generic commands.
/// </summary>
public static class ChatIntentParser
{
    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5
    };

    private static readonly string[] CreateTicketCommands =
    [
        "create a ticket to ",
        "create a ticket for ",
        "create a ticket ",
        "make a ticket to ",
        "make a ticket for ",
        "make a ticket ",
        "raise a ticket to ",
        "raise a ticket for ",
        "raise a ticket ",
        "turn this into a ticket",
        "create task to ",
        "create task for ",
        "create task ",
        "add this as a ticket",
        "ticket this"
    ];

    public static CreateTicketIntent? ParseCreateTicket(string request, string? previousMessage = null)
    {
        if (string.IsNullOrWhiteSpace(request)) return null;

        var lower = request.ToLowerInvariant().Trim();

        var splitIntent = ParseSplitTickets(request, previousMessage);
        if (splitIntent != null) return splitIntent;

        foreach (var cmd in CreateTicketCommands)
        {
            if (lower.StartsWith(cmd) || lower == cmd.TrimEnd())
            {
                var workText = string.Empty;
                if (lower.Length > cmd.Length)
                {
                    workText = request.Substring(cmd.Length).Trim();
                }
                
                // If it's just "ticket this" or "turn this into a ticket", use previous message
                if (string.IsNullOrWhiteSpace(workText) && !string.IsNullOrWhiteSpace(previousMessage))
                {
                    workText = previousMessage;
                    return new CreateTicketIntent
                    {
                        Intent = "CreateTicket",
                        Confidence = 1.0,
                        CommandText = request, // The whole request is the command
                        WorkText = workText,
                        TicketCount = 1,
                        RequiresClarification = false
                    };
                }

                // Normal extraction
                if (!string.IsNullOrWhiteSpace(workText))
                {
                    // Find original case command text
                    var originalCmd = request.Substring(0, cmd.Length);
                    return new CreateTicketIntent
                    {
                        Intent = "CreateTicket",
                        Confidence = 1.0,
                        CommandText = originalCmd.Trim(),
                        WorkText = workText,
                        TicketCount = 1,
                        RequiresClarification = false
                    };
                }

                return new CreateTicketIntent
                {
                    Intent = "CreateTicket",
                    Confidence = 1.0,
                    CommandText = request,
                    WorkText = string.Empty,
                    TicketCount = 1,
                    RequiresClarification = true,
                    ClarificationQuestions = ["What should the ticket cover?"]
                };
            }
        }

        return null;
    }

    private static CreateTicketIntent? ParseSplitTickets(string request, string? previousMessage)
    {
        var text = request.Trim();
        var lower = text.ToLowerInvariant();

        var isSplitCommand =
            lower.StartsWith("split this into ") ||
            lower.StartsWith("split that into ") ||
            lower.StartsWith("split into ") ||
            lower.StartsWith("split this plan into ") ||
            lower.StartsWith("turn this into tickets") ||
            lower.StartsWith("turn this plan into tickets") ||
            lower.StartsWith("turn the plan into tickets") ||
            lower == "create tickets" ||
            lower == "create draft tickets" ||
            lower.StartsWith("create tickets ") ||
            lower.StartsWith("create draft tickets ");

        if (!isSplitCommand) return null;

        var count = ExtractTicketCount(lower);
        var hints = ExtractSplitHints(text);
        if (hints.Count == 0)
            hints = ExtractCandidateTitles(previousMessage);
        if (count <= 1 && hints.Count > 1)
            count = hints.Count;
        if (count <= 1 && !string.IsNullOrWhiteSpace(previousMessage))
            count = 2;

        var workText = ExtractSplitWorkText(text, previousMessage);
        var requiresClarification = string.IsNullOrWhiteSpace(workText);

        return new CreateTicketIntent
        {
            Intent = count > 1 ? "CreateTickets" : "CreateTicket",
            Confidence = 1.0,
            CommandText = text,
            WorkText = workText,
            TicketCount = Math.Clamp(count, 1, 5),
            SplitHints = hints,
            RequiresClarification = requiresClarification,
            ClarificationQuestions = requiresClarification
                ? ["Which work should I create tickets from?"]
                : Array.Empty<string>()
        };
    }

    private static int ExtractTicketCount(string lower)
    {
        var tokens = lower.Split([' ', '\t', '\r', '\n', '.', ',', ':', ';'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            if ((tokens[i] == "ticket" || tokens[i] == "tickets") && i > 0)
            {
                var previous = tokens[i - 1];
                if (int.TryParse(previous, out var numeric)) return numeric;
                if (NumberWords.TryGetValue(previous, out var wordValue)) return wordValue;
            }
        }

        return 0;
    }

    private static List<string> ExtractSplitHints(string text)
    {
        var markerIndex = text.IndexOf(':');
        if (markerIndex < 0 || markerIndex >= text.Length - 1) return [];

        var tail = text[(markerIndex + 1)..];
        return tail
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(part => part.Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(part => part.Length > 0)
            .Take(5)
            .ToList();
    }

    private static List<string> ExtractCandidateTitles(string? previousMessage)
    {
        if (string.IsNullOrWhiteSpace(previousMessage))
            return [];

        var titles = new List<string>();
        foreach (var rawLine in previousMessage.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var candidate = TryExtractNumberedTitle(line) ?? TryExtractCandidateTitle(line);
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            titles.Add(candidate.Trim());
            if (titles.Count >= 5)
                break;
        }

        return titles;
    }

    private static string? TryExtractNumberedTitle(string line)
    {
        var dotIndex = line.IndexOf('.');
        if (dotIndex <= 0 || dotIndex >= line.Length - 1)
            return null;

        var prefix = line[..dotIndex].Trim();
        if (!int.TryParse(prefix, out _))
            return null;

        return CleanTitle(line[(dotIndex + 1)..]);
    }

    private static string? TryExtractCandidateTitle(string line)
    {
        const string candidatePrefix = "candidate:";
        if (!line.StartsWith(candidatePrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return CleanTitle(line[candidatePrefix.Length..]);
    }

    private static string? CleanTitle(string value)
    {
        var title = value.Trim().Trim('-', '*', ' ', '\t');
        if (title.StartsWith("**") && title.EndsWith("**") && title.Length > 4)
            title = title[2..^2].Trim();

        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string ExtractSplitWorkText(string request, string? previousMessage)
    {
        var colonIndex = request.IndexOf(':');
        if (colonIndex >= 0 && colonIndex < request.Length - 1)
        {
            var tail = request[(colonIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(tail))
                return tail;
        }

        return previousMessage?.Trim() ?? string.Empty;
    }

    public static bool IsChangeIntent(string request, CreateTicketIntent? ticketIntent)
    {
        if (ticketIntent != null) return true;

        if (string.IsNullOrWhiteSpace(request)) return false;

        var lower = request.ToLowerInvariant().Trim();

        var inspectionPrefixes = new[] {
            "check", "inspect", "what", "look", "explain", "how", "where", "find", "show", "why", "does", "is", "are", "can", "review", "verify", "who"
        };

        foreach (var prefix in inspectionPrefixes)
        {
            if (lower.StartsWith(prefix + " ") || lower == prefix) return false;
        }

        var changePrefixes = new[] {
            "implement", "replace", "change", "build", "generate", "add", "update", "fix", "refactor", "remove", "rewrite", "migrate", "create", "make", "raise"
        };

        foreach (var prefix in changePrefixes)
        {
            if (lower.StartsWith(prefix + " ") || lower == prefix) return true;
        }

        // Default to false (inspection/general) if we aren't explicitly commanding a change
        return false;
    }
}
