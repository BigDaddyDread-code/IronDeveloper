using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronDev.Data.Models;
using IronDev.Core.Models;

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
        "IronDev.Api/Controllers/",
        "IronDev.Client/",
        "IronDev.TauriShell/src/features/",
        "IronDev.TauriShell/src/components/",
    ];

    /// <summary>
    /// Sorts a set of retrieved snippets so that:
    ///   - Test files come last (or are excluded entirely if excludeTests=true).
    ///   - Boosted production paths come first.
    ///   - Within the same tier, original ordering is preserved.
    /// </summary>
    /// <summary>
    /// Sorts and filters a set of retrieved snippets based on the current route decision.
    /// This prevents irrelevant files (like conflict keyword tables or draft services)
    /// from clogging the evidence context for inspection routes.
    /// </summary>
    public static IReadOnlyList<CodeIndexEntry> RankAndFilter(
        IEnumerable<CodeIndexEntry> entries,
        ContextAgentRouteDecision route,
        bool excludeTests = true)
    {
        var list = entries.ToList();

        if (excludeTests)
            list = list.Where(e => !IsTestFile(e.FilePath)).ToList();

        var lowerWork = (route.EffectiveWorkText ?? string.Empty).ToLowerInvariant();
        bool isAuthQuery = lowerWork.Contains("auth") || lowerWork.Contains("login") || lowerWork.Contains("jwt");
        bool isTicketQuery = lowerWork.Contains("ticket") || lowerWork.Contains("archive");

        return list
            .Where(e => !IsExplicitlyExcluded(e, route, isAuthQuery, isTicketQuery))
            .OrderBy(e => IsTestFile(e.FilePath) ? 2 : 0)
            .ThenBy(e => GetRouteAwareBoost(e, isAuthQuery, isTicketQuery))
            .ThenBy(e => ProductionBoostScore(e.FilePath))
            .ThenBy(e => list.IndexOf(e))
            .ToList();
    }

    private static bool IsExplicitlyExcluded(CodeIndexEntry e, ContextAgentRouteDecision route, bool isAuthQuery, bool isTicketQuery)
    {
        var path = e.FilePath?.ToLowerInvariant() ?? string.Empty;
        var lowerWork = (route.EffectiveWorkText ?? string.Empty).ToLowerInvariant();

        // 1. Detect if the user is asking about the agent itself
        bool isContextAgentSelfQuery = lowerWork.Contains("context agent") 
                                    || lowerWork.Contains("routing") 
                                    || lowerWork.Contains("proof gate") 
                                    || lowerWork.Contains("tracing")
                                    || lowerWork.Contains("contextagent")
                                    || lowerWork.Contains("conflictassessment")
                                    || lowerWork.Contains("routejudge");

        // 2. Exclude Context Agent internals from product implementation questions
        if (!isContextAgentSelfQuery)
        {
            string[] internalFiles = [
                "contextagentservice.cs",
                "contextconflictservice.cs",
                "contextagentmodels.cs",
                "retrievalqualityhelpers.cs",
                "deepcodelookupservice.cs",
                "llmtraceservice.cs",
                "stubllmservice.cs",
                "stubroutejudge.cs",
                "contextagentroutejudgeservice.cs",
                "contextagentroutejudge.cs",
                "icontextagentroutejudge.cs",
                "contextagentmodels.cs"
            ];
            
            var fileName = Path.GetFileName(path);
            if (internalFiles.Any(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        // 3. Exclude DraftTicketService from product implementation questions
        // Unless the user explicitly asks about draft-ticket generation.
        bool isDraftQuery = lowerWork.Contains("draft");
        if (isTicketQuery && !isDraftQuery && path.Contains("draftticketservice.cs"))
            return true;

        return false;
    }

    private static int GetRouteAwareBoost(CodeIndexEntry e, bool isAuthQuery, bool isTicketQuery)
    {
        var path = e.FilePath?.ToLowerInvariant() ?? string.Empty;

        // 1. Auth Domain Boosts
        if (isAuthQuery)
        {
            if (path.Contains("authcontroller.cs") 
                || path.Contains("jwttokenservice.cs") 
                || path.Contains("userservice.cs")
                || path.Contains("authdtos.cs")
                || path.Contains("jwt-tenant-context.cs")
                || path.EndsWith("program.cs"))
                return -10; // High priority
                
            if (path.Contains("auth")) 
                return -5;
        }

        // 2. Ticket Domain Boosts
        if (isTicketQuery)
        {
            if (path.Contains("ticketservice.cs") 
                || path.Contains("datamodels.cs") 
                || path.Contains("ticketsworkspace.tsx")
                || path.Contains("ticketlist.tsx")
                || path.Contains("ticketdetail.tsx"))
                return -10;

            if (path.Contains("draftticketservice.cs"))
                return 15; // De-boost draft service relative to production
        }

        return 5;
    }

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

    // ── 3. Shallow snippet detection ─────────────────────────────────────────

    public static bool IsShallowSnippet(string snippet, string symbolName, string query)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return true;
        if (snippet.Length < 100) return true;

        var lower = snippet.ToLowerInvariant();
        var lowerQuery = query.ToLowerInvariant();

        // Interface declaration (no body)
        if (lower.Contains("interface ") && !lower.Contains(" class ") && !lower.Contains("{")) return true;

        // Missing braces for method (it might be an abstract method or just a signature)
        if (!string.IsNullOrEmpty(symbolName) && snippet.Contains(symbolName, StringComparison.OrdinalIgnoreCase))
        {
            if (!snippet.Contains("{") && !snippet.Contains("=>")) return true;
        }

        // ProjectTicket property filtering logic missing
        if (symbolName.Equals("ProjectTicket", StringComparison.OrdinalIgnoreCase))
        {
            if (!lower.Contains("public bool isdeleted") && !lower.Contains("{ get; set; }") && !lower.Contains("=>"))
                return true;
        }

        // Missing filtering logic for Archive
        if (lowerQuery.Contains("archive") && !lower.Contains("archive") && !lower.Contains("isdeleted")) return true;

        // If it's a method but has no body (likely just a signature from an interface or abstract class)
        if (!string.IsNullOrEmpty(symbolName) && !lower.Contains("interface ") && !lower.Contains("abstract "))
        {
            if (lower.Contains(" " + symbolName.ToLowerInvariant() + "(") && !lower.Contains("{") && !lower.Contains("=>"))
                return true;
        }

        return false;
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
         ["ArchiveTicketAsync", "IsDeleted", "GetRecentTicketsAsync", "GetTicketByIdAsync", "ProjectTicket", "TicketsWorkspace", "TicketList"]),
        ("ticket archive",
         ["ArchiveTicketAsync", "IsDeleted", "GetRecentTicketsAsync", "ProjectTicket"]),
        ("archive ticket",
         ["ArchiveTicketAsync", "IsDeleted", "GetRecentTicketsAsync", "ProjectTicket"]),
        ("archiveticket",
         ["ArchiveTicketAsync", "IsDeleted"]),

        // ── Ticket delete / hard delete ───────────────────────────────────────
        ("delete ticket",
         ["ArchiveTicketAsync", "DeleteTicketAsync", "IsDeleted", "TicketsWorkspace", "TicketList", "TicketService"]),
        ("deleteticket",
         ["DeleteTicketAsync", "ArchiveTicketAsync", "IsDeleted"]),
        ("fix delete",
         ["ArchiveTicketAsync", "DeleteTicketAsync", "IsDeleted", "TicketService", "TicketsWorkspace", "TicketList"]),

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

        // ── Authentication / Authorization ────────────────────────────────────
        ("auth",
         ["AuthController", "JwtTokenService", "IJwtTokenService", "JwtTenantContext", "UserService", "UserProfileDto", "LoginRequest", "LoginResponse", "Program.cs"]),
        ("authentication",
         ["AuthController", "JwtTokenService", "IJwtTokenService", "UserService", "JwtTenantContext", "Program.cs"]),
        ("jwt",
         ["JwtTokenService", "IJwtTokenService", "JwtTenantContext", "AuthController"]),
        ("login",
         ["AuthController", "LoginRequest", "LoginResponse", "UserService"]),
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
    ///
    /// NOTE: "create a ticket" and "raise a ticket" are intentionally excluded here.
    /// Those requests are domain-specific and are handled by the conflict assessment
    /// stage (Stage 3.5) — not the pre-LLM clarification gate (Stage 0).
    /// Only truly vague fix/bug/delete requests with no technical domain are caught here.
    /// </summary>
    private static readonly string[] AmbiguousActionPhrases =
    [
        "fix delete",
        "fix the delete",
        "delete bug",
        "create a ticket to fix delete",      // specific: vague delete fix
        "create ticket for delete",
        "create ticket to fix",
        "fix the bug",
        "fix this",
    ];

    /// <summary>
    /// Returns true when a user request is likely a vague create/fix intent
    /// that should trigger clarification instead of code search expansion.
    /// </summary>
    public static (bool ShouldClarify, string? MatchedPhrase) ShouldPreferClarification(string userRequest)
    {
        if (string.IsNullOrWhiteSpace(userRequest)) return (false, null);

        var lower = userRequest.ToLowerInvariant();

        // Explicit ambiguous action patterns — only truly vague ones
        foreach (var phrase in AmbiguousActionPhrases)
            if (lower.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return (true, phrase);

        return (false, null);
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
    /// <summary>
    /// Returns true when the symbol name is a meaningful domain-level or logic-level
    /// symbol. Non-semantic symbols (like readonly, Ok, Unauthorized) are skipped
    /// during deep evidence lookup to avoid wasting budget on low-value context.
    /// </summary>
    public static bool IsSemanticSymbol(string? symbolName)
    {
        if (string.IsNullOrWhiteSpace(symbolName)) return false;
        if (symbolName.Length < 3) return false;

        var lower = symbolName.ToLowerInvariant();
        string[] nonSemantic = ["readonly", "ok", "unauthorized", "result", "task", "void", "string", "int", "bool", "guid", "datetime"];
        
        return !nonSemantic.Contains(lower);
    }
}
