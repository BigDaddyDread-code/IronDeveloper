using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

internal static class AgentSkillMemoryContextEvidence
{
    public const string AuthorityDowngradedWarning =
        "Memory context claimed authority and was downgraded to evidence only.";

    public static AgentSkillMemoryContext? SanitizeEvidenceOnly(AgentSkillMemoryContext? context)
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
            CanUseExternalSystem = false
        };
    }

    public static IReadOnlyList<string> EvidencePaths(AgentSkillMemoryContext? context)
    {
        if (context is null)
            return [];

        return Merge(
            context.EvidencePaths,
            context.Items.SelectMany(item => item.EvidencePaths));
    }

    public static IReadOnlyList<string> Warnings(AgentSkillMemoryContext? context)
    {
        if (context is null)
            return [];

        return Merge(
            context.Warnings,
            context.Items.SelectMany(item => item.Warnings));
    }

    public static IReadOnlyList<string> Merge(params IEnumerable<string>[] values) =>
        values
            .SelectMany(value => value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool ClaimsAuthority(AgentSkillMemoryContext context) =>
        context.CanApprove ||
        context.CanExecute ||
        context.CanMutateSource ||
        context.CanMutateWorkspace ||
        context.CanWriteMemory ||
        context.CanCreateTicket ||
        context.CanUseExternalSystem;
}
