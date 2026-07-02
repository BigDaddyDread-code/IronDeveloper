using System.Text.Json.Serialization;

namespace IronDev.Core.Agents;

public sealed record ConsciencePolicyDecisionRequest
{
    public required string ActionType { get; init; }
    public required string ObservedProject { get; init; }
    public required string AffectedProject { get; init; }
    public IReadOnlyList<string> Evidence { get; init; } = [];
    public IReadOnlyList<string> RequestedTools { get; init; } = [];
    public IReadOnlyList<string> MemoryAuthorityRefs { get; init; } = [];
    public IReadOnlyList<string> SafetyBoundaryRefs { get; init; } = [];
}

public sealed record ConsciencePolicyDecision
{
    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("confidence")]
    public required decimal Confidence { get; init; }

    [JsonPropertyName("reasons")]
    public IReadOnlyList<string> Reasons { get; init; } = [];

    [JsonPropertyName("allowingFactors")]
    public IReadOnlyList<string> AllowingFactors { get; init; } = [];

    [JsonPropertyName("blockingFactors")]
    public IReadOnlyList<string> BlockingFactors { get; init; } = [];

    [JsonPropertyName("missingEvidence")]
    public IReadOnlyList<string> MissingEvidence { get; init; } = [];

    [JsonPropertyName("violatedBoundaries")]
    public IReadOnlyList<string> ViolatedBoundaries { get; init; } = [];

    [JsonPropertyName("requiredNextSteps")]
    public IReadOnlyList<string> RequiredNextSteps { get; init; } = [];

    [JsonPropertyName("observedProject")]
    public required string ObservedProject { get; init; }

    [JsonPropertyName("affectedProject")]
    public required string AffectedProject { get; init; }

    [JsonPropertyName("authoritySources")]
    public IReadOnlyList<string> AuthoritySources { get; init; } = [];

    [JsonPropertyName("requestedTools")]
    public IReadOnlyList<string> RequestedTools { get; init; } = [];

    [JsonPropertyName("boundary")]
    public required string Boundary { get; init; }
}
