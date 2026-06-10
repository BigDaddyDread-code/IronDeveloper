using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.AgentMemory.Collective;
using IronDev.Data;

namespace IronDev.Infrastructure.AgentMemory;

public sealed class SqlCollectiveMemoryPromotionService : ICollectiveMemoryPromotionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICollectiveMemoryContractValidator _validator;

    public SqlCollectiveMemoryPromotionService(
        IDbConnectionFactory connectionFactory,
        ICollectiveMemoryContractValidator? validator = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? new CollectiveMemoryContractValidator();
    }

    public async Task<CollectiveMemoryPromotionResult> PromoteAsync(
        CollectiveMemoryPromotionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = ValidateRequest(request).ToList();
        if (issues.Any(IsError))
            return Blocked(request, issues);

        if (request.Decision == CollectiveMemoryPromotionDecision.Defer)
        {
            issues.Add(Warning(
                "COLLECTIVE_PROMOTION_DEFERRED",
                "Deferred collective-memory promotion creates no collective memory in this PR."));

            return new CollectiveMemoryPromotionResult
            {
                PromotionRequestId = request.PromotionRequestId,
                Decision = request.Decision,
                CreatedCollectiveMemory = false,
                CollectiveMemoryId = null,
                Outcome = CollectiveMemoryPromotionOutcome.Deferred,
                Issues = issues
            };
        }

        if (request.Decision is CollectiveMemoryPromotionDecision.Deprecate or CollectiveMemoryPromotionDecision.Supersede)
        {
            issues.Add(Error(
                "COLLECTIVE_PROMOTION_LIFECYCLE_NOT_SUPPORTED",
                "Deprecate and supersede promotion decisions are not supported by PR 16."));
            return Blocked(request, issues);
        }

        var persisted = BuildPersistedItem(request);
        foreach (var issue in _validator.Validate(persisted))
            issues.Add(Error(issue.Code, issue.Message));

        if (issues.Any(IsError))
            return Blocked(request, issues);

        using var connection = _connectionFactory.CreateConnection();

        await connection.ExecuteAsync(new CommandDefinition(
            "agent.usp_CollectiveMemory_CreateFromManualPromotion",
            new
            {
                persisted.CollectiveMemoryId,
                persisted.Scope.TenantId,
                persisted.Scope.ProjectId,
                persisted.Scope.KnowledgeDomainId,
                persisted.Scope.ComponentId,
                persisted.Scope.RepositoryId,
                MemoryType = (int)persisted.MemoryType,
                AuthorityLevel = (int)persisted.AuthorityLevel,
                persisted.Title,
                persisted.Summary,
                SourcesJson = Serialize(persisted.Sources),
                EvidenceRefsJson = Serialize(persisted.EvidenceRefs),
                ContradictionsJson = Serialize(persisted.Contradictions),
                SupersedesJson = Serialize(persisted.Supersedes),
                persisted.Confidence,
                CreatedAtUtc = persisted.CreatedAt.UtcDateTime,
                LastReviewedAtUtc = persisted.LastReviewedAt?.UtcDateTime,
                LastConfirmedAtUtc = persisted.LastConfirmedAt?.UtcDateTime,
                ExpiresAtUtc = persisted.ExpiresAt?.UtcDateTime,
                persisted.DecisionId,
                persisted.ThoughtLedgerEntryId,
                persisted.CorrelationId,
                persisted.CollectiveMemoryJson,
                persisted.ContentHashSha256,
                CreatedEventId = BuildEventId(request.PromotionRequestId, "created"),
                DecisionEventId = BuildEventId(
                    request.PromotionRequestId,
                    request.Decision == CollectiveMemoryPromotionDecision.Accept ? "accepted" : "rejected"),
                DecisionEventType = request.Decision == CollectiveMemoryPromotionDecision.Accept
                    ? (int)CollectiveMemoryEventType.Accepted
                    : (int)CollectiveMemoryEventType.Rejected,
                Reason = request.Reason ?? BuildDefaultReason(request),
                CreatedByUserId = request.DecidedByUserId,
                CreatedByAgentId = request.DecidedByAgentId,
                EventJson = Serialize(new
                {
                    request.PromotionRequestId,
                    decision = request.Decision.ToString(),
                    aggregationId = request.AggregationResult.Aggregate.AggregationId,
                    readiness = request.AggregationResult.Aggregate.Readiness.ToString(),
                    request.DecisionId,
                    request.CorrelationId
                })
            },
            cancellationToken: cancellationToken,
            commandType: CommandType.StoredProcedure));

        return new CollectiveMemoryPromotionResult
        {
            PromotionRequestId = request.PromotionRequestId,
            Decision = request.Decision,
            CreatedCollectiveMemory = true,
            CollectiveMemoryId = persisted.CollectiveMemoryId,
            Outcome = request.Decision == CollectiveMemoryPromotionDecision.Accept
                ? CollectiveMemoryPromotionOutcome.AcceptedCreated
                : CollectiveMemoryPromotionOutcome.RejectedRecorded,
            Issues = issues
        };
    }

    private IReadOnlyList<CollectiveMemoryPromotionIssue> ValidateRequest(CollectiveMemoryPromotionRequest request)
    {
        var issues = new List<CollectiveMemoryPromotionIssue>();

        if (string.IsNullOrWhiteSpace(request.PromotionRequestId))
            issues.Add(Error("COLLECTIVE_PROMOTION_REQUEST_ID_REQUIRED", "PromotionRequestId is required."));

        if (request.Candidate is null)
        {
            issues.Add(Error("COLLECTIVE_PROMOTION_CANDIDATE_REQUIRED", "Candidate collective memory is required."));
        }
        else
        {
            foreach (var issue in _validator.Validate(request.Candidate))
                issues.Add(Error(issue.Code, issue.Message));
        }

        if (request.AggregationResult is null)
        {
            issues.Add(Error("COLLECTIVE_PROMOTION_AGGREGATION_REQUIRED", "AggregationResult is required."));
        }
        else
        {
            if (request.AggregationResult.HasErrors)
                issues.Add(Error("COLLECTIVE_PROMOTION_AGGREGATION_HAS_ERRORS", "AggregationResult contains errors and cannot be promoted."));

            if (request.Candidate is not null &&
                !string.Equals(request.AggregationResult.Aggregate.CollectiveMemoryId, request.Candidate.CollectiveMemoryId, StringComparison.Ordinal))
            {
                issues.Add(Error("COLLECTIVE_PROMOTION_AGGREGATION_CANDIDATE_MISMATCH", "AggregationResult collective-memory ID must match the candidate."));
            }
        }

        if (!Enum.IsDefined(request.Decision))
            issues.Add(Error("COLLECTIVE_PROMOTION_DECISION_INVALID", "Promotion decision is invalid."));

        if (string.IsNullOrWhiteSpace(request.DecisionId))
            issues.Add(Error("COLLECTIVE_PROMOTION_DECISION_ID_REQUIRED", "DecisionId is required."));

        if (request.DecidedAt == default)
            issues.Add(Error("COLLECTIVE_PROMOTION_DECIDED_AT_REQUIRED", "DecidedAt is required."));

        if (string.IsNullOrWhiteSpace(request.DecidedByUserId) && string.IsNullOrWhiteSpace(request.DecidedByAgentId))
            issues.Add(Error("COLLECTIVE_PROMOTION_ACTOR_REQUIRED", "At least one actor is required."));

        if (request.Decision == CollectiveMemoryPromotionDecision.Accept)
        {
            if (request.AggregationResult is not null && request.AggregationResult.Aggregate.Readiness != CollectiveMemoryEvidenceReadiness.ReadyForHumanReview)
                issues.Add(Error("COLLECTIVE_PROMOTION_NOT_READY_FOR_HUMAN_REVIEW", "Accept requires ReadyForHumanReview aggregation readiness."));

            if (string.IsNullOrWhiteSpace(request.DecidedByUserId))
                issues.Add(Error("COLLECTIVE_PROMOTION_HUMAN_ACTOR_REQUIRED", "Accept requires an explicit human/governance actor."));

            if (request.Candidate?.EvidenceRefs is null || request.Candidate.EvidenceRefs.Count == 0)
                issues.Add(Error("COLLECTIVE_PROMOTION_EVIDENCE_REQUIRED", "Accept requires candidate evidence."));
        }

        if ((request.Decision is CollectiveMemoryPromotionDecision.Reject or
                CollectiveMemoryPromotionDecision.Deprecate or
                CollectiveMemoryPromotionDecision.Supersede) &&
            string.IsNullOrWhiteSpace(request.Reason))
        {
            issues.Add(Error("COLLECTIVE_PROMOTION_REASON_REQUIRED", "Reject, deprecate, and supersede require a reason."));
        }

        if (ContainsRawPrivateReasoning(request.Reason))
            issues.Add(Error("COLLECTIVE_PROMOTION_RAW_PRIVATE_REASONING_BLOCKED", "Promotion reason must not contain raw private reasoning markers."));

        return issues;
    }

    private static CollectiveMemoryItem BuildPersistedItem(CollectiveMemoryPromotionRequest request)
    {
        if (request.Decision == CollectiveMemoryPromotionDecision.Accept)
        {
            return request.Candidate with
            {
                AuthorityLevel = CollectiveMemoryAuthorityLevel.Accepted,
                Status = CollectiveMemoryStatus.Active,
                ReviewState = CollectiveMemoryReviewState.ApprovedForAcceptance,
                LastReviewedAt = request.DecidedAt,
                DecisionId = request.DecisionId,
                ThoughtLedgerEntryId = request.ThoughtLedgerEntryId ?? request.Candidate.ThoughtLedgerEntryId,
                CorrelationId = request.CorrelationId ?? request.Candidate.CorrelationId
            };
        }

        return request.Candidate with
        {
            AuthorityLevel = CollectiveMemoryAuthorityLevel.Rejected,
            Status = CollectiveMemoryStatus.Rejected,
            ReviewState = CollectiveMemoryReviewState.RejectedByReview,
            LastReviewedAt = request.DecidedAt,
            DecisionId = request.DecisionId,
            ThoughtLedgerEntryId = request.ThoughtLedgerEntryId ?? request.Candidate.ThoughtLedgerEntryId,
            CorrelationId = request.CorrelationId ?? request.Candidate.CorrelationId
        };
    }

    private static string BuildDefaultReason(CollectiveMemoryPromotionRequest request) =>
        request.Decision == CollectiveMemoryPromotionDecision.Accept
            ? "Manual governed review accepted collective memory for governance use."
            : "Manual governed review rejected collective memory.";

    private static CollectiveMemoryPromotionResult Blocked(
        CollectiveMemoryPromotionRequest request,
        IReadOnlyList<CollectiveMemoryPromotionIssue> issues) =>
        new()
        {
            PromotionRequestId = request.PromotionRequestId ?? string.Empty,
            Decision = request.Decision,
            CreatedCollectiveMemory = false,
            CollectiveMemoryId = null,
            Outcome = CollectiveMemoryPromotionOutcome.Blocked,
            Issues = issues
        };

    private static string BuildEventId(string promotionRequestId, string suffix)
    {
        var value = $"{promotionRequestId}-{suffix}";
        return value.Length <= 100 ? value : value[..100];
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static bool IsError(CollectiveMemoryPromotionIssue issue) =>
        string.Equals(issue.Severity, "Error", StringComparison.Ordinal);

    private static CollectiveMemoryPromotionIssue Error(string code, string message) =>
        new() { Code = code, Severity = "Error", Message = message };

    private static CollectiveMemoryPromotionIssue Warning(string code, string message) =>
        new() { Code = code, Severity = "Warning", Message = message };

    private static bool ContainsRawPrivateReasoning(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var markers = new[] { "RawPrompt", "RawCompletion", "ChainOfThought", "Scratchpad", "PrivateReasoning" };
        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
