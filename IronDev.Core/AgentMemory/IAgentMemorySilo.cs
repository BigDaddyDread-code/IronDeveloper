namespace IronDev.Core.AgentMemory;

public interface IAgentMemorySilo
{
    AgentMemoryScope Scope { get; }

    Task CreateAsync(
        AgentLocalMemoryDraft draft,
        CancellationToken cancellationToken = default);

    Task AddEventAsync(
        AgentLocalMemoryEventDraft draft,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentLocalMemoryItem>> QueryAsync(
        AgentLocalMemoryQuery query,
        CancellationToken cancellationToken = default);

    Task<AgentLocalMemoryItem?> GetAsync(
        string memoryItemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentLocalMemoryEventRecord>> GetEventHistoryAsync(
        string memoryItemId,
        CancellationToken cancellationToken = default);
}
