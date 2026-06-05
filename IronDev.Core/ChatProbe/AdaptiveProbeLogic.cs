namespace IronDev.Core.ChatProbe;

/// <summary>
/// Injects reactive probe messages between scripted steps based on what
/// IronDev just said. Bounded by ProbeRunOptions.MaxAdaptiveProbes.
/// All triggers are deterministic — no LLM needed.
/// </summary>
public sealed class AdaptiveProbeLogic
{
    private static readonly IReadOnlyList<AdaptiveTrigger> Triggers =
    [
        // ── Overbuild triggers ────────────────────────────────────────────────

        new AdaptiveTrigger
        {
            Description  = "Multiplayer when user said online single-player",
            ShouldFire   = (userMsg, response, _) =>
                ContainsWordMatch(userMsg, "online") &&
                !ContainsWordMatch(userMsg, "multiplayer") &&
                ContainsAny(response, "multiplayer", "multi-player"),
            ProbeMessage = "no, I mean online single-player so other people can play in browser"
        },

        new AdaptiveTrigger
        {
            Description  = "OAuth/WebSockets when not asked for auth",
            ShouldFire   = (userMsg, response, _) =>
                !ContainsAny(userMsg, "login", "auth", "account", "user") &&
                ContainsAll(response, "oauth", "websocket"),
            ProbeMessage = "I didn't ask for login or real-time, keep it simple"
        },

        // ── Clarification triggers ────────────────────────────────────────────

        new AdaptiveTrigger
        {
            Description  = "Generic 'what features do you want' question",
            ShouldFire   = (_, response, _) =>
                ContainsAny(response,
                    "what features do you want",
                    "what features would you like",
                    "what functionality do you need",
                    "can you tell me more about what features"),
            ProbeMessage = "you recommend"
        },

        new AdaptiveTrigger
        {
            Description  = "Asks do you want to save",
            ShouldFire   = (_, response, _) =>
                ContainsAny(response,
                    "do you want to save",
                    "would you like to save",
                    "shall i save",
                    "should i save this"),
            ProbeMessage = "yes"
        },

        // ── Context binding triggers ──────────────────────────────────────────

        new AdaptiveTrigger
        {
            Description  = "Generic answer not specific to project",
            ShouldFire   = (userMsg, response, previousTurns) =>
            {
                // Only fire if we've had at least 1 prior turn with project context
                if (previousTurns.Count == 0) return false;

                // Response is suspiciously generic if it doesn't mention
                // any word from the user message or previous context
                var words = userMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 4)
                    .Select(w => w.ToLowerInvariant());

                return !words.Any(w => response.Contains(w, StringComparison.OrdinalIgnoreCase))
                    && response.Length > 50;
            },
            ProbeMessage = "be specific to this project"
        },

        new AdaptiveTrigger
        {
            Description  = "Asks what decisions have been made when we already told it",
            ShouldFire   = (_, response, previousTurns) =>
            {
                if (previousTurns.Count < 2) return false;

                var asksForDecisions = ContainsAny(response,
                    "what decisions have already been made",
                    "what have we decided so far",
                    "what decisions were made");

                var hasPreviousContent = previousTurns.Any(t =>
                    t.AssistantResponse.Length > 100);

                return asksForDecisions && hasPreviousContent;
            },
            ProbeMessage = "I already told you all that, use what we discussed"
        },

        // ── Framework over-ask trigger ────────────────────────────────────────

        new AdaptiveTrigger
        {
            Description  = "Asks about framework when user explicitly said rules first",
            ShouldFire   = (userMsg, response, _) =>
                ContainsAny(userMsg, "rules first", "rules only", "just rules") &&
                ContainsAny(response, "framework", "what language", "which platform", "what technology"),
            ProbeMessage = "I said rules first, framework later"
        },

        // ── Recommendation redirect ───────────────────────────────────────────

        new AdaptiveTrigger
        {
            Description  = "Asks user to choose when user asked for recommendation",
            ShouldFire   = (userMsg, response, _) =>
                ContainsAny(userMsg, "you recommend", "what recommend", "what would you", "what slice") &&
                ContainsAny(response,
                    "would you prefer",
                    "which would you like",
                    "which option",
                    "you could choose",
                    "it depends on your preference"),
            ProbeMessage = "just pick one, your call"
        }
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Given the current turn's user message, assistant response, and history,
    /// return an adaptive probe message to inject — or null if no trigger fires.
    /// </summary>
    public string? GetAdaptiveProbe(
        string userMessage,
        string assistantResponse,
        IReadOnlyList<ProbeTurnResult> previousTurns,
        int adaptiveProbesUsed,
        ProbeRunOptions options)
    {
        if (adaptiveProbesUsed >= options.MaxAdaptiveProbes)
            return null;

        var responseLower = assistantResponse.ToLowerInvariant();
        var userLower = userMessage.ToLowerInvariant();

        foreach (var trigger in Triggers)
        {
            try
            {
                if (trigger.ShouldFire(userLower, responseLower, previousTurns))
                    return trigger.ProbeMessage;
            }
            catch
            {
                // Trigger evaluation must never crash the run
            }
        }

        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAll(string text, params string[] terms) =>
        terms.All(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsWordMatch(string text, string word) =>
        text.Contains(' ' + word, StringComparison.Ordinal) ||
        text.Contains(word + ' ', StringComparison.Ordinal) ||
        text.StartsWith(word, StringComparison.Ordinal) ||
        text.EndsWith(word, StringComparison.Ordinal) ||
        text == word;

    // ── Trigger model ─────────────────────────────────────────────────────────

    private sealed class AdaptiveTrigger
    {
        public required string Description { get; init; }
        public required Func<string, string, IReadOnlyList<ProbeTurnResult>, bool> ShouldFire { get; init; }
        public required string ProbeMessage { get; init; }
    }
}
