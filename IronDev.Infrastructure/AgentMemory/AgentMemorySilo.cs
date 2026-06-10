using IronDev.Core.AgentMemory;

namespace IronDev.Infrastructure.AgentMemory;

internal sealed class AgentMemorySilo : IAgentMemorySilo
{
    private readonly IAgentLocalMemoryStore _store;
    private readonly IAgentMemoryInfluenceStore _influenceStore;
    private readonly IAgentMemoryHandoffStore _handoffStore;

    internal AgentMemorySilo(
        AgentMemoryScope scope,
        IAgentLocalMemoryStore store,
        IAgentMemoryInfluenceStore influenceStore,
        IAgentMemoryHandoffStore handoffStore)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _influenceStore = influenceStore ?? throw new ArgumentNullException(nameof(influenceStore));
        _handoffStore = handoffStore ?? throw new ArgumentNullException(nameof(handoffStore));
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

    public Task RecordInfluenceAsync(
        MemoryInfluenceDraft draft,
        CancellationToken cancellationToken = default) =>
        _influenceStore.RecordAsync(Scope, draft, cancellationToken);

    public Task<IReadOnlyList<MemoryInfluenceRecord>> QueryInfluencesAsync(
        MemoryInfluenceQuery query,
        CancellationToken cancellationToken = default) =>
        _influenceStore.QueryAsync(Scope, query, cancellationToken);

    public Task<IReadOnlyList<MemoryInfluenceRecord>> GetInfluencesForMemoryAsync(
        string memoryItemId,
        CancellationToken cancellationToken = default) =>
        _influenceStore.QueryAsync(Scope, new MemoryInfluenceQuery { MemoryItemId = memoryItemId }, cancellationToken);

    public Task<IReadOnlyList<MemoryInfluenceRecord>> GetInfluencesForDecisionAsync(
        string decisionId,
        CancellationToken cancellationToken = default) =>
        _influenceStore.QueryAsync(Scope, new MemoryInfluenceQuery { DecisionId = decisionId }, cancellationToken);

    public Task CreateHandoffAsync(
        HandoffMemorySliceDraft draft,
        CancellationToken cancellationToken = default) =>
        _handoffStore.CreateAsync(Scope, draft, cancellationToken);

    public Task<IReadOnlyList<HandoffMemorySliceRecord>> QueryIncomingHandoffsAsync(
        HandoffMemorySliceQuery query,
        CancellationToken cancellationToken = default) =>
        _handoffStore.QueryIncomingAsync(Scope, query, cancellationToken);

    public Task<IReadOnlyList<HandoffMemorySliceRecord>> QueryOutgoingHandoffsAsync(
        HandoffMemorySliceQuery query,
        CancellationToken cancellationToken = default) =>
        _handoffStore.QueryOutgoingAsync(Scope, query, cancellationToken);
}
