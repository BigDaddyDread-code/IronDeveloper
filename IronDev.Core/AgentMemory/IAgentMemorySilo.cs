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

    Task RecordInfluenceAsync(
        MemoryInfluenceDraft draft,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryInfluenceRecord>> QueryInfluencesAsync(
        MemoryInfluenceQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryInfluenceRecord>> GetInfluencesForMemoryAsync(
        string memoryItemId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryInfluenceRecord>> GetInfluencesForDecisionAsync(
        string decisionId,
        CancellationToken cancellationToken = default);

    Task CreateHandoffAsync(
        HandoffMemorySliceDraft draft,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HandoffMemorySliceRecord>> QueryIncomingHandoffsAsync(
        HandoffMemorySliceQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HandoffMemorySliceRecord>> QueryOutgoingHandoffsAsync(
        HandoffMemorySliceQuery query,
        CancellationToken cancellationToken = default);
}
