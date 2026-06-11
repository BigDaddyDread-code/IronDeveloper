namespace IronDev.Core.AgentMemory.Collective;

public sealed class CollectiveMemoryStabilityScorer : ICollectiveMemoryStabilityScorer
{
    public const string InputRequired = "CMEM_STABILITY_INPUT_REQUIRED";
    public const string StabilityRunIdRequired = "CMEM_STABILITY_RUN_ID_REQUIRED";
    public const string MemoryRequired = "CMEM_STABILITY_MEMORY_REQUIRED";
    public const string EvidenceAggregateRequired = "CMEM_STABILITY_EVIDENCE_AGGREGATE_REQUIRED";
    public const string AggregateMemoryIdMismatch = "CMEM_STABILITY_AGGREGATE_MEMORY_ID_MISMATCH";
    public const string AggregateScopeMismatch = "CMEM_STABILITY_AGGREGATE_SCOPE_MISMATCH";
    public const string EvaluatedAtRequired = "CMEM_STABILITY_EVALUATED_AT_REQUIRED";

    private readonly ICollectiveMemoryContractValidator _validator;

    public CollectiveMemoryStabilityScorer()
        : this(new CollectiveMemoryContractValidator())
    {
    }

    public CollectiveMemoryStabilityScorer(ICollectiveMemoryContractValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public CollectiveMemoryStabilityScore Score(CollectiveMemoryStabilityInput? input)
    {
        if (input is null)
        {
            return ErrorResult(
                string.Empty,
                string.Empty,
                null,
                default,
                [Issue(InputRequired, "Stability scoring input is required.")]);
        }

        var issues = Validate(input);

        if (issues.Any(issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase)))
        {
            return ErrorResult(
                input.StabilityRunId,
                input.Memory?.CollectiveMemoryId ?? string.Empty,
                input.Memory?.Scope,
                input.EvaluatedAt,
                issues);
        }

        var memory = input.Memory;
        var aggregate = input.EvidenceAggregate;
        var events = input.Events ?? [];
        var breakdown = new CollectiveMemoryStabilityBreakdown
        {
            EvidenceSupportScore = EvidenceSupportScore(aggregate.EvidenceQuality),
            SourceDiversityScore = SourceDiversityScore(aggregate.EvidenceCoverage),
            HumanReviewScore = HumanReviewScore(memory, events),
            AuthorityScore = AuthorityScore(memory.AuthorityLevel),
            RecencyScore = RecencyScore(memory, input.EvaluatedAt),
            ReuseScore = ReuseScore(events),
            ContradictionPenalty = ContradictionPenalty(aggregate.ConflictLevel),
            LifecyclePenalty = LifecyclePenalty(memory.Status),
            ExpiryPenalty = ExpiryPenalty(memory, input.EvaluatedAt)
        };

        var positive =
            breakdown.EvidenceSupportScore * 0.25m +
            breakdown.SourceDiversityScore * 0.15m +
            breakdown.HumanReviewScore * 0.15m +
            breakdown.AuthorityScore * 0.15m +
            breakdown.RecencyScore * 0.10m +
            breakdown.ReuseScore * 0.10m;

        var negative =
            breakdown.ContradictionPenalty * 0.30m +
            breakdown.LifecyclePenalty * 0.30m +
            breakdown.ExpiryPenalty * 0.20m;

        var score = Clamp01(positive - negative);
        var band = DetermineBand(score, memory.Status, aggregate.ConflictLevel, hasErrors: false);

        return new CollectiveMemoryStabilityScore
        {
            StabilityRunId = input.StabilityRunId,
            CollectiveMemoryId = memory.CollectiveMemoryId,
            Scope = memory.Scope,
            Score = score,
            Band = band,
            Breakdown = breakdown,
            Signals = BuildSignals(breakdown),
            Issues = issues,
            EvaluatedAt = input.EvaluatedAt
        };
    }

    private IReadOnlyList<CollectiveMemoryScoringIssue> Validate(CollectiveMemoryStabilityInput input)
    {
        var issues = new List<CollectiveMemoryScoringIssue>();

        if (string.IsNullOrWhiteSpace(input.StabilityRunId))
            issues.Add(Issue(StabilityRunIdRequired, "StabilityRunId is required."));

        if (input.EvaluatedAt == default)
            issues.Add(Issue(EvaluatedAtRequired, "EvaluatedAt is required and must be supplied by the caller."));

        if (input.Memory is null)
        {
            issues.Add(Issue(MemoryRequired, "Collective memory is required."));
        }
        else
        {
            foreach (var validationIssue in _validator.Validate(input.Memory))
            {
                issues.Add(Issue(
                    validationIssue.Code,
                    validationIssue.Message));
            }
        }

        if (input.EvidenceAggregate is null)
        {
            issues.Add(Issue(EvidenceAggregateRequired, "Evidence aggregate is required."));
        }

        if (input.Memory is not null && input.EvidenceAggregate is not null)
        {
            if (!string.Equals(input.Memory.CollectiveMemoryId, input.EvidenceAggregate.CollectiveMemoryId, StringComparison.Ordinal))
            {
                issues.Add(Issue(
                    AggregateMemoryIdMismatch,
                    "Evidence aggregate collective memory ID must match the scored memory ID."));
            }

            if (!ScopesEqual(input.Memory.Scope, input.EvidenceAggregate.Scope))
            {
                issues.Add(Issue(
                    AggregateScopeMismatch,
                    "Evidence aggregate scope must match the scored memory scope."));
            }
        }

        return issues;
    }

