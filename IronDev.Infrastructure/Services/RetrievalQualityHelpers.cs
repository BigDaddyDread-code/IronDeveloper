using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronDev.Data.Models;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Retrieval quality helpers for Context Agent v1.
///
/// Three concerns live here:
///   1. Query expansion — broad/user-facing terms → concrete C# symbol queries.
///   2. Production-file ranking — boost production code, deprioritise test files.
///   3. Clarification-first detection — vague create/fix requests should ask first.
///
/// All methods are pure / static so they can be unit-tested without any DI wiring.
/// </summary>
public static class RetrievalQualityHelpers
{
    // ── 1. File classification ────────────────────────────────────────────────

    /// <summary>
    /// Patterns that identify test-fixture or seeded-data files.
    /// These should never be selected as primary implementation evidence.
    /// </summary>
    private static readonly string[] TestFilePatterns =
    [
        "IntegrationTests",
        "UnitTests",
        "Tests.cs",
        "Tests/",
        "Test/",
        "Spec.cs",
        "GroundingQuality",
        "SeedCodeIndex",
        "ChatGroundingQuality",
    ];

    /// <summary>
    /// Returns true when the file path looks like a test / seed file.
    /// Case-insensitive to handle both path separator styles.
    /// </summary>
    public static bool IsTestFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var normalised = filePath.Replace('\\', '/');
        return TestFilePatterns.Any(p =>
            normalised.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    // ── 2. Production-first ranking ───────────────────────────────────────────

    /// <summary>
    /// Production files that should be boosted for typical domain queries.
    /// Order matters: earlier = higher priority score.
    /// </summary>
    private static readonly string[] ProductionBoostPaths =
    [
        "IronDev.Infrastructure/Services/TicketService.cs",
        "IronDev.Infrastructure/Services/",
        "IronDev.Core/Models/DataModels.cs",
        "IronDev.Core/Models/",
        "IronDev.Core/Interfaces/",
        "IronDeveloper/ViewModels/Workspaces/",
        "IronDeveloper/Views/Workspaces/",
    ];

    /// <summary>
    /// Sorts a set of retrieved snippets so that:
    ///   - Test files come last (or are excluded entirely if excludeTests=true).
    ///   - Boosted production paths come first.
    ///   - Within the same tier, original ordering is preserved.
    /// </summary>
    public static IReadOnlyList<CodeIndexEntry> RankByProductionFirst(
        IEnumerable<CodeIndexEntry> entries,
        bool excludeTests = false)
    {
        var list = entries.ToList();

        if (excludeTests)
            list = list.Where(e => !IsTestFile(e.FilePath)).ToList();

        return list
            .OrderBy(e => IsTestFile(e.FilePath) ? 2 : 0)           // test files last
            .ThenBy(e => ProductionBoostScore(e.FilePath))           // boosted paths first
            .ThenBy(e => list.IndexOf(e))                            // stable within tier
            .ToList();
    }

    private static int ProductionBoostScore(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return 99;
        for (int i = 0; i < ProductionBoostPaths.Length; i++)
            if (filePath.Contains(ProductionBoostPaths[i], StringComparison.OrdinalIgnoreCase))
                return i;
        return 50;
    }

    // ── 3. Query expansion ───────────────────────────────────────────────────

    /// <summary>
    /// Maps broad or user-facing query terms to concrete C# symbol / file queries.
    ///
    /// Rules:
    /// - Match is case-insensitive, whole-word not required (substring is fine).
    /// - Each match contributes its expansion list.
    /// - Duplicates across matches are deduplicated while preserving order.
    /// - The original query is always included as the first entry.
    /// - Expansions are capped at MaxExpansionPerTerm to avoid blowing limits.
    /// </summary>
    private static readonly (string Trigger, string[] Expansions)[] ExpansionTable =
    [
        // ── LLM Console ───────────────────────────────────────────────────────
        ("llm console",
         ["LlmConsoleViewModel", "LlmConsoleView", "LlmTraceService", "ILlmTraceService"]),
        ("lm console",
         ["LlmConsoleViewModel", "LlmConsoleView", "LlmTraceService"]),
        ("trace console",
         ["LlmConsoleViewModel", "LlmTraceService", "ILlmTraceService"]),
        ("llm trace",
         ["LlmTraceService", "ILlmTraceService", "LlmTraceEntry", "AddTrace"]),

        // ── Ticket archive / soft delete ─────────────────────────────────────
        ("soft archive",
         ["ArchiveTicketAsync", "IsDeleted", "GetRecentTicketsAsync", "GetTicketByIdAsync", "ProjectTicket", "TicketsWorkspaceViewModel"]),
        ("ticket archive",
         ["ArchiveTicketAsync", "IsDeleted", "GetRecentTicketsAsync", "ProjectTicket"]),
        ("archive ticket",
         ["ArchiveTicketAsync", "IsDeleted", "GetRecentTicketsAsync", "ProjectTicket"]),
        ("archiveticket",
         ["ArchiveTicketAsync", "IsDeleted"]),

        // ── Ticket delete / hard delete ───────────────────────────────────────
        ("delete ticket",
         ["ArchiveTicketAsync", "DeleteTicketAsync", "IsDeleted", "TicketsWorkspaceViewModel", "TicketService"]),
        ("deleteticket",
         ["DeleteTicketAsync", "ArchiveTicketAsync", "IsDeleted"]),
        ("fix delete",
         ["ArchiveTicketAsync", "DeleteTicketAsync", "IsDeleted", "TicketService", "TicketsWorkspaceViewModel"]),

        // ── IsDeleted / filtering ─────────────────────────────────────────────
        ("isdeleted",
         ["IsDeleted", "ProjectTicket", "GetRecentTicketsAsync", "GetTicketByIdAsync", "ArchiveTicketAsync"]),
        ("soft delete",
         ["IsDeleted", "ArchiveTicketAsync", "GetRecentTicketsAsync", "ProjectTicket"]),

        // ── TicketService / ticket management ─────────────────────────────────
        ("ticketservice",
         ["TicketService", "ArchiveTicketAsync", "GetRecentTicketsAsync", "GetTicketByIdAsync"]),
        ("ticket service",
         ["TicketService", "ArchiveTicketAsync", "GetRecentTicketsAsync", "GetTicketByIdAsync"]),

        // ── Chat history ──────────────────────────────────────────────────────
        ("delete chat",
         ["DeleteSessionAsync", "ChatHistoryService", "ProjectChatSession"]),
        ("chat history",
         ["ChatHistoryService", "IChatHistoryService", "ProjectChatSession", "GetRecentSessionsAsync"]),

        // ── Context Agent itself ───────────────────────────────────────────────
        ("context agent",
         ["ContextAgentService", "IContextAgentService", "ContextAgentResult", "ContextSufficiencyResult"]),

        // ── Prompt / context builder ───────────────────────────────────────────
        ("prompt context",
         ["PromptContextBuilder", "IPromptContextBuilder", "ChatContextPacket", "BuildPacketAsync"]),
        ("context builder",
         ["PromptContextBuilder", "ChatContextPacket", "BuildPacketAsync"]),

        // ── Code index / indexing ─────────────────────────────────────────────
        ("code index",
         ["CodeIndexService", "ICodeIndexService", "GetRelevantSnippetsAsync", "SqlCodeIndexService"]),
        ("index",
         ["CodeIndexService", "GetRelevantSnippetsAsync", "IndexDirectoryAsync"]),
    ];

    /// <summary>
    /// Expands a list of LLM-requested search queries into a deduplicated,
    /// concrete symbol-level query list. The original queries are always
    /// included first to preserve intent.
    /// </summary>
    public static IReadOnlyList<string> ExpandQueries(IEnumerable<string> rawQueries)
    {
        var result  = new List<string>();
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var query in rawQueries)
        {
            if (string.IsNullOrWhiteSpace(query)) continue;

            // Always include the original query
            if (seen.Add(query.Trim()))
                result.Add(query.Trim());

            // Find all matching expansion entries
            foreach (var (trigger, expansions) in ExpansionTable)
            {
                if (query.Contains(trigger, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var expansion in expansions)
                    {
                        if (seen.Add(expansion))
                            result.Add(expansion);
                    }
                }
            }
        }

        return result;
    }

