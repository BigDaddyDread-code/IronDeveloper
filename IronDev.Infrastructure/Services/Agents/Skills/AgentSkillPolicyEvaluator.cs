using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentSkillPolicyEvaluator : IAgentSkillPolicyEvaluator
{
    private readonly IAgentSkillRegistry _skillRegistry;
    private readonly IProjectApprovalPolicyEvaluator _policyEvaluator;

    public AgentSkillPolicyEvaluator(
        IAgentSkillRegistry skillRegistry,
        IProjectApprovalPolicyEvaluator policyEvaluator)
    {
        _skillRegistry = skillRegistry ?? throw new ArgumentNullException(nameof(skillRegistry));
        _policyEvaluator = policyEvaluator ?? throw new ArgumentNullException(nameof(policyEvaluator));
    }

    public AgentSkillPolicyEvaluation Evaluate(AgentSkillPolicyEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Policy);

        var skill = _skillRegistry.Find(request.SkillId);
        if (skill is null)
            return UnknownSkill(request.SkillId);

        var policyResult = _policyEvaluator.Evaluate(new ProjectApprovalEvaluationRequest
        {
            ProjectId = request.ProjectId,
            RiskTier = skill.RiskTier,
            ActionType = AgentSkillPolicyActionTypes.AgentSkill,
            RequestedAction = request.RequestedAction ?? skill.SkillId,
            EvidenceHash = request.EvidenceHash,
            RunId = request.RunId,
            WorkspacePath = request.WorkspacePath,
            SourceRepo = request.SourceRepo,
            Policy = request.Policy
        });

        return Combine(skill, policyResult);
    }

    private static AgentSkillPolicyEvaluation UnknownSkill(string skillId) =>
        new()
        {
            SkillId = skillId,
            Decision = ProjectApprovalDecisions.BlockedByPolicy,
            Reason = "Unknown skill. Blocked by policy.",
            RiskTier = "unknown",
            Category = "unknown",
            SkillKnown = false,
            HumanApprovalRequired = false,
            AutomaticExecutionAllowed = false,
            SkillExecutionAllowedByPolicy = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = false,
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            MatchedRuleDescription = null,
            Warnings = [$"Unknown skill '{skillId}'. Blocked by policy."]
        };

    private static AgentSkillPolicyEvaluation Combine(
        AgentSkillDefinition skill,
        ProjectApprovalEvaluationResult policyResult)
    {
        if (string.Equals(policyResult.Decision, ProjectApprovalDecisions.BlockedByPolicy, StringComparison.Ordinal))
        {
            return Build(
                skill,
                policyResult,
                ProjectApprovalDecisions.BlockedByPolicy,
                policyResult.Reason,
                humanApprovalRequired: false,
                skillExecutionAllowedByPolicy: false,
                automaticExecutionAllowed: false,
                warnings: policyResult.Warnings);
        }

        if (IsAllowedWorkspaceMutationSkill(skill, policyResult))
        {
            return Build(
                skill,
                policyResult,
                ProjectApprovalDecisions.AllowedByPolicy,
                policyResult.Reason,
                humanApprovalRequired: false,
                skillExecutionAllowedByPolicy: true,
                automaticExecutionAllowed: false,
                warnings: policyResult.Warnings,
                workspaceMutationAllowed: true);
        }

        var blockers = DescribeNonExecutableReasons(skill);
        if (skill.RequiresHumanApproval || blockers.Count > 0)
        {
            var warnings = policyResult.Warnings
                .Concat(blockers)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var reason = skill.RequiresHumanApproval
                ? "Skill requires human approval."
                : "Skill cannot be automatically executed by policy.";

            if (blockers.Count > 0)
                reason = $"{reason} {string.Join(" ", blockers)}";

            return Build(
                skill,
                policyResult,
                ProjectApprovalDecisions.ApprovalRequired,
                reason,
                humanApprovalRequired: true,
                skillExecutionAllowedByPolicy: false,
                automaticExecutionAllowed: false,
                warnings);
        }

        if (string.Equals(policyResult.Decision, ProjectApprovalDecisions.ApprovalRequired, StringComparison.Ordinal))
        {
            return Build(
                skill,
                policyResult,
                ProjectApprovalDecisions.ApprovalRequired,
                policyResult.Reason,
                humanApprovalRequired: true,
                skillExecutionAllowedByPolicy: false,
                automaticExecutionAllowed: false,
                warnings: policyResult.Warnings);
        }

        var automaticExecutionAllowed =
            policyResult.AutomaticExecutionAllowed &&
            !skill.RequiresHumanApproval &&
            IsMetadataOnlyAllowedSkill(skill);

        return Build(
            skill,
            policyResult,
            ProjectApprovalDecisions.AllowedByPolicy,
            policyResult.Reason,
            humanApprovalRequired: false,
            skillExecutionAllowedByPolicy: automaticExecutionAllowed,
            automaticExecutionAllowed,
            warnings: policyResult.Warnings);
    }

    private static AgentSkillPolicyEvaluation Build(
        AgentSkillDefinition skill,
        ProjectApprovalEvaluationResult policyResult,
        string decision,
        string reason,
        bool humanApprovalRequired,
        bool skillExecutionAllowedByPolicy,
        bool automaticExecutionAllowed,
        IReadOnlyList<string> warnings) =>
        Build(
            skill,
            policyResult,
            decision,
            reason,
            humanApprovalRequired,
            skillExecutionAllowedByPolicy,
            automaticExecutionAllowed,
            warnings,
            workspaceMutationAllowed: false);

    private static AgentSkillPolicyEvaluation Build(
        AgentSkillDefinition skill,
        ProjectApprovalEvaluationResult policyResult,
        string decision,
        string reason,
        bool humanApprovalRequired,
        bool skillExecutionAllowedByPolicy,
        bool automaticExecutionAllowed,
        IReadOnlyList<string> warnings,
        bool workspaceMutationAllowed) =>
        new()
        {
            SkillId = skill.SkillId,
            Decision = decision,
            Reason = reason,
            RiskTier = skill.RiskTier,
            Category = skill.Category,
            SkillKnown = true,
            HumanApprovalRequired = humanApprovalRequired,
            AutomaticExecutionAllowed = automaticExecutionAllowed,
            SkillExecutionAllowedByPolicy = skillExecutionAllowedByPolicy,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = workspaceMutationAllowed,
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            MatchedRuleDescription = policyResult.MatchedRuleDescription,
            Warnings = warnings
        };

    private static IReadOnlyList<string> DescribeNonExecutableReasons(AgentSkillDefinition skill)
    {
        var warnings = new List<string>();

        if (skill.CanExecuteProcess)
            warnings.Add("Skill uses process execution; process execution is not enabled by the skill policy evaluator.");

        if (skill.CanMutateWorkspace)
            warnings.Add("Skill can mutate a disposable workspace; workspace mutation is not enabled by the skill policy evaluator.");

        if (skill.CanMutateSource)
            warnings.Add("Skill can mutate source; source mutation is not enabled by the skill policy evaluator.");

        if (skill.CanWriteMemory)
            warnings.Add("Skill can write memory; memory writes are not enabled by the skill policy evaluator.");

        if (skill.CanCreateTicket)
            warnings.Add("Skill can create tickets; ticket creation is not enabled by the skill policy evaluator.");

        if (skill.CanUseExternalSystem)
            warnings.Add("Skill can use an external system; external system access is not enabled by the skill policy evaluator.");

        return warnings;
    }

    private static bool IsMetadataOnlyAllowedSkill(AgentSkillDefinition skill) =>
        !skill.CanExecuteProcess &&
        !skill.CanMutateWorkspace &&
        !skill.CanMutateSource &&
        !skill.CanWriteMemory &&
        !skill.CanCreateTicket &&
        !skill.CanUseExternalSystem;

    private static bool IsAllowedWorkspaceMutationSkill(
        AgentSkillDefinition skill,
        ProjectApprovalEvaluationResult policyResult)
    {
        var allowedPrepare =
            string.Equals(skill.SkillId, AgentSkillIds.WorkspacePrepare, StringComparison.Ordinal) &&
            string.Equals(skill.RiskTier, ProjectApprovalRiskTiers.WorkspacePreparation, StringComparison.Ordinal);
        var allowedValidate =
            string.Equals(skill.SkillId, AgentSkillIds.WorkspaceValidate, StringComparison.Ordinal) &&
            string.Equals(skill.RiskTier, ProjectApprovalRiskTiers.WorkspaceValidation, StringComparison.Ordinal) &&
            skill.CanExecuteProcess;

        return (allowedPrepare || allowedValidate) &&
            skill.CanMutateWorkspace &&
            !skill.CanMutateSource &&
            !skill.CanUseExternalSystem &&
            !skill.CanCreateTicket &&
            !skill.CanWriteMemory &&
            string.Equals(policyResult.Decision, ProjectApprovalDecisions.AllowedByPolicy, StringComparison.Ordinal);
    }
}
