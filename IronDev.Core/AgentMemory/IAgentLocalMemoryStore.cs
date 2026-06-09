namespace IronDev.Core.AgentMemory;

public interface IAgentLocalMemoryStore
{
    Task CreateAsync(
        AgentLocalMemoryItem item,
        CancellationToken cancellationToken = default);

    Task AddEventAsync(
        AgentMemoryScope scope,
        AgentLocalMemoryEventRecord memoryEvent,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentLocalMemoryItem>> QueryOwnMemoryAsync(
        AgentMemoryScope scope,
        AgentLocalMemoryQuery query,
        CancellationToken cancellationToken = default);

    Task<AgentLocalMemoryItem?> GetOwnMemoryItemAsync(
        AgentMemoryScope scope,
        string memoryItemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentLocalMemoryEventRecord>> GetEventHistoryAsync(
        AgentMemoryScope scope,
        string memoryItemId,
        CancellationToken cancellationToken = default);
}