    private static CollectiveMemoryStabilityScore ErrorResult(
        string stabilityRunId,
        string collectiveMemoryId,
        CollectiveMemoryScope? scope,
        DateTimeOffset evaluatedAt,
        IReadOnlyList<CollectiveMemoryScoringIssue> issues) =>
        new()
        {
            StabilityRunId = stabilityRunId,
            CollectiveMemoryId = collectiveMemoryId,
            Scope = scope,
            Score = 0m,
            Band = CollectiveMemoryStabilityBand.Unknown,
            Breakdown = new CollectiveMemoryStabilityBreakdown(),
            Signals = [],
            Issues = issues,
            EvaluatedAt = evaluatedAt
        };

    private static decimal EvidenceSupportScore(CollectiveMemoryEvidenceQuality quality) =>
        quality switch
        {
            CollectiveMemoryEvidenceQuality.Strong => 1.0m,
            CollectiveMemoryEvidenceQuality.Moderate => 0.65m,
            CollectiveMemoryEvidenceQuality.Weak => 0.3m,
            _ => 0m
        };

    private static decimal SourceDiversityScore(CollectiveMemoryEvidenceCoverage coverage) =>
        coverage switch
        {
            CollectiveMemoryEvidenceCoverage.MultipleIndependentSourceTypes => 1.0m,
            CollectiveMemoryEvidenceCoverage.MultipleSameTypeSources => 0.65m,
            CollectiveMemoryEvidenceCoverage.SingleSource => 0.3m,
            _ => 0m
        };

    private static decimal HumanReviewScore(CollectiveMemoryItem memory, IReadOnlyList<CollectiveMemoryEventRecord> events)
    {
        if (memory.LastReviewedAt.HasValue)
            return 1.0m;

        if (events.Any(item => item.EventType == CollectiveMemoryEventType.Reviewed))
            return 0.75m;

        return memory.ReviewState == CollectiveMemoryReviewState.NeedsHumanReview ? 0.2m : 0m;
    }

    private static decimal AuthorityScore(CollectiveMemoryAuthorityLevel authorityLevel) =>
        authorityLevel switch
        {
            CollectiveMemoryAuthorityLevel.Accepted => 1.0m,
            CollectiveMemoryAuthorityLevel.Reviewed => 0.75m,
            CollectiveMemoryAuthorityLevel.Candidate => 0.25m,
            CollectiveMemoryAuthorityLevel.Deprecated => 0.1m,
            _ => 0m
        };

    private static decimal RecencyScore(CollectiveMemoryItem memory, DateTimeOffset evaluatedAt)
    {
        if (memory.LastConfirmedAt.HasValue && memory.LastConfirmedAt.Value >= evaluatedAt.AddDays(-30))
            return 1.0m;

        if (memory.LastReviewedAt.HasValue && memory.LastReviewedAt.Value >= evaluatedAt.AddDays(-90))
            return 0.75m;

        if (memory.CreatedAt >= evaluatedAt.AddDays(-180))
            return 0.4m;

        return 0.2m;
    }

    private static decimal ReuseScore(IReadOnlyList<CollectiveMemoryEventRecord> events)
    {
        if (events.Any(item => item.EventType == CollectiveMemoryEventType.Reviewed))
            return 0.5m;

        if (events.Any(item => item.EventType == CollectiveMemoryEventType.Accepted))
            return 0.5m;

        return 0m;
    }

    private static decimal ContradictionPenalty(CollectiveMemoryEvidenceConflictLevel conflictLevel) =>
        conflictLevel switch
        {
            CollectiveMemoryEvidenceConflictLevel.High => 1.0m,
            CollectiveMemoryEvidenceConflictLevel.Medium => 0.65m,
            CollectiveMemoryEvidenceConflictLevel.Low => 0.3m,
            _ => 0m
        };

    private static decimal LifecyclePenalty(CollectiveMemoryStatus status) =>
        status switch
        {
            CollectiveMemoryStatus.Invalidated => 1.0m,
            CollectiveMemoryStatus.Rejected => 1.0m,
            CollectiveMemoryStatus.Deprecated => 0.75m,
            CollectiveMemoryStatus.Superseded => 0.75m,
            CollectiveMemoryStatus.UnderReview => 0.25m,
            CollectiveMemoryStatus.Proposed => 0.15m,
            CollectiveMemoryStatus.Draft => 0.15m,
            _ => 0m
        };

    private static decimal ExpiryPenalty(CollectiveMemoryItem memory, DateTimeOffset evaluatedAt)
    {
        if (!memory.ExpiresAt.HasValue)
            return 0m;

        if (memory.ExpiresAt.Value <= evaluatedAt)
            return 1.0m;

        return memory.ExpiresAt.Value <= evaluatedAt.AddDays(14) ? 0.5m : 0m;
    }

