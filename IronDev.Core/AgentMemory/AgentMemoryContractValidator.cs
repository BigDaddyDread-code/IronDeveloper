namespace IronDev.Core.AgentMemory;

public sealed class AgentMemoryContractValidator : IAgentMemoryContractValidator
{
    public const string ScopeRequired = "MEM001_SCOPE_REQUIRED";
    public const string TenantRequired = "MEM002_TENANT_REQUIRED";
    public const string ProjectRequired = "MEM003_PROJECT_REQUIRED";
    public const string CampaignRequired = "MEM004_CAMPAIGN_REQUIRED";
    public const string RunRequired = "MEM005_RUN_REQUIRED";
    public const string AgentRequired = "MEM006_AGENT_REQUIRED";
    public const string TitleRequired = "MEM007_TITLE_REQUIRED";
    public const string SummaryRequired = "MEM008_SUMMARY_REQUIRED";
    public const string ConfidenceOutOfRange = "MEM009_CONFIDENCE_OUT_OF_RANGE";
    public const string EvidenceRequired = "MEM010_EVIDENCE_REQUIRED";
    public const string LocalMemoryCannotBeAccepted = "MEM011_LOCAL_MEMORY_CANNOT_BE_ACCEPTED";
    public const string LocalMemoryCannotBeSystemRule = "MEM012_LOCAL_MEMORY_CANNOT_BE_SYSTEM_RULE";
    public const string CandidatePatternRequiresLimitations = "MEM013_CANDIDATE_PATTERN_REQUIRES_LIMITATIONS";
    public const string HandoffSourceRequired = "MEM014_HANDOFF_SOURCE_REQUIRED";
    public const string HandoffTargetRequired = "MEM015_HANDOFF_TARGET_REQUIRED";
    public const string HandoffMemoryItemRequired = "MEM016_HANDOFF_MEMORY_ITEM_REQUIRED";
    public const string HandoffAllowedUseRequired = "MEM017_HANDOFF_ALLOWED_USE_REQUIRED";
    public const string InfluenceDecisionRequired = "MEM018_INFLUENCE_DECISION_REQUIRED";
    public const string InfluenceTypeRequired = "MEM019_INFLUENCE_TYPE_REQUIRED";
    public const string SelfReferentialEvidenceBlocked = "MEM020_SELF_REFERENTIAL_EVIDENCE_BLOCKED";
    public const string InfluenceMemoryItemRequired = "MEM021_INFLUENCE_MEMORY_ITEM_REQUIRED";

