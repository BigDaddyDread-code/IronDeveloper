using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory.Collective;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlCollectiveMemoryRetrievalService : ICollectiveMemoryRetrievalService
{
    public const string ScopeRequired = "COLLECTIVE_RETRIEVAL_SCOPE_REQUIRED";
    public const string TenantRequired = "COLLECTIVE_RETRIEVAL_TENANT_REQUIRED";
    public const string ProjectRequired = "COLLECTIVE_RETRIEVAL_PROJECT_REQUIRED";
    public const string InvalidMode = "COLLECTIVE_RETRIEVAL_INVALID_MODE";
    public const string InvalidAuthorityFilter = "COLLECTIVE_RETRIEVAL_INVALID_AUTHORITY_FILTER";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICollectiveMemoryStabilityScorer _stabilityScorer;

    public SqlCollectiveMemoryRetrievalService(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new CollectiveMemoryStabilityScorer())
    {
    }

    public SqlCollectiveMemoryRetrievalService(
        IDbConnectionFactory connectionFactory,
        ICollectiveMemoryStabilityScorer stabilityScorer)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _stabilityScorer = stabilityScorer ?? throw new ArgumentNullException(nameof(stabilityScorer));
    }

    public async Task<CollectiveMemoryRetrievalResult> RetrieveAsync(
        CollectiveMemoryRetrievalQuery query,
        CancellationToken cancellationToken = default)
    {
        var retrievalId = $"collective-retrieval-{Guid.NewGuid():N}";
        var retrievedAt = DateTimeOffset.UtcNow;
        var issues = Validate(query);

        if (issues.Any(issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase)))
        {
            return new CollectiveMemoryRetrievalResult
            {
                RetrievalId = retrievalId,
                Query = query,
                Matches = [],
                Issues = issues,
                RetrievedAt = retrievedAt
            };
        }

        var take = ClampTake(query.Take);
        var prefilterTake = Math.Max(take * 4, 50);

        using var connection = _connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<CollectiveMemoryRetrievalRow>(new CommandDefinition(
            """
            SELECT TOP (@Take) *
            FROM agent.vwCollectiveMemoryCurrentState
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND (@KnowledgeDomainId IS NULL OR KnowledgeDomainId = @KnowledgeDomainId)
              AND (@ComponentId IS NULL OR ComponentId = @ComponentId)
              AND (@RepositoryId IS NULL OR RepositoryId = @RepositoryId)
              AND (@MemoryType IS NULL OR MemoryType = @MemoryType)
              AND (@DecisionId IS NULL OR DecisionId = @DecisionId)
              AND
              (
                  @Text IS NULL
                  OR Title LIKE '%' + @Text + '%'
                  OR Summary LIKE '%' + @Text + '%'
                  OR ISNULL(CollectiveMemoryJson, '') LIKE '%' + @Text + '%'
              )
            ORDER BY CreatedAtUtc DESC, CollectiveMemoryId DESC
            """,
            new
            {
                query.Scope.TenantId,
                query.Scope.ProjectId,
                query.Scope.KnowledgeDomainId,
                query.Scope.ComponentId,
                query.Scope.RepositoryId,
                MemoryType = query.MemoryType is null ? (int?)null : (int)query.MemoryType.Value,
                query.DecisionId,
                Text = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text,
                Take = prefilterTake
            },
            cancellationToken: cancellationToken))).ToArray();

        var matches = new List<CollectiveMemoryRetrievalMatch>();

        foreach (var row in rows)
        {
            var item = ToItem(row);
            var events = await LoadEventsAsync(item.Scope, item.CollectiveMemoryId, cancellationToken);
            var stabilityScore = _stabilityScorer.Score(new CollectiveMemoryStabilityInput
            {
                StabilityRunId = $"{retrievalId}-{item.CollectiveMemoryId}-stability",
                Memory = item,
                EvidenceAggregate = BuildAggregate(item, retrievedAt),
                Events = events,
                EvaluatedAt = retrievedAt,
                CorrelationId = retrievalId
            });

            if (!MatchesAuthorityFilter(item.AuthorityLevel, query.AuthorityFilter))
                continue;

            if (!query.IncludeExpired && item.ExpiresAt.HasValue && item.ExpiresAt.Value <= retrievedAt)
                continue;

            if (!query.IncludeContradicted && item.Contradictions.Count > 0)
                continue;

            if (!MatchesDefaultLifecycle(item.Status, query.AuthorityFilter))
                continue;

            if (!string.IsNullOrWhiteSpace(query.SourceId) && !item.Sources.Any(source => string.Equals(source.SourceId, query.SourceId, StringComparison.Ordinal)))
                continue;

            if (query.MinimumStabilityBand.HasValue && stabilityScore.Band < query.MinimumStabilityBand.Value)
                continue;

            var rank = Rank(item, stabilityScore, query, retrievedAt);

            matches.Add(new CollectiveMemoryRetrievalMatch
            {
                RetrievalMatchId = $"{retrievalId}-{item.CollectiveMemoryId}",
                Memory = item,
                RankScore = rank.Score,
                RankReason = rank.Reason,
                StabilityScore = stabilityScore,
                AuthorityFilterApplied = query.AuthorityFilter,
                IsAuthoritativeForAction = false,
                RequiresConscienceBeforeUse = true,
                RequiresPolicyApprovalForAction = true,
                EvidenceIds = item.EvidenceRefs.Select(evidence => evidence.EvidenceId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray(),
                SourceIds = item.Sources.Select(source => source.SourceId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray(),
                Warnings = BuildWarnings(item, stabilityScore, retrievedAt)
            });
        }

        return new CollectiveMemoryRetrievalResult
        {
            RetrievalId = retrievalId,
            Query = query with { Take = take },
            Matches = matches
                .OrderByDescending(match => match.RankScore)
                .ThenBy(match => match.Memory.CollectiveMemoryId, StringComparer.Ordinal)
                .Take(take)
                .ToArray(),
            Issues = issues,
            RetrievedAt = retrievedAt
        };
    }

    private async Task<IReadOnlyList<CollectiveMemoryEventRecord>> LoadEventsAsync(
        CollectiveMemoryScope scope,
        string collectiveMemoryId,
        CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<CollectiveMemoryEventRow>(new CommandDefinition(
            """
            SELECT e.*
            FROM agent.CollectiveMemoryEvent e
            INNER JOIN agent.CollectiveMemoryItem i
                ON i.CollectiveMemoryId = e.CollectiveMemoryId
            WHERE i.TenantId = @TenantId
              AND i.ProjectId = @ProjectId
              AND e.CollectiveMemoryId = @CollectiveMemoryId
            ORDER BY e.CreatedAtUtc, e.CollectiveMemoryEventId
            """,
            new { scope.TenantId, scope.ProjectId, CollectiveMemoryId = collectiveMemoryId },
            cancellationToken: cancellationToken))).ToArray();

        return rows.Select(row => new CollectiveMemoryEventRecord
        {
            CollectiveMemoryEventId = row.CollectiveMemoryEventId,
            CollectiveMemoryId = row.CollectiveMemoryId,
            EventType = (CollectiveMemoryEventType)row.EventType,
            Reason = row.Reason,
            CreatedAt = row.CreatedAtUtc,
            CreatedByUserId = row.CreatedByUserId,
            CreatedByAgentId = row.CreatedByAgentId,
            DecisionId = row.DecisionId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            CorrelationId = row.CorrelationId,
            EventJson = row.EventJson
        }).ToArray();
    }

    private static IReadOnlyList<CollectiveMemoryRetrievalIssue> Validate(CollectiveMemoryRetrievalQuery query)
    {
        var issues = new List<CollectiveMemoryRetrievalIssue>();

        if (query is null)
        {
            issues.Add(Issue(ScopeRequired, "Retrieval query is required."));
            return issues;
        }

        if (query.Scope is null)
        {
            issues.Add(Issue(ScopeRequired, "Collective memory retrieval requires a scope."));
            return issues;
        }

        if (string.IsNullOrWhiteSpace(query.Scope.TenantId))
            issues.Add(Issue(TenantRequired, "Collective memory retrieval requires a tenant ID."));

        if (string.IsNullOrWhiteSpace(query.Scope.ProjectId))
            issues.Add(Issue(ProjectRequired, "Collective memory retrieval requires a project ID."));

        if (!Enum.IsDefined(query.Mode))
            issues.Add(Issue(InvalidMode, "Collective memory retrieval mode is invalid."));

        if (!Enum.IsDefined(query.AuthorityFilter))
            issues.Add(Issue(InvalidAuthorityFilter, "Collective memory retrieval authority filter is invalid."));

        return issues;
    }

    private static bool MatchesAuthorityFilter(
        CollectiveMemoryAuthorityLevel authorityLevel,
        CollectiveMemoryRetrievalAuthorityFilter filter) =>
        filter switch
        {
            CollectiveMemoryRetrievalAuthorityFilter.AcceptedOnly =>
                authorityLevel == CollectiveMemoryAuthorityLevel.Accepted,
            CollectiveMemoryRetrievalAuthorityFilter.ReviewedOrAccepted =>
                authorityLevel is CollectiveMemoryAuthorityLevel.Reviewed or CollectiveMemoryAuthorityLevel.Accepted,
            CollectiveMemoryRetrievalAuthorityFilter.IncludeCandidates =>
                authorityLevel is CollectiveMemoryAuthorityLevel.Candidate or CollectiveMemoryAuthorityLevel.Reviewed or CollectiveMemoryAuthorityLevel.Accepted,
            CollectiveMemoryRetrievalAuthorityFilter.IncludeRejectedAndDeprecated => true,
            _ => false
        };

    private static bool MatchesDefaultLifecycle(
        CollectiveMemoryStatus status,
        CollectiveMemoryRetrievalAuthorityFilter authorityFilter)
    {
        if (authorityFilter == CollectiveMemoryRetrievalAuthorityFilter.IncludeRejectedAndDeprecated)
            return true;

        return status is not CollectiveMemoryStatus.Rejected and
            not CollectiveMemoryStatus.Deprecated and
            not CollectiveMemoryStatus.Superseded and
            not CollectiveMemoryStatus.Invalidated;
    }

    private static CollectiveMemoryEvidenceAggregate BuildAggregate(
        CollectiveMemoryItem item,
        DateTimeOffset retrievedAt)
    {
        var uniqueSourceCount = item.Sources
            .Select(source => source.SourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var uniqueSourceTypeCount = item.Sources
            .Select(source => source.SourceType)
            .Distinct()
            .Count();
        var evidenceCount = item.EvidenceRefs.Count;
        var contradictionWeight = item.Contradictions.Sum(contradiction => contradiction.Weight ?? 1.0m);
        var supportWeight = item.EvidenceRefs.Sum(evidence => evidence.Weight ?? 1.0m);

        return new CollectiveMemoryEvidenceAggregate
        {
            AggregationId = $"retrieval-aggregate-{item.CollectiveMemoryId}",
            CollectiveMemoryId = item.CollectiveMemoryId,
            Scope = item.Scope,
            SupportingEvidenceCount = evidenceCount,
            WeakSupportingEvidenceCount = 0,
            NeutralEvidenceCount = 0,
            ContradictingEvidenceCount = item.Contradictions.Count,
            WeakContradictingEvidenceCount = 0,
            UniqueSourceCount = uniqueSourceCount,
            UniqueSourceTypeCount = uniqueSourceTypeCount,
            SupportWeight = supportWeight,
            ContradictionWeight = contradictionWeight,
            EvidenceQuality = EvidenceQuality(evidenceCount, uniqueSourceTypeCount, contradictionWeight),
            EvidenceCoverage = EvidenceCoverage(uniqueSourceCount, uniqueSourceTypeCount),
            ConflictLevel = ConflictLevel(supportWeight, contradictionWeight),
            Readiness = contradictionWeight > 0m
                ? CollectiveMemoryEvidenceReadiness.NeedsContradictionReview
                : CollectiveMemoryEvidenceReadiness.ReadyForHumanReview,
            AggregatedAt = retrievedAt,
            EvidenceContributionIds = item.EvidenceRefs.Select(evidence => evidence.EvidenceId).ToArray(),
            ContradictionContributionIds = item.Contradictions.Select(contradiction => contradiction.ContradictionId).ToArray(),
            ReviewWarnings = ["Retrieval aggregate is advisory only and does not grant authority."]
        };
    }

    private static CollectiveMemoryEvidenceQuality EvidenceQuality(
        int evidenceCount,
        int uniqueSourceTypeCount,
        decimal contradictionWeight)
    {
        if (evidenceCount <= 0)
            return CollectiveMemoryEvidenceQuality.Unknown;

        if (evidenceCount >= 2 && uniqueSourceTypeCount >= 2 && contradictionWeight == 0m)
            return CollectiveMemoryEvidenceQuality.Strong;

        return evidenceCount >= 1 ? CollectiveMemoryEvidenceQuality.Moderate : CollectiveMemoryEvidenceQuality.Weak;
    }

    private static CollectiveMemoryEvidenceCoverage EvidenceCoverage(int uniqueSourceCount, int uniqueSourceTypeCount)
    {
        if (uniqueSourceCount <= 0)
            return CollectiveMemoryEvidenceCoverage.None;

        if (uniqueSourceTypeCount >= 2)
            return CollectiveMemoryEvidenceCoverage.MultipleIndependentSourceTypes;

        return uniqueSourceCount >= 2
            ? CollectiveMemoryEvidenceCoverage.MultipleSameTypeSources
            : CollectiveMemoryEvidenceCoverage.SingleSource;
    }

    private static CollectiveMemoryEvidenceConflictLevel ConflictLevel(decimal supportWeight, decimal contradictionWeight)
    {
        if (contradictionWeight <= 0m)
            return CollectiveMemoryEvidenceConflictLevel.None;

        if (contradictionWeight >= supportWeight)
            return CollectiveMemoryEvidenceConflictLevel.High;

        return contradictionWeight >= supportWeight * 0.5m
            ? CollectiveMemoryEvidenceConflictLevel.Medium
            : CollectiveMemoryEvidenceConflictLevel.Low;
    }

    private static (decimal Score, string Reason) Rank(
        CollectiveMemoryItem item,
        CollectiveMemoryStabilityScore stabilityScore,
        CollectiveMemoryRetrievalQuery query,
        DateTimeOffset retrievedAt)
    {
        var authority = AuthorityWeight(item.AuthorityLevel);
        var stability = StabilityWeight(stabilityScore.Band);
        var textMatch = TextMatchWeight(item, query.Text);
        var recency = RecencyWeight(item, retrievedAt);
        var evidence = EvidenceWeight(item.EvidenceRefs.Count);
        var score = Clamp01(
            authority * 0.30m +
            stability * 0.30m +
            textMatch * 0.20m +
            recency * 0.10m +
            evidence * 0.10m);

        return (score, $"authority={authority:0.##}; stability={stability:0.##}; text={textMatch:0.##}; recency={recency:0.##}; evidence={evidence:0.##}");
    }

    private static decimal AuthorityWeight(CollectiveMemoryAuthorityLevel authorityLevel) =>
        authorityLevel switch
        {
            CollectiveMemoryAuthorityLevel.Accepted => 1.0m,
            CollectiveMemoryAuthorityLevel.Reviewed => 0.75m,
            CollectiveMemoryAuthorityLevel.Candidate => 0.35m,
            CollectiveMemoryAuthorityLevel.Deprecated => 0.1m,
            _ => 0m
        };

    private static decimal StabilityWeight(CollectiveMemoryStabilityBand stabilityBand) =>
        stabilityBand switch
        {
            CollectiveMemoryStabilityBand.StronglyStable => 1.0m,
            CollectiveMemoryStabilityBand.Stable => 0.75m,
            CollectiveMemoryStabilityBand.Emerging => 0.4m,
            CollectiveMemoryStabilityBand.Unstable => 0.1m,
            _ => 0m
        };

    private static decimal TextMatchWeight(CollectiveMemoryItem item, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0.5m;

        if (string.Equals(item.Title, text, StringComparison.OrdinalIgnoreCase) ||
            item.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
            return 1.0m;

        if (item.Summary.Contains(text, StringComparison.OrdinalIgnoreCase))
            return 0.75m;

        return item.CollectiveMemoryJson?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
            ? 0.4m
            : 0m;
    }

    private static decimal RecencyWeight(CollectiveMemoryItem item, DateTimeOffset retrievedAt)
    {
        if (item.LastConfirmedAt.HasValue && item.LastConfirmedAt.Value >= retrievedAt.AddDays(-30))
            return 1.0m;

        if (item.LastReviewedAt.HasValue && item.LastReviewedAt.Value >= retrievedAt.AddDays(-90))
            return 0.75m;

        return item.CreatedAt >= retrievedAt.AddDays(-180) ? 0.4m : 0.2m;
    }

    private static decimal EvidenceWeight(int evidenceCount)
    {
        if (evidenceCount >= 3)
            return 1.0m;
        if (evidenceCount >= 2)
            return 0.75m;
        return evidenceCount == 1 ? 0.4m : 0m;
    }

    private static IReadOnlyList<string> BuildWarnings(
        CollectiveMemoryItem item,
        CollectiveMemoryStabilityScore stabilityScore,
        DateTimeOffset retrievedAt)
    {
        var warnings = new List<string>
        {
            "Collective memory retrieval is advisory only.",
            "Retrieval does not grant policy approval.",
            "Retrieval does not grant tool execution approval.",
            "Conscience/policy/approval checks are still required before action."
        };

        if (item.AuthorityLevel != CollectiveMemoryAuthorityLevel.Accepted)
            warnings.Add("Candidate memory is not accepted.");

        if (item.Contradictions.Count > 0)
            warnings.Add("Memory has contradiction pressure.");

        if (item.Status is CollectiveMemoryStatus.Deprecated or CollectiveMemoryStatus.Rejected or CollectiveMemoryStatus.Superseded or CollectiveMemoryStatus.Invalidated)
            warnings.Add("Memory is deprecated, rejected, superseded, or invalidated.");

        if (item.ExpiresAt.HasValue)
            warnings.Add(item.ExpiresAt.Value <= retrievedAt ? "Memory is expired." : "Memory has an expiry date and may need review.");

        if (stabilityScore.Band is CollectiveMemoryStabilityBand.Unknown or CollectiveMemoryStabilityBand.Unstable)
            warnings.Add("Stability score is Unknown or Unstable.");

        return warnings;
    }

    private static CollectiveMemoryItem ToItem(CollectiveMemoryRetrievalRow row) =>
        new()
        {
            CollectiveMemoryId = row.CollectiveMemoryId,
            Scope = new CollectiveMemoryScope
            {
                TenantId = row.TenantId,
                ProjectId = row.ProjectId,
                KnowledgeDomainId = row.KnowledgeDomainId,
                ComponentId = row.ComponentId,
                RepositoryId = row.RepositoryId
            },
            MemoryType = (CollectiveMemoryType)row.MemoryType,
            AuthorityLevel = (CollectiveMemoryAuthorityLevel)row.AuthorityLevel,
            Status = Enum.Parse<CollectiveMemoryStatus>(row.CurrentStatus),
            ReviewState = Enum.Parse<CollectiveMemoryReviewState>(row.CurrentReviewState),
            Title = row.Title,
            Summary = row.Summary,
            Sources = DeserializeArray<CollectiveMemorySourceRef>(row.SourcesJson),
            EvidenceRefs = DeserializeArray<CollectiveMemoryEvidenceRef>(row.EvidenceRefsJson),
            Contradictions = DeserializeArray<CollectiveMemoryContradictionRef>(row.ContradictionsJson),
            Supersedes = DeserializeArray<CollectiveMemorySupersessionRef>(row.SupersedesJson),
            Confidence = row.Confidence,
            CreatedAt = row.CreatedAtUtc,
            LastReviewedAt = row.LastReviewedAtUtc,
            LastConfirmedAt = row.LastConfirmedAtUtc,
            ExpiresAt = row.ExpiresAtUtc,
            DecisionId = row.DecisionId,
            ThoughtLedgerEntryId = row.ThoughtLedgerEntryId,
            CorrelationId = row.CorrelationId,
            ContentHashSha256 = row.ContentHashSha256,
            CollectiveMemoryJson = row.CollectiveMemoryJson
        };

    private static IReadOnlyList<T> DeserializeArray<T>(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<T>>(json, JsonOptions) ?? [];

    private static int ClampTake(int take) => Math.Clamp(take <= 0 ? 10 : take, 1, 50);

    private static decimal Clamp01(decimal value)
    {
        if (value < 0m)
            return 0m;

        return value > 1m ? 1m : value;
    }

    private static CollectiveMemoryRetrievalIssue Issue(string code, string message, string severity = "Error") =>
        new()
        {
            Code = code,
            Severity = severity,
            Message = message
        };

    private sealed record CollectiveMemoryRetrievalRow
    {
        public required string CollectiveMemoryId { get; init; }
        public required string TenantId { get; init; }
        public required string ProjectId { get; init; }
        public string? KnowledgeDomainId { get; init; }
        public string? ComponentId { get; init; }
        public string? RepositoryId { get; init; }
        public required int MemoryType { get; init; }
        public required int AuthorityLevel { get; init; }
        public required string CurrentStatus { get; init; }
        public required string CurrentReviewState { get; init; }
        public required string Title { get; init; }
        public required string Summary { get; init; }
        public required string SourcesJson { get; init; }
        public required string EvidenceRefsJson { get; init; }
        public required string ContradictionsJson { get; init; }
        public required string SupersedesJson { get; init; }
        public required decimal Confidence { get; init; }
        public required DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? LastReviewedAtUtc { get; init; }
        public DateTimeOffset? LastConfirmedAtUtc { get; init; }
        public DateTimeOffset? ExpiresAtUtc { get; init; }
        public string? DecisionId { get; init; }
        public string? ThoughtLedgerEntryId { get; init; }
        public string? CorrelationId { get; init; }
        public string? CollectiveMemoryJson { get; init; }
        public string? ContentHashSha256 { get; init; }
    }

    private sealed record CollectiveMemoryEventRow
    {
        public required string CollectiveMemoryEventId { get; init; }
        public required string CollectiveMemoryId { get; init; }
        public required int EventType { get; init; }
        public required string Reason { get; init; }
        public required DateTimeOffset CreatedAtUtc { get; init; }
        public string? CreatedByUserId { get; init; }
        public string? CreatedByAgentId { get; init; }
        public string? DecisionId { get; init; }
        public string? ThoughtLedgerEntryId { get; init; }
        public string? CorrelationId { get; init; }
        public string? EventJson { get; init; }
    }
}
