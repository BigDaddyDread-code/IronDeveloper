using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Services;

namespace IronDev.AI;

public interface IPromptContextBuilder
{
    Task<string> BuildAsync(int projectId, Guid sessionId, string userRequest, CancellationToken cancellationToken = default);
}

public sealed class PromptContextBuilder : IPromptContextBuilder
{
    private readonly IChatHistoryService _chatHistoryService;
    private readonly IProjectMemoryService _projectMemoryService;
    private readonly ICodeIndexService _codeIndexService;

    public PromptContextBuilder(
        IChatHistoryService chatHistoryService,
        IProjectMemoryService projectMemoryService,
        ICodeIndexService codeIndexService)
    {
        _chatHistoryService = chatHistoryService;
        _projectMemoryService = projectMemoryService;
        _codeIndexService = codeIndexService;
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
