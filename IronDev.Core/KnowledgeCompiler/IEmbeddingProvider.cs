using System.Threading;
using System.Threading.Tasks;

namespace IronDev.Core.KnowledgeCompiler;

public interface IEmbeddingProvider
{
    Task<EmbeddingResult> EmbedAsync(string input, CancellationToken ct = default);
}
