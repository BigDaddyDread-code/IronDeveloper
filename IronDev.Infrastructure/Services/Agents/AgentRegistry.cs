using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class AgentRegistry : IAgentRegistry
{
    private readonly Dictionary<string, AgentDefinition> _definitions;
    private readonly Dictionary<string, IIronDevAgent> _agents;

    public AgentRegistry(IEnumerable<IIronDevAgent> agents, IEnumerable<AgentDefinition>? definitions = null)
    {
        _agents = agents.ToDictionary(agent => agent.AgentName, StringComparer.OrdinalIgnoreCase);
        _definitions = (definitions ?? AgentModelDefaults.CreateDefaultDefinitions())
            .ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AgentDefinition> ListDefinitions() => _definitions.Values.OrderBy(definition => definition.Name).ToArray();

    public AgentDefinition GetDefinition(string agentName)
    {
        if (_definitions.TryGetValue(agentName, out var definition))
            return definition;

        throw new InvalidOperationException($"Unknown IronDev agent definition: {agentName}");
    }

    public IIronDevAgent GetAgent(string agentName)
    {
        if (_agents.TryGetValue(agentName, out var agent))
            return agent;

        throw new InvalidOperationException($"IronDev agent is not registered: {agentName}");
    }
}
