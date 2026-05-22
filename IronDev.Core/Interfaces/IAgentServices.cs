using IronDev.Core.Agents;

namespace IronDev.Core.Interfaces;

public interface IIronDevAgent
{
    string AgentName { get; }
    string Purpose { get; }
    string DefaultModelProfile { get; }
    IReadOnlyList<string> AllowedTools { get; }

    Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default);
}

public interface IAgentRegistry
{
    IReadOnlyList<AgentDefinition> ListDefinitions();
    AgentDefinition GetDefinition(string agentName);
    IIronDevAgent GetAgent(string agentName);
}

public interface IAgentModelResolver
{
    IReadOnlyList<ModelProfile> ListProfiles();
    ModelProfile ResolveProfile(string profileName);
    ModelProfile ResolveForAgent(AgentDefinition definition);
}

public interface IAgentRunner
{
    Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default);
}
