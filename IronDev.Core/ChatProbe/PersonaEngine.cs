namespace IronDev.Core.ChatProbe;

/// <summary>
/// Registry of user personas with text-transform functions.
/// The transform is applied to each probe step's UserMessage before sending,
/// simulating realistic messy user behaviour.
/// </summary>
public static class PersonaEngine
{
    // ── Typo dictionary for MessyRob ──────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string> MessyRobTypos =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["architecture"]    = "artecture",
            ["recommendation"]  = "recomenation",
            ["recommend"]       = "recomenation",
            ["language"]        = "lanuage",
            ["sudoku"]          = "sudko",
            ["sql server"]      = "sql sever",
            ["interface"]       = "intrface",
            ["online"]          = "onlien",
            ["solitaire"]       = "soltaire",
            ["planning"]        = "planing",
            ["application"]     = "applicaton",
            ["feature"]         = "feture",
            ["database"]        = "databse",
            ["document"]        = "documant",
            ["booking"]         = "bocking",
            ["schedule"]        = "scheduel",
            ["tracker"]         = "trackr",
            ["summary"]         = "summery",
            ["implementation"]  = "implentation",
            ["let us"]          = "lets",
            ["going to"]        = "gonna",
        };

    // ── Scope-creep suffixes for ScopeCreeper ────────────────────────────────

    private static readonly IReadOnlyList<string> ScopeCreepSuffixes =
    [
        " and make it online",
        " also add AI recommendations",
        " and user accounts",
        " and make it work on mobile",
        " oh and can it have a leaderboard",
        " also maybe multiplayer",
        " and a backend API",
        " and real-time updates"
    ];

    // ── Personas ──────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<PersonaProfile> AllPersonas =
    [
        new PersonaProfile
        {
            Id          = PersonaId.MessyRob,
            Name        = "Messy Rob",
            Description = "Typos, partial phrases, rough grammar. Real user messy input.",
            TextTransform = ApplyMessyRob
        },
        new PersonaProfile
        {
            Id          = PersonaId.VagueFounder,
            Name        = "Vague Founder",
            Description = "Half-ideas, expects AI to fill in the blanks.",
            TextTransform = ApplyVagueFounder
        },
        new PersonaProfile
        {
            Id          = PersonaId.ShortcutUser,
            Name        = "Shortcut User",
            Description = "Replies yes, ok, that one, do that.",
            TextTransform = ApplyShortcutUser
        },
        new PersonaProfile
        {
            Id          = PersonaId.ScopeCreeper,
            Name        = "Scope Creeper",
            Description = "Asks for online, AI, backend, multiplayer, accounts.",
            TextTransform = ApplyScopeCreeper
        },
        new PersonaProfile
        {
            Id          = PersonaId.Contradictor,
            Name        = "Contradictor",
            Description = "No, actually. Not that. Changes topic midstream.",
            TextTransform = ApplyContradictorTransform
        },
        new PersonaProfile
        {
            Id          = PersonaId.Formalizer,
            Name        = "Formalizer",
            Description = "Save this. Make a doc. Break into tickets.",
            TextTransform = ApplyFormalizer
        }
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    public static IReadOnlyList<PersonaProfile> GetAll() => AllPersonas;

    public static PersonaProfile GetById(PersonaId id) =>
        AllPersonas.First(p => p.Id == id);

    public static PersonaProfile? ParseName(string name) =>
        AllPersonas.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Id.ToString(), name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Id.ToString().Replace("-", ""), name.Replace("-", ""), StringComparison.OrdinalIgnoreCase));

    /// <summary>Pick a persona deterministically from a seed value.</summary>
    public static PersonaProfile GetFromSeed(int seed)
    {
        var index = Math.Abs(seed) % AllPersonas.Count;
        return AllPersonas[index];
    }

    // ── Transform implementations ─────────────────────────────────────────────

    private static string ApplyMessyRob(string input)
    {
        // Apply known typos (longest match first to avoid partial overlaps)
        var result = input;
        foreach (var (correct, typo) in MessyRobTypos.OrderByDescending(kv => kv.Key.Length))
        {
            // Case-insensitive replace preserving case structure
            result = ReplaceWordsCaseInsensitive(result, correct, typo);
        }

        // Strip trailing punctuation from some sentences (Rob doesn't always finish)
        if (result.EndsWith('.') && result.Length > 20)
            result = result.TrimEnd('.');

        return result;
    }

    private static string ApplyVagueFounder(string input)
    {
        // Add filler phrases that signal vagueness
        var fillers = new[] { "something like ", "kind of ", "you know, " };
        if (input.Length > 20 && !input.StartsWith("I want", StringComparison.OrdinalIgnoreCase))
        {
            var filler = fillers[Math.Abs(input.GetHashCode()) % fillers.Length];
            return filler + char.ToLower(input[0]) + input[1..];
        }

        return input;
    }

    private static string ApplyShortcutUser(string input)
    {
        // For confirmation-style messages, collapse to brevity
        var lower = input.ToLowerInvariant();
        if (lower.Contains("yes") || lower.Contains("agree") || lower.Contains("sounds good"))
            return "yes";
        if (lower.Contains("ok") || lower.Contains("okay"))
            return "ok";
        if (lower.Contains("that one") || lower.Contains("first") || lower.Contains("option"))
            return "that one";
        if (lower.Contains("do that") || lower.Contains("let's do") || lower.Contains("lets do"))
            return "do that";
        if (lower.Contains("go ahead") || lower.Contains("proceed"))
            return "ok go ahead";

        return input;
    }

    private static string ApplyScopeCreeper(string input)
    {
        // Append a scope-creep suffix to non-confirmation messages
        var lower = input.ToLowerInvariant();
        if (lower.Length < 10 || lower is "yes" or "ok" or "that one" or "do that")
            return input;

        var suffix = ScopeCreepSuffixes[Math.Abs(input.GetHashCode()) % ScopeCreepSuffixes.Count];
        return input.TrimEnd('.', '?', '!') + suffix;
    }

    private static string ApplyContradictorTransform(string input)
    {
        // Randomly prefix with contradiction markers
        var prefixes = new[] { "no, actually ", "not that, ", "wait — ", "hmm no, " };
        var lower = input.ToLowerInvariant();

        // Don't prefix if message already starts with a contradiction marker
        if (lower.StartsWith("no") || lower.StartsWith("wait") || lower.StartsWith("hmm"))
            return input;

        var prefix = prefixes[Math.Abs(input.GetHashCode()) % prefixes.Length];
        return prefix + char.ToLower(input[0]) + input[1..];
    }

    private static string ApplyFormalizer(string input)
    {
        // Append or replace with formalization intents
        var lower = input.ToLowerInvariant();

        // If it's already a save/create message, pass through
        if (lower.Contains("save") || lower.Contains("ticket") || lower.Contains("doc"))
            return input;

        // Append a formalizer tail
        var tails = new[] { ", can we save this", " — make a doc", ", break into tickets" };
        var tail = tails[Math.Abs(input.GetHashCode()) % tails.Length];
        return input.TrimEnd('.', '?') + tail;
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string ReplaceWordsCaseInsensitive(string input, string search, string replacement)
    {
        // Simple case-insensitive word-boundary replacement
        var index = input.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return input;

        return input[..index] + replacement + input[(index + search.Length)..];
    }
}
