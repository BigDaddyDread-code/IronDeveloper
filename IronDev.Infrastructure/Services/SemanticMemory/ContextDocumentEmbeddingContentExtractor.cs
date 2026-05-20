using System.Text;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;

namespace IronDev.Infrastructure.Services.SemanticMemory;

public sealed class ContextDocumentEmbeddingContentExtractor : IEmbeddingContentExtractor
{
    public string Extract(ProjectContextDocument document)
    {
        if (document == null)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"Title: {document.Title}");
        sb.AppendLine($"Document Type: {document.DocumentType}");
        sb.AppendLine($"Authority Level: {document.AuthorityLevel}");
        
        if (!string.IsNullOrWhiteSpace(document.AppliesToCapability))
            sb.AppendLine($"Applies to Capability: {document.AppliesToCapability}");

        if (!string.IsNullOrWhiteSpace(document.AppliesToArea))
            sb.AppendLine($"Applies to Area: {document.AppliesToArea}");

        if (!string.IsNullOrWhiteSpace(document.Tags))
            sb.AppendLine($"Tags: {document.Tags}");

        if (!string.IsNullOrWhiteSpace(document.Summary))
            sb.AppendLine($"Summary: {document.Summary}");

        sb.AppendLine("Content:");
        sb.AppendLine(document.Content);

        return sb.ToString();
    }
}
