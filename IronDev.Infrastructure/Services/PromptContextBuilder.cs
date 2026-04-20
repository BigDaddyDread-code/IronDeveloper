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
}

public interface IPromptContextBuilder
{
    Task<string> BuildAsync(int projectId, Guid sessionId, string userRequest, CancellationToken cancellationToken = default);
    Task<ChatContextPacket> BuildPacketAsync(int projectId, Guid sessionId, string userRequest, CancellationToken cancellationToken = default);
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

    public async Task<string> BuildAsync(int projectId, Guid sessionId, string userRequest, CancellationToken cancellationToken = default)
    {
        var recentMessages = await _chatHistoryService.GetRecentMessagesAsync(projectId, sessionId, 8, cancellationToken);
        var latestSummary = await _projectMemoryService.GetLatestSummaryAsync(projectId, cancellationToken);
        var decisions = await _projectMemoryService.GetRecentDecisionsAsync(projectId, 8, cancellationToken);
        
        // Simple file search MVP: Find a word that might be a filename or keyword
        var query = ExtractSearchQuery(userRequest);
        var matchedFiles = new System.Collections.Generic.List<IronDev.Data.Models.ProjectFile>();
        
        if (!string.IsNullOrWhiteSpace(query))
        {
            var results = await _codeIndexService.SearchFilesAsync(projectId, query, 3, cancellationToken);
            matchedFiles.AddRange(results);
        }

        var sb = new StringBuilder();

        sb.AppendLine("You are assisting with the IronDev software project.");
        sb.AppendLine("IMPORTANT LOGIC RULE:");
        sb.AppendLine("If you and the user finalize a new technical rule, architectural choice, or project decision during this turn, output a hidden XML tag block anywhere in your response like this:");
        sb.AppendLine("<decision>Decision Title | The detailed rule</decision>");
        sb.AppendLine();

        if (matchedFiles.Count > 0)
        {
            sb.AppendLine("## Relevant Code Context");
            sb.AppendLine("The following file fragments match the request context:");
            sb.AppendLine();
            foreach (var file in matchedFiles)
            {
                sb.AppendLine($"### File: {file.FilePath}");
                sb.AppendLine("```");
                
                var content = file.Content;
                if (string.IsNullOrWhiteSpace(content))
                {
                    // Fallback to chunks
                    var symbols = await _codeIndexService.GetSymbolsAsync(file.Id, cancellationToken);
                    foreach (var s in symbols)
                    {
                        sb.AppendLine($"// Symbol: {s.Namespace}.{s.SymbolName} ({s.SymbolType})");
                        sb.AppendLine(s.ChunkText);
                        sb.AppendLine();
                    }
                }
                else
                {
                    if (content.Length > 4000) content = content.Substring(0, 4000) + "\n...[TRUNCATED]...";
                    sb.AppendLine(content);
                }
                sb.AppendLine("```");
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

        sb.AppendLine("Current user request:");
        sb.AppendLine(userRequest);

        return sb.ToString();
    }

    public async Task<ChatContextPacket> BuildPacketAsync(int projectId, Guid sessionId, string userRequest, CancellationToken cancellationToken = default)
    {
        var packet = new ChatContextPacket();
        var decisions = await _projectMemoryService.GetRecentDecisionsAsync(projectId, 3, cancellationToken);
        var tickets = await _ticketService.GetRecentTicketsAsync(projectId, 3, cancellationToken);
        var query = ExtractSearchQuery(userRequest);
        
        if (!string.IsNullOrWhiteSpace(query))
        {
            var results = await _codeIndexService.GetRelevantSnippetsAsync(projectId, query, 5, cancellationToken);
            foreach (var r in results)
            {
                packet.Snippets.Add($"File: {r.FilePath}\nSymbol: {r.SymbolName}\n```\n{r.ChunkText}\n```");
            }
        }

        foreach (var t in tickets)
        {
            packet.Tickets.Add($"[{t.TicketType}] {t.Title} ({t.Status})");
        }

        foreach (var d in decisions)
        {
            packet.Decisions.Add($"{d.Title}: {d.Detail}");
        }

        packet.FormattedPrompt = await BuildAsync(projectId, sessionId, userRequest, cancellationToken);

        return packet;
    }


    private static string ExtractSearchQuery(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        
        // Split by punctuation but keep dots for filenames
        var words = text.Split(new[] { ' ', '\n', '\r', '\t', '?', '!', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        
        // 1. Look for explicit filenames
        var fileLike = words.FirstOrDefault(w => w.Contains(".") && (w.EndsWith(".cs") || w.EndsWith(".xaml") || w.EndsWith(".sql") || w.EndsWith(".js") || w.EndsWith(".ts")));
        if (fileLike != null) return fileLike.Trim('\'', '\"', '`');
        
        // 2. Look for CamelCase words that might be class names (e.g., CodeWorkbenchViewModel)
        var camelCase = words.FirstOrDefault(w => w.Length > 8 && char.IsUpper(w[0]) && w.Any(char.IsLower));
        if (camelCase != null) return camelCase.Trim('\'', '\"', '`');

        // 3. Fallback: longest technical-looking word
        return words.Where(w => w.Length > 4)
            .OrderByDescending(w => w.Length)
            .FirstOrDefault() ?? string.Empty;
    }
}
