using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public class StaticIronDevAgent : IIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;

    public StaticIronDevAgent(AgentDefinition definition, IAgentModelResolver modelResolver)
    {
        Definition = definition;
        _modelResolver = modelResolver;
    }

    protected AgentDefinition Definition { get; }

    public string AgentName => Definition.Name;
    public string Purpose => Definition.Purpose;
    public string DefaultModelProfile => Definition.DefaultModelProfile;
    public IReadOnlyList<string> AllowedTools => Definition.AllowedTools;

    public virtual Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);

        return Task.FromResult(new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Skipped,
            Summary = $"{AgentName} is registered but not implemented in 014.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model
        });
    }
}
