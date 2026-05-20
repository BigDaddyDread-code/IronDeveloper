using System;

namespace IronDev.Infrastructure.Services.SemanticMemory;

public static class CosineSimilarity
{
    public static float Compute(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length == 0 || b.Length == 0 || a.Length != b.Length)
            return 0f;

        double dotProduct = 0.0;
        double magnitudeA = 0.0;
        double magnitudeB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        if (magnitudeA == 0.0 || magnitudeB == 0.0)
            return 0f;

        return (float)(dotProduct / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB)));
    }
}
