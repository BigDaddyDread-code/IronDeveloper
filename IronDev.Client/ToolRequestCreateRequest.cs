namespace IronDev.Client;

public sealed record ToolRequestCreateRequest
{
    public int ProjectId { get; init; }
    public string RequestKind { get; init; } = string.Empty;
    public string ToolKind { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> InputRefs { get; init; } = [];
    public IReadOnlyList<string> PolicyRefs { get; init; } = [];
    public string? RiskLevel { get; init; }
    public bool? DryRunRequired { get; init; }
    public string? CorrelationId { get; init; }
}
