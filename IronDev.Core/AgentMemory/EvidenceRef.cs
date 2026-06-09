namespace IronDev.Core.AgentMemory;

public sealed record EvidenceRef
{
    public required string EvidenceId { get; init; }

    public required EvidenceType EvidenceType { get; init; }

    public required string SourceId { get; init; }

    public string? SourceUri { get; init; }

    public string? Summary { get; init; }

    public DateTimeOffset? CapturedAt { get; init; }
}
