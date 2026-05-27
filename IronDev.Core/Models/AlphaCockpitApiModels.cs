using IronDev.Data.Models;

namespace IronDev.Core.Models;

public sealed record SupersedeDecisionRequest
{
    public ProjectDecision Replacement { get; init; } = new();
}

public sealed record MemorySearchRequest
{
    public string Query { get; init; } = string.Empty;
    public int Take { get; init; } = 20;
    public bool IncludeStale { get; init; }
}

public sealed record MemorySearchResponseDto
{
    public int ProjectId { get; init; }
    public string Query { get; init; } = string.Empty;
    public Guid TraceId { get; init; } = Guid.Empty;
    public IReadOnlyList<MemorySearchResultDto> Results { get; init; } = [];
}

public sealed record MemorySearchResultDto
{
    public string ResultId { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Excerpt { get; init; } = string.Empty;
    public double Score { get; init; }
    public double AuthorityScore { get; init; }
    public int RawVectorRank { get; init; }
    public int FinalRank { get; init; }
    public string MatchReason { get; init; } = string.Empty;
    public Guid TraceId { get; init; } = Guid.Empty;
}

public sealed record MemoryStatusDto
{
    public int ProjectId { get; init; }
    public string ProviderName { get; init; } = string.Empty;
    public string ProviderStatus { get; init; } = string.Empty;
    public int DocumentCount { get; init; }
    public int EmbeddedCount { get; init; }
    public int StaleEmbeddingCount { get; init; }
    public DateTime? LastEmbeddedAtUtc { get; init; }
    public DateTime? LastRebuildAtUtc { get; init; }
}

public sealed record MemoryReindexResponseDto
{
    public int ProjectId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record ProjectServicesStatusDto
{
    public int ProjectId { get; init; }
    public string ApiStatus { get; init; } = "healthy";
    public string DatabaseStatus { get; init; } = string.Empty;
    public string MemoryStatus { get; init; } = string.Empty;
    public string TestAgentAvailability { get; init; } = "not_exposed";
    public IReadOnlyList<string> ConfiguredModelProfiles { get; init; } = [];
    public IReadOnlyList<string> WorkspacePaths { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
