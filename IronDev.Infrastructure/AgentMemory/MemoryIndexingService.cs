using IronDev.Core.AgentMemory;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class MemoryIndexingService : IMemoryIndexingService
{
    private readonly IMemoryIndexProjectionBuilder _projectionBuilder;
    private readonly IMemoryIndexQueueStore _queueStore;
    private readonly IWeaviateMemoryIndexer _indexer;

    public MemoryIndexingService(
        IMemoryIndexProjectionBuilder projectionBuilder,
        IMemoryIndexQueueStore queueStore,
        IWeaviateMemoryIndexer indexer)
    {
        _projectionBuilder = projectionBuilder ?? throw new ArgumentNullException(nameof(projectionBuilder));
        _queueStore = queueStore ?? throw new ArgumentNullException(nameof(queueStore));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
    }

    public async Task QueueRunAsync(
        string tenantId,
        string projectId,
        string campaignId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidRunScope(tenantId, projectId, campaignId, runId);

        var runProjections = await _projectionBuilder.BuildRunProjectionsAsync(
            tenantId,
            projectId,
            campaignId,
            runId,
            cancellationToken).ConfigureAwait(false);
        var proposalProjections = await _projectionBuilder.BuildProposalProjectionsAsync(
            tenantId,
            projectId,
            campaignId,
            runId,
            cancellationToken).ConfigureAwait(false);

        foreach (var projection in runProjections.Concat(proposalProjections))
            await _queueStore.QueueAsync(projection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ProcessPendingAsync(
        string tenantId,
        string projectId,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(projectId))
            throw new InvalidOperationException("Memory indexing requires tenant and project identity.");

        var pending = await _queueStore.QueryPendingAsync(
            tenantId,
            projectId,
            take,
            cancellationToken).ConfigureAwait(false);

        var processed = 0;
        foreach (var record in pending)
        {
            processed++;
            try
            {
                var result = await _indexer.IndexAsync(ToProjection(record), cancellationToken).ConfigureAwait(false);
                if (result.Success)
                {
                    await _queueStore.AddEventAsync(
                        record.IndexRecordId,
                        MemoryIndexEventType.Indexed,
                        result.WeaviateObjectId,
                        error: null,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _queueStore.AddEventAsync(
                        record.IndexRecordId,
                        MemoryIndexEventType.Failed,
                        weaviateObjectId: null,
                        result.Error ?? "Weaviate indexing failed.",
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await _queueStore.AddEventAsync(
                    record.IndexRecordId,
                    MemoryIndexEventType.Failed,
                    weaviateObjectId: null,
                    ex.Message,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return processed;
    }

    private static MemoryIndexProjection ToProjection(MemoryIndexQueueRecord record) =>
        new()
        {
            IndexRecordId = record.IndexRecordId,
            TenantId = record.TenantId,
            ProjectId = record.ProjectId,
            CampaignId = record.CampaignId,
            RunId = record.RunId,
            AgentId = record.AgentId,
            ArtifactType = record.ArtifactType,
            ArtifactId = record.ArtifactId,
            AuthorityLevel = record.AuthorityLevel,
            Title = record.Title,
            Summary = record.Summary,
            EvidenceRefs = record.EvidenceRefs,
            CreatedAt = record.CreatedAt,
            DecisionId = record.DecisionId,
            ThoughtLedgerEntryId = record.ThoughtLedgerEntryId,
            CorrelationId = record.CorrelationId,
            Metadata = record.Metadata,
            SourceHashSha256 = record.SourceHashSha256
        };

    private static void ThrowIfInvalidRunScope(string tenantId, string projectId, string campaignId, string runId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(campaignId) ||
            string.IsNullOrWhiteSpace(runId))
        {
            throw new InvalidOperationException("Memory indexing requires tenant, project, campaign, and run identity.");
        }
    }
}
