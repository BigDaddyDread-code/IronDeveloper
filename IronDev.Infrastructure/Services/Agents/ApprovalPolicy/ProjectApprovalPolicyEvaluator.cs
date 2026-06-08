using IronDev.Core.Agents.ApprovalPolicy;

namespace IronDev.Infrastructure.Services.Agents.ApprovalPolicy;

public sealed class ProjectApprovalPolicyEvaluator : IProjectApprovalPolicyEvaluator
{
    public ProjectApprovalEvaluationResult Evaluate(ProjectApprovalEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Policy);

        var warnings = new List<string>();
        var riskTier = Normalize(request.RiskTier);
        if (!ProjectApprovalRiskTiers.IsKnown(riskTier))
        {
            warnings.Add($"Unknown risk tier '{request.RiskTier}'. Approval required by project policy.");
            return Build(
                ProjectApprovalDecisions.ApprovalRequired,
                "Unknown risk tier. Approval required by project policy.",
                ProjectApprovalModes.AskEveryTime,
                matchedRuleDescription: null,
                warnings);
        }

        if (!string.Equals(request.ProjectId, request.Policy.ProjectId, StringComparison.OrdinalIgnoreCase))
            warnings.Add("Request projectId does not match the evaluated project policy.");

        var match = FindMatchingRule(request, riskTier);
        var appliedMode = match.Rule is null
            ? NormalizeMode(request.Policy.DefaultMode, warnings)
            : NormalizeMode(match.Rule.Mode, warnings);
        var matchedRuleDescription = match.Rule is null
            ? $"DefaultMode={appliedMode}"
            : DescribeRule(match.Rule);

        if (string.Equals(appliedMode, ProjectApprovalModes.AlwaysBlock, StringComparison.Ordinal))
        {
            return Build(
                ProjectApprovalDecisions.BlockedByPolicy,
                match.Rule?.Reason ?? "Blocked by project policy.",
                appliedMode,
                matchedRuleDescription,
                warnings);
        }

        if (string.Equals(appliedMode, ProjectApprovalModes.AskEveryTime, StringComparison.Ordinal))
        {
            return Build(
                ProjectApprovalDecisions.ApprovalRequired,
                match.Rule?.Reason ?? (match.Rule is null
                    ? "No matching approval rule found. Approval required by project policy."
                    : "Approval required by project policy."),
                appliedMode,
                matchedRuleDescription,
                warnings);
        }

        if (ProjectApprovalRiskTiers.IsExternalSystem(riskTier))
        {
            warnings.Add("External system auto-allow is not supported in the current release.");
            return Build(
                ProjectApprovalDecisions.BlockedByPolicy,
                "Blocked by project policy. External system auto-allow is not supported in the current release.",
                appliedMode,
                matchedRuleDescription,
                warnings);
        }

        if (ProjectApprovalRiskTiers.RequiresApprovalWhenAlwaysAllowed(riskTier))
        {
            warnings.Add($"Project policy requested always_allow, but this risk tier cannot be auto-allowed in the current release: {riskTier}.");
            return Build(
                ProjectApprovalDecisions.ApprovalRequired,
                "Approval required by project policy. Project policy requested always_allow, but this risk tier cannot be auto-allowed in the current release.",
                appliedMode,
                matchedRuleDescription,
                warnings);
        }

        var automaticExecutionAllowed = ProjectApprovalRiskTiers.CanAutoExecuteWhenAllowed(riskTier);
        var reason = automaticExecutionAllowed
            ? match.Rule?.Reason ?? "Allowed by project policy."
            : match.Rule?.Reason ?? "Allowed by project policy. Policy does not allow automatic execution.";

