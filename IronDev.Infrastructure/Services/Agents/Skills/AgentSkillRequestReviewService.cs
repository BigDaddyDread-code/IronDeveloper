using System.Text;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentSkillRequestReviewService : IAgentSkillRequestReviewService
{
    public AgentSkillRequestReview Create(AgentSkillRequestReviewInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.RequestPackage);

        var request = input.RequestPackage;
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AgentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SkillId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RiskTier);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Category);

        var reviewStatus = ResolveReviewStatus(request);
        var blockers = BuildBlockers(request, reviewStatus);
        var checklist = BuildReviewChecklist(request, reviewStatus);

        return new AgentSkillRequestReview
        {
            ReviewId = BuildReviewId(request.RequestId),
            RequestId = request.RequestId,
            ProjectId = request.ProjectId,
            AgentName = request.AgentName,
            SkillId = request.SkillId,
            Purpose = request.Purpose,
            ReviewStatus = reviewStatus,
            Summary = BuildSummary(reviewStatus),
            Decision = request.Decision,
            RiskTier = request.RiskTier,
            Category = request.Category,
            HumanReviewRequired = true,
            HumanApprovalRequired = string.Equals(reviewStatus, AgentSkillRequestReviewStatuses.ApprovalRequired, StringComparison.Ordinal) ||
                                    string.Equals(reviewStatus, AgentSkillRequestReviewStatuses.BlockedForDangerousCapability, StringComparison.Ordinal),
            ApprovalCanBeGrantedByReview = false,
            ExecutionCanStartFromReview = false,
            SourceMutationAllowed = request.SourceMutationAllowed,
            WorkspaceMutationAllowed = request.WorkspaceMutationAllowed,
            ExternalSystemAllowed = request.ExternalSystemAllowed,
            CreatesTicketAllowed = request.CreatesTicketAllowed,
            WritesMemoryAllowed = request.WritesMemoryAllowed,
            EvidencePaths = request.EvidencePaths,
            ParametersSummary = request.ParametersSummary,
            ReviewChecklist = checklist,
            Blockers = blockers,
            Warnings = request.Warnings
        };
    }

    private static string ResolveReviewStatus(AgentSkillRequestPackage request)
    {
        if (!request.SkillKnown)
            return AgentSkillRequestReviewStatuses.BlockedForUnknownSkill;

        if (string.Equals(request.Decision, ProjectApprovalDecisions.BlockedByPolicy, StringComparison.Ordinal))
            return AgentSkillRequestReviewStatuses.BlockedByPolicy;

        if (IsDangerousCapability(request))
            return AgentSkillRequestReviewStatuses.BlockedForDangerousCapability;

        if (string.Equals(request.Decision, ProjectApprovalDecisions.ApprovalRequired, StringComparison.Ordinal))
            return AgentSkillRequestReviewStatuses.ApprovalRequired;

        if (string.Equals(request.Decision, ProjectApprovalDecisions.AllowedByPolicy, StringComparison.Ordinal) &&
            request.SkillExecutionAllowedByPolicy &&
            !request.ExecutionCanStartFromRequest)
        {
            return AgentSkillRequestReviewStatuses.ReadyForHumanReview;
        }

        return AgentSkillRequestReviewStatuses.BlockedByPolicy;
    }

    private static string BuildSummary(string reviewStatus) =>
        reviewStatus switch
        {
            AgentSkillRequestReviewStatuses.BlockedForUnknownSkill =>
                "The requested skill is unknown and must not be executed.",
            AgentSkillRequestReviewStatuses.BlockedByPolicy =>
                "Project policy blocks this skill request.",
            AgentSkillRequestReviewStatuses.ApprovalRequired =>
                "This skill request requires separate human approval before any future execution path can exist.",
            AgentSkillRequestReviewStatuses.ReadyForHumanReview =>
                "Project policy allows this skill request, but the review package cannot execute it.",
            AgentSkillRequestReviewStatuses.BlockedForDangerousCapability =>
                "This request involves a dangerous capability and cannot proceed from this review package.",
            _ =>
                "Skill request review could not determine a safe review status."
        };

    private static IReadOnlyList<string> BuildBlockers(
        AgentSkillRequestPackage request,
        string reviewStatus)
    {
        var blockers = new List<string>();

        if (string.Equals(reviewStatus, AgentSkillRequestReviewStatuses.BlockedForUnknownSkill, StringComparison.Ordinal))
            blockers.Add("Unknown skill.");

        if (string.Equals(reviewStatus, AgentSkillRequestReviewStatuses.BlockedByPolicy, StringComparison.Ordinal))
            blockers.Add("Blocked by project policy.");

        if (string.Equals(reviewStatus, AgentSkillRequestReviewStatuses.BlockedForDangerousCapability, StringComparison.Ordinal))
            blockers.Add("Dangerous capability requires a separate approval/execution flow.");

        if (request.ExecutionCanStartFromRequest)
            blockers.Add("Request package unexpectedly claims execution authority.");

        if (request.ApprovalCanBeGrantedByRequest)
            blockers.Add("Request package unexpectedly claims approval authority.");

        return blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildReviewChecklist(
        AgentSkillRequestPackage request,
        string reviewStatus)
    {
        var checklist = new List<string>();

        if (string.Equals(reviewStatus, AgentSkillRequestReviewStatuses.BlockedForUnknownSkill, StringComparison.Ordinal))
        {
            checklist.Add("Confirm the skill ID.");
            checklist.Add("Do not infer or invent a replacement skill.");
            checklist.Add("Do not execute unknown skills.");
        }
        else if (string.Equals(reviewStatus, AgentSkillRequestReviewStatuses.BlockedByPolicy, StringComparison.Ordinal))
        {
            checklist.Add("Do not execute this skill.");
            checklist.Add("Review the project approval policy.");
            checklist.Add("Change policy only through an explicit separate configuration flow.");
        }
        else if (string.Equals(reviewStatus, AgentSkillRequestReviewStatuses.ApprovalRequired, StringComparison.Ordinal))
        {
            checklist.Add("Review skill risk tier.");
            checklist.Add("Review policy reason.");
            checklist.Add("Review evidence paths and parameters.");
            checklist.Add("Confirm request package cannot execute.");
            checklist.Add("Obtain separate approval evidence in a later explicit flow.");
        }
        else if (string.Equals(reviewStatus, AgentSkillRequestReviewStatuses.ReadyForHumanReview, StringComparison.Ordinal))
        {
            checklist.Add("Confirm requested skill matches intended action.");
            checklist.Add("Confirm evidence paths and parameters are correct.");
            checklist.Add("Confirm review package is not execution authority.");
        }
        else if (string.Equals(reviewStatus, AgentSkillRequestReviewStatuses.BlockedForDangerousCapability, StringComparison.Ordinal))
        {
            checklist.Add("Review skill risk tier.");
            checklist.Add("Review policy reason.");
            checklist.Add("Review evidence paths and parameters.");
            checklist.Add("Confirm request package cannot execute.");
            checklist.Add("Obtain separate approval evidence in a later explicit flow.");
        }

        AddDangerousBoundaryChecklist(request, checklist);
        checklist.Add("Approval cannot be granted by this review package.");
        checklist.Add("Execution cannot start from this review package.");

        return request.ReviewChecklist
            .Concat(checklist)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddDangerousBoundaryChecklist(
        AgentSkillRequestPackage request,
        List<string> checklist)
    {
        if (string.Equals(request.RiskTier, ProjectApprovalRiskTiers.SourceMutation, StringComparison.Ordinal))
            checklist.Add("Source mutation is not allowed from this review package.");

        if (string.Equals(request.RiskTier, ProjectApprovalRiskTiers.GitOperation, StringComparison.Ordinal) ||
            string.Equals(request.Category, AgentSkillCategories.Git, StringComparison.Ordinal))
            checklist.Add("Git operations are not allowed from this review package.");

        if (string.Equals(request.RiskTier, ProjectApprovalRiskTiers.TicketWrite, StringComparison.Ordinal) ||
            string.Equals(request.Category, AgentSkillCategories.Ticketing, StringComparison.Ordinal))
            checklist.Add("Ticket creation is not allowed from this review package.");

        if (string.Equals(request.RiskTier, ProjectApprovalRiskTiers.MemoryWrite, StringComparison.Ordinal))
            checklist.Add("Memory writes are not allowed from this review package.");

        if (string.Equals(request.RiskTier, ProjectApprovalRiskTiers.ExternalSystem, StringComparison.Ordinal) ||
            string.Equals(request.Category, AgentSkillCategories.External, StringComparison.Ordinal) ||
            request.SkillId.StartsWith("github.", StringComparison.OrdinalIgnoreCase))
            checklist.Add("External system access is not allowed from this review package.");

        if (string.Equals(request.RiskTier, ProjectApprovalRiskTiers.WorkspacePreparation, StringComparison.Ordinal) &&
            string.Equals(request.SkillId, AgentSkillIds.WorkspacePrepare, StringComparison.Ordinal) &&
            request.WorkspaceMutationAllowed)
        {
            checklist.Add("Workspace mutation is limited to disposable workspace preparation.");
            checklist.Add("Source repository mutation is not allowed from this review package.");
        }
        else if (string.Equals(request.RiskTier, ProjectApprovalRiskTiers.WorkspaceValidation, StringComparison.Ordinal) &&
                 string.Equals(request.SkillId, AgentSkillIds.WorkspaceValidate, StringComparison.Ordinal) &&
                 request.WorkspaceMutationAllowed)
        {
            checklist.Add("Workspace mutation is limited to disposable workspace validation evidence.");
            checklist.Add("Validation process execution must stay behind the governed validation service.");
            checklist.Add("Source repository mutation is not allowed from this review package.");
        }
        else if (string.Equals(request.RiskTier, ProjectApprovalRiskTiers.WorkspaceReporting, StringComparison.Ordinal) &&
                 string.Equals(request.SkillId, AgentSkillIds.WorkspaceDiff, StringComparison.Ordinal) &&
                 request.WorkspaceMutationAllowed)
        {
            checklist.Add("Workspace mutation is limited to disposable workspace diff evidence.");
            checklist.Add("Source repository mutation is not allowed from this review package.");
        }
        else if (string.Equals(request.RiskTier, ProjectApprovalRiskTiers.WorkspacePackaging, StringComparison.Ordinal) &&
                 string.Equals(request.SkillId, AgentSkillIds.WorkspacePromotionPackage, StringComparison.Ordinal) &&
                 request.WorkspaceMutationAllowed)
        {
            checklist.Add("Workspace mutation is limited to promotion package evidence.");
            checklist.Add("Promotion package creation does not approve or apply source changes.");
            checklist.Add("Source repository mutation is not allowed from this review package.");
        }
        else if (string.Equals(request.RiskTier, ProjectApprovalRiskTiers.WorkspacePackaging, StringComparison.Ordinal) &&
                 string.Equals(request.SkillId, AgentSkillIds.WorkspaceFailurePackage, StringComparison.Ordinal) &&
                 request.WorkspaceMutationAllowed)
        {
            checklist.Add("Workspace mutation is limited to failure package evidence.");
            checklist.Add("Failure package creation does not retry, repair, approve, or apply source changes.");
            checklist.Add("Source repository mutation is not allowed from this review package.");
        }
    }

    private static bool IsDangerousCapability(AgentSkillRequestPackage request) =>
        string.Equals(request.RiskTier, ProjectApprovalRiskTiers.SourceMutation, StringComparison.Ordinal) ||
        string.Equals(request.RiskTier, ProjectApprovalRiskTiers.GitOperation, StringComparison.Ordinal) ||
        string.Equals(request.RiskTier, ProjectApprovalRiskTiers.TicketWrite, StringComparison.Ordinal) ||
        string.Equals(request.RiskTier, ProjectApprovalRiskTiers.MemoryWrite, StringComparison.Ordinal) ||
        string.Equals(request.RiskTier, ProjectApprovalRiskTiers.ExternalSystem, StringComparison.Ordinal) ||
        string.Equals(request.Category, AgentSkillCategories.External, StringComparison.Ordinal) ||
        request.SkillId.StartsWith("github.", StringComparison.OrdinalIgnoreCase);

    private static string BuildReviewId(string requestId) =>
        Sanitize($"skill-request-review-{requestId}");

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
