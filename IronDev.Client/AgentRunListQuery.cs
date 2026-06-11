namespace IronDev.Client;

public sealed record AgentRunListQuery
{
    public required int ProjectId { get; init; }
    public string? AgentId { get; init; }
    public string? AgentKind { get; init; }
    public string? Status { get; init; }
    public string? TriggerType { get; init; }
    public string? CreatedAfterUtc { get; init; }
    public string? CreatedBeforeUtc { get; init; }
    public string? RunId { get; init; }
    public string? CorrelationId { get; init; }
    public int? Take { get; init; }
    public int? Skip { get; init; }
}
