namespace IronDev.Core.AgentMemory;

public interface IMemoryImprovementProposalService
{
    Task CreateAsync(
        MemoryImprovementProposalDraft draft,
        CancellationToken cancellationToken = default);

    Task AddEventAsync(
        AgentMemoryScope scope,
        MemoryImprovementProposalEventDraft draft,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryImprovementProposalRecord>> QueryAsync(
        string tenantId,
        string projectId,
        string campaignId,
        string runId,
        MemoryImprovementProposalQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryImprovementProposalEventRecord>> GetEventsAsync(
        AgentMemoryScope scope,
        string proposalId,
        CancellationToken cancellationToken = default);
}
