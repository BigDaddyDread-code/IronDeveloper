namespace IronDev.Core.AgentMemory.Collective;

public sealed class CollectiveMemoryEvidenceAggregator : ICollectiveMemoryEvidenceAggregator
{
    public const string InputRequired = "CMEMAGG001_INPUT_REQUIRED";
    public const string AggregationIdRequired = "CMEMAGG002_AGGREGATION_ID_REQUIRED";
    public const string CandidateRequired = "CMEMAGG003_CANDIDATE_REQUIRED";
    public const string CandidateInvalid = "CMEMAGG004_CANDIDATE_INVALID";
    public const string EvidenceContributionsRequired = "CMEMAGG005_EVIDENCE_CONTRIBUTIONS_REQUIRED";
    public const string ContributionIdRequired = "CMEMAGG006_CONTRIBUTION_ID_REQUIRED";
    public const string ContributionSourceRequired = "CMEMAGG007_CONTRIBUTION_SOURCE_REQUIRED";
    public const string ContributionEvidenceRequired = "CMEMAGG008_CONTRIBUTION_EVIDENCE_REQUIRED";
    public const string ContributionTypeInvalid = "CMEMAGG009_CONTRIBUTION_TYPE_INVALID";
    public const string ContributionWeightOutOfRange = "CMEMAGG010_CONTRIBUTION_WEIGHT_OUT_OF_RANGE";
    public const string ContradictionContributionIdRequired = "CMEMAGG011_CONTRADICTION_CONTRIBUTION_ID_REQUIRED";
    public const string ContradictionRequired = "CMEMAGG012_CONTRADICTION_REQUIRED";
    public const string ContradictionWeightOutOfRange = "CMEMAGG013_CONTRADICTION_WEIGHT_OUT_OF_RANGE";
    public const string RawPrivateReasoningBlocked = "CMEMAGG014_RAW_PRIVATE_REASONING_BLOCKED";

    private static readonly string[] RawPrivateReasoningMarkers =
    [
        "RawPrompt",
        "Prompt",
        "RawCompletion",
        "Completion",
        "ChainOfThought",
        "Scratchpad",
        "PrivateReasoning"
    ];

    private readonly ICollectiveMemoryContractValidator _candidateValidator;

    public CollectiveMemoryEvidenceAggregator()
        : this(new CollectiveMemoryContractValidator())
    {
    }

    public CollectiveMemoryEvidenceAggregator(ICollectiveMemoryContractValidator candidateValidator)
    {
        _candidateValidator = candidateValidator ?? throw new ArgumentNullException(nameof(candidateValidator));
    }

    public CollectiveMemoryAggregationResult Aggregate(CollectiveMemoryAggregationInput input)
    {
        if (input is null)
        {
            return new CollectiveMemoryAggregationResult
            {
                Aggregate = EmptyAggregate(string.Empty, null, DateTimeOffset.MinValue),
                Issues =
                [
                    Error(InputRequired, "Collective memory aggregation input is required.")
                ]
            };
        }

        var issues = new List<CollectiveMemoryAggregationIssue>();

        if (string.IsNullOrWhiteSpace(input.AggregationId))
            AddError(issues, AggregationIdRequired, "Collective memory aggregation ID is required.");

        if (input.Candidate is null)
        {
            AddError(issues, CandidateRequired, "Collective memory aggregation candidate is required.");
        }
        else
        {
            foreach (var candidateIssue in _candidateValidator.Validate(input.Candidate))
            {
                AddError(
                    issues,
                    CandidateInvalid,
                    $"Collective memory candidate is invalid: {candidateIssue.Code} {candidateIssue.Message}");
            }
        }

        if (input.EvidenceContributions is null)
            AddError(issues, EvidenceContributionsRequired, "Collective memory aggregation requires an evidence contribution collection.");

        ValidateEvidenceContributions(input.EvidenceContributions, issues);
        ValidateContradictionContributions(input.ContradictionContributions, issues);

        return new CollectiveMemoryAggregationResult
        {
            Aggregate = BuildAggregate(input),
            Issues = issues
        };
    }