    public MemoryValidationResult Validate(AgentLocalMemoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var issues = new List<MemoryValidationIssue>();

        if (string.IsNullOrWhiteSpace(item.MemoryItemId))
            Add(issues, EvidenceRequired, "Memory item ID is required.");

        ValidateScope(item.Scope, issues);

        if (string.IsNullOrWhiteSpace(item.Title))
            Add(issues, TitleRequired, "Memory title is required.");

        if (string.IsNullOrWhiteSpace(item.Summary))
            Add(issues, SummaryRequired, "Memory summary is required.");

        ValidateConfidence(item.Confidence, issues);

        if (item.AuthorityLevel == MemoryAuthorityLevel.Accepted)
            Add(issues, LocalMemoryCannotBeAccepted, "Local agent memory cannot be created as Accepted.");

        if (item.AuthorityLevel == MemoryAuthorityLevel.SystemRule)
            Add(issues, LocalMemoryCannotBeSystemRule, "Local agent memory cannot be created as SystemRule.");

        if (item.MemoryType == AgentMemoryType.CandidatePattern && string.IsNullOrWhiteSpace(item.KnownLimitations))
            Add(issues, CandidatePatternRequiresLimitations, "Candidate pattern memory requires known limitations.");

        if (RequiresEvidence(item) && IsEmpty(item.EvidenceRefs))
            Add(issues, EvidenceRequired, "Memory claims require evidence references.");

        if (item.EvidenceRefs is not null)
        {
            foreach (var evidence in item.EvidenceRefs)
            {
                if (evidence is null)
                    continue;

                if (string.Equals(evidence.EvidenceId, item.MemoryItemId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(evidence.SourceId, item.MemoryItemId, StringComparison.OrdinalIgnoreCase))
                {
                    Add(issues, SelfReferentialEvidenceBlocked, "A memory item must not cite itself as evidence.");
                    break;
                }
            }
        }

        return ToResult(issues);
    }

    public MemoryValidationResult Validate(MemoryInfluenceRecord influence)
    {
        ArgumentNullException.ThrowIfNull(influence);

        var issues = new List<MemoryValidationIssue>();

        if (string.IsNullOrWhiteSpace(influence.MemoryItemId))
            Add(issues, InfluenceMemoryItemRequired, "Influence record requires a memory item ID.");

        ValidateScope(influence.Scope, issues);

        if (string.IsNullOrWhiteSpace(influence.DecisionId))
            Add(issues, InfluenceDecisionRequired, "Influence record requires a decision ID.");

        if (!Enum.IsDefined(influence.InfluenceType))
            Add(issues, InfluenceTypeRequired, "Influence record requires a valid influence type.");

        ValidateConfidence(influence.Confidence, issues);

        if (IsEmpty(influence.EvidenceRefs))
            Add(issues, EvidenceRequired, "Influence records require evidence references.");

        return ToResult(issues);
    }

    public MemoryValidationResult Validate(HandoffMemorySlice handoffSlice)
    {
        ArgumentNullException.ThrowIfNull(handoffSlice);

        var issues = new List<MemoryValidationIssue>();

        if (string.IsNullOrWhiteSpace(handoffSlice.SourceAgentId))
            Add(issues, HandoffSourceRequired, "Handoff memory requires a source agent ID.");

        if (string.IsNullOrWhiteSpace(handoffSlice.TargetAgentId))
            Add(issues, HandoffTargetRequired, "Handoff memory requires a target agent ID.");

        if (string.IsNullOrWhiteSpace(handoffSlice.CampaignId))
            Add(issues, CampaignRequired, "Handoff memory requires a campaign ID.");

        if (string.IsNullOrWhiteSpace(handoffSlice.RunId))
            Add(issues, RunRequired, "Handoff memory requires a run ID.");

        if (IsEmpty(handoffSlice.MemoryItemIds))
            Add(issues, HandoffMemoryItemRequired, "Handoff memory requires at least one memory item ID.");

        if (string.IsNullOrWhiteSpace(handoffSlice.Summary))
            Add(issues, SummaryRequired, "Handoff memory requires a summary.");

        if (IsEmpty(handoffSlice.EvidenceRefs))
            Add(issues, EvidenceRequired, "Handoff memory requires evidence references.");

        if (!Enum.IsDefined(handoffSlice.AllowedUse))
            Add(issues, HandoffAllowedUseRequired, "Handoff memory requires an allowed use.");

        ValidateConfidence(handoffSlice.Confidence, issues);

        return ToResult(issues);
    }

    private static bool RequiresEvidence(AgentLocalMemoryItem item)
    {
        if (item.MemoryType != AgentMemoryType.Working)
            return true;

        return item.ExpiresAt is null;
    }

    private static void ValidateScope(AgentMemoryScope? scope, List<MemoryValidationIssue> issues)
    {
        if (scope is null)
        {
            Add(issues, ScopeRequired, "Memory scope is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(scope.TenantId))
            Add(issues, TenantRequired, "Memory scope requires a tenant ID.");

        if (string.IsNullOrWhiteSpace(scope.ProjectId))
            Add(issues, ProjectRequired, "Memory scope requires a project ID.");

        if (string.IsNullOrWhiteSpace(scope.CampaignId))
            Add(issues, CampaignRequired, "Memory scope requires a campaign ID.");

        if (string.IsNullOrWhiteSpace(scope.RunId))
            Add(issues, RunRequired, "Memory scope requires a run ID.");

        if (string.IsNullOrWhiteSpace(scope.AgentId))
            Add(issues, AgentRequired, "Memory scope requires an agent ID.");
    }

    private static void ValidateConfidence(decimal confidence, List<MemoryValidationIssue> issues)
    {
        if (confidence < 0m || confidence > 1m)
            Add(issues, ConfidenceOutOfRange, "Confidence must be between 0.0 and 1.0.");
    }

    private static MemoryValidationResult ToResult(IReadOnlyList<MemoryValidationIssue> issues)
    {
        if (issues.Count == 0)
            return MemoryValidationResult.Valid();

        return new MemoryValidationResult
        {
            IsValid = false,
            Issues = issues
        };
    }

    private static bool IsEmpty<T>(IReadOnlyCollection<T>? items) =>
        items is null || items.Count == 0;

    private static void Add(List<MemoryValidationIssue> issues, string code, string message)
    {
        issues.Add(new MemoryValidationIssue
        {
            Code = code,
            Message = message,
            Severity = MemoryValidationSeverity.Error
        });
    }
}
