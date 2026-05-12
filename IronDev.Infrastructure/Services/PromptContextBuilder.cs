using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Services;

namespace IronDev.AI;

public class ChatContextPacket
{
    public System.Collections.Generic.List<string> Snippets  { get; init; } = new();
    public System.Collections.Generic.List<string> Tickets   { get; init; } = new();
    public System.Collections.Generic.List<string> Decisions { get; init; } = new();
    public string FormattedPrompt { get; set; } = string.Empty;

    /// <summary>Structured file paths from matched code snippets.</summary>
    public System.Collections.Generic.List<string> MatchedFilePaths { get; init; } = new();
    /// <summary>Structured symbol names from matched code snippets.</summary>
    public System.Collections.Generic.List<string> MatchedSymbols { get; init; } = new();

    /// <summary>Chat intent inferred from the user request.</summary>
    public ChatIntent Intent { get; set; } = ChatIntent.General;

    /// <summary>True when the project has no code index (IndexingStatus != 'Ready').</summary>
    public bool IsProjectNotIndexed { get; set; }

    // ── Memory filter diagnostics (populated by BuildPacketDataAsync) ────────
    /// <summary>Items excluded by IsJunkMemory across decisions + tickets + summary.</summary>
    public int FilteredMemoryCount { get; set; }
    /// <summary>Items that passed the filter and were included in the prompt.</summary>
    public int IncludedMemoryCount { get; set; }
    /// <summary>Arch-poison terms found in excluded memory items.</summary>
    public System.Collections.Generic.List<string> PollutedTermsFound { get; init; } = new();
}

/// <summary>
/// Result returned by BuildFullPromptForTestingAsync — used by the Prompt Playground
/// to display intent, retrieved context, and the full prompt without an LLM call.
/// </summary>
public sealed class PromptPreviewResult
{
    public string PromptText        { get; set; } = string.Empty;
    public string DetectedIntent    { get; set; } = string.Empty;
    public string ProjectIndexStatus{ get; set; } = string.Empty;
    public string ContextQuality    { get; set; } = string.Empty;
    public List<IronDev.Data.Models.CodeIndexEntry> RetrievedItems { get; init; } = new();

    // ── Prompt Pollution Diagnostics (Fix 2) ──────────────────────────────
    /// <summary>True if any injected memory contained forbidden generic terms.</summary>
    public bool   ContextPolluted      { get; set; }
    /// <summary>Which forbidden terms were found in injected memory (before filtering).</summary>
    public List<string> PollutedTermsFound { get; init; } = new();
    /// <summary>How many memory items (decisions + tickets + summary) were filtered out.</summary>
    public int FilteredMemoryCount  { get; set; }
    /// <summary>How many memory items passed the filter and were included in the prompt.</summary>
    public int IncludedMemoryCount  { get; set; }
}

/// <summary>
/// Classifies the high-level intent of a user chat message so that retrieval
/// and prompt assembly can be tailored to the right area of the codebase.
/// </summary>
public enum ChatIntent
{
    General,
    CodeQuery,
    /// <summary>
    /// User is asking about saved/persisted ticket management
    /// (e.g. delete tickets, archive tickets, list tickets, ticket persistence).
    /// These questions relate to ProjectTicket / TicketsWorkspaceViewModel —
    /// NOT to DraftTicket / the Chat→Draft Ticket review flow.
    /// </summary>
    SavedTicketManagement,
    /// <summary>
    /// User is asking about the Chat→Draft Ticket generation/review flow,
    /// DraftTicket models, regenerating drafts, etc.
    /// </summary>
    DraftTicketFlow,
    /// <summary>
    /// User is asking for a global analysis or overview of the project structure/codebase.
    /// Grounding should pull in core architectural files.
    /// </summary>
    AnalyzeCodebase,
}

public interface IPromptContextBuilder
{
    Task<string> BuildAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default);
    Task<ChatContextPacket> BuildPacketAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Developer-only: builds the full prompt for a sample user message and returns
    /// the intent, retrieved context, and prompt text without making an LLM call.
    /// Used by the Prompt Playground.
    /// </summary>
    Task<PromptPreviewResult> BuildFullPromptForTestingAsync(int projectId, string userMessage, CancellationToken ct = default);
}

public sealed class PromptContextBuilder : IPromptContextBuilder
{
    private readonly IChatHistoryService    _chatHistoryService;
    private readonly IProjectMemoryService  _projectMemoryService;
    private readonly ICodeIndexService      _codeIndexService;
    private readonly ITicketService         _ticketService;
    private readonly IChatFeedbackService   _feedbackService;
    private readonly IProjectService        _projectService;

    public PromptContextBuilder(
        IChatHistoryService   chatHistoryService,
        IProjectMemoryService projectMemoryService,
        ICodeIndexService     codeIndexService,
        ITicketService        ticketService,
        IChatFeedbackService  feedbackService,
        IProjectService       projectService)
    {
        _chatHistoryService   = chatHistoryService;
        _projectMemoryService = projectMemoryService;
        _codeIndexService     = codeIndexService;
        _ticketService        = ticketService;
        _feedbackService      = feedbackService;
        _projectService       = projectService;
    }