    private static CollectiveMemoryEvidenceAggregate BuildAggregate(CollectiveMemoryAggregationInput input)
    {
        var evidenceContributions = ValidEvidenceContributions(input.EvidenceContributions).ToArray();
        var contradictionContributions = ValidContradictionContributions(input.ContradictionContributions).ToArray();

        var supportingEvidenceCount = evidenceContributions.Count(contribution =>
            contribution.ContributionType == CollectiveMemoryEvidenceContributionType.SupportsClaim);
        var weakSupportingEvidenceCount = evidenceContributions.Count(contribution =>
            contribution.ContributionType == CollectiveMemoryEvidenceContributionType.WeaklySupportsClaim);
        var neutralEvidenceCount = evidenceContributions.Count(contribution =>
            contribution.ContributionType == CollectiveMemoryEvidenceContributionType.NeutralContext);
        var contradictingEvidenceCount = evidenceContributions.Count(contribution =>
            contribution.ContributionType == CollectiveMemoryEvidenceContributionType.ContradictsClaim);
        var weakContradictingEvidenceCount = evidenceContributions.Count(contribution =>
            contribution.ContributionType == CollectiveMemoryEvidenceContributionType.WeaklyContradictsClaim);

        var supportWeight =
            evidenceContributions
                .Where(contribution => contribution.ContributionType == CollectiveMemoryEvidenceContributionType.SupportsClaim)
                .Sum(contribution => contribution.Weight) +
            evidenceContributions
                .Where(contribution => contribution.ContributionType == CollectiveMemoryEvidenceContributionType.WeaklySupportsClaim)
                .Sum(contribution => contribution.Weight * 0.5m);

        var contradictionWeight =
            evidenceContributions
                .Where(contribution => contribution.ContributionType == CollectiveMemoryEvidenceContributionType.ContradictsClaim)
                .Sum(contribution => contribution.Weight) +
            evidenceContributions
                .Where(contribution => contribution.ContributionType == CollectiveMemoryEvidenceContributionType.WeaklyContradictsClaim)
                .Sum(contribution => contribution.Weight * 0.5m) +
            contradictionContributions.Sum(contribution => contribution.Weight);

        var sourceRefs = evidenceContributions
            .Select(contribution => contribution.Source)
            .Concat(contradictionContributions.Select(contribution => contribution.Contradiction.Source))
            .ToArray();

        var uniqueSourceCount = sourceRefs
            .Select(source => source.SourceId)
            .Where(sourceId => !string.IsNullOrWhiteSpace(sourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var uniqueSourceTypeCount = sourceRefs
            .Select(source => source.SourceType)
            .Distinct()
            .Count();

        var coverage = ClassifyCoverage(uniqueSourceCount, uniqueSourceTypeCount);
        var quality = ClassifyQuality(supportWeight, contradictionWeight, uniqueSourceCount, uniqueSourceTypeCount);
        var conflictLevel = ClassifyConflictLevel(supportWeight, contradictionWeight);
        var readiness = ClassifyReadiness(supportWeight, quality, coverage, conflictLevel);
        var reviewWarnings = BuildReviewWarnings(readiness, quality, conflictLevel).ToArray();

        return new CollectiveMemoryEvidenceAggregate
        {
            AggregationId = input.AggregationId ?? string.Empty,
            CollectiveMemoryId = input.Candidate?.CollectiveMemoryId ?? string.Empty,
            Scope = input.Candidate?.Scope ?? EmptyScope(),
            SupportingEvidenceCount = supportingEvidenceCount,
            WeakSupportingEvidenceCount = weakSupportingEvidenceCount,
            NeutralEvidenceCount = neutralEvidenceCount,
            ContradictingEvidenceCount = contradictingEvidenceCount,
            WeakContradictingEvidenceCount = weakContradictingEvidenceCount,
            UniqueSourceCount = uniqueSourceCount,
            UniqueSourceTypeCount = uniqueSourceTypeCount,
            SupportWeight = supportWeight,
            ContradictionWeight = contradictionWeight,
            EvidenceQuality = quality,
            EvidenceCoverage = coverage,
            ConflictLevel = conflictLevel,
            Readiness = readiness,
            AggregatedAt = input.AggregatedAt,
            EvidenceContributionIds = evidenceContributions
                .Select(contribution => contribution.ContributionId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray(),
            ContradictionContributionIds = contradictionContributions
                .Select(contribution => contribution.ContributionId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray(),
            ReviewWarnings = reviewWarnings
        };
    }

    private static CollectiveMemoryEvidenceAggregate EmptyAggregate(
        string aggregationId,
        CollectiveMemoryItem? candidate,
        DateTimeOffset aggregatedAt) =>
        new()
        {
            AggregationId = aggregationId,
            CollectiveMemoryId = candidate?.CollectiveMemoryId ?? string.Empty,
            Scope = candidate?.Scope ?? EmptyScope(),
            SupportingEvidenceCount = 0,
            WeakSupportingEvidenceCount = 0,
            NeutralEvidenceCount = 0,
            ContradictingEvidenceCount = 0,
            WeakContradictingEvidenceCount = 0,
            UniqueSourceCount = 0,
            UniqueSourceTypeCount = 0,
            SupportWeight = 0m,
            ContradictionWeight = 0m,
            EvidenceQuality = CollectiveMemoryEvidenceQuality.Unknown,
            EvidenceCoverage = CollectiveMemoryEvidenceCoverage.None,
            ConflictLevel = CollectiveMemoryEvidenceConflictLevel.None,
            Readiness = CollectiveMemoryEvidenceReadiness.InsufficientEvidence,
            AggregatedAt = aggregatedAt
        };

    private static void ValidateEvidenceContributions(
        IReadOnlyList<CollectiveMemoryEvidenceContribution>? evidenceContributions,
        List<CollectiveMemoryAggregationIssue> issues)
    {
        if (evidenceContributions is null)
            return;

        foreach (var contribution in evidenceContributions)
        {
            if (contribution is null)
                continue;

            if (string.IsNullOrWhiteSpace(contribution.ContributionId))
                AddError(issues, ContributionIdRequired, "Collective memory evidence contribution ID is required.");

            if (contribution.Source is null)
                AddError(issues, ContributionSourceRequired, "Collective memory evidence contribution requires a source.");

            if (contribution.Evidence is null)
                AddError(issues, ContributionEvidenceRequired, "Collective memory evidence contribution requires evidence.");

            if (!Enum.IsDefined(contribution.ContributionType))
                AddError(issues, ContributionTypeInvalid, "Collective memory evidence contribution type must be valid.");

            if (contribution.Weight < 0m || contribution.Weight > 1m)
                AddError(issues, ContributionWeightOutOfRange, "Collective memory evidence contribution weight must be between 0.0 and 1.0.");

            if (ContainsRawPrivateReasoning(contribution.Summary) ||
                ContainsRawPrivateReasoning(contribution.Source?.EvidenceUri) ||
                ContainsRawPrivateReasoning(contribution.Evidence?.Summary))
            {
                AddError(issues, RawPrivateReasoningBlocked, "Collective memory evidence contribution must not include raw private reasoning markers.");
            }
        }
    }

    private static void ValidateContradictionContributions(
        IReadOnlyList<CollectiveMemoryContradictionContribution>? contradictionContributions,
        List<CollectiveMemoryAggregationIssue> issues)
    {
        if (contradictionContributions is null)
            return;

        foreach (var contribution in contradictionContributions)
        {
            if (contribution is null)
                continue;

            if (string.IsNullOrWhiteSpace(contribution.ContributionId))
                AddError(issues, ContradictionContributionIdRequired, "Collective memory contradiction contribution ID is required.");

            if (contribution.Contradiction is null)
                AddError(issues, ContradictionRequired, "Collective memory contradiction contribution requires a contradiction.");

            if (contribution.Weight < 0m || contribution.Weight > 1m)
                AddError(issues, ContradictionWeightOutOfRange, "Collective memory contradiction contribution weight must be between 0.0 and 1.0.");

            if (ContainsRawPrivateReasoning(contribution.Summary) ||
                ContainsRawPrivateReasoning(contribution.Contradiction?.Summary) ||
                ContainsRawPrivateReasoning(contribution.Contradiction?.Source?.EvidenceUri))
            {
                AddError(issues, RawPrivateReasoningBlocked, "Collective memory contradiction contribution must not include raw private reasoning markers.");
            }
        }
    }

    private static IEnumerable<CollectiveMemoryEvidenceContribution> ValidEvidenceContributions(
        IReadOnlyList<CollectiveMemoryEvidenceContribution>? evidenceContributions)
    {
        if (evidenceContributions is null)
            yield break;

        foreach (var contribution in evidenceContributions)
        {
            if (contribution is null ||
                contribution.Source is null ||
                contribution.Evidence is null ||
                !Enum.IsDefined(contribution.ContributionType) ||
                contribution.Weight < 0m ||
                contribution.Weight > 1m)
            {
                continue;
            }

            yield return contribution;
        }
    }

    private static IEnumerable<CollectiveMemoryContradictionContribution> ValidContradictionContributions(
        IReadOnlyList<CollectiveMemoryContradictionContribution>? contradictionContributions)
    {
        if (contradictionContributions is null)
            yield break;

        foreach (var contribution in contradictionContributions)
        {
            if (contribution is null ||
                contribution.Contradiction is null ||
                contribution.Contradiction.Source is null ||
                contribution.Weight < 0m ||
                contribution.Weight > 1m)
            {
                continue;
            }

            yield return contribution;
        }
    }

    private static CollectiveMemoryEvidenceCoverage ClassifyCoverage(int uniqueSourceCount, int uniqueSourceTypeCount)
    {
        if (uniqueSourceCount == 0)
            return CollectiveMemoryEvidenceCoverage.None;

        if (uniqueSourceCount == 1)
            return CollectiveMemoryEvidenceCoverage.SingleSource;

        if (uniqueSourceTypeCount <= 1)
            return CollectiveMemoryEvidenceCoverage.MultipleSameTypeSources;

        return CollectiveMemoryEvidenceCoverage.MultipleIndependentSourceTypes;
    }

    private static CollectiveMemoryEvidenceQuality ClassifyQuality(
        decimal supportWeight,
        decimal contradictionWeight,
        int uniqueSourceCount,
        int uniqueSourceTypeCount)
    {
        if (supportWeight == 0m)
            return CollectiveMemoryEvidenceQuality.Unknown;

        if (supportWeight >= 2m &&
            uniqueSourceTypeCount >= 2 &&
            contradictionWeight == 0m)
        {
            return CollectiveMemoryEvidenceQuality.Strong;
        }

        if (supportWeight >= 1m &&
            uniqueSourceCount >= 1 &&
            contradictionWeight < supportWeight)
        {
            return CollectiveMemoryEvidenceQuality.Moderate;
        }

        return CollectiveMemoryEvidenceQuality.Weak;
    }

    private static CollectiveMemoryEvidenceConflictLevel ClassifyConflictLevel(decimal supportWeight, decimal contradictionWeight)
    {
        if (contradictionWeight == 0m)
            return CollectiveMemoryEvidenceConflictLevel.None;

        if (contradictionWeight >= supportWeight)
            return CollectiveMemoryEvidenceConflictLevel.High;

        if (contradictionWeight >= 0.5m)
            return CollectiveMemoryEvidenceConflictLevel.Medium;

        return CollectiveMemoryEvidenceConflictLevel.Low;
    }

    private static CollectiveMemoryEvidenceReadiness ClassifyReadiness(
        decimal supportWeight,
        CollectiveMemoryEvidenceQuality quality,
        CollectiveMemoryEvidenceCoverage coverage,
        CollectiveMemoryEvidenceConflictLevel conflictLevel)
    {
        if (conflictLevel is CollectiveMemoryEvidenceConflictLevel.Medium or CollectiveMemoryEvidenceConflictLevel.High)
            return CollectiveMemoryEvidenceReadiness.NeedsContradictionReview;

        if (quality is CollectiveMemoryEvidenceQuality.Moderate or CollectiveMemoryEvidenceQuality.Strong &&
            coverage is CollectiveMemoryEvidenceCoverage.MultipleSameTypeSources or CollectiveMemoryEvidenceCoverage.MultipleIndependentSourceTypes &&
            conflictLevel is CollectiveMemoryEvidenceConflictLevel.None or CollectiveMemoryEvidenceConflictLevel.Low)
        {
            return CollectiveMemoryEvidenceReadiness.ReadyForHumanReview;
        }

        if (supportWeight > 0m && coverage == CollectiveMemoryEvidenceCoverage.SingleSource)
            return CollectiveMemoryEvidenceReadiness.NeedsMoreSources;

        return CollectiveMemoryEvidenceReadiness.InsufficientEvidence;
    }

    private static IEnumerable<string> BuildReviewWarnings(
        CollectiveMemoryEvidenceReadiness readiness,
        CollectiveMemoryEvidenceQuality quality,
        CollectiveMemoryEvidenceConflictLevel conflictLevel)
    {
        if (readiness == CollectiveMemoryEvidenceReadiness.ReadyForHumanReview)
            yield return "Evidence aggregation is ready for human review only; it does not grant authority.";

        if (quality is CollectiveMemoryEvidenceQuality.Unknown or CollectiveMemoryEvidenceQuality.Weak)
            yield return "Supporting evidence is weak or unavailable.";

        if (conflictLevel is CollectiveMemoryEvidenceConflictLevel.Medium or CollectiveMemoryEvidenceConflictLevel.High)
            yield return "Contradicting evidence requires explicit review.";
    }

    private static bool ContainsRawPrivateReasoning(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return RawPrivateReasoningMarkers.Any(marker =>
            value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static CollectiveMemoryScope EmptyScope() =>
        new()
        {
            TenantId = string.Empty,
            ProjectId = string.Empty
        };

    private static CollectiveMemoryAggregationIssue Error(string code, string message) =>
        new()
        {
            Code = code,
            Severity = "Error",
            Message = message
        };

    private static void AddError(List<CollectiveMemoryAggregationIssue> issues, string code, string message) =>
        issues.Add(Error(code, message));
}
