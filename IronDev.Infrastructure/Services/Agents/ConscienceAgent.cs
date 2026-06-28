using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class ConscienceAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;

    public ConscienceAgent(AgentDefinition definition, IAgentModelResolver modelResolver)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
    }

    public override Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var actionType = ReadInput(request, "action_type", string.Empty);
        var observedProject = ReadInput(request, "observed_project", string.Empty);
        var affectedProject = ReadInput(request, "affected_project", string.Empty);
        var evidence = SplitInput(ReadInput(request, "evidence", string.Empty));
        var requestedTools = SplitInput(ReadInput(request, "requested_tools", string.Empty));
        var memoryAuthorityRefs = SplitInput(ReadInput(request, "memory_authority_refs", string.Empty));
        var safetyBoundaryRefs = SplitInput(ReadInput(request, "safety_boundary_refs", string.Empty));

        var result = ConsciencePolicyDecisionEvaluator.Evaluate(new ConsciencePolicyDecisionRequest
        {
            ActionType = actionType,
            ObservedProject = observedProject,
            AffectedProject = affectedProject,
            Evidence = evidence,
            RequestedTools = requestedTools,
            MemoryAuthorityRefs = memoryAuthorityRefs,
            SafetyBoundaryRefs = safetyBoundaryRefs
        });

        return Task.FromResult(new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Succeeded,
            Summary = $"ConscienceAgent decision={result.Decision} for {actionType}.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = 0,
            OutputJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            CommandsRun = [$"conscience review --action-type {QuoteIfNeeded(actionType)}"],
            EvidencePaths = [],
            CompletedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static string ReadInput(AgentRequest request, string key, string defaultValue) =>
        request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;

    private static IReadOnlyList<string> SplitInput(string value) =>
        value.Split(['|', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
