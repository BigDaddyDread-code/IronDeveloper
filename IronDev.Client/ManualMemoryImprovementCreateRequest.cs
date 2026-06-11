namespace IronDev.Client;

public sealed record ManualMemoryImprovementCreateRequest
{
    public int ProjectId { get; init; }
    public string TargetAgentRunId { get; init; } = string.Empty;
    public string? Focus { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public string? CorrelationId { get; init; }
}
