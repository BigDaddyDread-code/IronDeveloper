using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlMemoryIndexProjectionBuilder : IMemoryIndexProjectionBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlMemoryIndexProjectionBuilder(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<MemoryIndexProjection>> BuildRunProjectionsAsync(
        string tenantId,
        string projectId,
        string campaignId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidRunScope(tenantId, projectId, campaignId, runId);

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var memories = (await connection.QueryAsync<MemoryRow>(new CommandDefinition(
            """
            SELECT
                MemoryItemId,
                AgentId,
                MemoryType,
                AuthorityLevel,
                CurrentEventType,
                CreatedAtUtc
            FROM agent.vwAgentLocalMemoryCurrentState
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId;
            """,
            new { TenantId = tenantId, ProjectId = projectId, CampaignId = campaignId, RunId = runId },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var influences = (await connection.QueryAsync<InfluenceRow>(new CommandDefinition(
            """
            SELECT
                InfluenceId,
                AgentId,
                MemoryItemId,
                DecisionId,
                InfluenceType,
                InfluenceSummary,
                Confidence,
                CreatedAtUtc,
                ThoughtLedgerEntryId,
                CorrelationId
            FROM agent.AgentMemoryInfluenceRecord
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId;
            """,
            new { TenantId = tenantId, ProjectId = projectId, CampaignId = campaignId, RunId = runId },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var handoffs = (await connection.QueryAsync<HandoffRow>(new CommandDefinition(
            """
            SELECT
                HandoffMemorySliceId,
                SourceAgentId,
                TargetAgentId,
                MemoryItemIdsJson,
                Summary,
                AllowedUse,
                Confidence,
                DecisionId,
                ThoughtLedgerEntryId,
                CorrelationId,
                CreatedAtUtc
            FROM agent.AgentMemoryHandoffSlice
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId;
            """,
            new { TenantId = tenantId, ProjectId = projectId, CampaignId = campaignId, RunId = runId },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var projections = new List<MemoryIndexProjection>();
        var generatedAt = DateTimeOffset.UtcNow;
        var agentIds = memories.Select(item => item.AgentId)
            .Concat(influences.Select(item => item.AgentId))
            .Concat(handoffs.Select(item => item.SourceAgentId))
            .Concat(handoffs.Select(item => item.TargetAgentId))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        projections.Add(new MemoryIndexProjection
        {
            IndexRecordId = BuildIndexId("idx-run", tenantId, projectId, campaignId, runId),
            TenantId = tenantId,
            ProjectId = projectId,
            CampaignId = campaignId,
            RunId = runId,
            ArtifactType = MemoryIndexArtifactType.RunMemoryReport,
            ArtifactId = runId,
            AuthorityLevel = MemoryIndexAuthorityLevel.ObservedProjection,
            Title = $"Run memory report for {runId}",
            Summary = $"Run {runId} memory report: {agentIds.Length} agents, {memories.Length} memory items, {influences.Length} influence records, {handoffs.Length} handoff slices.",
            EvidenceRefs = [BuildEvidence("run-report", runId, EvidenceType.RunReport, generatedAt)],
            CreatedAt = generatedAt,
            Metadata = new Dictionary<string, string>
            {
                ["agentCount"] = agentIds.Length.ToString(),
                ["totalMemoryItemsCreated"] = memories.Length.ToString(),
                ["totalInfluenceRecords"] = influences.Length.ToString(),
                ["totalHandoffSlices"] = handoffs.Length.ToString(),
                ["findingCount"] = "0"
            },
            SourceHashSha256 = BuildSourceHash("run", tenantId, projectId, campaignId, runId, memories.Length.ToString(), influences.Length.ToString(), handoffs.Length.ToString())
        });

        foreach (var agentId in agentIds)
        {
            var createdMemoryCount = memories.Count(item => string.Equals(item.AgentId, agentId, StringComparison.Ordinal));
            var influenceCount = influences.Count(item => string.Equals(item.AgentId, agentId, StringComparison.Ordinal));
            var outgoingCount = handoffs.Count(item => string.Equals(item.SourceAgentId, agentId, StringComparison.Ordinal));
            var incomingCount = handoffs.Count(item => string.Equals(item.TargetAgentId, agentId, StringComparison.Ordinal));

            projections.Add(new MemoryIndexProjection
            {
                IndexRecordId = BuildIndexId("idx-agent", tenantId, projectId, campaignId, runId, agentId),
                TenantId = tenantId,
                ProjectId = projectId,
                CampaignId = campaignId,
                RunId = runId,
                AgentId = agentId,
                ArtifactType = MemoryIndexArtifactType.AgentRunMemoryReport,
                ArtifactId = $"{runId}:{agentId}",
                AuthorityLevel = MemoryIndexAuthorityLevel.ObservedProjection,
                Title = $"Agent memory report for {agentId}",
                Summary = $"Agent {agentId} in run {runId}: {createdMemoryCount} memory items, {influenceCount} influence records, {outgoingCount} outgoing handoffs, {incomingCount} incoming handoffs.",
                EvidenceRefs = [BuildEvidence("agent-run-report", $"{runId}:{agentId}", EvidenceType.RunReport, generatedAt)],
                CreatedAt = generatedAt,
                Metadata = new Dictionary<string, string>
                {
                    ["agentId"] = agentId,
                    ["createdMemoryCount"] = createdMemoryCount.ToString(),
                    ["influenceRecordCount"] = influenceCount.ToString(),
                    ["outgoingHandoffCount"] = outgoingCount.ToString(),
                    ["incomingHandoffCount"] = incomingCount.ToString(),
                    ["reviewCandidateCount"] = "0"
                },
                SourceHashSha256 = BuildSourceHash("agent", tenantId, projectId, campaignId, runId, agentId, createdMemoryCount.ToString(), influenceCount.ToString(), outgoingCount.ToString(), incomingCount.ToString())
            });
        }

        foreach (var influence in influences.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.InfluenceId, StringComparer.Ordinal))
        {
            projections.Add(new MemoryIndexProjection
            {
                IndexRecordId = BuildIndexId("idx-influence", tenantId, projectId, campaignId, runId, influence.InfluenceId),
                TenantId = tenantId,
                ProjectId = projectId,
                CampaignId = campaignId,
                RunId = runId,
                AgentId = influence.AgentId,
                ArtifactType = MemoryIndexArtifactType.MemoryInfluenceSummary,
                ArtifactId = influence.InfluenceId,
                AuthorityLevel = MemoryIndexAuthorityLevel.ObservedProjection,
                Title = $"Memory influence {influence.InfluenceId}",
                Summary = $"Influence {influence.InfluenceId} used memory {influence.MemoryItemId} for decision {influence.DecisionId}: {influence.InfluenceSummary}",
                EvidenceRefs = [BuildEvidence("influence", influence.InfluenceId, EvidenceType.TraceEvent, ToUtc(influence.CreatedAtUtc))],
                CreatedAt = ToUtc(influence.CreatedAtUtc),
                DecisionId = influence.DecisionId,
                ThoughtLedgerEntryId = influence.ThoughtLedgerEntryId,
                CorrelationId = influence.CorrelationId,
                Metadata = new Dictionary<string, string>
                {
                    ["influenceId"] = influence.InfluenceId,
                    ["memoryItemId"] = influence.MemoryItemId,
                    ["influenceType"] = ((MemoryInfluenceType)influence.InfluenceType).ToString(),
                    ["confidence"] = influence.Confidence.ToString("0.####")
                },
                SourceHashSha256 = BuildSourceHash("influence", influence.InfluenceId, influence.MemoryItemId, influence.DecisionId, influence.InfluenceSummary)
            });
        }

        foreach (var handoff in handoffs.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.HandoffMemorySliceId, StringComparer.Ordinal))
        {
            var memoryIds = JsonSerializer.Deserialize<IReadOnlyList<string>>(handoff.MemoryItemIdsJson, JsonOptions) ?? Array.Empty<string>();
            projections.Add(new MemoryIndexProjection
            {
                IndexRecordId = BuildIndexId("idx-handoff", tenantId, projectId, campaignId, runId, handoff.HandoffMemorySliceId),
                TenantId = tenantId,
                ProjectId = projectId,
                CampaignId = campaignId,
                RunId = runId,
                AgentId = handoff.SourceAgentId,
                ArtifactType = MemoryIndexArtifactType.HandoffSummary,
                ArtifactId = handoff.HandoffMemorySliceId,
                AuthorityLevel = MemoryIndexAuthorityLevel.ObservedProjection,
                Title = $"Memory handoff {handoff.HandoffMemorySliceId}",
                Summary = $"Handoff {handoff.HandoffMemorySliceId} from {handoff.SourceAgentId} to {handoff.TargetAgentId} included {memoryIds.Count} memory references for {((HandoffMemoryAllowedUse)handoff.AllowedUse)}: {handoff.Summary}",
                EvidenceRefs = [BuildEvidence("handoff", handoff.HandoffMemorySliceId, EvidenceType.HandoffPayload, ToUtc(handoff.CreatedAtUtc))],
                CreatedAt = ToUtc(handoff.CreatedAtUtc),
                DecisionId = handoff.DecisionId,
                ThoughtLedgerEntryId = handoff.ThoughtLedgerEntryId,
                CorrelationId = handoff.CorrelationId,
                Metadata = new Dictionary<string, string>
                {
                    ["handoffMemorySliceId"] = handoff.HandoffMemorySliceId,
                    ["sourceAgentId"] = handoff.SourceAgentId,
                    ["targetAgentId"] = handoff.TargetAgentId,
                    ["allowedUse"] = ((HandoffMemoryAllowedUse)handoff.AllowedUse).ToString(),
                    ["memoryItemCount"] = memoryIds.Count.ToString()
                },
                SourceHashSha256 = BuildSourceHash("handoff", handoff.HandoffMemorySliceId, handoff.SourceAgentId, handoff.TargetAgentId, handoff.Summary)
            });
        }

        return projections;
    }

    public async Task<IReadOnlyList<MemoryIndexProjection>> BuildProposalProjectionsAsync(
        string tenantId,
        string projectId,
        string campaignId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidRunScope(tenantId, projectId, campaignId, runId);

        using var connection = _connectionFactory.CreateConnection();
        await OpenAsync(connection, cancellationToken).ConfigureAwait(false);

        var proposals = (await connection.QueryAsync<ProposalRow>(new CommandDefinition(
            """
            WITH LatestEvent AS
            (
                SELECT
                    ProposalId,
                    EventType,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY ProposalId
                        ORDER BY CreatedAtUtc DESC, ProposalEventId DESC
                    ) AS rn
                FROM agent.AgentMemoryImprovementProposalEvent
            )
            SELECT
                p.ProposalId,
                p.AgentId,
                p.ProposalType,
                ISNULL(le.EventType, 1) AS CurrentStatus,
                p.Title,
                p.Summary,
                p.EvidenceRefsJson,
                p.Confidence,
                p.CreatedAtUtc,
                p.ProposedByAgentId,
                p.ProposedByUserId,
                p.CorrelationId,
                p.ThoughtLedgerEntryId
            FROM agent.AgentMemoryImprovementProposal p
            LEFT JOIN LatestEvent le
                ON le.ProposalId = p.ProposalId
               AND le.rn = 1
            WHERE p.TenantId = @TenantId
              AND p.ProjectId = @ProjectId
              AND p.CampaignId = @CampaignId
              AND p.RunId = @RunId;
            """,
            new { TenantId = tenantId, ProjectId = projectId, CampaignId = campaignId, RunId = runId },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var proposalEvents = (await connection.QueryAsync<ProposalEventRow>(new CommandDefinition(
            """
            SELECT
                e.ProposalEventId,
                e.ProposalId,
                p.AgentId,
                e.EventType,
                e.Reason,
                e.CreatedAtUtc,
                e.CreatedByUserId,
                e.CreatedByAgentId,
                e.ThoughtLedgerEntryId,
                e.CorrelationId
            FROM agent.AgentMemoryImprovementProposalEvent e
            INNER JOIN agent.AgentMemoryImprovementProposal p
                ON p.ProposalId = e.ProposalId
            WHERE p.TenantId = @TenantId
              AND p.ProjectId = @ProjectId
              AND p.CampaignId = @CampaignId
              AND p.RunId = @RunId;
            """,
            new { TenantId = tenantId, ProjectId = projectId, CampaignId = campaignId, RunId = runId },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var projections = new List<MemoryIndexProjection>();
        foreach (var proposal in proposals.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.ProposalId, StringComparer.Ordinal))
        {
            var status = (MemoryImprovementProposalStatus)proposal.CurrentStatus;
            var authority = MapProposalAuthority(status);
            var evidenceRefs = JsonSerializer.Deserialize<IReadOnlyList<EvidenceRef>>(proposal.EvidenceRefsJson, JsonOptions) ?? Array.Empty<EvidenceRef>();

            projections.Add(new MemoryIndexProjection
            {
                IndexRecordId = BuildIndexId("idx-proposal", tenantId, projectId, campaignId, runId, proposal.ProposalId),
                TenantId = tenantId,
                ProjectId = projectId,
                CampaignId = campaignId,
                RunId = runId,
                AgentId = proposal.AgentId,
                ArtifactType = MemoryIndexArtifactType.MemoryImprovementProposal,
                ArtifactId = proposal.ProposalId,
                AuthorityLevel = authority,
                Title = proposal.Title,
                Summary = $"Memory improvement proposal {proposal.ProposalId} is {status}: {proposal.Summary}",
                EvidenceRefs = evidenceRefs,
                CreatedAt = ToUtc(proposal.CreatedAtUtc),
                ThoughtLedgerEntryId = proposal.ThoughtLedgerEntryId,
                CorrelationId = proposal.CorrelationId,
                Metadata = new Dictionary<string, string>
                {
                    ["proposalId"] = proposal.ProposalId,
                    ["proposalType"] = ((MemoryImprovementProposalType)proposal.ProposalType).ToString(),
                    ["proposalStatus"] = status.ToString(),
                    ["retrievalAuthority"] = authority.ToString(),
                    ["reviewedPositiveIsPromotion"] = "false",
                    ["confidence"] = proposal.Confidence.ToString("0.####")
                },
                SourceHashSha256 = BuildSourceHash("proposal", proposal.ProposalId, proposal.CurrentStatus.ToString(), proposal.Title, proposal.Summary)
            });
        }

        foreach (var proposalEvent in proposalEvents.OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.ProposalEventId, StringComparer.Ordinal))
        {
            var eventType = (MemoryImprovementProposalEventType)proposalEvent.EventType;
            projections.Add(new MemoryIndexProjection
            {
                IndexRecordId = BuildIndexId("idx-proposal-event", tenantId, projectId, campaignId, runId, proposalEvent.ProposalEventId),
                TenantId = tenantId,
                ProjectId = projectId,
                CampaignId = campaignId,
                RunId = runId,
                AgentId = proposalEvent.AgentId,
                ArtifactType = MemoryIndexArtifactType.MemoryImprovementProposalEvent,
                ArtifactId = proposalEvent.ProposalEventId,
                AuthorityLevel = MapProposalEventAuthority(eventType),
                Title = $"Memory proposal event {eventType}",
                Summary = $"Proposal {proposalEvent.ProposalId} recorded event {eventType}. {proposalEvent.Reason ?? "No reason supplied."}",
                EvidenceRefs = [BuildEvidence("proposal-event", proposalEvent.ProposalEventId, EvidenceType.TraceEvent, ToUtc(proposalEvent.CreatedAtUtc))],
                CreatedAt = ToUtc(proposalEvent.CreatedAtUtc),
                ThoughtLedgerEntryId = proposalEvent.ThoughtLedgerEntryId,
                CorrelationId = proposalEvent.CorrelationId,
                Metadata = new Dictionary<string, string>
                {
                    ["proposalEventId"] = proposalEvent.ProposalEventId,
                    ["proposalId"] = proposalEvent.ProposalId,
                    ["eventType"] = eventType.ToString(),
                    ["createdByAgentId"] = proposalEvent.CreatedByAgentId ?? string.Empty,
                    ["createdByUserId"] = proposalEvent.CreatedByUserId ?? string.Empty
                },
                SourceHashSha256 = BuildSourceHash("proposal-event", proposalEvent.ProposalEventId, proposalEvent.ProposalId, eventType.ToString(), proposalEvent.Reason ?? string.Empty)
            });
        }

        return projections;
    }

    private static MemoryIndexAuthorityLevel MapProposalAuthority(MemoryImprovementProposalStatus status) =>
        status switch
        {
            MemoryImprovementProposalStatus.Submitted => MemoryIndexAuthorityLevel.ReviewQueue,
            MemoryImprovementProposalStatus.AcceptedForFutureImplementation => MemoryIndexAuthorityLevel.ReviewedPositive,
            MemoryImprovementProposalStatus.Rejected => MemoryIndexAuthorityLevel.Rejected,
            MemoryImprovementProposalStatus.Withdrawn => MemoryIndexAuthorityLevel.Deprecated,
            MemoryImprovementProposalStatus.Superseded => MemoryIndexAuthorityLevel.Deprecated,
            _ => MemoryIndexAuthorityLevel.ObservedProjection
        };

    private static MemoryIndexAuthorityLevel MapProposalEventAuthority(MemoryImprovementProposalEventType eventType) =>
        eventType switch
        {
            MemoryImprovementProposalEventType.Submitted => MemoryIndexAuthorityLevel.ReviewQueue,
            MemoryImprovementProposalEventType.AcceptedForFutureImplementation => MemoryIndexAuthorityLevel.ReviewedPositive,
            MemoryImprovementProposalEventType.Rejected => MemoryIndexAuthorityLevel.Rejected,
            MemoryImprovementProposalEventType.Withdrawn => MemoryIndexAuthorityLevel.Deprecated,
            MemoryImprovementProposalEventType.Superseded => MemoryIndexAuthorityLevel.Deprecated,
            _ => MemoryIndexAuthorityLevel.ObservedProjection
        };

    private static EvidenceRef BuildEvidence(string prefix, string sourceId, EvidenceType evidenceType, DateTimeOffset capturedAt) =>
        new()
        {
            EvidenceId = $"memory-index-{prefix}-{BuildShortHash(sourceId)}",
            EvidenceType = evidenceType,
            SourceId = sourceId,
            Summary = $"Memory indexing projection source {sourceId}.",
            CapturedAt = capturedAt
        };

    private static string BuildIndexId(string prefix, params string?[] parts) =>
        $"{prefix}-{BuildShortHash(string.Join(":", parts.Where(part => !string.IsNullOrWhiteSpace(part))))}";

    private static string BuildShortHash(string value) =>
        BuildSourceHash(value)[..32];

    private static string BuildSourceHash(params string?[] parts)
    {
        var raw = string.Join("\n", parts.Select(part => part ?? string.Empty));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void ThrowIfInvalidRunScope(string tenantId, string projectId, string campaignId, string runId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(campaignId) ||
            string.IsNullOrWhiteSpace(runId))
        {
            throw new InvalidOperationException("Memory index projections require tenant, project, campaign, and run identity.");
        }
    }

    private static async Task OpenAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State == ConnectionState.Open)
            return;

        if (connection is DbConnection dbConnection)
            await dbConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        else
            connection.Open();
    }

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class MemoryRow
    {
        public string MemoryItemId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public int MemoryType { get; set; }
        public int AuthorityLevel { get; set; }
        public int? CurrentEventType { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    private sealed class InfluenceRow
    {
        public string InfluenceId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string MemoryItemId { get; set; } = string.Empty;
        public string DecisionId { get; set; } = string.Empty;
        public int InfluenceType { get; set; }
        public string InfluenceSummary { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? CorrelationId { get; set; }
    }

    private sealed class HandoffRow
    {
        public string HandoffMemorySliceId { get; set; } = string.Empty;
        public string SourceAgentId { get; set; } = string.Empty;
        public string TargetAgentId { get; set; } = string.Empty;
        public string MemoryItemIdsJson { get; set; } = "[]";
        public string Summary { get; set; } = string.Empty;
        public int AllowedUse { get; set; }
        public decimal Confidence { get; set; }
        public string? DecisionId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    private sealed class ProposalRow
    {
        public string ProposalId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public int ProposalType { get; set; }
        public int CurrentStatus { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string EvidenceRefsJson { get; set; } = "[]";
        public decimal Confidence { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? ProposedByAgentId { get; set; }
        public string? ProposedByUserId { get; set; }
        public string? CorrelationId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
    }

    private sealed class ProposalEventRow
    {
        public string ProposalEventId { get; set; } = string.Empty;
        public string ProposalId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public int EventType { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? CreatedByUserId { get; set; }
        public string? CreatedByAgentId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? CorrelationId { get; set; }
    }
}
