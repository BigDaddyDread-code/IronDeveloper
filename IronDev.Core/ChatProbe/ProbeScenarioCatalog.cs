namespace IronDev.Core.ChatProbe;

/// <summary>
/// Static catalog of probe scenarios covering games, business apps,
/// developer tools, and consumer apps.
/// Each scenario ships with domain hints and a scripted conversation spine.
/// </summary>
public static class ProbeScenarioCatalog
{
    private static readonly IReadOnlyList<ProbeScenario> All =
    [
        // ── Games ─────────────────────────────────────────────────────────────

        new ProbeScenario
        {
            ScenarioId     = "fishing-game",
            Name           = "Fishing Game",
            Category       = ProjectCategory.Game,
            ProjectIdea    = "fishing game where the fish get smarter each day",
            DomainTerms    = ["fish AI", "difficulty curve", "bait", "catch mechanic", "daily progression"],
            DangerZones    = [],
            Steps          =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "I want to build a fishing game where the fish get smarter each day" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "explain how that would work",
                    ExpectedMode = "Exploration" },
                new() { Order = 3, Kind = ProbeKind.Formalize,
                    UserMessage = "ok lets save that as discussion",
                    ExpectedMode = "Formalization",
                    ExpectGateSaveDiscussion = true },
                new() { Order = 4, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "we clarify the rules better",
                    ExpectedMode = "Exploration" },
                new() { Order = 5, Kind = ProbeKind.AskArchitectureDoc,
                    UserMessage = "can you create artecture document with whats already decided and question need answering",
                    ExpectedMode = "Formalization" }
            ]
        },

        new ProbeScenario
        {
            ScenarioId  = "solitaire-online",
            Name        = "Solitaire Online",
            Category    = ProjectCategory.Game,
            ProjectIdea = "Solitaire online",
            DomainTerms = ["card game", "Klondike", "deck", "foundation", "tableau", "browser"],
            DangerZones = ["multiplayer when user said online single-player"],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "I want to make solitaire online so people can play in the browser" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "what rules should we use",
                    ExpectedMode = "Exploration" },
                new() { Order = 3, Kind = ProbeKind.ScopeCreep,
                    UserMessage = "ok can go step by step, lets do rules first" },
                new() { Order = 4, Kind = ProbeKind.TopicCorrection,
                    UserMessage = "no I mean online single-player, not multiplayer" },
                new() { Order = 5, Kind = ProbeKind.Formalize,
                    UserMessage = "save this discussion",
                    ExpectedMode = "Formalization",
                    ExpectGateSaveDiscussion = true }
            ]
        },

        new ProbeScenario
        {
            ScenarioId  = "goblin-shopkeeper",
            Name        = "Goblin Shopkeeper",
            Category    = ProjectCategory.Game,
            ProjectIdea = "goblin shopkeeper idle game",
            DomainTerms = ["inventory", "gold", "haggling", "customers", "upgrades", "idle"],
            DangerZones = [],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "i want a goblin shopkeeper game where you sell stuff to adventurers" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "what slice be first" },
                new() { Order = 3, Kind = ProbeKind.ShortConfirm,
                    UserMessage = "ok that one" },
                new() { Order = 4, Kind = ProbeKind.Formalize,
                    UserMessage = "can save this",
                    ExpectedMode = "Formalization",
                    ExpectGateSaveDiscussion = true }
            ]
        },

        new ProbeScenario
        {
            ScenarioId  = "tower-defence",
            Name        = "Tower Defence",
            Category    = ProjectCategory.Game,
            ProjectIdea = "tower defence game in Unity",
            DomainTerms = ["towers", "waves", "enemies", "path", "currency", "Unity"],
            DangerZones = [],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "tower defence game, want it in Unity" },
                new() { Order = 2, Kind = ProbeKind.AskWhatNext,
                    UserMessage = "what would be good first steps" },
                new() { Order = 3, Kind = ProbeKind.ScopeCreep,
                    UserMessage = "and can we add online leaderboard and accounts" },
                new() { Order = 4, Kind = ProbeKind.Contradict,
                    UserMessage = "no actually lets keep it offline first" },
                new() { Order = 5, Kind = ProbeKind.Formalize,
                    UserMessage = "break into tickets",
                    ExpectedMode = "Formalization",
                    ExpectGateCreateTicket = true }
            ]
        },

        // ── Business Apps ─────────────────────────────────────────────────────

        new ProbeScenario
        {
            ScenarioId  = "recipe-app",
            Name        = "Recipe App",
            Category    = ProjectCategory.BusinessApp,
            ProjectIdea = "recipe management app",
            DomainTerms = ["ingredients", "steps", "categories", "search", "favourites"],
            DangerZones = [],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "recipe app, want to save and search recipes" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "you recomenation for how to build this" },
                new() { Order = 3, Kind = ProbeKind.ShortConfirm,
                    UserMessage = "yes" },
                new() { Order = 4, Kind = ProbeKind.Formalize,
                    UserMessage = "save this",
                    ExpectedMode = "Formalization",
                    ExpectGateSaveDiscussion = true }
            ]
        },

        new ProbeScenario
        {
            ScenarioId  = "dog-walking-booking",
            Name        = "Dog Walking Booking App",
            Category    = ProjectCategory.BusinessApp,
            ProjectIdea = "dog walking booking app for small business",
            DomainTerms = ["bookings", "walkers", "schedule", "dogs", "payments", "notifications"],
            DangerZones = ["payments before bookings"],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "want to build a dog walking booking app for my small business" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "what would the first screen be" },
                new() { Order = 3, Kind = ProbeKind.ScopeCreep,
                    UserMessage = "can we add online payments and AI scheduling" },
                new() { Order = 4, Kind = ProbeKind.Contradict,
                    UserMessage = "no not AI, just manual booking for now" },
                new() { Order = 5, Kind = ProbeKind.Formalize,
                    UserMessage = "make a doc",
                    ExpectedMode = "Formalization",
                    ExpectGateSaveDiscussion = true }
            ]
        },

        new ProbeScenario
        {
            ScenarioId  = "invoice-tracker",
            Name        = "Invoice Tracker",
            Category    = ProjectCategory.BusinessApp,
            ProjectIdea = "invoice tracking app",
            DomainTerms = ["invoices", "clients", "due dates", "paid", "overdue", "export"],
            DangerZones = [],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "invoice tracker, keep track of who owes me money" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "what database should I use" },
                new() { Order = 3, Kind = ProbeKind.ShortConfirm,
                    UserMessage = "ok" },
                new() { Order = 4, Kind = ProbeKind.Formalize,
                    UserMessage = "break into tickets",
                    ExpectedMode = "Formalization",
                    ExpectGateCreateTicket = true }
            ]
        },

        new ProbeScenario
        {
            ScenarioId  = "staff-roster",
            Name        = "Staff Roster",
            Category    = ProjectCategory.BusinessApp,
            ProjectIdea = "staff roster and shift management app",
            DomainTerms = ["shifts", "roster", "employees", "leave", "availability", "notifications"],
            DangerZones = [],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "need a staff roster app for a cafe, about 10 staff" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "what lanuage should we use" },
                new() { Order = 3, Kind = ProbeKind.ScopeCreep,
                    UserMessage = "can we do web app and mobile and desktop" },
                new() { Order = 4, Kind = ProbeKind.Contradict,
                    UserMessage = "no just web for now" },
                new() { Order = 5, Kind = ProbeKind.AskArchitectureDoc,
                    UserMessage = "create artecture doc",
                    ExpectedMode = "Formalization" }
            ]
        },

        // ── Developer Tools ───────────────────────────────────────────────────

        new ProbeScenario
        {
            ScenarioId  = "natural-language-powershell",
            Name        = "Natural Language to PowerShell",
            Category    = ProjectCategory.DeveloperTool,
            ProjectIdea = "natural-language-to-PowerShell console app",
            DomainTerms = ["PowerShell", "command preview", "risk", "confirmation", "execution log"],
            DangerZones = ["blind command execution", "no confirmation", "unsafe commands"],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "console app that turns plain english into powershell commands" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "how would it handle dangerous commands" },
                new() { Order = 3, Kind = ProbeKind.ShortConfirm,
                    UserMessage = "yes show a preview first" },
                new() { Order = 4, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "what would first slice be" },
                new() { Order = 5, Kind = ProbeKind.Formalize,
                    UserMessage = "save this discussion",
                    ExpectedMode = "Formalization",
                    ExpectGateSaveDiscussion = true }
            ]
        },

        new ProbeScenario
        {
            ScenarioId  = "sql-helper",
            Name        = "SQL Helper",
            Category    = ProjectCategory.DeveloperTool,
            ProjectIdea = "SQL query helper tool",
            DomainTerms = ["SQL Server", "queries", "explain plan", "query optimisation", "schema"],
            DangerZones = [],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "sql sever helper, write queries from plain english" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "json or sql sever for storing query history" },
                new() { Order = 3, Kind = ProbeKind.ShortConfirm,
                    UserMessage = "that one" },
                new() { Order = 4, Kind = ProbeKind.AskWhatNext,
                    UserMessage = "what next" }
            ]
        },

        new ProbeScenario
        {
            ScenarioId  = "log-summariser",
            Name        = "Log Summariser",
            Category    = ProjectCategory.DeveloperTool,
            ProjectIdea = "log file summariser developer tool",
            DomainTerms = ["log files", "errors", "warnings", "patterns", "summary", "filter"],
            DangerZones = [],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "tool to summarise big log files and find patterns" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "how do you recomenation to parse the logs" },
                new() { Order = 3, Kind = ProbeKind.ShortConfirm,
                    UserMessage = "ok do that" },
                new() { Order = 4, Kind = ProbeKind.Formalize,
                    UserMessage = "break into tickets",
                    ExpectedMode = "Formalization",
                    ExpectGateCreateTicket = true }
            ]
        },

        // ── Consumer Apps ─────────────────────────────────────────────────────

        new ProbeScenario
        {
            ScenarioId  = "habit-tracker",
            Name        = "Habit Tracker",
            Category    = ProjectCategory.ConsumerApp,
            ProjectIdea = "habit tracking app",
            DomainTerms = ["habits", "streaks", "reminders", "goals", "progress", "daily"],
            DangerZones = [],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "habbit tracker app, track daily habits and streaks" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "what platform you recomenation" },
                new() { Order = 3, Kind = ProbeKind.ScopeCreep,
                    UserMessage = "and AI that predicts when youll fail a habit" },
                new() { Order = 4, Kind = ProbeKind.Contradict,
                    UserMessage = "no actually just simple tracking first" },
                new() { Order = 5, Kind = ProbeKind.Formalize,
                    UserMessage = "save this as discussion",
                    ExpectedMode = "Formalization",
                    ExpectGateSaveDiscussion = true }
            ]
        },

        new ProbeScenario
        {
            ScenarioId  = "budget-app",
            Name        = "Budget App",
            Category    = ProjectCategory.ConsumerApp,
            ProjectIdea = "personal budget tracking app",
            DomainTerms = ["transactions", "categories", "budget", "spending", "income", "reports"],
            DangerZones = [],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "budget app to track spending, categorise transactions" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "sudko or csv for imports" },
                new() { Order = 3, Kind = ProbeKind.ShortConfirm,
                    UserMessage = "yes csv" },
                new() { Order = 4, Kind = ProbeKind.AskArchitectureDoc,
                    UserMessage = "artecture doc please",
                    ExpectedMode = "Formalization" }
            ]
        },

        new ProbeScenario
        {
            ScenarioId  = "travel-planner",
            Name        = "Travel Planner",
            Category    = ProjectCategory.ConsumerApp,
            ProjectIdea = "travel planner app",
            DomainTerms = ["trips", "itinerary", "bookings", "budget", "packing list", "destinations"],
            DangerZones = [],
            Steps       =
            [
                new() { Order = 1, Kind = ProbeKind.Seed,
                    UserMessage = "travel planer app, plan trips and track what I want to do" },
                new() { Order = 2, Kind = ProbeKind.AskRecommendation,
                    UserMessage = "what would be the best intrface" },
                new() { Order = 3, Kind = ProbeKind.ShortConfirm,
                    UserMessage = "ok that sounds good" },
                new() { Order = 4, Kind = ProbeKind.Formalize,
                    UserMessage = "can we make a doc",
                    ExpectedMode = "Formalization",
                    ExpectGateSaveDiscussion = true }
            ]
        }
    ];

    /// <summary>Get all scenarios.</summary>
    public static IReadOnlyList<ProbeScenario> GetAll() => All;

    /// <summary>Get a scenario by id (case-insensitive).</summary>
    public static ProbeScenario? GetById(string scenarioId) =>
        All.FirstOrDefault(s => string.Equals(s.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Deterministically pick a scenario from a seed value.</summary>
    public static ProbeScenario GetFromSeed(int seed)
    {
        var index = Math.Abs(seed) % All.Count;
        return All[index];
    }

    /// <summary>Produce a repeatable batch of scenario+index pairs for a given seed and count.</summary>
    public static IReadOnlyList<(ProbeScenario Scenario, int Seed)> GetBatch(int count, int seed = 0)
    {
        var results = new List<(ProbeScenario, int)>(count);
        for (var i = 0; i < count; i++)
        {
            var derivedSeed = seed + i;
            results.Add((GetFromSeed(derivedSeed), derivedSeed));
        }

        return results;
    }
}
