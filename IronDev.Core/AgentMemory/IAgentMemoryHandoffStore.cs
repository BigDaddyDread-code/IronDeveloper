namespace IronDev.Core.AgentMemory;

public interface IAgentMemoryHandoffStore
{
    Task CreateAsync(
        AgentMemoryScope sourceScope,
        HandoffMemorySliceDraft draft,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HandoffMemorySliceRecord>> QueryIncomingAsync(
        AgentMemoryScope targetScope,
        HandoffMemorySliceQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HandoffMemorySliceRecord>> QueryOutgoingAsync(
        AgentMemoryScope sourceScope,
        HandoffMemorySliceQuery query,
        CancellationToken cancellationToken = default);
}
