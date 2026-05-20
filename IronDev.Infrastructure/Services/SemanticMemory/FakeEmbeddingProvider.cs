using System;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services.SemanticMemory;

public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingOptions _options;

    public FakeEmbeddingProvider(EmbeddingOptions options)
    {
        _options = options;
    }

    public Task<EmbeddingResult> EmbedAsync(string input, CancellationToken ct = default)
    {
        int dimensions = _options.Dimensions > 0 ? _options.Dimensions : 1536;
        float[] vector = new float[dimensions];

        // Seed with a deterministic hash of the input text
        int hash = GetDeterministicHashCode(input);
        var random = new Random(hash);

        for (int i = 0; i < dimensions; i++)
        {
            vector[i] = (float)random.NextDouble() * 2f - 1f;
        }

        // Normalize the vector
        float sumOfSquares = 0f;
        for (int i = 0; i < dimensions; i++)
        {
            sumOfSquares += vector[i] * vector[i];
        }

        if (sumOfSquares > 0f)
        {
            float magnitude = (float)Math.Sqrt(sumOfSquares);
            for (int i = 0; i < dimensions; i++)
            {
                vector[i] /= magnitude;
            }
        }

        return Task.FromResult(new EmbeddingResult
        {
            Vector = vector,
            Model = _options.Model ?? "fake-embedding-model"
        });
    }

    private static int GetDeterministicHashCode(string str)
    {
        if (str == null)
            return 0;

        unchecked
        {
            int hash1 = (5381 << 16) + 5381;
            int hash2 = hash1;

            for (int i = 0; i < str.Length; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ str[i];
                if (i == str.Length - 1)
                    break;
                hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
            }

            return hash1 + (hash2 * 1566083941);
        }
    }
}
