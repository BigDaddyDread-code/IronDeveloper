using IronDev.Core.AgentMemory;

namespace IronDev.Infrastructure.AgentMemory;

internal sealed class AgentMemorySilo : IAgentMemorySilo
{
    private readonly IAgentLocalMemoryStore _store;

    internal AgentMemorySilo(
        AgentMemoryScope scope,
        IAgentLocalMemoryStore store)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public AgentMemoryScope Scope { get; }

    public Task CreateAsync(
        AgentLocalMemoryDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var item = new AgentLocalMemoryItem
        {
            MemoryItemId = draft.MemoryItemId,
            Scope = Scope,
            MemoryType = draft.MemoryType,
            AuthorityLevel = draft.AuthorityLevel,
            Title = draft.Title,
            Summary = draft.Summary,
            EvidenceRefs = draft.EvidenceRefs,
            Confidence = draft.Confidence,
            Status = MemoryLifecycleStatus.Active,
            CreatedAt = draft.CreatedAt,
            ExpiresAt = draft.ExpiresAt,
            SupersedesMemoryItemId = draft.SupersedesMemoryItemId,
            KnownLimitations = draft.KnownLimitations
        };

        return _store.CreateAsync(item, cancellationToken);
    }

    public Task AddEventAsync(
        AgentLocalMemoryEventDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var memoryEvent = new AgentLocalMemoryEventRecord
        {
            MemoryEventId = draft.MemoryEventId,
            MemoryItemId = draft.MemoryItemId,
            EventType = draft.EventType,
            EventReason = draft.EventReason,
            CreatedAt = draft.CreatedAt,
            CreatedByAgentId = Scope.AgentId,
            CreatedByUserId = draft.CreatedByUserId,
            DecisionId = draft.DecisionId,
            ThoughtLedgerEntryId = draft.ThoughtLedgerEntryId,
            EventJson = draft.EventJson
        };

        return _store.AddEventAsync(Scope, memoryEvent, cancellationToken);
    }

    public Task<IReadOnlyList<AgentLocalMemoryItem>> QueryAsync(
        AgentLocalMemoryQuery query,
        CancellationToken cancellationToken = default) =>
        _store.QueryOwnMemoryAsync(Scope, query, cancellationToken);

    public Task<AgentLocalMemoryItem?> GetAsync(
        string memoryItemId,
        CancellationToken cancellationToken = default) =>
        _store.GetOwnMemoryItemAsync(Scope, memoryItemId, cancellationToken);

    public Task<IReadOnlyList<AgentLocalMemoryEventRecord>> GetEventHistoryAsync(
        string memoryItemId,
        CancellationToken cancellationToken = default) =>
        _store.GetEventHistoryAsync(Scope, memoryItemId, cancellationToken);
}
