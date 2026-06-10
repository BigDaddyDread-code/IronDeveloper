using IronDev.Core.Agents;

namespace IronDev.Core.Agents.Concrete;

public sealed class MemoryImprovementDetectionResultValidator
{
    public const string DetectionResultIdRequired = "MEMORY_IMPROVEMENT_DETECTION_RESULT_ID_REQUIRED";
    public const string DetectedAtRequired = "MEMORY_IMPROVEMENT_DETECTED_AT_REQUIRED";
    public const string PatternFindingIdRequired = "MEMORY_IMPROVEMENT_PATTERN_FINDING_ID_REQUIRED";
    public const string PatternTypeInvalid = "MEMORY_IMPROVEMENT_PATTERN_TYPE_INVALID";
    public const string PatternSummaryRequired = "MEMORY_IMPROVEMENT_PATTERN_SUMMARY_REQUIRED";
    public const string PatternConfidenceInvalid = "MEMORY_IMPROVEMENT_PATTERN_CONFIDENCE_INVALID";
    public const string ProposalDraftIdRequired = "MEMORY_IMPROVEMENT_PROPOSAL_DRAFT_ID_REQUIRED";
    public const string ProposalTitleRequired = "MEMORY_IMPROVEMENT_PROPOSAL_TITLE_REQUIRED";
    public const string ProposalSummaryRequired = "MEMORY_IMPROVEMENT_PROPOSAL_SUMMARY_REQUIRED";
    public const string ProposalRationaleRequired = "MEMORY_IMPROVEMENT_PROPOSAL_RATIONALE_REQUIRED";
    public const string ProposalSourcePatternRequired = "MEMORY_IMPROVEMENT_PROPOSAL_SOURCE_PATTERN_REQUIRED";
    public const string ProposalOnlyRequired = "MEMORY_IMPROVEMENT_PROPOSAL_ONLY_REQUIRED";
    public const string CreatesCollectiveMemoryBlocked = "MEMORY_IMPROVEMENT_CREATES_COLLECTIVE_MEMORY_BLOCKED";
    public const string PromotesMemoryBlocked = "MEMORY_IMPROVEMENT_PROMOTES_MEMORY_BLOCKED";
    public const string HumanReviewRequired = "MEMORY_IMPROVEMENT_HUMAN_REVIEW_REQUIRED";
    public const string ProposalEvidenceRequired = "MEMORY_IMPROVEMENT_PROPOSAL_EVIDENCE_REQUIRED";
    public const string RawPrivateReasoningBlocked = "MEMORY_IMPROVEMENT_RAW_PRIVATE_REASONING_BLOCKED";
    public const string AuthorityClaimBlocked = "MEMORY_IMPROVEMENT_AUTHORITY_CLAIM_BLOCKED";
    public const string BoundaryWarningsRequired = "MEMORY_IMPROVEMENT_BOUNDARY_WARNINGS_REQUIRED";

    private static readonly IReadOnlyList<string> RawPrivateReasoningMarkers =
    [
        "RawPrompt",
        "RawCompletion",
        "ChainOfThought",
        "Scratchpad",
        "PrivateReasoning"
    ];

    private static readonly IReadOnlyList<string> AuthorityClaims =
    [
        "approved",
        "authorized",
        "accepted memory",
        "promoted memory",
        "human approved",
        "policy cleared",
        "authoritative for action"
    ];

    public IReadOnlyList<AgentDefinitionValidationIssue> Validate(MemoryImprovementDetectionResult result)
    {
        var issues = new List<AgentDefinitionValidationIssue>();

        if (string.IsNullOrWhiteSpace(result.DetectionResultId))
            AddError(issues, DetectionResultIdRequired, "DetectionResultId is required.");

        if (result.DetectedAt == default)
            AddError(issues, DetectedAtRequired, "DetectedAt is required.");

        ValidateBoundaryWarnings(result.Warnings, issues);
        ValidateText("DetectionResultId", result.DetectionResultId, issues);
        ValidateText("DetectedByAgentId", result.DetectedByAgentId, issues);
        ValidateText("CorrelationId", result.CorrelationId, issues);

        foreach (var warning in result.Warnings)
            ValidateRawPrivateReasoning("Warnings", warning, issues);

        foreach (var finding in result.Findings)
            ValidateFinding(finding, issues);

        foreach (var draft in result.ProposalDrafts)
            ValidateProposalDraft(draft, issues);

        return issues;
    }

    private static void ValidateFinding(MemoryImprovementPatternFinding finding, List<AgentDefinitionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(finding.PatternFindingId))
            AddError(issues, PatternFindingIdRequired, "PatternFindingId is required.");

