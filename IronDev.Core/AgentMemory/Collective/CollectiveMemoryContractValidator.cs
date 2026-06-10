using System.Text.Json;

namespace IronDev.Core.AgentMemory.Collective;

public sealed class CollectiveMemoryContractValidator : ICollectiveMemoryContractValidator
{
    public const string CollectiveMemoryIdRequired = "CMEM001_COLLECTIVE_MEMORY_ID_REQUIRED";
    public const string ScopeRequired = "CMEM002_SCOPE_REQUIRED";
    public const string TenantRequired = "CMEM003_TENANT_REQUIRED";
    public const string ProjectRequired = "CMEM004_PROJECT_REQUIRED";
    public const string TitleRequired = "CMEM005_TITLE_REQUIRED";
    public const string SummaryRequired = "CMEM006_SUMMARY_REQUIRED";
    public const string InvalidEnumValue = "CMEM007_INVALID_ENUM_VALUE";
    public const string ConfidenceOutOfRange = "CMEM008_CONFIDENCE_OUT_OF_RANGE";
    public const string SourceRequired = "CMEM009_SOURCE_REQUIRED";
    public const string EvidenceRequired = "CMEM010_EVIDENCE_REQUIRED";
    public const string SourceIdRequired = "CMEM011_SOURCE_ID_REQUIRED";
    public const string EvidenceIdRequired = "CMEM012_EVIDENCE_ID_REQUIRED";
    public const string EvidenceSourceIdRequired = "CMEM013_EVIDENCE_SOURCE_ID_REQUIRED";
    public const string EvidenceWeightOutOfRange = "CMEM014_EVIDENCE_WEIGHT_OUT_OF_RANGE";
    public const string ContradictionSourceRequired = "CMEM015_CONTRADICTION_SOURCE_REQUIRED";
    public const string ContradictionSummaryRequired = "CMEM016_CONTRADICTION_SUMMARY_REQUIRED";
    public const string ContradictionWeightOutOfRange = "CMEM017_CONTRADICTION_WEIGHT_OUT_OF_RANGE";
    public const string AcceptedReviewDateRequired = "CMEM018_ACCEPTED_REVIEW_DATE_REQUIRED";
    public const string AcceptedDecisionRequired = "CMEM019_ACCEPTED_DECISION_REQUIRED";
    public const string AcceptedReviewStateRequired = "CMEM020_ACCEPTED_REVIEW_STATE_REQUIRED";
    public const string RejectedActiveConflict = "CMEM021_REJECTED_ACTIVE_CONFLICT";
    public const string RejectedExplanationRequired = "CMEM022_REJECTED_EXPLANATION_REQUIRED";
    public const string InvalidCollectiveMemoryJson = "CMEM023_INVALID_COLLECTIVE_MEMORY_JSON";
    public const string RawPrivateReasoningBlocked = "CMEM024_RAW_PRIVATE_REASONING_BLOCKED";
    public const string SupersessionIdRequired = "CMEM025_SUPERSESSION_ID_REQUIRED";
    public const string SupersessionReasonRequired = "CMEM026_SUPERSESSION_REASON_REQUIRED";

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

    public IReadOnlyList<CollectiveMemoryValidationIssue> Validate(CollectiveMemoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var issues = new List<CollectiveMemoryValidationIssue>();

        if (string.IsNullOrWhiteSpace(item.CollectiveMemoryId))
            AddError(issues, CollectiveMemoryIdRequired, "Collective memory ID is required.");

        ValidateScope(item.Scope, issues);
        ValidateEnums(item, issues);

        if (string.IsNullOrWhiteSpace(item.Title))
            AddError(issues, TitleRequired, "Collective memory title is required.");

        if (string.IsNullOrWhiteSpace(item.Summary))
            AddError(issues, SummaryRequired, "Collective memory summary is required.");

        if (item.Confidence < 0m || item.Confidence > 1m)
            AddError(issues, ConfidenceOutOfRange, "Collective memory confidence must be between 0.0 and 1.0.");

        var rejectedWithExplanation = IsRejectedWithExplanation(item);

        if (IsEmpty(item.Sources) && !rejectedWithExplanation)
            AddError(issues, SourceRequired, "Collective memory requires at least one source.");

        if (IsEmpty(item.EvidenceRefs) && !rejectedWithExplanation)
            AddError(issues, EvidenceRequired, "Collective memory requires at least one evidence reference.");

        ValidateSources(item.Sources, issues);
        ValidateEvidence(item.EvidenceRefs, issues);
        ValidateContradictions(item.Contradictions, issues);
        ValidateSupersessions(item.Supersedes, issues);
        ValidateAuthorityStatusReviewRules(item, issues);
        ValidateCollectiveMemoryJson(item.CollectiveMemoryJson, issues);
        ValidateRawPrivateReasoningMarkers(item, issues);

        return issues;
    }