        return new ProjectApprovalEvaluationResult
        {
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            Reason = reason,
            AppliedMode = appliedMode,
            MatchedRuleDescription = matchedRuleDescription,
            HumanApprovalRequired = false,
            AutomaticExecutionAllowed = automaticExecutionAllowed,
            SourceMutationAllowed = false,
            Warnings = warnings
        };
    }

    private static PolicyRuleMatch FindMatchingRule(ProjectApprovalEvaluationRequest request, string riskTier)
    {
        for (var specificity = 4; specificity >= 1; specificity--)
        {
            var candidates = request.Policy.Rules
                .Where(rule => MatchesSpecificity(rule, request, riskTier, specificity))
                .ToArray();

            if (candidates.Length == 0)
                continue;

            return new PolicyRuleMatch(SelectMostRestrictive(candidates), specificity);
        }

        return new PolicyRuleMatch(null, 0);
    }

    private static bool MatchesSpecificity(
        ProjectApprovalPolicyRule rule,
        ProjectApprovalEvaluationRequest request,
        string riskTier,
        int specificity)
    {
        if (!string.Equals(Normalize(rule.RiskTier), riskTier, StringComparison.Ordinal))
            return false;

        var ruleActionType = NormalizeOptional(rule.ActionType);
        var ruleRequestedAction = NormalizeOptional(rule.RequestedAction);
        var requestActionType = NormalizeOptional(request.ActionType);
        var requestRequestedAction = NormalizeOptional(request.RequestedAction);

        return specificity switch
        {
            4 => !string.IsNullOrWhiteSpace(ruleActionType) &&
                 !string.IsNullOrWhiteSpace(ruleRequestedAction) &&
                 string.Equals(ruleActionType, requestActionType, StringComparison.Ordinal) &&
                 string.Equals(ruleRequestedAction, requestRequestedAction, StringComparison.Ordinal),
            3 => !string.IsNullOrWhiteSpace(ruleActionType) &&
                 string.IsNullOrWhiteSpace(ruleRequestedAction) &&
                 string.Equals(ruleActionType, requestActionType, StringComparison.Ordinal),
            2 => string.IsNullOrWhiteSpace(ruleActionType) &&
                 !string.IsNullOrWhiteSpace(ruleRequestedAction) &&
                 string.Equals(ruleRequestedAction, requestRequestedAction, StringComparison.Ordinal),
            1 => string.IsNullOrWhiteSpace(ruleActionType) &&
                 string.IsNullOrWhiteSpace(ruleRequestedAction),
            _ => false
        };
    }

    private static ProjectApprovalPolicyRule SelectMostRestrictive(IReadOnlyList<ProjectApprovalPolicyRule> rules)
    {
        return rules
            .OrderByDescending(rule => ModePriority(Normalize(rule.Mode)))
            .First();
    }

    private static int ModePriority(string mode) =>
        mode switch
        {
            ProjectApprovalModes.AlwaysBlock => 3,
            ProjectApprovalModes.AskEveryTime => 2,
            ProjectApprovalModes.AlwaysAllow => 1,
            _ => 2
        };

    private static string NormalizeMode(string mode, List<string> warnings)
    {
        var normalized = Normalize(mode);
        if (ProjectApprovalModes.All.Contains(normalized))
            return normalized;

        warnings.Add($"Unknown approval policy mode '{mode}'. Approval required by project policy.");
        return ProjectApprovalModes.AskEveryTime;
    }

    private static ProjectApprovalEvaluationResult Build(
        string decision,
        string reason,
        string appliedMode,
        string? matchedRuleDescription,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Decision = decision,
            Reason = reason,
            AppliedMode = appliedMode,
            MatchedRuleDescription = matchedRuleDescription,
            HumanApprovalRequired = string.Equals(decision, ProjectApprovalDecisions.ApprovalRequired, StringComparison.Ordinal),
            AutomaticExecutionAllowed = false,
            SourceMutationAllowed = false,
            Warnings = warnings
        };

    private static string DescribeRule(ProjectApprovalPolicyRule rule)
    {
        var parts = new List<string>
        {
            $"riskTier={Normalize(rule.RiskTier)}",
            $"mode={Normalize(rule.Mode)}"
        };

        if (!string.IsNullOrWhiteSpace(rule.ActionType))
            parts.Add($"actionType={rule.ActionType}");

        if (!string.IsNullOrWhiteSpace(rule.RequestedAction))
            parts.Add($"requestedAction={rule.RequestedAction}");

        if (!string.IsNullOrWhiteSpace(rule.Reason))
            parts.Add($"reason={rule.Reason}");

        return string.Join("; ", parts);
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string NormalizeOptional(string? value)
    {
        var normalized = Normalize(value);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
    }

    private sealed record PolicyRuleMatch(ProjectApprovalPolicyRule? Rule, int Specificity);
}
