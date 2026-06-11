namespace IronDev.Client;

public sealed record ManualCriticReviewCreateRequest
{
    public int ProjectId { get; init; }
    public string TargetAgentRunId { get; init; } = string.Empty;
    public string? ReviewKind { get; init; }
    public string? Focus { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public string? CorrelationId { get; init; }
    public string? Reason { get; init; }
}
