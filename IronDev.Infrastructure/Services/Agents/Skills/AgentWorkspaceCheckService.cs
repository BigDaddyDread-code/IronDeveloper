using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentWorkspaceCheckService : IAgentWorkspaceCheckService
{
    public Task<AgentWorkspaceCheckResult> CheckAsync(
        AgentWorkspaceCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sourceRepo = NormalizePath(request.SourceRepo);
        var workspacePath = NormalizePath(request.WorkspacePath);
        var workspaceParent = Directory.GetParent(workspacePath)?.FullName;

        var sourceRepoExists = Directory.Exists(sourceRepo);
        var workspacePathExists = Directory.Exists(workspacePath);
        var workspaceParentExists = workspaceParent is not null && Directory.Exists(workspaceParent);
        var workspaceUnderSource = IsSameOrInside(sourceRepo, workspacePath);
        var sourceUnderWorkspace = IsSameOrInside(workspacePath, sourceRepo);
        var workspaceUnderSourceGit = IsSameOrInside(Path.Combine(sourceRepo, ".git"), workspacePath);
        var sourceAndWorkspaceAreDistinct = !PathsEqual(sourceRepo, workspacePath) &&
            !workspaceUnderSource &&
            !sourceUnderWorkspace;
        var workspaceInsideAllowedRoot = workspaceParentExists &&
            !workspaceUnderSource &&
            !workspaceUnderSourceGit &&
            !PathsEqual(sourceRepo, workspacePath);

        var blockers = new List<string>();
        var warnings = new List<string>();

        if (!sourceRepoExists)
            blockers.Add($"Source repository does not exist: {sourceRepo}");
        if (!workspaceParentExists)
            blockers.Add($"Workspace parent directory does not exist: {workspaceParent ?? workspacePath}");
        if (workspacePathExists)
            blockers.Add($"Workspace path already exists: {workspacePath}");
        if (!sourceAndWorkspaceAreDistinct)
            blockers.Add("Source repository and workspace path must be isolated from each other.");
        if (workspaceUnderSourceGit)
            blockers.Add("Workspace path must not be under the source repository .git directory.");
        if (!workspaceInsideAllowedRoot)
            blockers.Add("Workspace path is not inside an allowed disposable workspace root.");

        var readyForPrepare = blockers.Count == 0;
        if (!readyForPrepare)
            warnings.Add("Workspace check completed but the workspace is not ready for prepare.");

        return Task.FromResult(new AgentWorkspaceCheckResult
        {
            CheckAvailable = true,
            ProjectId = request.ProjectId,
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            SourceRepoExists = sourceRepoExists,
            WorkspacePathExists = workspacePathExists,
            WorkspaceInsideAllowedRoot = workspaceInsideAllowedRoot,
            SourceAndWorkspaceAreDistinct = sourceAndWorkspaceAreDistinct,
            ReadyForPrepare = readyForPrepare,
            EvidencePaths = request.EvidencePaths,
            Warnings = warnings,
            Blockers = blockers
        });
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim());

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            TrimEndingDirectorySeparator(left),
            TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrInside(string parent, string candidate)
    {
        var normalizedParent = TrimEndingDirectorySeparator(NormalizePath(parent));
        var normalizedCandidate = TrimEndingDirectorySeparator(NormalizePath(candidate));

        return string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(
                normalizedParent + Path.AltDirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimEndingDirectorySeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
