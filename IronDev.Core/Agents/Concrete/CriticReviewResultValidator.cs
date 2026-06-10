using IronDev.Core.Agents;

namespace IronDev.Core.Agents.Concrete;

public sealed class CriticReviewResultValidator
{
    public const string ReviewResultIdRequired = "CRITIC_REVIEW_RESULT_ID_REQUIRED";
    public const string ReviewRequestIdRequired = "CRITIC_REVIEW_REQUEST_ID_REQUIRED";
    public const string VerdictInvalid = "CRITIC_REVIEW_VERDICT_INVALID";
    public const string ReviewedAtRequired = "CRITIC_REVIEW_REVIEWED_AT_REQUIRED";
    public const string FindingIdRequired = "CRITIC_FINDING_ID_REQUIRED";
    public const string FindingTitleRequired = "CRITIC_FINDING_TITLE_REQUIRED";
    public const string FindingProblemRequired = "CRITIC_FINDING_PROBLEM_REQUIRED";
    public const string FindingWhyItMattersRequired = "CRITIC_FINDING_WHY_IT_MATTERS_REQUIRED";
    public const string FindingRequiredFixRequired = "CRITIC_FINDING_REQUIRED_FIX_REQUIRED";
    public const string NoObjectionCannotBlockMerge = "CRITIC_NO_OBJECTION_CANNOT_BLOCK_MERGE";
    public const string RecommendBlockRequiresBlockingFinding = "CRITIC_RECOMMEND_BLOCK_REQUIRES_BLOCKING_FINDING";
    public const string RawPrivateReasoningBlocked = "CRITIC_RAW_PRIVATE_REASONING_BLOCKED";
    public const string AuthorityClaimBlocked = "CRITIC_AUTHORITY_CLAIM_BLOCKED";
    public const string BoundaryWarningsRequired = "CRITIC_BOUNDARY_WARNINGS_REQUIRED";

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

    public IReadOnlyList<AgentDefinitionValidationIssue> Validate(CriticReviewResult result)
    {
        var issues = new List<AgentDefinitionValidationIssue>();

        if (string.IsNullOrWhiteSpace(result.ReviewResultId))
            AddError(issues, ReviewResultIdRequired, "ReviewResultId is required.");

        if (string.IsNullOrWhiteSpace(result.ReviewRequestId))
            AddError(issues, ReviewRequestIdRequired, "ReviewRequestId is required.");

        if (!Enum.IsDefined(result.Verdict))
            AddError(issues, VerdictInvalid, "Critic review verdict is invalid.");

        if (result.ReviewedAt == default)
            AddError(issues, ReviewedAtRequired, "ReviewedAt is required.");

        if (result.Verdict == CriticReviewVerdict.NoObjection &&
            result.Findings.Any(finding => finding.BlocksMerge))
        {
            AddError(issues, NoObjectionCannotBlockMerge, "NoObjection cannot include a blocking finding.");
        }

        if (result.Verdict == CriticReviewVerdict.RecommendBlock &&
            !result.Findings.Any(finding => finding.BlocksMerge))
        {
            AddError(issues, RecommendBlockRequiresBlockingFinding, "RecommendBlock requires at least one blocking finding.");
        }

        ValidateBoundaryWarnings(result.Warnings, issues);
        ValidateText("ReviewResultId", result.ReviewResultId, issues);
        ValidateText("ReviewRequestId", result.ReviewRequestId, issues);
        ValidateText("ReviewedByAgentId", result.ReviewedByAgentId, issues);
        ValidateText("CorrelationId", result.CorrelationId, issues);

        foreach (var warning in result.Warnings)
            ValidateText("Warnings", warning, issues);

        foreach (var finding in result.Findings)
            ValidateFinding(finding, issues);

        return issues;
    }

    private static void ValidateFinding(CriticFinding finding, List<AgentDefinitionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(finding.FindingId))
            AddError(issues, FindingIdRequired, "FindingId is required.");

        if (string.IsNullOrWhiteSpace(finding.Title))
            AddError(issues, FindingTitleRequired, "Finding Title is required.");

        if (string.IsNullOrWhiteSpace(finding.Problem))
            AddError(issues, FindingProblemRequired, "Finding Problem is required.");

        if (string.IsNullOrWhiteSpace(finding.WhyItMatters))
            AddError(issues, FindingWhyItMattersRequired, "Finding WhyItMatters is required.");

        if (string.IsNullOrWhiteSpace(finding.RequiredFix))
            AddError(issues, FindingRequiredFixRequired, "Finding RequiredFix is required.");

        ValidateText("FindingId", finding.FindingId, issues);
        ValidateText("Title", finding.Title, issues);
        ValidateText("Problem", finding.Problem, issues);
        ValidateText("WhyItMatters", finding.WhyItMatters, issues);
        ValidateText("RequiredFix", finding.RequiredFix, issues);

        foreach (var evidenceRef in finding.EvidenceRefs)
            ValidateText("EvidenceRefs", evidenceRef, issues);
    }

    private static void ValidateBoundaryWarnings(IReadOnlyList<string> warnings, List<AgentDefinitionValidationIssue> issues)
    {
        if (!Contains(warnings, "recommendations only") ||
            !Contains(warnings, "does not grant or deny approval") ||
            !Contains(warnings, "Governance and human approval remain separate"))
        {
            AddError(
                issues,
                BoundaryWarningsRequired,
                "Critic review result must state that findings are recommendations only and approval remains separate.");
        }
    }

    private static void ValidateText(string fieldName, string? value, List<AgentDefinitionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var marker in RawPrivateReasoningMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                AddError(issues, RawPrivateReasoningBlocked, $"{fieldName} contains raw/private reasoning marker '{marker}'.");
        }

        foreach (var claim in AuthorityClaims)
        {
            if (value.Contains(claim, StringComparison.OrdinalIgnoreCase))
                AddError(issues, AuthorityClaimBlocked, $"{fieldName} contains authority/promotion claim '{claim}'.");
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
