using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Services;

namespace IronDev.AI;

public class ChatContextPacket
{
    public System.Collections.Generic.List<string> Snippets { get; init; } = new();
    public System.Collections.Generic.List<string> Tickets { get; init; } = new();
    public System.Collections.Generic.List<string> Decisions { get; init; } = new();
    public string FormattedPrompt { get; set; } = string.Empty;

    /// <summary>Structured file paths from matched code snippets.</summary>
    public System.Collections.Generic.List<string> MatchedFilePaths { get; init; } = new();
    /// <summary>Structured symbol names from matched code snippets.</summary>
    public System.Collections.Generic.List<string> MatchedSymbols { get; init; } = new();
}

public interface IPromptContextBuilder
{
    Task<string> BuildAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default);
    Task<ChatContextPacket> BuildPacketAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken = default);
}

public sealed class PromptContextBuilder : IPromptContextBuilder
{
    private readonly IChatHistoryService _chatHistoryService;
    private readonly IProjectMemoryService _projectMemoryService;
    private readonly ICodeIndexService _codeIndexService;
    private readonly ITicketService _ticketService;

    public PromptContextBuilder(
        IChatHistoryService chatHistoryService,
        IProjectMemoryService projectMemoryService,
        ICodeIndexService codeIndexService,
        ITicketService ticketService)
    {
        _chatHistoryService = chatHistoryService;
        _projectMemoryService = projectMemoryService;
        _codeIndexService = codeIndexService;
        _ticketService = ticketService;
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

    private async Task<ChatContextPacket> BuildPacketDataAsync(int projectId, long sessionId, string userRequest, CancellationToken cancellationToken)
    {
        var packet = new ChatContextPacket();
        var isCodeQuery = IsCodeQuery(userRequest);
        var ticketTake = isCodeQuery ? 2 : 5;
        var decisionTake = isCodeQuery ? 2 : 5;
        var snippetTake = isCodeQuery ? 5 : 3;

        var decisions = await _projectMemoryService.GetRecentDecisionsAsync(projectId, decisionTake, cancellationToken);
        var tickets = await _ticketService.GetRecentTicketsAsync(projectId, ticketTake, cancellationToken);
        var queries = ExtractSearchQueries(userRequest);
        
        var snippetList = new System.Collections.Generic.List<IronDev.Data.Models.CodeIndexEntry>();

        foreach (var query in queries)
        {
            var results = await _codeIndexService.GetRelevantSnippetsAsync(projectId, query, snippetTake, cancellationToken);
            snippetList.AddRange(results);
        }

        var topSnippets = snippetList.GroupBy(x => x.Id).Select(g => g.First()).Take(snippetTake).ToList();

        foreach (var r in topSnippets)
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
            packet.Tickets.Add($"[{t.TicketType}] {t.Title} ({t.Status}): {content}");
        }

        foreach (var d in decisions)
        {
            packet.Decisions.Add($"{d.Title}: {d.Detail}");
        }

        var recentMessages = await _chatHistoryService.GetRecentMessagesAsync(projectId, sessionId, 8, cancellationToken);
        var latestSummary = await _projectMemoryService.GetLatestSummaryAsync(projectId, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("You are IronDev Architect, an expert AI assistant integrated into the IronDev engineering platform.");
        sb.AppendLine("IMPORTANT INSTRUCTIONS:");
        sb.AppendLine("1. Answer the user's question directly and concisely.");
        sb.AppendLine("2. Do NOT dump raw context/code unless explicitly requested. Use the provided snippets, tickets, and decisions quietly as supporting evidence.");
        sb.AppendLine("3. Summarize implementation flow in natural language.");
        sb.AppendLine("4. List the main files/classes involved when relevant.");
        sb.AppendLine("5. Mention uncertainty explicitly if the provided context is incomplete to fully answer the user's question.");
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

        sb.AppendLine("IMPORTANT LOGIC RULE:");
        sb.AppendLine("If you and the user finalize a new technical rule, architectural choice, or project decision during this turn, output a hidden XML tag block anywhere in your response like this:");
        sb.AppendLine("<decision>Decision Title | The detailed rule</decision>");
        sb.AppendLine();

        if (topSnippets.Count > 0)
        {
            sb.AppendLine("## Relevant Code Snippets");
            foreach (var snippet in packet.Snippets)
            {
                sb.AppendLine(snippet);
                sb.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(latestSummary?.Summary))
        {
            sb.AppendLine("Project summary:");
            sb.AppendLine(latestSummary.Summary);
            sb.AppendLine();
        }

        if (decisions.Count > 0)
        {
            sb.AppendLine("Important project decisions:");
            foreach (var decision in decisions.OrderBy(x => x.CreatedDate))
            {
                sb.AppendLine($"- {decision.Title}: {decision.Detail}");
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
                sb.AppendLine($"- {ticket}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Current user request:");
        sb.AppendLine(userRequest);

        packet.FormattedPrompt = sb.ToString();
        return packet;
    }

    private static bool IsCodeQuery(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("where is") || 
               lower.Contains("what file") || 
               lower.Contains("what code") || 
               lower.Contains("summarize implementation") ||
               lower.Contains("how does");
    }


    private static System.Collections.Generic.List<string> ExtractSearchQueries(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new System.Collections.Generic.List<string>();
        
        var words = text.Split(new[] { ' ', '\n', '\r', '\t', '?', '!', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var queries = new System.Collections.Generic.List<string>();
        
        // 1. Explicit filenames
        var fileLike = words.FirstOrDefault(w => w.Contains(".") && (w.EndsWith(".cs") || w.EndsWith(".xaml") || w.EndsWith(".sql") || w.EndsWith(".js") || w.EndsWith(".ts")));
        if (fileLike != null) queries.Add(fileLike.Trim('\'', '\"', '`'));
        
        // 2. CamelCase
        var camelCase = words.FirstOrDefault(w => w.Length > 8 && char.IsUpper(w[0]) && w.Any(char.IsLower));
        if (camelCase != null) queries.Add(camelCase.Trim('\'', '\"', '`'));

        // 3. Keyword expansion
        var lower = text.ToLowerInvariant();
        if (lower.Contains("index")) queries.Add("index");
        if (lower.Contains("login") || lower.Contains("auth")) queries.Add("auth");
        if (lower.Contains("overview") || lower.Contains("dashboard")) queries.Add("overview");
        if (lower.Contains("ticket") || lower.Contains("work item")) queries.Add("ticket");
        if (lower.Contains("decision") || lower.Contains("architecture")) queries.Add("decision");

        // 4. Fallback: longest technical-looking word
        if (queries.Count == 0)
        {
            var fallback = words.Where(w => w.Length > 4).OrderByDescending(w => w.Length).FirstOrDefault();
            if (fallback != null) queries.Add(fallback.Trim('\'', '\"', '`'));
        }

        return queries.Distinct().ToList();
    }
}