    // ── 4. Clarification-first detection ─────────────────────────────────────

    /// <summary>
    /// Patterns for vague create/fix intents that should prefer clarification
    /// over immediate code search, when combined with an ambiguous domain term.
    /// </summary>
    private static readonly string[] AmbiguousActionPhrases =
    [
        "fix delete",
        "fix the delete",
        "delete bug",
        "create a ticket to fix delete",
        "create ticket for delete",
        "create ticket to fix",
        "fix the bug",
        "fix this",
        "create a ticket",    // bare "create a ticket" without further domain
        "raise a ticket",
    ];

    private static readonly string[] AmbiguousDomainTerms =
    [
        " delete",
        " fix",
        " issue",
        " bug",
        " problem",
    ];

    /// <summary>
    /// Returns true when a user request is likely a vague create/fix intent
    /// that should trigger clarification instead of code search expansion.
    ///
    /// Heuristic: matches an ambiguous action phrase OR a bare "create a ticket"
    /// without a specific domain noun (e.g., no service/method name).
    /// </summary>
    public static bool ShouldPreferClarification(string userRequest)
    {
        if (string.IsNullOrWhiteSpace(userRequest)) return false;

        var lower = userRequest.ToLowerInvariant();

        // Explicit ambiguous action patterns
        foreach (var phrase in AmbiguousActionPhrases)
            if (lower.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    /// <summary>
    /// Generates standardised clarification questions for vague delete/fix requests.
    /// Called before the LLM sufficiency check when ShouldPreferClarification is true.
    /// </summary>
    public static IReadOnlyList<string> GetDeleteClarificationQuestions()
        =>
        [
            "Are you asking about deleting chat conversations?",
            "Are you asking about soft-archiving (hiding) saved tickets using IsDeleted?",
            "Are you asking about hard-deleting ticket records from the database?",
            "Are you asking about deleting implementation plans?",
            "Or is this about a different kind of delete?",
        ];

    // ── 5. Snippet depth guard ────────────────────────────────────────────────

    /// <summary>
    /// Minimum useful snippet length. Snippets shorter than this are likely
    /// interface-only declarations with no implementation body — they get
    /// deprioritised relative to longer, body-containing snippets.
    /// </summary>
    private const int MinUsefulSnippetChars = 80;

    /// <summary>
    /// True when the snippet is long enough to contain implementation body,
    /// not just an interface stub or property declaration.
    /// </summary>
    public static bool IsDeepSnippet(string? chunkText)
        => !string.IsNullOrWhiteSpace(chunkText) && chunkText.Length >= MinUsefulSnippetChars;

    /// <summary>
    /// Sorts ranked entries so deeper snippets are preferred within the same
    /// production-vs-test tier, without evicting shallow snippets entirely.
    /// </summary>
    public static IReadOnlyList<CodeIndexEntry> PreferDeepSnippets(
        IReadOnlyList<CodeIndexEntry> entries)
    {
        return entries
            .OrderBy(e => IsTestFile(e.FilePath) ? 1 : 0)
            .ThenByDescending(e => e.ChunkText?.Length ?? 0)
            .ToList();
    }
}