        if (!Enum.IsDefined(finding.PatternType))
            AddError(issues, PatternTypeInvalid, "PatternType is invalid.");

        if (string.IsNullOrWhiteSpace(finding.Summary))
            AddError(issues, PatternSummaryRequired, "Pattern Summary is required.");

        if (finding.Confidence < 0m || finding.Confidence > 1m)
            AddError(issues, PatternConfidenceInvalid, "Pattern confidence must be between 0 and 1.");

        ValidateText("PatternFindingId", finding.PatternFindingId, issues);
        ValidateText("Summary", finding.Summary, issues);

        foreach (var evidenceRef in finding.EvidenceRefs)
            ValidateText("EvidenceRefs", evidenceRef, issues);

        foreach (var memoryId in finding.RelatedMemoryIds)
            ValidateText("RelatedMemoryIds", memoryId, issues);

        foreach (var proposalId in finding.RelatedProposalIds)
            ValidateText("RelatedProposalIds", proposalId, issues);
    }

    private static void ValidateProposalDraft(MemoryImprovementProposalDraft draft, List<AgentDefinitionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(draft.ProposalDraftId))
            AddError(issues, ProposalDraftIdRequired, "ProposalDraftId is required.");

        if (string.IsNullOrWhiteSpace(draft.Title))
            AddError(issues, ProposalTitleRequired, "Proposal Title is required.");

        if (string.IsNullOrWhiteSpace(draft.Summary))
            AddError(issues, ProposalSummaryRequired, "Proposal Summary is required.");

        if (string.IsNullOrWhiteSpace(draft.Rationale))
            AddError(issues, ProposalRationaleRequired, "Proposal Rationale is required.");

        if (draft.SourcePattern is null)
        {
            AddError(issues, ProposalSourcePatternRequired, "Proposal SourcePattern is required.");
        }
        else
        {
            ValidateFinding(draft.SourcePattern, issues);
        }

        if (!draft.IsProposalOnly)
            AddError(issues, ProposalOnlyRequired, "Memory improvement drafts must be proposal-only.");

        if (draft.CreatesCollectiveMemory)
            AddError(issues, CreatesCollectiveMemoryBlocked, "Memory improvement drafts must not create CollectiveMemory.");

        if (draft.PromotesMemory)
            AddError(issues, PromotesMemoryBlocked, "Memory improvement drafts must not promote memory.");

        if (!draft.RequiresHumanReview)
            AddError(issues, HumanReviewRequired, "Memory improvement drafts require human review.");

        if (draft.EvidenceRefs.Count == 0)
            AddError(issues, ProposalEvidenceRequired, "Memory improvement drafts require evidence references.");

        ValidateText("ProposalDraftId", draft.ProposalDraftId, issues);
        ValidateText("Title", draft.Title, issues);
        ValidateText("Summary", draft.Summary, issues);
        ValidateText("Rationale", draft.Rationale, issues);

        foreach (var evidenceRef in draft.EvidenceRefs)
            ValidateText("EvidenceRefs", evidenceRef, issues);
    }

    private static void ValidateBoundaryWarnings(IReadOnlyList<string> warnings, List<AgentDefinitionValidationIssue> issues)
    {
        if (!Contains(warnings, "proposal-only") ||
            !Contains(warnings, "do not create accepted memory") ||
            !Contains(warnings, "require governed review"))
        {
            AddError(
                issues,
                BoundaryWarningsRequired,
                "Memory improvement output must state proposal-only boundaries and governed review requirement.");
        }
    }

    private static void ValidateText(string fieldName, string? value, List<AgentDefinitionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        ValidateRawPrivateReasoning(fieldName, value, issues);

        foreach (var claim in AuthorityClaims)
        {
            if (value.Contains(claim, StringComparison.OrdinalIgnoreCase))
                AddError(issues, AuthorityClaimBlocked, $"{fieldName} contains authority/promotion claim '{claim}'.");
        }
    }

    private static void ValidateRawPrivateReasoning(string fieldName, string? value, List<AgentDefinitionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var marker in RawPrivateReasoningMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                AddError(issues, RawPrivateReasoningBlocked, $"{fieldName} contains raw/private reasoning marker '{marker}'.");
        }
    }

    private static bool Contains(IReadOnlyList<string> values, string expected) =>
        values.Any(value => value.Contains(expected, StringComparison.OrdinalIgnoreCase));

    private static void AddError(List<AgentDefinitionValidationIssue> issues, string code, string message) =>
        issues.Add(new AgentDefinitionValidationIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message
        });
}
