using IronDev.Data.Models;

namespace IronDev.Core.KnowledgeCompiler;

public interface IEmbeddingContentExtractor
{
    string Extract(ProjectContextDocument document);
}
