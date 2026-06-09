namespace IronDev.Core.AgentMemory;

public sealed record MemoryValidationIssue
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public required MemoryValidationSeverity Severity { get; init; }
}
