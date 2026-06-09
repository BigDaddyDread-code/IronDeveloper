using System.Text;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentSkillRequestService : IAgentSkillRequestService
{
    private readonly IAgentSkillPolicyEvaluator _skillPolicyEvaluator;

    public AgentSkillRequestService(IAgentSkillPolicyEvaluator skillPolicyEvaluator)
    {
        _skillPolicyEvaluator = skillPolicyEvaluator ?? throw new ArgumentNullException(nameof(skillPolicyEvaluator));
    }

    public AgentSkillRequestPackage Create(AgentSkillRequestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.AgentName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SkillId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Purpose);
        ArgumentNullException.ThrowIfNull(input.Policy);

        var evaluation = _skillPolicyEvaluator.Evaluate(new AgentSkillPolicyEvaluationRequest
        {
            ProjectId = input.ProjectId,
            SkillId = input.SkillId,
            Policy = input.Policy,
            RequestedAction = input.RequestedAction,
            EvidenceHash = input.EvidenceHash,
            RunId = input.RunId,
            WorkspacePath = input.WorkspacePath,
            SourceRepo = input.SourceRepo
        });

        return new AgentSkillRequestPackage
        {
            RequestId = BuildRequestId(input),
            ProjectId = input.ProjectId,
            AgentName = input.AgentName,
            SkillId = input.SkillId,
            Purpose = input.Purpose,
            SkillKnown = evaluation.SkillKnown,
            Decision = evaluation.Decision,
            Reason = evaluation.Reason,
            RiskTier = evaluation.RiskTier,
            Category = evaluation.Category,
            HumanApprovalRequired = evaluation.HumanApprovalRequired,
            AutomaticExecutionAllowedByPolicy = evaluation.AutomaticExecutionAllowed,
            SkillExecutionAllowedByPolicy = evaluation.SkillExecutionAllowedByPolicy,
            ExecutionCanStartFromRequest = false,
            ApprovalCanBeGrantedByRequest = false,
            SourceMutationAllowed = evaluation.SourceMutationAllowed,
            WorkspaceMutationAllowed = evaluation.WorkspaceMutationAllowed,
            ExternalSystemAllowed = evaluation.ExternalSystemAllowed,
            CreatesTicketAllowed = evaluation.CreatesTicketAllowed,
            WritesMemoryAllowed = evaluation.WritesMemoryAllowed,
            MatchedRuleDescription = evaluation.MatchedRuleDescription,
            EvidencePaths = input.EvidencePaths,
            ParametersSummary = input.ParametersSummary,
            Warnings = evaluation.Warnings,
            ReviewChecklist = BuildReviewChecklist(evaluation)
        };
    }

    private static IReadOnlyList<string> BuildReviewChecklist(AgentSkillPolicyEvaluation evaluation)
    {
        var checklist = new List<string>();

        if (!evaluation.SkillKnown)
        {
            checklist.Add("Confirm the skill ID is valid.");
            checklist.Add("Do not infer or invent a replacement skill.");
            checklist.Add("Do not execute unknown skills.");
        }
        else if (string.Equals(evaluation.Decision, ProjectApprovalDecisions.BlockedByPolicy, StringComparison.Ordinal))
        {
            checklist.Add("Do not execute this skill.");
            checklist.Add("Review project approval policy.");
            checklist.Add("Change policy only through an explicit separate configuration flow.");
        }
        else if (string.Equals(evaluation.Decision, ProjectApprovalDecisions.ApprovalRequired, StringComparison.Ordinal))
        {
            checklist.Add("Review skill risk tier.");
            checklist.Add("Review policy reason.");
            checklist.Add("Review evidence paths and parameters.");
            checklist.Add("Obtain separate approval evidence before execution is possible.");
        }
        else if (string.Equals(evaluation.Decision, ProjectApprovalDecisions.AllowedByPolicy, StringComparison.Ordinal) &&
                 evaluation.SkillExecutionAllowedByPolicy &&
                 IsLowRisk(evaluation.RiskTier))
        {
            checklist.Add("Confirm requested skill matches the intended action.");
            checklist.Add("Confirm evidence paths are correct.");
            checklist.Add("Confirm this request package is not execution authority.");
        }
        else
        {
            checklist.Add("Review skill policy evaluation before taking any action.");
            checklist.Add("Confirm this request package is not execution authority.");
        }

        AddDangerousBoundaryChecklist(evaluation, checklist);
        checklist.Add("Execution cannot start from this request package.");
        checklist.Add("Approval cannot be granted by this request package.");

        return checklist
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddDangerousBoundaryChecklist(
        AgentSkillPolicyEvaluation evaluation,
        List<string> checklist)
    {
        if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.SourceMutation, StringComparison.Ordinal))
            checklist.Add("Source mutation is not allowed from this request package.");

        if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.GitOperation, StringComparison.Ordinal) ||
            string.Equals(evaluation.Category, AgentSkillCategories.Git, StringComparison.Ordinal))
            checklist.Add("Git operations are not allowed from this request package.");

        if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.TicketWrite, StringComparison.Ordinal) ||
            string.Equals(evaluation.Category, AgentSkillCategories.Ticketing, StringComparison.Ordinal))
            checklist.Add("Ticket creation is not allowed from this request package.");

        if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.ExternalSystem, StringComparison.Ordinal) ||
            evaluation.SkillId.StartsWith("github.", StringComparison.OrdinalIgnoreCase))
            checklist.Add("External system access is not allowed from this request package.");

        if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.MemoryWrite, StringComparison.Ordinal))
            checklist.Add("Memory writes are not allowed from this request package.");

        if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.WorkspacePreparation, StringComparison.Ordinal) &&
            string.Equals(evaluation.SkillId, AgentSkillIds.WorkspacePrepare, StringComparison.Ordinal) &&
            evaluation.WorkspaceMutationAllowed)
        {
            checklist.Add("Workspace mutation is limited to disposable workspace preparation.");
            checklist.Add("Source repository mutation is not allowed from this request package.");
        }
        else if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.WorkspaceValidation, StringComparison.Ordinal) &&
                 string.Equals(evaluation.SkillId, AgentSkillIds.WorkspaceValidate, StringComparison.Ordinal) &&
                 evaluation.WorkspaceMutationAllowed)
        {
            checklist.Add("Workspace mutation is limited to disposable workspace validation evidence.");
            checklist.Add("Validation process execution must stay behind the governed validation service.");
            checklist.Add("Source repository mutation is not allowed from this request package.");
        }
        else if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.WorkspaceReporting, StringComparison.Ordinal) &&
                 string.Equals(evaluation.SkillId, AgentSkillIds.WorkspaceDiff, StringComparison.Ordinal) &&
                 evaluation.WorkspaceMutationAllowed)
        {
            checklist.Add("Workspace mutation is limited to disposable workspace diff evidence.");
            checklist.Add("Source repository mutation is not allowed from this request package.");
        }
        else if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.WorkspacePackaging, StringComparison.Ordinal) &&
                 string.Equals(evaluation.SkillId, AgentSkillIds.WorkspacePromotionPackage, StringComparison.Ordinal) &&
                 evaluation.WorkspaceMutationAllowed)
        {
            checklist.Add("Workspace mutation is limited to promotion package evidence.");
            checklist.Add("Promotion package creation does not approve or apply source changes.");
            checklist.Add("Source repository mutation is not allowed from this request package.");
        }
        else if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.WorkspacePreparation, StringComparison.Ordinal))
        {
            checklist.Add("Workspace mutation is not allowed from this request package.");
        }
        else if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.WorkspaceValidation, StringComparison.Ordinal))
        {
            checklist.Add("Workspace validation is not allowed from this request package.");
        }
        else if (string.Equals(evaluation.RiskTier, ProjectApprovalRiskTiers.WorkspacePackaging, StringComparison.Ordinal))
        {
            checklist.Add("Workspace promotion package creation is not allowed from this request package.");
        }
    }

    private static bool IsLowRisk(string riskTier) =>
        string.Equals(riskTier, ProjectApprovalRiskTiers.ReadOnly, StringComparison.Ordinal) ||
        string.Equals(riskTier, ProjectApprovalRiskTiers.WorkspaceReporting, StringComparison.Ordinal) ||
        string.Equals(riskTier, ProjectApprovalRiskTiers.WorkspaceIntent, StringComparison.Ordinal);

    private static string BuildRequestId(AgentSkillRequestInput input)
    {
        var parts = new List<string>
        {
            "skill-request",
            input.ProjectId,
            input.AgentName,
            input.SkillId
        };

        if (!string.IsNullOrWhiteSpace(input.RunId))
            parts.Add(input.RunId);

        return Sanitize(string.Join('-', parts));
    }

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
