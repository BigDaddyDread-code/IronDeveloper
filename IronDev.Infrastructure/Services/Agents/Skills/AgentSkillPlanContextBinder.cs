using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentSkillPlanContextBinder : IAgentSkillPlanContextBinder
{
    private static readonly ISet<string> AllowedStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AgentSkillPlanStepStatuses.Planned,
            AgentSkillPlanStepStatuses.Ready,
            AgentSkillPlanStepStatuses.InProgress,
            AgentSkillPlanStepStatuses.Satisfied,
            AgentSkillPlanStepStatuses.Blocked,
            AgentSkillPlanStepStatuses.Failed,
            AgentSkillPlanStepStatuses.Skipped,
            AgentSkillPlanStepStatuses.Unknown
        };

    public AgentSkillPlanContext Bind(AgentSkillPlanContextBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SkillId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestedAction);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Purpose);

        var warnings = new List<string>();
        var planId = request.PlanId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(planId))
        {
            warnings.Add("No plan context was provided.");
            return BuildContext(request, planId, null, [], warnings, available: false);
        }

        var currentStep = request.Steps.FirstOrDefault(step =>
            !string.IsNullOrWhiteSpace(request.CurrentStepId) &&
            string.Equals(step.StepId, request.CurrentStepId, StringComparison.Ordinal));

        if (currentStep is null)
            warnings.Add("Plan step was not provided.");
        else if (!string.Equals(currentStep.IntendedSkillId, request.SkillId, StringComparison.Ordinal))
            warnings.Add("Plan step intended skill does not match requested skill.");

        foreach (var step in request.Steps)
        {
            if (!AllowedStatuses.Contains(step.Status))
                warnings.Add($"Plan step '{step.StepId}' has unknown status '{step.Status}'.");
        }

        var orderedSteps = OrderSteps(request.Steps, currentStep).ToArray();
        return BuildContext(request, planId, currentStep, orderedSteps, warnings, available: true);
    }

    private static IEnumerable<AgentSkillPlanContextStep> OrderSteps(
        IReadOnlyList<AgentSkillPlanContextStep> steps,
        AgentSkillPlanContextStep? currentStep)
    {
        var dependencyIds = currentStep?.DependsOnStepIds ?? [];
        return steps
            .OrderBy(step => currentStep is not null && string.Equals(step.StepId, currentStep.StepId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(step => dependencyIds.Contains(step.StepId, StringComparer.Ordinal) ? 0 : 1)
            .ThenBy(step => step.StepId, StringComparer.Ordinal);
    }

    private static AgentSkillPlanContext BuildContext(
        AgentSkillPlanContextBindingRequest request,
        string planId,
        AgentSkillPlanContextStep? currentStep,
        IReadOnlyList<AgentSkillPlanContextStep> steps,
        IEnumerable<string> warnings,
        bool available)
    {
        var evidencePaths = AgentSkillPlanContextEvidence.Merge(
            request.EvidencePaths,
            steps.SelectMany(step => step.EvidencePaths));
        var dependencyStepIds = currentStep?.DependsOnStepIds ?? [];
        var rationale = BuildRationale(request, currentStep);

        return new AgentSkillPlanContext
        {
            PlanContextAvailable = available,
            BindingId = BuildBindingId(request, planId),
            ProjectId = request.ProjectId,
            SkillId = request.SkillId,
            PlanId = planId,
            CurrentStepId = currentStep?.StepId ?? request.CurrentStepId,
            CurrentStepTitle = currentStep?.Title,
            RequestedAction = request.RequestedAction,
            Rationale = rationale,
            Steps = steps,
            DependencyStepIds = dependencyStepIds,
            EvidencePaths = evidencePaths,
            Warnings = warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Blockers = [],
            CanApprove = false,
            CanExecute = false,
            CanMutateSource = false,
            CanMutateWorkspace = false,
            CanWriteMemory = false,
            CanCreateTicket = false,
            CanUseExternalSystem = false,
            CanChangePolicy = false
        };
    }

    private static string BuildRationale(
        AgentSkillPlanContextBindingRequest request,
        AgentSkillPlanContextStep? currentStep)
    {
        if (currentStep is not null)
            return $"Plan step '{currentStep.StepId}' requested skill '{request.SkillId}' for action '{request.RequestedAction}'.";

        return $"Plan '{request.PlanId}' requested skill '{request.SkillId}' for action '{request.RequestedAction}'.";
    }

    private static string BuildBindingId(
        AgentSkillPlanContextBindingRequest request,
        string planId)
    {
        var raw = string.Join('|',
            request.ProjectId,
            request.SkillId,
            request.RequestedAction,
            request.Purpose,
            planId,
            request.CurrentStepId ?? string.Empty);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..12].ToLowerInvariant();
        return Sanitize($"skill-plan-context-{request.ProjectId}-{request.SkillId}-{hash}");
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
