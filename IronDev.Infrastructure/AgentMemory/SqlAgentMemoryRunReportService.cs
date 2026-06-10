using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlAgentMemoryRunReportService : IAgentMemoryRunReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlAgentMemoryRunReportService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<RunMemoryReport> BuildAsync(
        RunMemoryReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfInvalid(request);

        var take = Math.Clamp(request.TakePerAgent <= 0 ? 100 : request.TakePerAgent, 1, 500);
        var generatedAt = DateTimeOffset.UtcNow;

        using var connection = _connectionFactory.CreateConnection();

        var memoryRows = (await connection.QueryAsync<MemoryRow>(new CommandDefinition(
            """
            WITH Ranked AS
            (
                SELECT
                    MemoryItemId,
                    TenantId,
                    ProjectId,
                    CampaignId,
                    RunId,
                    AgentId,
                    MemoryType,
                    AuthorityLevel,
                    Title,
                    Summary,
                    Confidence,
                    CreatedAtUtc,
                    ExpiresAtUtc,
                    SupersedesMemoryItemId,
                    KnownLimitations,
                    CurrentEventType,
                    CurrentEventAtUtc,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY AgentId
                        ORDER BY CreatedAtUtc DESC, MemoryItemId DESC
                    ) AS rn
                FROM agent.vwAgentLocalMemoryCurrentState
                WHERE TenantId = @TenantId
                  AND ProjectId = @ProjectId
                  AND CampaignId = @CampaignId
                  AND RunId = @RunId
            )
            SELECT
                MemoryItemId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                MemoryType,
                AuthorityLevel,
                Title,
                Summary,
                Confidence,
                CreatedAtUtc,
                ExpiresAtUtc,
                SupersedesMemoryItemId,
                KnownLimitations,
                CurrentEventType,
                CurrentEventAtUtc
            FROM Ranked
            WHERE rn <= @Take
            ORDER BY AgentId, CreatedAtUtc DESC, MemoryItemId DESC;
            """,
            new
            {
                request.TenantId,
                request.ProjectId,
                request.CampaignId,
                request.RunId,
                Take = take
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var eventRows = (await connection.QueryAsync<EventRow>(new CommandDefinition(
            """
            SELECT
                i.AgentId,
                e.MemoryEventId,
                e.MemoryItemId,
                e.EventType,
                e.EventReason,
                e.CreatedAtUtc,
                e.DecisionId,
                e.ThoughtLedgerEntryId
            FROM agent.AgentLocalMemoryEvent e
            INNER JOIN agent.AgentLocalMemoryItem i
                ON i.MemoryItemId = e.MemoryItemId
            WHERE i.TenantId = @TenantId
              AND i.ProjectId = @ProjectId
              AND i.CampaignId = @CampaignId
              AND i.RunId = @RunId
            ORDER BY i.AgentId, e.CreatedAtUtc, e.MemoryEventId;
            """,
            new
            {
                request.TenantId,
                request.ProjectId,
                request.CampaignId,
                request.RunId
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var influenceRows = (await connection.QueryAsync<InfluenceRow>(new CommandDefinition(
            """
            WITH Ranked AS
            (
                SELECT
                    InfluenceId,
                    TenantId,
                    ProjectId,
                    CampaignId,
                    RunId,
                    AgentId,
                    MemoryItemId,
                    DecisionId,
                    InfluenceType,
                    InfluenceSummary,
                    Confidence,
                    MemoryAuthorityLevelAtInfluence,
                    MemoryLifecycleStatusAtInfluence,
                    AffectedArtifactType,
                    AffectedArtifactId,
                    CreatedAtUtc,
                    ThoughtLedgerEntryId,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY AgentId
                        ORDER BY CreatedAtUtc DESC, InfluenceId DESC
                    ) AS rn
                FROM agent.AgentMemoryInfluenceRecord
                WHERE TenantId = @TenantId
                  AND ProjectId = @ProjectId
                  AND CampaignId = @CampaignId
                  AND RunId = @RunId
            )
            SELECT
                InfluenceId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                AgentId,
                MemoryItemId,
                DecisionId,
                InfluenceType,
                InfluenceSummary,
                Confidence,
                MemoryAuthorityLevelAtInfluence,
                MemoryLifecycleStatusAtInfluence,
                AffectedArtifactType,
                AffectedArtifactId,
                CreatedAtUtc,
                ThoughtLedgerEntryId
            FROM Ranked
            WHERE rn <= @Take
            ORDER BY AgentId, CreatedAtUtc DESC, InfluenceId DESC;
            """,
            new
            {
                request.TenantId,
                request.ProjectId,
                request.CampaignId,
                request.RunId,
                Take = take
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var handoffRows = (await connection.QueryAsync<HandoffRow>(new CommandDefinition(
            """
            SELECT
                HandoffMemorySliceId,
                TenantId,
                ProjectId,
                CampaignId,
                RunId,
                SourceAgentId,
                TargetAgentId,
                MemoryItemIdsJson,
                MemorySnapshotsJson,
                Summary,
                AllowedUse,
                Confidence,
                InfluenceIdsJson,
                DecisionId,
                ThoughtLedgerEntryId,
                CorrelationId,
                CreatedAtUtc,
                ExpiresAtUtc
            FROM agent.AgentMemoryHandoffSlice
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND CampaignId = @CampaignId
              AND RunId = @RunId
            ORDER BY CreatedAtUtc DESC, HandoffMemorySliceId DESC;
            """,
            new
            {
                request.TenantId,
                request.ProjectId,
                request.CampaignId,
                request.RunId
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var eventsByMemory = eventRows
            .GroupBy(row => row.MemoryItemId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var memoryItems = memoryRows
            .Select(row => ToMemoryReportItem(row, eventsByMemory.TryGetValue(row.MemoryItemId, out var events)
                ? events
                : Array.Empty<EventRow>(), generatedAt))
            .ToArray();

        var influences = influenceRows.Select(ToInfluenceReportItem).ToArray();
        var handoffs = handoffRows.Select(ToHandoffReportItem).ToArray();
        var findings = BuildFindings(memoryRows, memoryItems, influences, handoffs).ToArray();

        var agentIds = memoryRows.Select(row => row.AgentId)
            .Concat(eventRows.Select(row => row.AgentId))
            .Concat(influenceRows.Select(row => row.AgentId))
            .Concat(handoffRows.Select(row => row.SourceAgentId))
            .Concat(handoffRows.Select(row => row.TargetAgentId))
            .Where(agentId => !string.IsNullOrWhiteSpace(agentId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(agentId => agentId, StringComparer.Ordinal)
            .ToArray();

        var reports = agentIds.Select(agentId =>
            BuildAgentReport(
                agentId,
                memoryRows,
                eventRows,
                memoryItems,
                influences,
                handoffs,
                findings,
                take)).ToArray();

        return new RunMemoryReport
        {
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            CampaignId = request.CampaignId,
            RunId = request.RunId,
            GeneratedAt = generatedAt,
            TakePerAgent = take,
            AgentCount = reports.Length,
            TotalMemoryItemsCreated = memoryRows.Length,
            TotalLifecycleEvents = eventRows.Count(row => row.EventType != (int)AgentLocalMemoryEventType.Created),
            TotalInfluenceRecords = influenceRows.Length,
            TotalHandoffSlices = handoffRows.Length,
            Agents = reports,
            Findings = findings
        };
    }

    private static AgentRunMemoryReport BuildAgentReport(
        string agentId,
        IReadOnlyList<MemoryRow> memoryRows,
        IReadOnlyList<EventRow> eventRows,
        IReadOnlyList<AgentMemoryReportItem> memoryItems,
        IReadOnlyList<AgentMemoryInfluenceReportItem> influences,
        IReadOnlyList<AgentMemoryHandoffReportItem> handoffs,
        IReadOnlyList<RunMemoryFinding> findings,
        int take)
    {
        var agentMemoryIds = memoryRows
            .Where(row => string.Equals(row.AgentId, agentId, StringComparison.Ordinal))
            .Select(row => row.MemoryItemId)
            .ToHashSet(StringComparer.Ordinal);

        var agentMemory = memoryItems
            .Where(item => agentMemoryIds.Contains(item.MemoryItemId))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.MemoryItemId, StringComparer.Ordinal)
            .Take(take)
            .ToArray();

        var agentEvents = eventRows
            .Where(row => string.Equals(row.AgentId, agentId, StringComparison.Ordinal))
            .ToArray();

        var agentInfluences = influences
            .Where(item => memoryRows.Any(row =>
                string.Equals(row.AgentId, agentId, StringComparison.Ordinal) &&
                string.Equals(row.MemoryItemId, item.MemoryItemId, StringComparison.Ordinal)))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.InfluenceId, StringComparer.Ordinal)
            .Take(take)
            .ToArray();

        var outgoing = handoffs
            .Where(item => string.Equals(item.SourceAgentId, agentId, StringComparison.Ordinal))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.HandoffMemorySliceId, StringComparer.Ordinal)
            .Take(take)
            .ToArray();

        var incoming = handoffs
            .Where(item => string.Equals(item.TargetAgentId, agentId, StringComparison.Ordinal))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.HandoffMemorySliceId, StringComparer.Ordinal)
            .Take(take)
            .ToArray();

        var activityReferences = BuildActivityReferences(agentId, agentMemory, agentEvents, agentInfluences, outgoing, incoming)
            .OrderByDescending(item => item.OccurredAt)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.ActivityId, StringComparer.Ordinal)
            .Take(take)
            .ToArray();

        var reviewCandidates = findings
            .Where(finding => string.Equals(finding.AgentId, agentId, StringComparison.Ordinal))
            .Select(finding => new AgentMemoryReviewCandidate
            {
                Severity = finding.Severity,
                FindingType = finding.FindingType,
                AgentId = finding.AgentId,
                Summary = finding.Summary,
                MemoryItemId = finding.MemoryItemId,
                InfluenceId = finding.InfluenceId,
                HandoffMemorySliceId = finding.HandoffMemorySliceId
            })
            .ToArray();

        return new AgentRunMemoryReport
        {
            AgentId = agentId,
            CreatedMemoryCount = agentMemoryIds.Count,
            LifecycleEventCount = agentEvents.Count(row => row.EventType != (int)AgentLocalMemoryEventType.Created),
            InfluenceRecordCount = agentInfluences.Length,
            OutgoingHandoffCount = outgoing.Length,
            IncomingHandoffCount = incoming.Length,
            MemoryItems = agentMemory,
            InfluenceRecords = agentInfluences,
            OutgoingHandoffs = outgoing,
            IncomingHandoffs = incoming,
            ActivityReferences = activityReferences,
            ReviewCandidates = reviewCandidates
        };
    }

    private static IEnumerable<MemoryActivityReference> BuildActivityReferences(
        string agentId,
        IReadOnlyList<AgentMemoryReportItem> memoryItems,
        IReadOnlyList<EventRow> events,
        IReadOnlyList<AgentMemoryInfluenceReportItem> influences,
        IReadOnlyList<AgentMemoryHandoffReportItem> outgoing,
        IReadOnlyList<AgentMemoryHandoffReportItem> incoming)
    {
        foreach (var item in memoryItems)
        {
            yield return new MemoryActivityReference
            {
                Kind = MemoryActivityKind.LocalMemoryCreated,
                ActivityId = item.MemoryItemId,
                AgentId = agentId,
                MemoryItemId = item.MemoryItemId,
                OccurredAt = item.CreatedAt,
                Summary = item.Title
            };
        }

        foreach (var memoryEvent in events.Where(item => item.EventType != (int)AgentLocalMemoryEventType.Created))
        {
            yield return new MemoryActivityReference
            {
                Kind = MemoryActivityKind.LocalMemoryLifecycleEvent,
                ActivityId = memoryEvent.MemoryEventId,
                AgentId = agentId,
                MemoryItemId = memoryEvent.MemoryItemId,
                DecisionId = memoryEvent.DecisionId,
                ThoughtLedgerEntryId = memoryEvent.ThoughtLedgerEntryId,
                OccurredAt = ToUtc(memoryEvent.CreatedAtUtc),
                Summary = memoryEvent.EventReason ?? "Local memory lifecycle changed."
            };
        }

        foreach (var influence in influences)
        {
            yield return new MemoryActivityReference
            {
                Kind = MemoryActivityKind.MemoryInfluenceRecorded,
                ActivityId = influence.InfluenceId,
                AgentId = agentId,
                MemoryItemId = influence.MemoryItemId,
                DecisionId = influence.DecisionId,
                ThoughtLedgerEntryId = influence.ThoughtLedgerEntryId,
                OccurredAt = influence.CreatedAt,
                Summary = influence.InfluenceSummary
            };
        }

        foreach (var handoff in outgoing)
        {
            yield return new MemoryActivityReference
            {
                Kind = MemoryActivityKind.MemoryHandoffOutgoing,
                ActivityId = handoff.HandoffMemorySliceId,
                AgentId = agentId,
                ThoughtLedgerEntryId = handoff.ThoughtLedgerEntryId,
                OccurredAt = handoff.CreatedAt,
                Summary = handoff.Summary
            };
        }

        foreach (var handoff in incoming)
        {
            yield return new MemoryActivityReference
            {
                Kind = MemoryActivityKind.MemoryHandoffIncoming,
                ActivityId = handoff.HandoffMemorySliceId,
                AgentId = agentId,
                ThoughtLedgerEntryId = handoff.ThoughtLedgerEntryId,
                OccurredAt = handoff.CreatedAt,
                Summary = handoff.Summary
            };
        }
    }

    private static IEnumerable<RunMemoryFinding> BuildFindings(
        IReadOnlyList<MemoryRow> memoryRows,
        IReadOnlyList<AgentMemoryReportItem> memoryItems,
        IReadOnlyList<AgentMemoryInfluenceReportItem> influences,
        IReadOnlyList<AgentMemoryHandoffReportItem> handoffs)
    {
        var memoryById = memoryItems.ToDictionary(item => item.MemoryItemId, StringComparer.Ordinal);
        var ownerByMemoryId = memoryRows.ToDictionary(row => row.MemoryItemId, row => row.AgentId, StringComparer.Ordinal);

        foreach (var influence in influences)
        {
            if (!ownerByMemoryId.TryGetValue(influence.MemoryItemId, out var ownerAgentId))
                ownerAgentId = "unknown";

            if (influence.Confidence < 0.5m)
            {
                yield return new RunMemoryFinding
                {
                    FindingType = RunMemoryFindingType.LowConfidenceInfluence,
                    Severity = MemoryReviewCandidateSeverity.Warning,
                    AgentId = ownerAgentId,
                    MemoryItemId = influence.MemoryItemId,
                    InfluenceId = influence.InfluenceId,
                    Summary = $"Influence '{influence.InfluenceId}' has confidence below 0.5."
                };
            }

            if (string.IsNullOrWhiteSpace(influence.ThoughtLedgerEntryId))
            {
                yield return new RunMemoryFinding
                {
                    FindingType = RunMemoryFindingType.MissingThoughtLedgerReference,
                    Severity = MemoryReviewCandidateSeverity.Info,
                    AgentId = ownerAgentId,
                    MemoryItemId = influence.MemoryItemId,
                    InfluenceId = influence.InfluenceId,
                    Summary = $"Influence '{influence.InfluenceId}' has no ThoughtLedgerEntryId."
                };
            }

            if (memoryById.TryGetValue(influence.MemoryItemId, out var memory) &&
                memory.CurrentStatus == MemoryLifecycleStatus.Expired)
            {
                yield return new RunMemoryFinding
                {
                    FindingType = RunMemoryFindingType.ExpiredMemoryHadInfluence,
                    Severity = MemoryReviewCandidateSeverity.High,
                    AgentId = ownerAgentId,
                    MemoryItemId = influence.MemoryItemId,
                    InfluenceId = influence.InfluenceId,
                    Summary = $"Expired memory '{influence.MemoryItemId}' has recorded influence '{influence.InfluenceId}'."
                };
            }
        }

        foreach (var memory in memoryItems)
        {
            if (memory.MemoryType != AgentMemoryType.CandidatePattern)
                continue;

            var agentId = ownerByMemoryId.TryGetValue(memory.MemoryItemId, out var ownerAgentId)
                ? ownerAgentId
                : "unknown";

            yield return new RunMemoryFinding
            {
                FindingType = RunMemoryFindingType.CandidatePatternMemory,
                Severity = MemoryReviewCandidateSeverity.Info,
                AgentId = agentId,
                MemoryItemId = memory.MemoryItemId,
                Summary = $"CandidatePattern memory '{memory.MemoryItemId}' requires review before promotion."
            };
        }

        foreach (var handoff in handoffs)
        {
            if (handoff.AllowedUse == HandoffMemoryAllowedUse.NeedsVerification)
            {
                yield return new RunMemoryFinding
                {
                    FindingType = RunMemoryFindingType.NeedsVerificationHandoff,
                    Severity = MemoryReviewCandidateSeverity.Warning,
                    AgentId = handoff.TargetAgentId,
                    HandoffMemorySliceId = handoff.HandoffMemorySliceId,
                    Summary = $"Handoff '{handoff.HandoffMemorySliceId}' requires target-agent verification."
                };
            }

            if (string.IsNullOrWhiteSpace(handoff.ThoughtLedgerEntryId))
            {
                yield return new RunMemoryFinding
                {
                    FindingType = RunMemoryFindingType.MissingThoughtLedgerReference,
                    Severity = MemoryReviewCandidateSeverity.Info,
                    AgentId = handoff.SourceAgentId,
                    HandoffMemorySliceId = handoff.HandoffMemorySliceId,
                    Summary = $"Handoff '{handoff.HandoffMemorySliceId}' has no ThoughtLedgerEntryId."
                };
            }

            foreach (var snapshot in handoff.MemorySnapshots.Where(snapshot => snapshot.StatusAtHandoff == MemoryLifecycleStatus.ProposedForReview))
            {
                yield return new RunMemoryFinding
                {
                    FindingType = RunMemoryFindingType.ProposedMemoryHandedOff,
                    Severity = MemoryReviewCandidateSeverity.Warning,
                    AgentId = handoff.SourceAgentId,
                    MemoryItemId = snapshot.MemoryItemId,
                    HandoffMemorySliceId = handoff.HandoffMemorySliceId,
                    Summary = $"Handoff '{handoff.HandoffMemorySliceId}' included proposed memory '{snapshot.MemoryItemId}'."
                };
            }
        }
    }

    private static AgentMemoryReportItem ToMemoryReportItem(
        MemoryRow row,
        IReadOnlyList<EventRow> events,
        DateTimeOffset generatedAt) =>
        new()
        {
            MemoryItemId = row.MemoryItemId,
            MemoryType = (AgentMemoryType)row.MemoryType,
            AuthorityLevel = (MemoryAuthorityLevel)row.AuthorityLevel,
            CurrentStatus = ToLifecycleStatus(row.CurrentEventType, row.ExpiresAtUtc, generatedAt),
            Title = row.Title,
            Summary = row.Summary,
            Confidence = row.Confidence,
            CreatedAt = ToUtc(row.CreatedAtUtc),
            ExpiresAt = row.ExpiresAtUtc is null ? null : ToUtc(row.ExpiresAtUtc.Value),
            LifecycleEventCount = events.Count(item => item.EventType != (int)AgentLocalMemoryEventType.Created),
            LifecycleThoughtLedgerEntryIds = events
                .Where(item => !string.IsNullOrWhiteSpace(item.ThoughtLedgerEntryId))
                .Select(item => item.ThoughtLedgerEntryId!)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            SupersedesMemoryItemId = row.SupersedesMemoryItemId,
            KnownLimitations = row.KnownLimitations
        };

    private static AgentMemoryInfluenceReportItem ToInfluenceReportItem(InfluenceRow row) =>
        new()
        {
            InfluenceId = row.InfluenceId,
            MemoryItemId = row.MemoryItemId,
            DecisionId = row.DecisionId,
            InfluenceType = (MemoryInfluenceType)row.InfluenceType,
            InfluenceSummary = row.InfluenceSummary,
            Confidence = row.Confidence,
            MemoryAuthorityLevelAtInfluence = (MemoryAuthorityLevel)row.MemoryAuthorityLevelAtInfluence,
            MemoryStatusAtInfluence = (MemoryLifecycleStatus)row.MemoryLifecycleStatusAtInfluence,
            CreatedAt = ToUtc(row.CreatedAtUtc),
            AffectedArtifactType = row.AffectedArtifactType,
            AffectedArtifactId = row.AffectedArtifactId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId
        };

    private static AgentMemoryHandoffReportItem ToHandoffReportItem(HandoffRow row) =>
        new()
        {
            HandoffMemorySliceId = row.HandoffMemorySliceId,
            SourceAgentId = row.SourceAgentId,
            TargetAgentId = row.TargetAgentId,
            MemoryItemIds = DeserializeStringArray(row.MemoryItemIdsJson),
            Summary = row.Summary,
            AllowedUse = (HandoffMemoryAllowedUse)row.AllowedUse,
            Confidence = row.Confidence,
            MemorySnapshots = DeserializeSnapshots(row.MemorySnapshotsJson),
            CreatedAt = ToUtc(row.CreatedAtUtc),
            ExpiresAt = row.ExpiresAtUtc is null ? null : ToUtc(row.ExpiresAtUtc.Value),
            InfluenceIds = string.IsNullOrWhiteSpace(row.InfluenceIdsJson) ? null : DeserializeStringArray(row.InfluenceIdsJson),
            DecisionId = row.DecisionId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            CorrelationId = row.CorrelationId
        };

    private static IReadOnlyList<string> DeserializeStringArray(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? Array.Empty<string>();

    private static IReadOnlyList<HandoffMemoryItemSnapshot> DeserializeSnapshots(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Array.Empty<HandoffMemoryItemSnapshot>()
            : JsonSerializer.Deserialize<IReadOnlyList<HandoffMemoryItemSnapshot>>(json, JsonOptions) ?? Array.Empty<HandoffMemoryItemSnapshot>();

    private static MemoryLifecycleStatus ToLifecycleStatus(
        int? eventType,
        DateTime? expiresAtUtc,
        DateTimeOffset generatedAt)
    {
        if (expiresAtUtc is not null && ToUtc(expiresAtUtc.Value) <= generatedAt)
            return MemoryLifecycleStatus.Expired;

        return eventType switch
        {
            (int)AgentLocalMemoryEventType.Superseded => MemoryLifecycleStatus.Superseded,
            (int)AgentLocalMemoryEventType.Expired => MemoryLifecycleStatus.Expired,
            (int)AgentLocalMemoryEventType.Invalidated => MemoryLifecycleStatus.Invalidated,
            (int)AgentLocalMemoryEventType.ProposedForReview => MemoryLifecycleStatus.ProposedForReview,
            (int)AgentLocalMemoryEventType.Rejected => MemoryLifecycleStatus.Rejected,
            (int)AgentLocalMemoryEventType.Accepted => MemoryLifecycleStatus.Accepted,
            _ => MemoryLifecycleStatus.Active
        };
    }

    private static void ThrowIfInvalid(RunMemoryReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.CampaignId) ||
            string.IsNullOrWhiteSpace(request.RunId))
        {
            throw new InvalidOperationException("Run memory report requires tenant, project, campaign, and run identity.");
        }
    }

    private static DateTimeOffset ToUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class MemoryRow
    {
        public string MemoryItemId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public int MemoryType { get; set; }
        public int AuthorityLevel { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public string? SupersedesMemoryItemId { get; set; }
        public string? KnownLimitations { get; set; }
        public int? CurrentEventType { get; set; }
        public DateTime? CurrentEventAtUtc { get; set; }
    }

    private sealed class EventRow
    {
        public string AgentId { get; set; } = string.Empty;
        public string MemoryEventId { get; set; } = string.Empty;
        public string MemoryItemId { get; set; } = string.Empty;
        public int EventType { get; set; }
        public string? EventReason { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? DecisionId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
    }

    private sealed class InfluenceRow
    {
        public string InfluenceId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string MemoryItemId { get; set; } = string.Empty;
        public string DecisionId { get; set; } = string.Empty;
        public int InfluenceType { get; set; }
        public string InfluenceSummary { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public int MemoryAuthorityLevelAtInfluence { get; set; }
        public int MemoryLifecycleStatusAtInfluence { get; set; }
        public string? AffectedArtifactType { get; set; }
        public string? AffectedArtifactId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
    }

    private sealed class HandoffRow
    {
        public string HandoffMemorySliceId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string RunId { get; set; } = string.Empty;
        public string SourceAgentId { get; set; } = string.Empty;
        public string TargetAgentId { get; set; } = string.Empty;
        public string MemoryItemIdsJson { get; set; } = "[]";
        public string MemorySnapshotsJson { get; set; } = "[]";
        public string Summary { get; set; } = string.Empty;
        public int AllowedUse { get; set; }
        public decimal Confidence { get; set; }
        public string? InfluenceIdsJson { get; set; }
        public string? DecisionId { get; set; }
        public string? ThoughtLedgerEntryId { get; set; }
        public string? CorrelationId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }
}
