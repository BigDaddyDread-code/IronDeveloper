namespace IronDev.Core.Agents;

public enum AgentRunStatus
{
    Succeeded,
    Failed,
    Blocked,
    Skipped
}

public sealed class AgentDefinition
{
    public required string Name { get; init; }
    public required string Purpose { get; init; }
    public required string DefaultModelProfile { get; init; }
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
}

public sealed class ModelProfile
{
    public required string Name { get; init; }
    public string Provider { get; init; } = "OpenAI";
    public required string Model { get; init; }
    public double Temperature { get; init; } = 0.2;
    public int MaxOutputTokens { get; init; } = 2000;
    public decimal? MaxCostPerRun { get; init; }
}

public sealed class AgentRequest
{
    public required string AgentName { get; init; }
    public string GoalId { get; init; } = string.Empty;
    public string DogfoodRunId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Inputs { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class AgentResult
{
    public required string AgentName { get; init; }
    public AgentRunStatus Status { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string ModelProfileName { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public string OutputJson { get; init; } = string.Empty;
    public IReadOnlyList<string> CommandsRun { get; init; } = [];
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
