using System;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using OpenAI.Embeddings;

namespace IronDev.Infrastructure.Services.SemanticMemory;

public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly EmbeddingOptions _options;

    public OpenAiEmbeddingProvider(EmbeddingOptions options)
    {
        _options = options;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new ArgumentException("API key is required for OpenAI embedding provider.");
        }

        _embeddingClient = new EmbeddingClient(options.Model ?? "text-embedding-3-small", options.ApiKey);
    }

    public async Task<EmbeddingResult> EmbedAsync(string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new EmbeddingResult
            {
                Vector = new float[_options.Dimensions > 0 ? _options.Dimensions : 1536],
                Model = _options.Model ?? "text-embedding-3-small"
            };
        }

        try
        {
            var response = await _embeddingClient.GenerateEmbeddingAsync(input, cancellationToken: ct);
            var floats = response.Value.ToFloats().ToArray();

            return new EmbeddingResult
            {
                Vector = floats,
                Model = _options.Model ?? "text-embedding-3-small"
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI Embedding generation failed: {ex.Message}", ex);
        }
    }
}
