namespace IronDev.Core.AgentMemory.Evaluation;

public sealed record MemoryQualityBenchmarkDefinition
{
    public required string BenchmarkId { get; init; }
    public required int Version { get; init; }
    public required IReadOnlyList<MemoryQualityBenchmarkCase> Cases { get; init; }
    public required IReadOnlyList<MemoryQualityObservedResult> ReferenceResults { get; init; }
}

public sealed record MemoryQualityBenchmarkCase
{
    public required string CaseId { get; init; }
    public required string Category { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string Query { get; init; }
    public string? ExpectedTop1 { get; init; }
    public IReadOnlyList<string> RequiredTop5 { get; init; } = [];
    public IReadOnlyList<string> ForbiddenScopeIds { get; init; } = [];
    public IReadOnlyList<string> StaleIds { get; init; } = [];
    public IReadOnlyList<MemoryAuthorityOrderPair> AuthorityOrder { get; init; } = [];
    public bool ExpectNoResult { get; init; }
}

public sealed record MemoryAuthorityOrderPair
{
    public required string Higher { get; init; }
    public required string Lower { get; init; }
}

public sealed record MemoryQualityObservedResult
{
    public required string CaseId { get; init; }
    public IReadOnlyList<string> ResultIds { get; init; } = [];
}

public sealed record MemoryQualityBenchmarkReport
{
    public required string BenchmarkId { get; init; }
    public required int Version { get; init; }
    public required int ScorableCaseCount { get; init; }
    public required double Top1Accuracy { get; init; }
    public required double Top5Accuracy { get; init; }
    public required int WrongScopeResultCount { get; init; }
    public required int StaleResultCount { get; init; }
    public required int AuthorityOrderErrors { get; init; }
    public required int NoResultErrors { get; init; }
    public required bool Acceptable { get; init; }
}

public static class MemoryQualityBenchmarkEvaluator
{
    public static MemoryQualityBenchmarkReport Evaluate(MemoryQualityBenchmarkDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var observed = definition.ReferenceResults.ToDictionary(result => result.CaseId, StringComparer.Ordinal);
        var scorable = definition.Cases.Where(item => item.ExpectedTop1 is not null).ToArray();
        var top1 = 0;
        var top5 = 0;
        var wrongScope = 0;
        var stale = 0;
        var authorityErrors = 0;
        var noResultErrors = 0;

        foreach (var item in definition.Cases)
        {
            var ids = observed.GetValueOrDefault(item.CaseId)?.ResultIds ?? [];
            if (item.ExpectNoResult && ids.Count != 0) noResultErrors++;
            if (item.ExpectedTop1 is not null)
            {
                if (ids.FirstOrDefault() == item.ExpectedTop1) top1++;
                var firstFive = ids.Take(5).ToHashSet(StringComparer.Ordinal);
                if (item.RequiredTop5.All(firstFive.Contains)) top5++;
            }

            wrongScope += ids.Count(item.ForbiddenScopeIds.Contains);
            stale += ids.Count(item.StaleIds.Contains);
            foreach (var order in item.AuthorityOrder)
            {
                var high = IndexOf(ids, order.Higher);
                var low = IndexOf(ids, order.Lower);
                if (low >= 0 && (high < 0 || low < high)) authorityErrors++;
            }
        }

        var top1Accuracy = scorable.Length == 0 ? 0 : (double)top1 / scorable.Length;
        var top5Accuracy = scorable.Length == 0 ? 0 : (double)top5 / scorable.Length;
        return new MemoryQualityBenchmarkReport
        {
            BenchmarkId = definition.BenchmarkId,
            Version = definition.Version,
            ScorableCaseCount = scorable.Length,
            Top1Accuracy = top1Accuracy,
            Top5Accuracy = top5Accuracy,
            WrongScopeResultCount = wrongScope,
            StaleResultCount = stale,
            AuthorityOrderErrors = authorityErrors,
            NoResultErrors = noResultErrors,
            Acceptable = top1Accuracy >= 0.80 && top5Accuracy == 1.0 && wrongScope == 0 && stale == 0 && authorityErrors == 0 && noResultErrors == 0
        };
    }

    private static int IndexOf(IReadOnlyList<string> values, string expected)
    {
        for (var index = 0; index < values.Count; index++)
            if (string.Equals(values[index], expected, StringComparison.Ordinal)) return index;
        return -1;
    }
}