    private static CollectiveMemoryStabilityBand DetermineBand(
        decimal score,
        CollectiveMemoryStatus status,
        CollectiveMemoryEvidenceConflictLevel conflictLevel,
        bool hasErrors)
    {
        if (hasErrors)
            return CollectiveMemoryStabilityBand.Unknown;

        if (status is CollectiveMemoryStatus.Rejected or CollectiveMemoryStatus.Invalidated ||
            conflictLevel == CollectiveMemoryEvidenceConflictLevel.High)
        {
            return CollectiveMemoryStabilityBand.Unstable;
        }

        if (score < 0.25m)
            return CollectiveMemoryStabilityBand.Unstable;
        if (score < 0.50m)
            return CollectiveMemoryStabilityBand.Emerging;
        if (score < 0.75m)
            return CollectiveMemoryStabilityBand.Stable;

        return CollectiveMemoryStabilityBand.StronglyStable;
    }

    private static IReadOnlyList<CollectiveMemoryAttractorSignal> BuildSignals(CollectiveMemoryStabilityBreakdown breakdown)
    {
        var signals = new List<CollectiveMemoryAttractorSignal>();

        AddSignal(signals, CollectiveMemoryAttractorSignalType.EvidenceSupport, breakdown.EvidenceSupportScore, "Evidence quality supports this memory.", isNegative: false);
        AddSignal(signals, CollectiveMemoryAttractorSignalType.SourceDiversity, breakdown.SourceDiversityScore, "Evidence source diversity supports this memory.", isNegative: false);
        AddSignal(signals, CollectiveMemoryAttractorSignalType.HumanReview, breakdown.HumanReviewScore, "Human review signal supports this memory.", isNegative: false);
        AddSignal(signals, CollectiveMemoryAttractorSignalType.AcceptanceAuthority, breakdown.AuthorityScore, "Authority level contributes to advisory stability.", isNegative: false);
        AddSignal(signals, CollectiveMemoryAttractorSignalType.RecentConfirmation, breakdown.RecencyScore, "Recent confirmation or review contributes to advisory stability.", isNegative: false);
        AddSignal(signals, CollectiveMemoryAttractorSignalType.ReuseEvidence, breakdown.ReuseScore, "Prior review or acceptance event contributes reuse evidence.", isNegative: false);
        AddSignal(signals, CollectiveMemoryAttractorSignalType.ContradictionPressure, breakdown.ContradictionPenalty, "Contradiction pressure reduces advisory stability.", isNegative: true);
        AddSignal(signals, CollectiveMemoryAttractorSignalType.RejectionPressure, RejectionPressureSignalWeight(breakdown.LifecyclePenalty), "Lifecycle state reduces advisory stability.", isNegative: true);
        AddSignal(signals, CollectiveMemoryAttractorSignalType.DeprecationPressure, DeprecationPressureSignalWeight(breakdown.LifecyclePenalty), "Deprecation or supersession pressure reduces advisory stability.", isNegative: true);
        AddSignal(signals, CollectiveMemoryAttractorSignalType.ExpiryPressure, breakdown.ExpiryPenalty, "Expiry pressure reduces advisory stability.", isNegative: true);

        return signals;
    }

    private static decimal RejectionPressureSignalWeight(decimal lifecyclePenalty) =>
        lifecyclePenalty is > 0m and < 0.75m ? lifecyclePenalty : lifecyclePenalty == 1.0m ? lifecyclePenalty : 0m;

    private static decimal DeprecationPressureSignalWeight(decimal lifecyclePenalty) =>
        lifecyclePenalty == 0.75m ? lifecyclePenalty : 0m;

    private static void AddSignal(
        List<CollectiveMemoryAttractorSignal> signals,
        CollectiveMemoryAttractorSignalType signalType,
        decimal weight,
        string reason,
        bool isNegative)
    {
        var clamped = Clamp01(weight);

        if (clamped == 0m)
            return;

        signals.Add(new CollectiveMemoryAttractorSignal
        {
            SignalType = signalType,
            Weight = clamped,
            Reason = reason,
            IsNegative = isNegative
        });
    }

    private static bool ScopesEqual(CollectiveMemoryScope? left, CollectiveMemoryScope? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal) &&
            string.Equals(left.ProjectId, right.ProjectId, StringComparison.Ordinal) &&
            string.Equals(left.KnowledgeDomainId, right.KnowledgeDomainId, StringComparison.Ordinal) &&
            string.Equals(left.ComponentId, right.ComponentId, StringComparison.Ordinal) &&
            string.Equals(left.RepositoryId, right.RepositoryId, StringComparison.Ordinal);
    }

    private static CollectiveMemoryScoringIssue Issue(string code, string message, string severity = "Error") =>
        new()
        {
            Code = code,
            Severity = severity,
            Message = message
        };

    private static decimal Clamp01(decimal value)
    {
        if (value < 0m)
            return 0m;

        return value > 1m ? 1m : value;
    }
}
