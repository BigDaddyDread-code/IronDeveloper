namespace IronDev.Core.AgentMemory;

public interface IAgentMemoryInfluenceStore
{
    Task RecordAsync(
        AgentMemoryScope scope,
        MemoryInfluenceDraft draft,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryInfluenceRecord>> QueryAsync(
        AgentMemoryScope scope,
        MemoryInfluenceQuery query,
        CancellationToken cancellationToken = default);
}
