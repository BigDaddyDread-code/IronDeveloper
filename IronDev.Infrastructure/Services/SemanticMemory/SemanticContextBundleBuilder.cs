using System.Text;
using IronDev.Core.KnowledgeCompiler;

namespace IronDev.Infrastructure.Services.SemanticMemory;

internal static class SemanticContextBundleBuilder
{
    public static SemanticContextBundle Build(
        int projectId,
        string query,
        string callerContext,
        IReadOnlyList<SemanticSearchResult> results,
        IReadOnlyList<string>? warnings = null)
    {
        return new SemanticContextBundle
        {
            ProjectId = projectId,
            Query = query,
            CallerContext = callerContext,
            Results = results,
            PromptContextMarkdown = BuildMarkdown(results),
            Warnings = warnings ?? []
        };
    }

    private static string BuildMarkdown(IReadOnlyList<SemanticSearchResult> results)
    {
        if (results.Count == 0)
            return "No semantic memory matches were retrieved.";

        var sb = new StringBuilder();
        sb.AppendLine("## Retrieved Project Memory");
        sb.AppendLine();

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var doc = result.Document;
            sb.AppendLine($"### {i + 1}. {doc.Title}");
            sb.AppendLine();
            sb.AppendLine($"- Type: {doc.DocumentType}");
            sb.AppendLine($"- Authority: {doc.AuthorityLevel}");
            sb.AppendLine($"- Score: {result.FinalScore:F2}");
            sb.AppendLine($"- Similarity: {result.SimilarityScore:F2}");
            sb.AppendLine($"- Match: {result.MatchReason}");
            if (result.IsStale)
                sb.AppendLine("- Warning: embedding may be stale");
            if (!string.IsNullOrWhiteSpace(doc.Summary))
                sb.AppendLine($"- Summary: {doc.Summary}");
            sb.AppendLine();
            sb.AppendLine(doc.Content);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }
}
