using System.Text.Json;

namespace IronDev.Core.Policy;

public static class ApprovalRequirementOutcomes
{
    public const string ApprovalRequired = "ApprovalRequired";
    public const string NoApprovalRequired = "NoApprovalRequired";
    public const string BlockedByPolicy = "BlockedByPolicy";
    public const string NoMatchingRuleFailClosed = "NoMatchingRuleFailClosed";
    public const string InvalidPolicyFailClosed = "InvalidPolicyFailClosed";
    public const string InvalidRequest = "InvalidRequest";

    public static IReadOnlyList<string> All { get; } =
    [
        ApprovalRequired,
        NoApprovalRequired,
        BlockedByPolicy,
        NoMatchingRuleFailClosed,
        InvalidPolicyFailClosed,
        InvalidRequest
    ];
}

public sealed record ApprovalRequirementEvaluationRequest
{
    public required Guid ProjectId { get; init; }
    public required ProjectAutonomyPolicy ProjectPolicy { get; init; }
    public required IReadOnlyList<ProjectApprovalRule> ApprovalRules { get; init; }
    public required string ApprovalScope { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? ActionName { get; init; }
    public required string RequestedByActorType { get; init; }
    public required string RequestedByActorId { get; init; }
    public Guid? CorrelationId { get; init; }
    public required int ContextVersion { get; init; }
    public required string ContextJson { get; init; }
}

public sealed record ApprovalRequirementEvaluationResult
{
    public required Guid ProjectId { get; init; }
    public required string Outcome { get; init; }
    public required string ApprovalScope { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string ReasonCode { get; init; }
    public string? Reason { get; init; }
    public required IReadOnlyList<ApprovalRequirement> Requirements { get; init; }
    public Guid? MatchedPolicyId { get; init; }
    public required IReadOnlyList<Guid> MatchedRuleIds { get; init; }
    public required bool FailClosed { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool GrantsExecution { get; init; }
    public required bool MutatesSource { get; init; }
    public required bool PromotesMemory { get; init; }
    public required bool StartsWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
}

public sealed record ApprovalRequirement
{
    public required string ApprovalScope { get; init; }
    public required string ApprovalType { get; init; }
    public required string RiskLevel { get; init; }
    public required IReadOnlyList<string> RequiredApproverTypes { get; init; }
    public int? QuorumCount { get; init; }
    public required string RequirementCode { get; init; }
    public string? RequirementReason { get; init; }
    public Guid? SourceRuleId { get; init; }
}

public interface IApprovalRequirementEvaluator
{
    ApprovalRequirementEvaluationResult Evaluate(ApprovalRequirementEvaluationRequest? request);
}

public sealed class ApprovalRequirementEvaluator : IApprovalRequirementEvaluator
{
    private static readonly string[] PrivateReasoningMarkers =
    [
        "hiddenReasoning",
        "hidden reasoning",
        "chainOfThought",
        "chain of thought",
        "chain-of-thought",
        "private reasoning",
        "privateReasoning",
        "scratchpad",
        "rawPrompt",
        "raw prompt",
        "rawCompletion",
        "raw completion",
        "rawToolOutput",
        "raw tool output",
        "entirePatch",
        "entire patch"
    ];

    public ApprovalRequirementEvaluationResult Evaluate(ApprovalRequirementEvaluationRequest? request)
    {
        var requestIssue = ValidateRequestShape(request);
        if (requestIssue is not null)
        {
            return Result(
                request?.ProjectId ?? Guid.Empty,
                ApprovalRequirementOutcomes.InvalidRequest,
                request?.ApprovalScope ?? string.Empty,
                request?.SubjectType ?? string.Empty,
                request?.SubjectId ?? string.Empty,
                requestIssue.Value.Code,
                requestIssue.Value.Message,
                [],
                null,
                [],
                failClosed: true);
        }

        var normalizedScope = ProjectApprovalRuleScopes.Normalize(request!.ApprovalScope);
        var policyIssue = ValidatePolicy(request);
        if (policyIssue is not null)
        {
            return Result(
                request.ProjectId,
                ApprovalRequirementOutcomes.InvalidPolicyFailClosed,
                normalizedScope,
                request.SubjectType,
                request.SubjectId,
                policyIssue.Value.Code,
                policyIssue.Value.Message,
                [],
                request.ProjectPolicy.ProjectAutonomyPolicyId,
                [],
                failClosed: true);
        }

        var ruleIssue = ValidateRules(request);
        if (ruleIssue is not null)
        {
            return Result(
                request.ProjectId,
                ApprovalRequirementOutcomes.InvalidPolicyFailClosed,
                normalizedScope,
                request.SubjectType,
                request.SubjectId,
                ruleIssue.Value.Code,
                ruleIssue.Value.Message,
                [],
                request.ProjectPolicy.ProjectAutonomyPolicyId,
                [],
                failClosed: true);
        }

        var matches = FindMatches(request, normalizedScope);
        if (matches.Count == 0)
        {
            return Result(
                request.ProjectId,
                ApprovalRequirementOutcomes.NoMatchingRuleFailClosed,
                normalizedScope,
                request.SubjectType,
                request.SubjectId,
                "NO_MATCHING_RULE_FAIL_CLOSED",
                "No active matching approval rule was found, so evaluation fails closed.",
                [],
                request.ProjectPolicy.ProjectAutonomyPolicyId,
                [],
                failClosed: true);
        }

        var strongest = SelectStrongest(matches);
        if (strongest.Ambiguous)
        {
            return Result(
                request.ProjectId,
                ApprovalRequirementOutcomes.NoMatchingRuleFailClosed,
                normalizedScope,
                request.SubjectType,
                request.SubjectId,
                "MATCHING_RULES_AMBIGUOUS_FAIL_CLOSED",
                "Multiple matching approval rules have equivalent restrictive precedence, so evaluation fails closed.",
                [],
                request.ProjectPolicy.ProjectAutonomyPolicyId,
                matches.Select(match => match.Rule.ProjectApprovalRuleId).Distinct().ToArray(),
                failClosed: true);
        }

        var rule = strongest.Match!.Rule;
        if (ProjectApprovalRuleScopes.IsSensitive(rule.ApprovalScope)
            && string.Equals(rule.ApprovalType, ProjectApprovalRuleApprovalTypes.None, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                request.ProjectId,
                ApprovalRequirementOutcomes.BlockedByPolicy,
                normalizedScope,
                request.SubjectType,
                request.SubjectId,
                "SENSITIVE_SCOPE_NONE_BLOCKED",
                "Sensitive scopes cannot be evaluated as no-approval-required.",
                [],
                request.ProjectPolicy.ProjectAutonomyPolicyId,
                [rule.ProjectApprovalRuleId],
                failClosed: true);
        }

        if (ProjectApprovalRuleScopes.IsSensitive(rule.ApprovalScope)
            && !rule.ApproverTypes.Any(ProjectApprovalRuleApproverTypes.IsHumanClass))
        {
            return Result(
                request.ProjectId,
                ApprovalRequirementOutcomes.BlockedByPolicy,
                normalizedScope,
                request.SubjectType,
                request.SubjectId,
                "SENSITIVE_SCOPE_HUMAN_APPROVER_REQUIRED",
                "Sensitive scopes require a human approver class.",
                [],
                request.ProjectPolicy.ProjectAutonomyPolicyId,
                [rule.ProjectApprovalRuleId],
                failClosed: true);
        }

        var requirement = RequirementFrom(rule);
        if (string.Equals(rule.ApprovalType, ProjectApprovalRuleApprovalTypes.None, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                request.ProjectId,
                ApprovalRequirementOutcomes.NoApprovalRequired,
                normalizedScope,
                request.SubjectType,
                request.SubjectId,
                "EXPLICIT_NON_SENSITIVE_NONE_RULE",
                "An active matching non-sensitive rule explicitly declares ApprovalType=None.",
                [requirement],
                request.ProjectPolicy.ProjectAutonomyPolicyId,
                [rule.ProjectApprovalRuleId],
                failClosed: false);
        }

        return Result(
            request.ProjectId,
            ApprovalRequirementOutcomes.ApprovalRequired,
            normalizedScope,
            request.SubjectType,
            request.SubjectId,
            "MATCHING_RULE_REQUIRES_APPROVAL",
            "An active matching rule declares approval requirements.",
            [requirement],
            request.ProjectPolicy.ProjectAutonomyPolicyId,
            [rule.ProjectApprovalRuleId],
            failClosed: false);
    }

    private static ValidationIssue? ValidateRequestShape(ApprovalRequirementEvaluationRequest? request)
    {
        if (request is null)
        {
            return Issue("REQUEST_REQUIRED", "Evaluation request is required.");
        }

        if (request.ProjectId == Guid.Empty)
        {
            return Issue("PROJECT_ID_REQUIRED", "Project ID is required.");
        }

        if (request.ProjectPolicy is null)
        {
            return Issue("PROJECT_POLICY_REQUIRED", "Project policy is required.");
        }

        if (request.ApprovalRules is null)
        {
            return Issue("APPROVAL_RULES_REQUIRED", "Approval rule collection is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ApprovalScope))
        {
            return Issue("APPROVAL_SCOPE_REQUIRED", "Approval scope is required.");
        }

        if (!ProjectApprovalRuleScopes.IsAllowed(request.ApprovalScope))
        {
            return Issue("APPROVAL_SCOPE_UNKNOWN", "Approval scope is not part of the bounded vocabulary.");
        }

        if (string.IsNullOrWhiteSpace(request.SubjectType))
        {
            return Issue("SUBJECT_TYPE_REQUIRED", "Subject type is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SubjectId))
        {
            return Issue("SUBJECT_ID_REQUIRED", "Subject ID is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByActorType))
        {
            return Issue("REQUESTED_BY_ACTOR_TYPE_REQUIRED", "Requesting actor type is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByActorId))
        {
            return Issue("REQUESTED_BY_ACTOR_ID_REQUIRED", "Requesting actor ID is required.");
        }

        if (request.ContextVersion <= 0)
        {
            return Issue("CONTEXT_VERSION_REQUIRED", "Context version must be positive.");
        }

        if (string.IsNullOrWhiteSpace(request.ContextJson))
        {
            return Issue("CONTEXT_JSON_REQUIRED", "Context JSON is required.");
        }

        if (ContainsAny(request.ContextJson, PrivateReasoningMarkers))
        {
            return Issue("CONTEXT_PRIVATE_REASONING", "Context JSON cannot contain hidden or private reasoning markers.");
        }

        try
        {
            using var document = JsonDocument.Parse(request.ContextJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Issue("CONTEXT_JSON_OBJECT_REQUIRED", "Context JSON must be an object.");
            }

            if (!document.RootElement.TryGetProperty("schema", out var schema)
                || schema.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(schema.GetString()))
            {
                return Issue("CONTEXT_SCHEMA_REQUIRED", "Context JSON requires a schema field.");
            }
        }
        catch (JsonException)
        {
            return Issue("CONTEXT_JSON_INVALID", "Context JSON is not valid JSON.");
        }

        return null;
    }

    private static ValidationIssue? ValidatePolicy(ApprovalRequirementEvaluationRequest request)
    {
        if (request.ProjectPolicy.ProjectId != request.ProjectId)
        {
            return Issue("POLICY_PROJECT_MISMATCH", "Project policy belongs to a different project.");
        }

        var policyValidation = new ProjectAutonomyPolicyValidator().Validate(request.ProjectPolicy);
        if (!policyValidation.IsValid)
        {
            return Issue("POLICY_INVALID", "Project policy failed validation.");
        }

        if (!string.Equals(request.ProjectPolicy.Status, nameof(ProjectAutonomyPolicyStatus.Active), StringComparison.OrdinalIgnoreCase))
        {
            return Issue("POLICY_NOT_ACTIVE", "Project policy must be active for requirement evaluation.");
        }

        return null;
    }

    private static ValidationIssue? ValidateRules(ApprovalRequirementEvaluationRequest request)
    {
        foreach (var rule in request.ApprovalRules)
        {
            if (rule.ProjectId != request.ProjectId)
            {
                return Issue("RULE_PROJECT_MISMATCH", "Approval rule belongs to a different project.");
            }

            if (rule.ProjectAutonomyPolicyId != request.ProjectPolicy.ProjectAutonomyPolicyId)
            {
                return Issue("RULE_POLICY_MISMATCH", "Approval rule targets a different project autonomy policy.");
            }

            var ruleValidation = ProjectApprovalRuleValidator.Validate(rule);
            if (!ruleValidation.IsValid)
            {
                return Issue("RULE_INVALID", "Approval rule failed validation.");
            }
        }

        return null;
    }

    private static List<RuleMatch> FindMatches(ApprovalRequirementEvaluationRequest request, string normalizedScope)
    {
        return request.ApprovalRules
            .Where(rule => string.Equals(rule.Status, ProjectApprovalRuleStatuses.Active, StringComparison.OrdinalIgnoreCase))
            .Where(rule => string.Equals(rule.ApprovalScope, normalizedScope, StringComparison.OrdinalIgnoreCase))
            .Select(rule => new RuleMatch(rule, SubjectMatchScore(rule.SubjectTypePattern, request.SubjectType), ActionMatchScore(rule.ActionNamePattern, request.ActionName)))
            .Where(match => match.SubjectScore > 0 && match.ActionScore > 0)
            .OrderByDescending(match => match.SubjectScore)
            .ThenByDescending(match => match.ActionScore)
            .ThenByDescending(match => ApprovalTypeRank(match.Rule.ApprovalType))
            .ThenByDescending(match => RiskRank(match.Rule.RiskLevel))
            .ThenBy(match => match.Rule.RuleName, StringComparer.Ordinal)
            .ThenBy(match => match.Rule.ProjectApprovalRuleId)
            .ToList();
    }

    private static StrongestRuleMatch SelectStrongest(IReadOnlyList<RuleMatch> matches)
    {
        if (matches.Count == 0)
        {
            return new StrongestRuleMatch(null, Ambiguous: false);
        }

        var strongest = matches[0];
        var equivalentStrongest = matches
            .Where(match => match.SubjectScore == strongest.SubjectScore
                && match.ActionScore == strongest.ActionScore
                && ApprovalTypeRank(match.Rule.ApprovalType) == ApprovalTypeRank(strongest.Rule.ApprovalType)
                && RiskRank(match.Rule.RiskLevel) == RiskRank(strongest.Rule.RiskLevel))
            .ToArray();

        return new StrongestRuleMatch(strongest, Ambiguous: equivalentStrongest.Length > 1);
    }

    private static int SubjectMatchScore(string? pattern, string subjectType)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern == "*")
        {
            return 1;
        }

        return string.Equals(pattern, subjectType, StringComparison.OrdinalIgnoreCase) ? 3 : 0;
    }

    private static int ActionMatchScore(string? pattern, string? actionName)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern == "*")
        {
            return 1;
        }

        if (string.IsNullOrWhiteSpace(actionName))
        {
            return 0;
        }

        if (pattern.EndsWith("*", StringComparison.Ordinal) && pattern.Length > 1)
        {
            var prefix = pattern[..^1];
            return actionName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        }

        return string.Equals(pattern, actionName, StringComparison.OrdinalIgnoreCase) ? 3 : 0;
    }

    private static int ApprovalTypeRank(string approvalType)
    {
        if (string.Equals(approvalType, "Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }

        if (string.Equals(approvalType, ProjectApprovalRuleApprovalTypes.HumanOnly, StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (string.Equals(approvalType, ProjectApprovalRuleApprovalTypes.AllOf, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (string.Equals(approvalType, ProjectApprovalRuleApprovalTypes.Quorum, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (string.Equals(approvalType, ProjectApprovalRuleApprovalTypes.Single, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(approvalType, ProjectApprovalRuleApprovalTypes.AnyOf, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private static int RiskRank(string riskLevel)
    {
        if (string.Equals(riskLevel, ProjectApprovalRuleRiskLevels.Critical, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (string.Equals(riskLevel, ProjectApprovalRuleRiskLevels.High, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (string.Equals(riskLevel, ProjectApprovalRuleRiskLevels.Medium, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    private static ApprovalRequirement RequirementFrom(ProjectApprovalRule rule) =>
        new()
        {
            ApprovalScope = ProjectApprovalRuleScopes.Normalize(rule.ApprovalScope),
            ApprovalType = ProjectApprovalRuleApprovalTypes.Normalize(rule.ApprovalType),
            RiskLevel = ProjectApprovalRuleRiskLevels.Normalize(rule.RiskLevel),
            RequiredApproverTypes = rule.ApproverTypes
                .Select(ProjectApprovalRuleApproverTypes.Normalize)
                .ToArray(),
            QuorumCount = rule.QuorumCount,
            RequirementCode = string.Equals(rule.ApprovalType, ProjectApprovalRuleApprovalTypes.None, StringComparison.OrdinalIgnoreCase)
                ? "NO_APPROVAL_REQUIRED_BY_EXPLICIT_RULE"
                : "APPROVAL_REQUIRED_BY_RULE",
            RequirementReason = "Requirement was derived from an active matching project approval rule.",
            SourceRuleId = rule.ProjectApprovalRuleId
        };

    private static ApprovalRequirementEvaluationResult Result(
        Guid projectId,
        string outcome,
        string approvalScope,
        string subjectType,
        string subjectId,
        string reasonCode,
        string? reason,
        IReadOnlyList<ApprovalRequirement> requirements,
        Guid? matchedPolicyId,
        IReadOnlyList<Guid> matchedRuleIds,
        bool failClosed) =>
        new()
        {
            ProjectId = projectId,
            Outcome = outcome,
            ApprovalScope = approvalScope,
            SubjectType = subjectType,
            SubjectId = subjectId,
            ReasonCode = reasonCode,
            Reason = reason,
            Requirements = requirements,
            MatchedPolicyId = matchedPolicyId,
            MatchedRuleIds = matchedRuleIds,
            FailClosed = failClosed,
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false
        };

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static ValidationIssue Issue(string code, string message) => new(code, message);

    private readonly record struct ValidationIssue(string Code, string Message);

    private sealed record RuleMatch(ProjectApprovalRule Rule, int SubjectScore, int ActionScore);

    private sealed record StrongestRuleMatch(RuleMatch? Match, bool Ambiguous);
}
