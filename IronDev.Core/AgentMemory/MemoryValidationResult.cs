namespace IronDev.Core.AgentMemory;

public sealed record MemoryValidationResult
{
    public required bool IsValid { get; init; }

    public required IReadOnlyList<MemoryValidationIssue> Issues { get; init; }

    public static MemoryValidationResult Valid() =>
        new()
        {
            IsValid = true,
            Issues = Array.Empty<MemoryValidationIssue>()
        };
}
