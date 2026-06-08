using System.Text;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentSkillRequestContextService : IAgentSkillRequestContextService
{
    public AgentSkillRequestContext Create(AgentSkillRequestContextInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.RequestPackage);
        ArgumentNullException.ThrowIfNull(input.ReviewPackage);

        var request = input.RequestPackage;
        var review = input.ReviewPackage;

        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AgentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SkillId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RiskTier);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(review.ReviewId);
        ArgumentException.ThrowIfNullOrWhiteSpace(review.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(review.ReviewStatus);

        var consistencyWarnings = new List<string>();
        var consistencyBlockers = new List<string>();
        var isConsistent = IsConsistent(request, review, consistencyWarnings);
        if (!isConsistent)
            consistencyBlockers.Add("Inconsistent request/review package.");

        var dangerousCapability = IsDangerousCapability(request, review);
        var recommendedNextAction = isConsistent
            ? ResolveRecommendedNextAction(review.ReviewStatus)
            : AgentSkillRequestContextRecommendedActions.CollectMissingEvidence;

        return new AgentSkillRequestContext
        {
            ContextId = BuildContextId(request.RequestId),
            RequestId = request.RequestId,
            ReviewId = review.ReviewId,
            ProjectId = request.ProjectId,
            AgentName = request.AgentName,
            SkillId = request.SkillId,
            Purpose = request.Purpose,
            SkillKnown = request.SkillKnown,
            Decision = request.Decision,
            ReviewStatus = review.ReviewStatus,
            RiskTier = request.RiskTier,
            Category = request.Category,
            HumanReviewRequired = true,
            HumanApprovalRequired = isConsistent && review.HumanApprovalRequired,
            PolicyAllowed = isConsistent && string.Equals(request.Decision, ProjectApprovalDecisions.AllowedByPolicy, StringComparison.Ordinal),
            PolicyBlocked = !isConsistent || string.Equals(request.Decision, ProjectApprovalDecisions.BlockedByPolicy, StringComparison.Ordinal),
            DangerousCapability = dangerousCapability,
            ExecutionCanStartFromContext = false,
            ApprovalCanBeGrantedByContext = false,
            SourceMutationAllowed = request.SourceMutationAllowed && review.SourceMutationAllowed,
            WorkspaceMutationAllowed = request.WorkspaceMutationAllowed && review.WorkspaceMutationAllowed,
            ExternalSystemAllowed = request.ExternalSystemAllowed && review.ExternalSystemAllowed,
            CreatesTicketAllowed = request.CreatesTicketAllowed && review.CreatesTicketAllowed,
            WritesMemoryAllowed = request.WritesMemoryAllowed && review.WritesMemoryAllowed,
            RecommendedNextAction = recommendedNextAction,
            EvidencePaths = Merge(request.EvidencePaths, review.EvidencePaths),
            ParametersSummary = Merge(request.ParametersSummary, review.ParametersSummary),
            ReviewChecklist = Merge(request.ReviewChecklist, review.ReviewChecklist),
            Blockers = Merge(consistencyBlockers, review.Blockers),
            Warnings = Merge(request.Warnings, review.Warnings, consistencyWarnings),
            Interpretation = BuildInterpretation(recommendedNextAction, dangerousCapability)
        };
    }

    private static bool IsConsistent(
        AgentSkillRequestPackage request,
        AgentSkillRequestReview review,
        List<string> warnings)
    {
        AddMismatch(warnings, request.RequestId, review.RequestId, "requestId");
        AddMismatch(warnings, request.ProjectId, review.ProjectId, "projectId");
        AddMismatch(warnings, request.AgentName, review.AgentName, "agentName");
        AddMismatch(warnings, request.SkillId, review.SkillId, "skillId");
        AddMismatch(warnings, request.Purpose, review.Purpose, "purpose");

        return warnings.Count == 0;
    }

    private static void AddMismatch(
        List<string> warnings,
        string requestValue,
        string reviewValue,
        string fieldName)
    {
        if (!string.Equals(requestValue, reviewValue, StringComparison.Ordinal))
            warnings.Add($"Request/review package mismatch for {fieldName}.");
    }

    private static string ResolveRecommendedNextAction(string reviewStatus) =>
        reviewStatus switch
        {
            AgentSkillRequestReviewStatuses.BlockedForUnknownSkill =>
                AgentSkillRequestContextRecommendedActions.StopUnknownSkill,
            AgentSkillRequestReviewStatuses.BlockedByPolicy =>
                AgentSkillRequestContextRecommendedActions.StopBlockedByPolicy,
            AgentSkillRequestReviewStatuses.BlockedForDangerousCapability =>
                AgentSkillRequestContextRecommendedActions.StopDangerousCapability,
            AgentSkillRequestReviewStatuses.ApprovalRequired =>
                AgentSkillRequestContextRecommendedActions.RequestSeparateApproval,
            AgentSkillRequestReviewStatuses.ReadyForHumanReview =>
                AgentSkillRequestContextRecommendedActions.ReviewRequest,
            _ =>
                AgentSkillRequestContextRecommendedActions.NoActionAvailable
        };

    private static IReadOnlyList<string> BuildInterpretation(
        string recommendedNextAction,
        bool dangerousCapability)
    {
        var interpretation = new List<string>();

        if (string.Equals(recommendedNextAction, AgentSkillRequestContextRecommendedActions.StopUnknownSkill, StringComparison.Ordinal))
        {
            interpretation.Add("Requested skill is unknown.");
            interpretation.Add("Do not infer or invent a replacement skill.");
        }
        else if (string.Equals(recommendedNextAction, AgentSkillRequestContextRecommendedActions.StopBlockedByPolicy, StringComparison.Ordinal))
        {
            interpretation.Add("Project policy blocks this skill request.");
            interpretation.Add("Do not execute this skill.");
        }
        else if (string.Equals(recommendedNextAction, AgentSkillRequestContextRecommendedActions.StopDangerousCapability, StringComparison.Ordinal))
        {
            interpretation.Add("This request involves a dangerous capability.");
            interpretation.Add("A separate approval/execution flow would be required later.");
            interpretation.Add("This context cannot approve or execute it.");
        }
        else if (string.Equals(recommendedNextAction, AgentSkillRequestContextRecommendedActions.RequestSeparateApproval, StringComparison.Ordinal))
        {
            interpretation.Add("This skill request requires separate approval evidence before any future execution path can exist.");
        }
        else if (string.Equals(recommendedNextAction, AgentSkillRequestContextRecommendedActions.ReviewRequest, StringComparison.Ordinal))
        {
            interpretation.Add("Project policy allows this low-risk skill request.");
            interpretation.Add("This context still cannot execute it.");
        }
        else if (string.Equals(recommendedNextAction, AgentSkillRequestContextRecommendedActions.CollectMissingEvidence, StringComparison.Ordinal))
        {
            interpretation.Add("Skill request context is inconsistent.");
            interpretation.Add("Do not execute.");
            interpretation.Add("Collect or regenerate request/review packages.");
        }
        else
        {
            interpretation.Add("No safe action is available from this context.");
        }

        if (dangerousCapability)
            interpretation.Add("Dangerous capability is surfaced for review only.");

        return interpretation.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsDangerousCapability(
        AgentSkillRequestPackage request,
        AgentSkillRequestReview review) =>
        string.Equals(request.RiskTier, ProjectApprovalRiskTiers.SourceMutation, StringComparison.Ordinal) ||
        string.Equals(request.RiskTier, ProjectApprovalRiskTiers.GitOperation, StringComparison.Ordinal) ||
        string.Equals(request.RiskTier, ProjectApprovalRiskTiers.TicketWrite, StringComparison.Ordinal) ||
        string.Equals(request.RiskTier, ProjectApprovalRiskTiers.MemoryWrite, StringComparison.Ordinal) ||
        string.Equals(request.RiskTier, ProjectApprovalRiskTiers.ExternalSystem, StringComparison.Ordinal) ||
        string.Equals(review.ReviewStatus, AgentSkillRequestReviewStatuses.BlockedForDangerousCapability, StringComparison.Ordinal) ||
        request.SkillId.StartsWith("github.", StringComparison.OrdinalIgnoreCase) ||
        request.SkillId.StartsWith("git.", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Merge(params IEnumerable<string>[] values) =>
        values
            .SelectMany(value => value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildContextId(string requestId) =>
        Sanitize($"skill-request-context-{requestId}");

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasDash = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            var next = char.IsLetterOrDigit(character) ? character : '-';
            if (next == '-' && previousWasDash)
                continue;

            builder.Append(next);
            previousWasDash = next == '-';
        }

        return builder.ToString().Trim('-');
    }
}
