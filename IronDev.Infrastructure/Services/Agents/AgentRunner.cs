using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class AgentRunner : IAgentRunner
{
    private readonly IAgentRegistry _registry;

    public AgentRunner(IAgentRegistry registry)
    {
        _registry = registry;
    }

    public Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var agent = _registry.GetAgent(request.AgentName);
        return agent.RunAsync(request, ct);
    }
}
