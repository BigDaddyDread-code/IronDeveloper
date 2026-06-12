namespace IronDev.Client;

public sealed record DogfoodLoopCreateRequest
{
    public int ProjectId { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;
    public IReadOnlyList<string> Observations { get; init; } = [];
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];
    public IReadOnlyList<string> AgentRunIds { get; init; } = [];
    public IReadOnlyList<string> CriticReviewRunIds { get; init; } = [];
    public IReadOnlyList<string> MemoryImprovementRunIds { get; init; } = [];
    public IReadOnlyList<string> ToolRequestIds { get; init; } = [];
    public IReadOnlyList<string> ToolGateDecisionIds { get; init; } = [];
    public IReadOnlyList<DogfoodLoopEvidenceReference> EvidenceRefs { get; init; } = [];
    public string? CorrelationId { get; init; }
}

public sealed record DogfoodLoopEvidenceReference
{
    public string RefType { get; init; } = "cli_evidence";
    public string RefId { get; init; } = string.Empty;
    public string Summary { get; init; } = "Caller-supplied CLI evidence reference.";
    public string Source { get; init; } = "cli";
}