    public async Task<string> BuildAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default)
    {
        var packet = await BuildPacketDataAsync(projectId, sessionId, userRequest, cancellationToken);
        return packet.FormattedPrompt;
    }

    public Task<ChatContextPacket> BuildPacketAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default)
    {
        return BuildPacketDataAsync(projectId, sessionId, userRequest, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PromptPreviewResult> BuildFullPromptForTestingAsync(int projectId, string userMessage, CancellationToken ct = default)
    {
        // Single pipeline pass — BuildPacketDataAsync fetches all DB data, runs the
        // memory filter, and records diagnostics on the packet. No extra DB calls here.
        var packet = await BuildPacketDataAsync(projectId, sessionId: 0, userRequest: userMessage, cancellationToken: ct);

        var project     = await _projectService.GetByIdAsync(projectId, ct);
        var indexStatus = project?.IndexingStatus ?? "Unknown";
        var quality     = string.Equals(indexStatus, "Ready", StringComparison.OrdinalIgnoreCase) ? "Indexed" : "Limited";

        // Retrieve and rank raw snippets for the playground list view
        var intent   = packet.Intent;
        var queries  = ExpandSearchQueries(userMessage, intent);
        var snippets = new List<IronDev.Data.Models.CodeIndexEntry>();
        foreach (var q in queries.Take(4))
        {
            var results = await _codeIndexService.GetRelevantSnippetsAsync(projectId, q, 5, ct);
            snippets.AddRange(results);
        }
        // Deduplicate before ranking: same (FilePath, SymbolName) pair only kept once
        var deduped = DeduplicateSnippets(snippets);
        var ranked  = RankSnippetsByIntent(deduped, intent, 14);

        // Diagnostics are already recorded on the packet by BuildPacketDataAsync
        // at each IsJunkMemory filter site — no extra DB calls needed here.
        var distinctPolluted = packet.PollutedTermsFound
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var preview = new PromptPreviewResult
        {
            PromptText          = packet.FormattedPrompt,
            DetectedIntent      = intent.ToString(),
            ProjectIndexStatus  = indexStatus,
            ContextQuality      = quality,
            ContextPolluted     = distinctPolluted.Count > 0,
            FilteredMemoryCount = packet.FilteredMemoryCount,
            IncludedMemoryCount = packet.IncludedMemoryCount,
        };
        preview.RetrievedItems.AddRange(ranked);
        preview.PollutedTermsFound.AddRange(distinctPolluted);
        return preview;
    }

    private async Task<ChatContextPacket> BuildPacketDataAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken)
    {
        var packet = new ChatContextPacket();

        // 1. Classify intent
        var intent = ClassifyIntent(userRequest);
        packet.Intent = intent;

        var isCodeQuery = intent == ChatIntent.CodeQuery || intent == ChatIntent.SavedTicketManagement;
        var ticketTake   = isCodeQuery ? 2 : 5;
        var decisionTake = isCodeQuery ? 2 : 5;
        var snippetTake  = isCodeQuery ? 8 : 3;

        // 2. Check project index status
        var project = await _projectService.GetByIdAsync(projectId, cancellationToken);
        var isNotIndexed = project?.IndexingStatus == null ||
                           !string.Equals(project.IndexingStatus, "Ready", StringComparison.OrdinalIgnoreCase);
        packet.IsProjectNotIndexed = isNotIndexed;

        var decisions = await _projectMemoryService.GetRecentDecisionsAsync(projectId, decisionTake, cancellationToken);
        var tickets   = await _ticketService.GetRecentTicketsAsync(projectId, ticketTake, cancellationToken);

        // 3. Build expanded search queries for the intent
        var queries = ExpandSearchQueries(userRequest, intent);

        // Fetch feedback preferences concurrently with snippet retrieval
        var feedbackTask = _feedbackService.GetProjectFeedbackSummaryAsync(projectId, cancellationToken);

        var snippetList = new List<IronDev.Data.Models.CodeIndexEntry>();
        foreach (var query in queries)
        {
            var results = await _codeIndexService.GetRelevantSnippetsAsync(projectId, query, snippetTake, cancellationToken);
            snippetList.AddRange(results);
        }

        // 4. Deduplicate using path+symbol+content dedup, then rank by intent
        //    Note: DeduplicateSnippets() is the canonical dedup — do not use GroupBy(Id) here.
        var deduped        = DeduplicateSnippets(snippetList);
        var rankedSnippets = RankSnippetsByIntent(deduped, intent, snippetTake);

        foreach (var r in rankedSnippets)
        {
            var shortChunk = r.ChunkText;
            if (shortChunk.Length > 800) shortChunk = shortChunk.Substring(0, 800) + "\n...[TRUNCATED]...";
            packet.Snippets.Add($"File: {r.FilePath}\nSymbol: {r.SymbolName}\n```\n{shortChunk}\n```");

            if (!string.IsNullOrWhiteSpace(r.FilePath) && !packet.MatchedFilePaths.Contains(r.FilePath))
                packet.MatchedFilePaths.Add(r.FilePath);
            if (!string.IsNullOrWhiteSpace(r.SymbolName) && !packet.MatchedSymbols.Contains(r.SymbolName))
                packet.MatchedSymbols.Add(r.SymbolName);
        }

        foreach (var t in tickets)
        {
            var content = string.IsNullOrWhiteSpace(t.Summary) ? t.Content : t.Summary;
            var ticketLine = $"[{t.TicketType}] {t.Title} ({t.Status}): {content}";

            // Intent-aware ticket exclusion:
            // For SavedTicketManagement, exclude tickets whose title/content is about the
            // DraftTicket subsystem — they describe Chat→Draft Ticket generation, not
            // saved-ticket persistence operations, and would pollute the context.
            if (intent == ChatIntent.SavedTicketManagement)
            {
                var titleLower = (t.Title ?? string.Empty).ToLowerInvariant();
                var bodyLower  = (content ?? string.Empty).ToLowerInvariant();
                if (titleLower.Contains("draft") || bodyLower.Contains("draftticket")
                    || bodyLower.Contains("codebasisticketgenerator") || bodyLower.Contains("ticket generator"))
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[PromptContextBuilder] Excluded DraftTicket-related ticket from SavedTicketManagement context: {t.Title}");
                    continue;
                }
            }

            packet.Tickets.Add(ticketLine);
        }

        foreach (var d in decisions)
        {
            packet.Decisions.Add($"{d.Title}: {d.Detail}");
        }

        var recentMessages = await _chatHistoryService.GetRecentMessagesAsync(projectId, sessionId, 8, cancellationToken);
        var latestSummary  = await _projectMemoryService.GetLatestSummaryAsync(projectId, cancellationToken);
        var feedbackPrefs  = await feedbackTask;

        // 5. Assemble the prompt
        var sb = new StringBuilder();
        sb.AppendLine("You are IronDev Architect, an expert AI assistant integrated into the IronDev engineering platform.");
        sb.AppendLine("IMPORTANT INSTRUCTIONS:");
        sb.AppendLine("1. Answer the user's question directly and concisely.");
        sb.AppendLine("2. Do NOT dump raw context/code unless explicitly requested. Use the provided snippets, tickets, and decisions quietly as supporting evidence.");
        sb.AppendLine("3. Summarize implementation flow in natural language.");
        sb.AppendLine("4. List the main files/classes involved when relevant.");
        sb.AppendLine("5. Mention uncertainty explicitly if the provided context is incomplete to fully answer the user's question.");
        sb.AppendLine();

        // Grounding-first rule — forces the model to answer from retrieved IronDev context only
        sb.AppendLine("GROUNDING-FIRST RULE (mandatory):");
        sb.AppendLine("You must answer ONLY from the retrieved IronDev project context provided below.");
        sb.AppendLine("Do NOT answer from general software engineering knowledge when the user is asking about this codebase.");
        sb.AppendLine("If the retrieved context does not prove that a method, class, or file exists in IronDev:");
        sb.AppendLine("  - Say 'inspect X to confirm' or 'add Y if missing' — do NOT say it exists.");
        sb.AppendLine("  - Do NOT use hedging language like 'likely', 'possibly', 'often something like', 'typically'.");
        sb.AppendLine("  - Do NOT invent file or method names not present in the retrieved snippets.");
        sb.AppendLine("If no snippets were retrieved, state clearly: 'Project context is limited — index the project for precise file recommendations.'");
        sb.AppendLine();

        // Anti-wrong-context rule (always present)
        sb.AppendLine("ARCHITECTURAL CONTEXT RULE:");
        sb.AppendLine("Do not assume DraftTicket is the saved ticket model.");
        sb.AppendLine("DraftTicket is ONLY for the Chat → Draft Ticket review flow (generating draft tickets from chat).");
        sb.AppendLine("For saved ticket management (delete, archive, list, select tickets), prefer: ProjectTicket / TicketsWorkspaceViewModel / TicketsWorkspaceView / TicketService / ticket persistence services.");
        sb.AppendLine("Do not recommend changing DraftTicketDtos.cs or CodebaseTicketGeneratorModels.cs for saved ticket operations.");
        sb.AppendLine("IronDev is a WPF application — do NOT mention Controllers, Repositories, TicketModel, TicketController, or ASP.NET MVC patterns.");
        sb.AppendLine("These terms do not exist in IronDev. Using them is a grounding failure.");
        sb.AppendLine();

        if (isCodeQuery)
        {
            sb.AppendLine("Since the user is asking an implementation or codebase-oriented question, please structure your response exactly as follows:");
            sb.AppendLine("- **Summary**: [High-level explanation of the code or system]");
            sb.AppendLine("- **Main files/classes involved**: [List of key files/symbols]");
            sb.AppendLine("- **How the flow works**: [Step by step natural language explanation]");
            sb.AppendLine("- **What to inspect next**: [Suggestions for the next files, classes, or tickets to look at]");
            sb.AppendLine();
        }

        if (intent == ChatIntent.AnalyzeCodebase)
        {
            sb.AppendLine("CODEBASE ANALYSIS CONTEXT:");
            sb.AppendLine("The user is asking for a global analysis or architectural overview of the project structure.");
            sb.AppendLine("Focus your answer on the core architecture: ShellViewModel, Workspace management, Service layer (TicketService, MemoryService), and Core interfaces.");
            sb.AppendLine("Your goal is to explain HOW the system is organized, citing specific files and classes from the snippets provided below.");
            sb.AppendLine("Avoid generic software architecture advice. If you see a specific pattern in the snippets (e.g. WPF MVVM with CommunityToolkit), mention it explicitly.");
            sb.AppendLine("If the user's request is broad, categorize your answer into: UI/ViewModels, Services/Logic, and Data Models.");
            sb.AppendLine();
        }

        if (intent == ChatIntent.SavedTicketManagement)
        {
            sb.AppendLine("SAVED TICKET MANAGEMENT CONTEXT:");
            sb.AppendLine("The user is asking about saved/persisted ticket management (e.g. deleting, archiving, listing tickets).");
            sb.AppendLine("Focus your answer on: TicketsWorkspaceViewModel, TicketsWorkspaceView.xaml, ProjectTicket, TicketService, GetTickets, SaveTicket, ticket persistence.");
            sb.AppendLine("For any delete/archive feature, a complete grounded answer MUST address all of:");
            sb.AppendLine("  1. Whether TicketService already has DeleteTicketAsync/ArchiveTicketAsync — say 'inspect to confirm, add if missing'.");
            sb.AppendLine("  2. A tenant/project ownership guard must be applied in TicketService before any destructive operation.");
            sb.AppendLine("  3. Prefer soft-delete (archive) before offering hard delete — explain the safety tradeoff.");
            sb.AppendLine("  4. A UI confirmation step in TicketsWorkspaceView.xaml before the command fires.");
            sb.AppendLine("  5. A command (e.g. ArchiveSelectedTicketCommand / DeleteSelectedTicketCommand) in TicketsWorkspaceViewModel.");
            sb.AppendLine("  6. Refresh the ticket list and clear SelectedTicket after deletion.");
            sb.AppendLine("  7. Check for linked ProjectImplementationPlans before hard delete.");
            sb.AppendLine("Do NOT use vague language like 'likely', 'usually', 'often something like'. Use 'inspect' or 'add if missing'.");
            sb.AppendLine("After giving your grounded answer, suggest using the Create Ticket feature if the user needs to track this work as a ticket.");
            sb.AppendLine();
        }

        sb.AppendLine("IMPORTANT LOGIC RULE:");
        sb.AppendLine("If you and the user finalize a new technical rule, architectural choice, or project decision during this turn, output a hidden XML tag block anywhere in your response like this:");
        sb.AppendLine("<decision>Decision Title | The detailed rule</decision>");
        sb.AppendLine();

        // Not-indexed warning
        if (isNotIndexed)
        {
            sb.AppendLine("⚠️ LIMITED CONTEXT WARNING: Project is not indexed (IndexingStatus is not 'Ready'), so affected files are best-effort.");
            sb.AppendLine("Do not present weak context as authoritative. Explicitly acknowledge that results may be incomplete.");
            sb.AppendLine();
        }

        // Ranked context section — high/low confidence split at 4
        if (rankedSnippets.Count > 0)
        {
            var highConf = rankedSnippets.Take(Math.Min(4, rankedSnippets.Count)).ToList();
            var lowConf  = rankedSnippets.Skip(4).ToList();

            sb.AppendLine("## Relevant project files (high confidence):");
            for (int i = 0; i < highConf.Count; i++)
            {
                var r = highConf[i];
                sb.AppendLine($"{i + 1}. {r.FilePath}");
                if (!string.IsNullOrWhiteSpace(r.SymbolName)) sb.AppendLine($"   Symbol: {r.SymbolName}");
                sb.AppendLine($"   Matched via: {GetMatchReason(r, intent)}");
            }
            sb.AppendLine();

            if (lowConf.Count > 0)
            {
                sb.AppendLine("## Potentially lower-confidence files:");
                for (int i = 0; i < lowConf.Count; i++)
                {
                    var r = lowConf[i];
                    sb.AppendLine($"{i + 1}. {r.FilePath}");
                    sb.AppendLine($"   Reason: lower priority match for this query intent");
                }
                sb.AppendLine("Use high-confidence files first. Do not recommend changing lower-confidence files unless the user explicitly asked about that area.");
                sb.AppendLine();
            }

            sb.AppendLine("## Code Snippets");
            foreach (var snippet in packet.Snippets)
            {
                sb.AppendLine(snippet);
                sb.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(latestSummary?.Summary))
        {
            var summary = latestSummary!.Summary.Trim();
            var (isJunkSummary, summaryTerms) = IsJunkMemory(summary);
            if (!isJunkSummary)
            {
                packet.IncludedMemoryCount++;
                sb.AppendLine("Project summary:");
                sb.AppendLine(summary);
                sb.AppendLine();
            }
            else
            {
                packet.FilteredMemoryCount++;
                packet.PollutedTermsFound.AddRange(summaryTerms);
            }
        }

        if (decisions.Count > 0)
        {
            sb.AppendLine("Important project decisions:");
            foreach (var decision in decisions.OrderBy(x => x.CreatedDate))
            {
                var detail = (decision.Detail ?? string.Empty).Trim();
                var title  = (decision.Title  ?? string.Empty).Trim();
                var (isJunk, decisionTerms) = IsJunkMemory($"{title}: {detail}");
                if (!isJunk)
                {
                    packet.IncludedMemoryCount++;
                    sb.AppendLine($"- {title}: {detail}");
                }
                else
                {
                    packet.FilteredMemoryCount++;
                    packet.PollutedTermsFound.AddRange(decisionTerms);
                }
            }
            sb.AppendLine();
        }

        if (recentMessages.Count > 0)
        {
            sb.AppendLine("Recent conversation:");
            foreach (var message in recentMessages)
            {
                sb.AppendLine($"{message.Role}: {message.Message}");
            }
            sb.AppendLine();
        }

        if (packet.Tickets.Count > 0)
        {
            sb.AppendLine("Relevant tickets:");
            foreach (var ticket in packet.Tickets)
            {
                var (isJunk, ticketTerms) = IsJunkMemory(ticket);
                if (!isJunk)
                {
                    packet.IncludedMemoryCount++;
                    sb.AppendLine($"- {ticket}");
                }
                else
                {
                    packet.FilteredMemoryCount++;
                    packet.PollutedTermsFound.AddRange(ticketTerms);
                }
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(feedbackPrefs))
        {
            sb.AppendLine(feedbackPrefs);
            sb.AppendLine();
        }

        sb.AppendLine("Current user request:");
        sb.AppendLine(userRequest);

        packet.FormattedPrompt = sb.ToString();
        return packet;
    }

    // ────────────────────────────────────────────────────────────────────
    // Memory Quality Filter
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Determines whether a memory item (ticket body, decision detail, summary)
    /// should be excluded from the prompt because it is junk, a placeholder response,
    /// or contains forbidden generic architectural terms that would pollute the context.
    /// Returns (isJunk: bool, pollutedTerms: list of matched forbidden terms).
    /// </summary>
    public static (bool isJunk, IReadOnlyList<string> pollutedTerms) IsJunkMemory(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < 20)
            return (true, Array.Empty<string>());

        var polluted = new List<string>();

        // ── Placeholder / AI-generated filler phrases ────────────────────────
        var junkPrefixes = new[]
        {
            "Certainly!",
            "It seems",
            "Could you",
            "Please provide",
            "Here is how",
            "Here's how",
            "In a typical",
            "In most",
            "As a general",
            "Great question",
            "Sure, here",
        };
        foreach (var prefix in junkPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return (true, Array.Empty<string>());
        }

        var junkContains = new[]
        {
            "It seems you",
            "It seems you're",
            "If you have a specific",
            "feel free to provide",
            "let me know if you need",
            "happy to help",
            "is a common approach",
            "in a typical application",
            "a placeholder for",
            "test content",
            "lorem ipsum",
            "Let's refine",
            "let's refine",
            "old chats",
            "typical approach",
            "please provide more specific",
            "provide more specific",
        };
        foreach (var phrase in junkContains)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return (true, Array.Empty<string>());
        }

        // ── Architectural poison terms — generic MVC/ORM concepts that do not exist in IronDev
        //    Detect BEFORE deciding to filter; if any are found the item is excluded
        //    AND the terms are reported to PromptPreviewResult.PollutedTermsFound.
        var archPoisonTerms = new[]
        {
            "TicketModel",
            "TicketController",
            "View Templates",
            "Repository",
            "typically include",
            "typically represented",
        };
        foreach (var term in archPoisonTerms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
                polluted.Add(term);
        }

        if (polluted.Count > 0)
            return (true, polluted);

        return (false, Array.Empty<string>());
    }

    // ────────────────────────────────────────────────────────────────────
    // Intent Classification
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies the user's chat message into a <see cref="ChatIntent"/>.
    ///
    /// SavedTicketManagement is detected FIRST to ensure that queries like
    /// "delete tickets affected files" are not misclassified as general
    /// ticket queries that would pull DraftTicket files.
    /// </summary>
    public static ChatIntent ClassifyIntent(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ChatIntent.General;
        var lower = text.ToLowerInvariant();

        // Draft ticket flow (check before saved-ticket because "draft ticket" is specific)
        if (IsDraftTicketQuery(lower)) return ChatIntent.DraftTicketFlow;

        // Saved ticket management — broad set of signals
        if (IsSavedTicketManagementQuery(lower)) return ChatIntent.SavedTicketManagement;

        // Analyze codebase — global architectural overview
        if (IsAnalyzeCodebaseQuery(lower)) return ChatIntent.AnalyzeCodebase;

        // Generic code / implementation query
        if (IsCodeQuery(lower)) return ChatIntent.CodeQuery;

        return ChatIntent.General;
    }

    private static bool IsDraftTicketQuery(string lower) =>
        lower.Contains("draft ticket") ||
        lower.Contains("chat to ticket") ||
        lower.Contains("chat → ticket") ||
        lower.Contains("chat -> ticket") ||
        lower.Contains("ticket generation") ||
        lower.Contains("regenerate ticket") ||
        lower.Contains("draft review") ||
        lower.Contains("approve draft") ||
        lower.Contains("draftticket");

    private static bool IsAnalyzeCodebaseQuery(string lower) =>
        lower.Contains("analyze codebase") ||
        lower.Contains("analyse codebase") ||
        lower.Contains("analyze the codebase") ||
        lower.Contains("project structure") ||
        lower.Contains("codebase overview") ||
        lower.Contains("architectural overview");

    public static bool IsSavedTicketManagementQuery(string lower) =>
        (lower.Contains("ticket") || lower.Contains("tickets")) &&
        (lower.Contains("delete") ||
         lower.Contains("remove") ||
         lower.Contains("archive") ||
         lower.Contains("ticket management") ||
         lower.Contains("ticket list") ||
         lower.Contains("ticket persistence") ||
         lower.Contains("saved ticket") ||
         lower.Contains("affect") ||         // "affected files for tickets"
         lower.Contains("implement ticket") ||
         lower.Contains("implement delete") ||
         lower.Contains("select ticket") ||
         lower.Contains("selected ticket") ||
         lower.Contains("list ticket") ||
         lower.Contains("workspace"));

    private static bool IsCodeQuery(string lower) =>
        lower.Contains("where is") ||
        lower.Contains("what file") ||
        lower.Contains("what code") ||
        lower.Contains("affected file") ||
        lower.Contains("summarize implementation") ||
        lower.Contains("how does") ||
        lower.Contains("how do i") ||
        lower.Contains("how can") ||
        lower.Contains("what does") ||
        lower.Contains("what do ") ||
        lower.Contains("what would") ||
        lower.Contains("what should") ||
        lower.Contains("what files") ||
        lower.Contains("implement") ||
        lower.Contains("what class") ||
        lower.Contains("which class") ||
        lower.Contains("set up") ||
        lower.Contains("configure") ||
        lower.Contains("fix grounding") ||
        lower.Contains("run with") ||
        lower.Contains("run irondev") ||
        lower.Contains("analyze codebase") ||
        lower.Contains("analyse codebase");

    // ────────────────────────────────────────────────────────────────────
    // Query Expansion
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a prioritised list of search terms to issue against the code
    /// index. For SavedTicketManagement queries the list is seeded with the
    /// known high-priority saved-ticket symbols so weak raw words (e.g.
    /// "ticket") do not dominate and pull in DraftTicket files.
    /// </summary>
    public static List<string> ExpandSearchQueries(string text, ChatIntent intent)
    {
        var queries = new List<string>();

        if (intent == ChatIntent.SavedTicketManagement)
        {
            // High-priority saved-ticket terms come first
            queries.AddRange(new[]
            {
                "TicketsWorkspaceViewModel",
                "TicketsWorkspaceView",
                "ITicketService",
                "TicketService",
                "ProjectTicket",
                "ProjectTickets",
                "SaveTicket",
                "GetTickets",
                "DeleteTicket",
                "ArchiveTicket",
                "selected ticket",
                "ticket list",
                "ticket persistence",
                "delete ticket",
                "archive ticket",
            });
        }
        else if (intent == ChatIntent.DraftTicketFlow)
        {
            queries.AddRange(new[]
            {
                "DraftTicket",
                "DraftTicketService",
                "IDraftTicketService",
                "GenerateDraft",
                "ApproveDraft",
                "ChatTicketContext",
            });
        }
        else if (intent == ChatIntent.AnalyzeCodebase)
        {
            // Pull core architectural files for a codebase overview
            queries.AddRange(new[]
            {
                "ShellViewModel",
                "AppShell",
                "MainViewModel",
                "INavigationService",
                "WorkspaceViewModel",
                "ProjectMemoryService",
                "IProjectMemoryService",
                "PromptContextBuilder",
                "DataModels",
                "ProjectTicket",
                "DraftTicket",
            });
        }

        // Always append the raw user terms as lower-priority fallbacks
        queries.AddRange(ExtractRawSearchTerms(text));

        return queries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ExtractRawSearchTerms(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        var words = text.Split(new[] { ' ', '\n', '\r', '\t', '?', '!', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var queries = new List<string>();

        // 1. Explicit filenames
        var fileLike = words.FirstOrDefault(w => w.Contains(".") && (w.EndsWith(".cs") || w.EndsWith(".xaml") || w.EndsWith(".sql") || w.EndsWith(".js") || w.EndsWith(".ts")));
        if (fileLike != null) queries.Add(fileLike.Trim('\'', '\"', '`'));

        // 2. CamelCase identifiers in the query
        var camelCase = words.FirstOrDefault(w => w.Length > 8 && char.IsUpper(w[0]) && w.Any(char.IsLower));
        if (camelCase != null) queries.Add(camelCase.Trim('\'', '\"', '`'));

        // 3. Keyword expansion
        var lower = text.ToLowerInvariant();
        if (lower.Contains("index"))                                        queries.Add("index");
        if (lower.Contains("login") || lower.Contains("auth"))              queries.Add("auth");
        if (lower.Contains("overview") || lower.Contains("dashboard"))      queries.Add("overview");
        if (lower.Contains("ticket") || lower.Contains("work item"))        queries.Add("ticket");
        if (lower.Contains("decision") || lower.Contains("architecture"))   queries.Add("decision");
        if (lower.Contains("chat") || lower.Contains("history"))            queries.Add("chat");
        if (lower.Contains("llm") || lower.Contains("ollama") || lower.Contains("provider")) queries.Add("LlmOptions");
        if (lower.Contains("grounding") || lower.Contains("context retrieval"))             queries.Add("PromptContextBuilder");
        if (lower.Contains("database") || lower.Contains("set up") || lower.Contains("setup")) queries.Add("local_dev_setup");

        // 4. Fallback: longest technical-looking word
        if (queries.Count == 0)
        {
            var fallback = words.Where(w => w.Length > 4).OrderByDescending(w => w.Length).FirstOrDefault();
            if (fallback != null) queries.Add(fallback.Trim('\'', '\"', '`'));
        }

        return queries.Distinct().ToList();
    }

    // ────────────────────────────────────────────────────────────────────
    // Snippet Deduplication
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes duplicate snippets from a merged multi-query result set.
    /// Two snippets are considered duplicates if they share the same (FilePath, SymbolName)
    /// pair, or if their ChunkText (trimmed, lowered) is identical.
    /// The first occurrence (highest-scored query result) is kept.
    /// </summary>
    public static List<IronDev.Data.Models.CodeIndexEntry> DeduplicateSnippets(
        IEnumerable<IronDev.Data.Models.CodeIndexEntry> snippets)
    {
        var seen         = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenChunks   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result       = new List<IronDev.Data.Models.CodeIndexEntry>();

        foreach (var s in snippets)
        {
            var key   = $"{s.FilePath ?? string.Empty}|{s.SymbolName ?? string.Empty}";
            var chunk = (s.ChunkText ?? string.Empty).Trim().ToLowerInvariant();

            // Primary dedup: same (FilePath, SymbolName) — always applied
            if (!seen.Add(key)) continue;

            // Content dedup: only applied for substantial chunks (≥50 chars)
            // to avoid false-positive deduplication of short test stubs / placeholder values
            if (chunk.Length >= 50 && !seenChunks.Add(chunk)) continue;

            result.Add(s);
        }
        return result;
    }

    // ────────────────────────────────────────────────────────────────────
    // Snippet Ranking
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-ranks snippets returned by the SQL index so that high-priority
    /// symbols for the detected intent appear first and low-priority symbols
    /// (e.g. DraftTicket files or test files) appear last.
    /// Deduplication is applied here as well to ensure the final list is clean.
    /// </summary>
    public static List<IronDev.Data.Models.CodeIndexEntry> RankSnippetsByIntent(
        List<IronDev.Data.Models.CodeIndexEntry> snippets,
        ChatIntent intent,
        int take)
    {
        // Always deduplicate first — prevents the same symbol appearing multiple times
        var deduped = DeduplicateSnippets(snippets);

        if (intent == ChatIntent.SavedTicketManagement)
        {
            // Hard-exclude DraftTicket snippets — they must never appear in SavedTicketManagement
            // results because DraftTicket is exclusively for Chat→Draft review, not saved-ticket ops.
            return deduped
                .Where(s => !IsDraftTicketSnippet(s))
                .OrderByDescending(s => ScoreSavedTicketRelevance(s))
                .Take(take)
                .ToList();
        }

        if (intent == ChatIntent.DraftTicketFlow)
        {
            return deduped
                .OrderByDescending(s => ScoreDraftTicketRelevance(s))
                .Take(take)
                .ToList();
        }

        // CodeQuery / General: production files > test files, otherwise preserve retrieval order
        return deduped
            .OrderByDescending(s => ScoreProductionPreference(s))
            .Take(take)
            .ToList();
    }

    /// <summary>
    /// Returns true if a snippet belongs to the DraftTicket/CodebaseTicketGenerator subsystem.
    /// These snippets must be excluded from SavedTicketManagement results — they only apply
    /// to the Chat → Draft Ticket review flow.
    /// </summary>
    public static bool IsDraftTicketSnippet(IronDev.Data.Models.CodeIndexEntry e)
    {
        var path   = e.FilePath   ?? string.Empty;
        var symbol = e.SymbolName ?? string.Empty;

        return ContainsAny(path,   "DraftTicket", "DraftTicketDto", "DraftTicketService",
                                    "CodebaseTicketGenerator", "CodebaseTicketGeneratorModels")
            || ContainsAny(symbol, "DraftTicket", "DraftTicketDto", "DraftTicketService",
                                    "IDraftTicketService", "CodebaseTicketGeneratorModels");
    }

    /// <summary>
    /// Scores a snippet for production-file preference (used for CodeQuery/General intents).
    /// Production sources outscore test projects.
    /// </summary>
    public static int ScoreProductionPreference(IronDev.Data.Models.CodeIndexEntry e)
    {
        var path  = e.FilePath ?? string.Empty;
        int score = 0;

        // Boost core production projects
        if (ContainsAny(path, "IronDeveloper/", "IronDev.Core/", "IronDev.Infrastructure/"))
            score += 20;

        // Demote test projects
        if (ContainsAny(path, "IntegrationTests", ".Tests/", "Test/", "Spec"))
            score -= 40;

        return score;
    }

    private static int ScoreSavedTicketRelevance(IronDev.Data.Models.CodeIndexEntry e)
    {
        var path   = e.FilePath   ?? string.Empty;
        var symbol = e.SymbolName ?? string.Empty;
        int score  = 0;

        // ── Safety guard: DraftTicket snippets should already be excluded by the
        //    hard-filter in RankSnippetsByIntent, but apply an extra penalty here
        //    as a belt-and-suspenders measure.
        if (IsDraftTicketSnippet(e))
            return -1000;

        // ── Tier 1: Core saved-ticket service symbols (highest priority) ─────
        // Use exact token matching to avoid DraftTicketService matching "TicketService"
        bool symbolIsTicketService = string.Equals(symbol, "ITicketService", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(symbol, "TicketService",  StringComparison.OrdinalIgnoreCase)
                                  || (symbol.Contains("TicketService", StringComparison.OrdinalIgnoreCase)
                                      && !symbol.Contains("Draft", StringComparison.OrdinalIgnoreCase));
        bool pathIsTicketService   = path.Contains("TicketService", StringComparison.OrdinalIgnoreCase)
                                  && !path.Contains("Draft", StringComparison.OrdinalIgnoreCase);
        if (symbolIsTicketService) score += 120;
        if (pathIsTicketService)   score += 110;

        // ── Tier 2: Primary UI/ViewModel symbols ─────────────────────────────
        if (ContainsAny(symbol, "TicketsWorkspaceViewModel", "TicketsWorkspaceView"))
            score += 100;
        if (ContainsAny(path, "TicketsWorkspaceView", "TicketsWorkspace"))
            score += 90;

        // ── Tier 3: Operation-specific symbols ───────────────────────────────
        if (ContainsAny(symbol, "ProjectTicket", "DeleteTicket", "ArchiveTicket", "SaveTicket",
                                "GetTicket", "GetTickets", "SelectedTicket", "ProjectTickets"))
            score += 80;
        if (ContainsAny(path, "ProjectTicket", "DataModels"))
            score += 60;

        // ── XAML preference: prefer .xaml over .xaml.cs for UI confirmation ──
        if (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            score += 30;
        else if (path.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase))
            score -= 10;

        // ── Production project preference ─────────────────────────────────────
        if (ContainsAny(path, "IronDeveloper/", "IronDev.Core/", "IronDev.Infrastructure/"))
            score += 20;

        // ── Medium priority: persistence / DB schema ─────────────────────────
        if (ContainsAny(path, "ProjectMemoryService", "Database/"))
            score += 40;
        if (ContainsAny(symbol, "ProjectMemoryService"))
            score += 30;

        // ── Demote test files ────────────────────────────────────────────────
        if (ContainsAny(path, "IntegrationTests", ".Tests/", "Spec", "Tests/"))
            score -= 60;

        return score;
    }

    private static int ScoreDraftTicketRelevance(IronDev.Data.Models.CodeIndexEntry e)
    {
        var path   = e.FilePath   ?? string.Empty;
        var symbol = e.SymbolName ?? string.Empty;
        int score  = 0;

        if (ContainsAny(symbol, "DraftTicket", "DraftTicketService", "IDraftTicketService", "GenerateDraft", "ApproveDraft", "ChatTicketContext"))
            score += 100;
        if (ContainsAny(path, "DraftTicketDto", "CodebaseTicketGenerator"))
            score += 80;

        return score;
    }

    private static bool ContainsAny(string source, params string[] terms) =>
        terms.Any(t => source.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static string GetMatchReason(IronDev.Data.Models.CodeIndexEntry e, ChatIntent intent) =>
        intent switch
        {
            ChatIntent.SavedTicketManagement => "saved ticket management — symbol/path matched ticket persistence terms",
            ChatIntent.DraftTicketFlow       => "draft ticket flow — symbol/path matched draft ticket terms",
            ChatIntent.CodeQuery             => "code query — matched implementation terms",
            _                               => "general match"
        };
}
