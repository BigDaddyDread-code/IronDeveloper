namespace IronDev.Infrastructure.Services.Agents;

internal static class AgentPlanPathResolver
{
    private const string ApprovedPlanDirectory = "tools/dogfood/test-agent-plans";

    public static string ResolveApprovedPlanPath(string repoRoot, string planPath, string agentName)
    {
        if (string.IsNullOrWhiteSpace(planPath))
            throw new InvalidOperationException($"{agentName} requires a non-empty plan path.");

        if (Path.IsPathRooted(planPath))
            throw new InvalidOperationException($"{agentName} plan_path must be relative to the approved dogfood test-plan directory.");

        var approvedRoot = EnsureTrailingSeparator(Path.GetFullPath(Path.Combine(repoRoot, ApprovedPlanDirectory)));
        var candidate = Path.GetFullPath(Path.Combine(repoRoot, planPath));

        if (!IsUnderDirectory(candidate, approvedRoot) &&
            !planPath.Contains(Path.DirectorySeparatorChar) &&
            !planPath.Contains(Path.AltDirectorySeparatorChar))
        {
            candidate = Path.GetFullPath(Path.Combine(approvedRoot, planPath));
        }

        if (!IsUnderDirectory(candidate, approvedRoot))
            throw new InvalidOperationException($"{agentName} plan_path must stay under {ApprovedPlanDirectory}.");

        if (!string.Equals(Path.GetExtension(candidate), ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{agentName} plan_path must reference an approved JSON test-plan file.");

        if (!File.Exists(candidate))
            throw new InvalidOperationException($"{agentName} plan_path does not reference an existing approved test plan.");

        return candidate;
    }

    private static bool IsUnderDirectory(string candidatePath, string approvedRoot) =>
        candidatePath.StartsWith(approvedRoot, StringComparison.OrdinalIgnoreCase);

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
