using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

internal static class AgentSkillPlanContextEvidence
{
    public const string AuthorityDowngradedWarning =
        "Plan context claimed authority and was downgraded to evidence only.";

    public static AgentSkillPlanContext? SanitizeEvidenceOnly(AgentSkillPlanContext? context)
    {
        if (context is null)
            return null;

        var warnings = context.Warnings.ToList();
        if (ClaimsAuthority(context))
            warnings.Add(AuthorityDowngradedWarning);

        return context with
        {
            Warnings = warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
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

    public static IReadOnlyList<string> EvidencePaths(AgentSkillPlanContext? context)
    {
        if (context is null)
            return [];

        return Merge(
            context.EvidencePaths,
            context.Steps.SelectMany(step => step.EvidencePaths));
    }

    public static IReadOnlyList<string> Warnings(AgentSkillPlanContext? context)
    {
        if (context is null)
            return [];

        return Merge(
            context.Warnings,
            context.Blockers,
            context.Steps.SelectMany(step => step.Warnings));
    }

    public static IReadOnlyList<string> Merge(params IEnumerable<string>[] values) =>
        values
            .SelectMany(value => value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool ClaimsAuthority(AgentSkillPlanContext context) =>
        context.CanApprove ||
        context.CanExecute ||
        context.CanMutateSource ||
        context.CanMutateWorkspace ||
        context.CanWriteMemory ||
        context.CanCreateTicket ||
        context.CanUseExternalSystem ||
        context.CanChangePolicy;
}
