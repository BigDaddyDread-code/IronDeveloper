using IronDev.Core.AgentMemory;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class AgentMemorySiloService : IAgentMemorySiloService
{
    private readonly IAgentLocalMemoryStore _store;
    private readonly IAgentMemoryInfluenceStore _influenceStore;
    private readonly IAgentMemoryHandoffStore _handoffStore;

    public AgentMemorySiloService(
        IAgentLocalMemoryStore store,
        IAgentMemoryInfluenceStore influenceStore,
        IAgentMemoryHandoffStore handoffStore)
    {
        _store = store;
        _influenceStore = influenceStore;
        _handoffStore = handoffStore;
    }

    public IAgentMemorySilo Open(AgentMemorySiloContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ThrowIfInvalid(context);

        var scope = new AgentMemoryScope
        {
            TenantId = context.TenantId,
            ProjectId = context.ProjectId,
            CampaignId = context.CampaignId,
            RunId = context.RunId,
            AgentId = context.AgentId
        };

        return new AgentMemorySilo(scope, _store, _influenceStore, _handoffStore);
    }

    private static void ThrowIfInvalid(AgentMemorySiloContext context)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            throw new InvalidOperationException("Agent memory silo context requires tenant identity.");

        if (string.IsNullOrWhiteSpace(context.ProjectId))
            throw new InvalidOperationException("Agent memory silo context requires project identity.");

        if (string.IsNullOrWhiteSpace(context.CampaignId))
            throw new InvalidOperationException("Agent memory silo context requires campaign identity.");

        if (string.IsNullOrWhiteSpace(context.RunId))
            throw new InvalidOperationException("Agent memory silo context requires run identity.");

        if (string.IsNullOrWhiteSpace(context.AgentId))
            throw new InvalidOperationException("Agent memory silo context requires agent identity.");
    }
}