    private static void ValidateScope(CollectiveMemoryScope? scope, List<CollectiveMemoryValidationIssue> issues)
    {
        if (scope is null)
        {
            AddError(issues, ScopeRequired, "Collective memory scope is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(scope.TenantId))
            AddError(issues, TenantRequired, "Collective memory scope requires a tenant ID.");

        if (string.IsNullOrWhiteSpace(scope.ProjectId))
            AddError(issues, ProjectRequired, "Collective memory scope requires a project ID.");
    }

    private static void ValidateEnums(CollectiveMemoryItem item, List<CollectiveMemoryValidationIssue> issues)
    {
        if (!Enum.IsDefined(item.MemoryType))
            AddError(issues, InvalidEnumValue, "Collective memory type must be valid.");

        if (!Enum.IsDefined(item.AuthorityLevel))
            AddError(issues, InvalidEnumValue, "Collective memory authority level must be valid.");

        if (!Enum.IsDefined(item.Status))
            AddError(issues, InvalidEnumValue, "Collective memory status must be valid.");

        if (!Enum.IsDefined(item.ReviewState))
            AddError(issues, InvalidEnumValue, "Collective memory review state must be valid.");
    }

    private static void ValidateSources(
        IReadOnlyList<CollectiveMemorySourceRef>? sources,
        List<CollectiveMemoryValidationIssue> issues)
    {
        if (sources is null)
            return;

        foreach (var source in sources)
        {
            if (source is null)
                continue;

            if (!Enum.IsDefined(source.SourceType))
                AddError(issues, InvalidEnumValue, "Collective memory source type must be valid.");

            if (string.IsNullOrWhiteSpace(source.SourceId))
                AddError(issues, SourceIdRequired, "Collective memory source ID is required.");
        }
    }

    private static void ValidateEvidence(
        IReadOnlyList<CollectiveMemoryEvidenceRef>? evidenceRefs,
        List<CollectiveMemoryValidationIssue> issues)
    {
        if (evidenceRefs is null)
            return;

        foreach (var evidence in evidenceRefs)
        {
            if (evidence is null)
                continue;

            if (string.IsNullOrWhiteSpace(evidence.EvidenceId))
                AddError(issues, EvidenceIdRequired, "Collective memory evidence ID is required.");

            if (!Enum.IsDefined(evidence.EvidenceType))
                AddError(issues, InvalidEnumValue, "Collective memory evidence type must be valid.");

            if (string.IsNullOrWhiteSpace(evidence.SourceId))
                AddError(issues, EvidenceSourceIdRequired, "Collective memory evidence source ID is required.");

            if (evidence.Weight is < 0m or > 1m)
                AddError(issues, EvidenceWeightOutOfRange, "Collective memory evidence weight must be between 0.0 and 1.0.");
        }
    }

    private static void ValidateContradictions(
        IReadOnlyList<CollectiveMemoryContradictionRef>? contradictions,
        List<CollectiveMemoryValidationIssue> issues)
    {
        if (contradictions is null)
            return;

        foreach (var contradiction in contradictions)
        {
            if (contradiction is null)
                continue;

            if (contradiction.Source is null)
            {
                AddError(issues, ContradictionSourceRequired, "Collective memory contradiction requires a source.");
            }
            else
            {
                ValidateSources([contradiction.Source], issues);
            }

            if (string.IsNullOrWhiteSpace(contradiction.Summary))
                AddError(issues, ContradictionSummaryRequired, "Collective memory contradiction requires a summary.");

            if (contradiction.Weight is < 0m or > 1m)
                AddError(issues, ContradictionWeightOutOfRange, "Collective memory contradiction weight must be between 0.0 and 1.0.");
        }
    }

    private static void ValidateSupersessions(
        IReadOnlyList<CollectiveMemorySupersessionRef>? supersessions,
        List<CollectiveMemoryValidationIssue> issues)
    {
        if (supersessions is null)
            return;

        foreach (var supersession in supersessions)
        {
            if (supersession is null)
                continue;

            if (string.IsNullOrWhiteSpace(supersession.SupersedesCollectiveMemoryId))
                AddError(issues, SupersessionIdRequired, "Collective memory supersession requires the superseded collective memory ID.");

            if (string.IsNullOrWhiteSpace(supersession.Reason))
                AddError(issues, SupersessionReasonRequired, "Collective memory supersession requires a reason.");
        }
    }

    private static void ValidateAuthorityStatusReviewRules(
        CollectiveMemoryItem item,
        List<CollectiveMemoryValidationIssue> issues)
    {
        if (item.AuthorityLevel == CollectiveMemoryAuthorityLevel.Accepted)
        {
            if (item.LastReviewedAt is null)
                AddError(issues, AcceptedReviewDateRequired, "Accepted collective memory requires a review date.");

            if (string.IsNullOrWhiteSpace(item.DecisionId))
                AddError(issues, AcceptedDecisionRequired, "Accepted collective memory requires a decision ID.");

            if (item.ReviewState == CollectiveMemoryReviewState.None)
                AddError(issues, AcceptedReviewStateRequired, "Accepted collective memory requires a non-empty review state.");
        }

        if (item.Status == CollectiveMemoryStatus.Active &&
            item.AuthorityLevel == CollectiveMemoryAuthorityLevel.Rejected)
        {
            AddError(issues, RejectedActiveConflict, "Rejected collective memory cannot be active.");
        }

        if (item.AuthorityLevel == CollectiveMemoryAuthorityLevel.Rejected &&
            item.Status == CollectiveMemoryStatus.Active)
        {
            AddError(issues, RejectedActiveConflict, "Active collective memory cannot have rejected authority.");
        }

        if ((item.AuthorityLevel == CollectiveMemoryAuthorityLevel.Rejected ||
             item.Status == CollectiveMemoryStatus.Rejected) &&
            !IsRejectedWithExplanation(item))
        {
            AddError(issues, RejectedExplanationRequired, "Rejected collective memory requires contradiction or review explanation.");
        }
    }

    private static void ValidateCollectiveMemoryJson(string? collectiveMemoryJson, List<CollectiveMemoryValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(collectiveMemoryJson))
            return;

        try
        {
            using var _ = JsonDocument.Parse(collectiveMemoryJson);
        }
        catch (JsonException)
        {
            AddError(issues, InvalidCollectiveMemoryJson, "Collective memory JSON must be valid JSON when provided.");
        }
    }

    private static void ValidateRawPrivateReasoningMarkers(
        CollectiveMemoryItem item,
        List<CollectiveMemoryValidationIssue> issues)
    {
        if (ContainsRawPrivateReasoning(item.Title) ||
            ContainsRawPrivateReasoning(item.Summary) ||
            ContainsRawPrivateReasoning(item.CollectiveMemoryJson) ||
            (item.EvidenceRefs?.Any(evidence => ContainsRawPrivateReasoning(evidence?.Summary)) ?? false) ||
            (item.Contradictions?.Any(contradiction => ContainsRawPrivateReasoning(contradiction?.Summary)) ?? false))
        {
            AddError(issues, RawPrivateReasoningBlocked, "Collective memory must not contain raw prompt, completion, scratchpad, chain-of-thought, or private reasoning markers.");
        }
    }

    private static bool ContainsRawPrivateReasoning(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return RawPrivateReasoningMarkers.Any(marker =>
            value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRejectedWithExplanation(CollectiveMemoryItem item) =>
        (item.Contradictions?.Count > 0) ||
        item.ReviewState == CollectiveMemoryReviewState.RejectedByReview ||
        !string.IsNullOrWhiteSpace(item.DecisionId);

    private static bool IsEmpty<T>(IReadOnlyCollection<T>? items) =>
        items is null || items.Count == 0;

    private static void AddError(List<CollectiveMemoryValidationIssue> issues, string code, string message)
    {
        issues.Add(new CollectiveMemoryValidationIssue
        {
            Code = code,
            Severity = "Error",
            Message = message
        });
    }
}
