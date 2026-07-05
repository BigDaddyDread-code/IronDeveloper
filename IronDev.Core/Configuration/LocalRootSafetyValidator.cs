namespace IronDev.Core.Configuration;

public static class LocalRootSafetyValidator
{
    public const string BoundaryStatement =
        "A safe root is a precondition for evidence. It is not evidence, approval, execution authority, or permission to mutate source.";

    private const string NextSafeAction =
        "Configure a dedicated root outside the source repository and user/system root.";

    public static LocalRootSafetyResult Validate(LocalRootSafetyRequest request)
    {
        var configKey = SafeConfigKey(request.ConfigKey, request.Kind);

        if (string.IsNullOrWhiteSpace(request.ConfiguredPath))
        {
            if (request.MustExist)
                return Unsafe(request.Kind, configKey, null, "MissingPath", "Required root path is not configured.");

            if (request.Kind == LocalRootKind.SandboxRepositoryPath)
            {
                return Safe(
                    request.Kind,
                    configKey,
                    null,
                    "SandboxRepoPathMissing",
                    "Optional sandbox repository path is not configured; re-execution must be reported as unavailable.");
            }

            return Safe(request.Kind, configKey, null, "NotConfigured", "Root is not configured.");
        }

        if (ContainsTraversalSegment(request.ConfiguredPath))
            return Unsafe(request.Kind, configKey, null, "PathTraversal", "Root path must not contain traversal segments.");

        if (!Path.IsPathFullyQualified(request.ConfiguredPath))
            return Unsafe(request.Kind, configKey, null, "RelativePath", "Root path must be absolute.");

        string normalizedPath;
        string repositoryRoot;
        try
        {
            normalizedPath = Normalize(request.ConfiguredPath);
            repositoryRoot = Normalize(request.RepositoryRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Unsafe(request.Kind, configKey, null, "InvalidPath", "Root path could not be normalized.");
        }

        if (IsRootPath(normalizedPath))
            return Unsafe(request.Kind, configKey, normalizedPath, "DriveRoot", "Root path must not be a drive or filesystem root.");

        if (File.Exists(normalizedPath))
            return Unsafe(request.Kind, configKey, normalizedPath, "PathIsFile", "Root path points to a file.");

        if (HasReparsePointInExistingAncestorChain(normalizedPath))
            return Unsafe(request.Kind, configKey, normalizedPath, "PathContainsSymlinkOrReparsePoint", "Root path must not be or live under a symlink or reparse point.");

        if (request.MustExist && !Directory.Exists(normalizedPath))
        {
            var reason = request.Kind == LocalRootKind.SandboxRepositoryPath
                ? "SandboxRepositoryMissing"
                : "MissingPath";
            return Unsafe(request.Kind, configKey, normalizedPath, reason, "Required root path does not exist.");
        }

        var broadRootReason = BroadRootReason(normalizedPath);
        if (broadRootReason is not null)
            return Unsafe(request.Kind, configKey, normalizedPath, broadRootReason, "Root path is too broad for local runtime output.");

        if (request.Kind == LocalRootKind.SandboxRepositoryPath)
        {
            if (PathsEqual(normalizedPath, repositoryRoot))
                return Unsafe(request.Kind, configKey, normalizedPath, "SandboxEqualsSourceRepository", "Sandbox repository path must not equal the source repository.");

            if (IsSameOrUnder(repositoryRoot, normalizedPath))
                return Unsafe(request.Kind, configKey, normalizedPath, "SandboxUnderSourceRepository", "Sandbox repository path must not be inside the source repository.");

            if (IsSameOrUnder(normalizedPath, repositoryRoot))
                return Unsafe(request.Kind, configKey, normalizedPath, "SandboxContainsSourceRepository", "Sandbox repository path must not contain the source repository.");
        }
        else
        {
            if (PathsEqual(normalizedPath, repositoryRoot))
                return Unsafe(request.Kind, configKey, normalizedPath, "RepositoryRoot", "Runtime root must not equal the source repository root.");

            if (IsSameOrUnder(repositoryRoot, normalizedPath))
                return Unsafe(request.Kind, configKey, normalizedPath, "UnderRepositoryRoot", "Runtime root must not be inside the source repository.");
        }

        return Safe(request.Kind, configKey, normalizedPath, null, "Root path passed local safety validation.");
    }

    public static LocalRootSafetyValidationResult ValidateRootSet(IReadOnlyList<LocalRootSafetyRequest> requests)
    {
        var results = requests.Select(Validate).ToArray();
        var rewritten = results.ToArray();

        for (var workspaceIndex = 0; workspaceIndex < rewritten.Length; workspaceIndex++)
        {
            var workspace = rewritten[workspaceIndex];
            if (!workspace.IsSafe || !IsWorkspaceKind(workspace.Kind) || string.IsNullOrWhiteSpace(workspace.NormalizedPath))
                continue;

            for (var otherIndex = 0; otherIndex < rewritten.Length; otherIndex++)
            {
                if (workspaceIndex == otherIndex)
                    continue;

                var other = rewritten[otherIndex];
                if (!other.IsSafe || !IsDurableOutputKind(other.Kind) || string.IsNullOrWhiteSpace(other.NormalizedPath))
                    continue;

                if (PathsEqual(workspace.NormalizedPath, other.NormalizedPath))
                {
                    rewritten[workspaceIndex] = Unsafe(workspace.Kind, workspace.ConfigKey, workspace.NormalizedPath, "WorkspaceContainsEvidence", "Workspace root must not equal an evidence or log root.");
                    rewritten[otherIndex] = Unsafe(other.Kind, other.ConfigKey, other.NormalizedPath, "EvidenceUnderWorkspace", "Evidence and log roots must not equal a disposable workspace root.");
                    continue;
                }

                if (IsSameOrUnder(workspace.NormalizedPath, other.NormalizedPath))
                {
                    var reason = other.Kind == LocalRootKind.LogsRoot ? "LogsUnderWorkspace" : "EvidenceUnderWorkspace";
                    rewritten[otherIndex] = Unsafe(other.Kind, other.ConfigKey, other.NormalizedPath, reason, "Evidence and log roots must not be under a disposable workspace root.");
                }

                if (IsSameOrUnder(other.NormalizedPath, workspace.NormalizedPath))
                {
                    rewritten[workspaceIndex] = Unsafe(workspace.Kind, workspace.ConfigKey, workspace.NormalizedPath, "WorkspaceUnderEvidence", "Workspace roots must not be under durable evidence or log roots.");
                }
            }
        }

        return new LocalRootSafetyValidationResult(rewritten.All(result => result.IsSafe), rewritten);
    }

    private static bool IsWorkspaceKind(LocalRootKind kind) =>
        kind is LocalRootKind.WorkspaceRoot or LocalRootKind.DisposableWorkspaceRoot;

    private static bool IsDurableOutputKind(LocalRootKind kind) =>
        kind is LocalRootKind.EvidenceRoot or LocalRootKind.LogsRoot or LocalRootKind.CanaryMeasurementRoot or LocalRootKind.BatchMapEvidenceRoot or LocalRootKind.SmokeArtifactRoot;

    private static LocalRootSafetyResult Safe(LocalRootKind kind, string configKey, string? normalizedPath, string? reasonCode, string message) =>
        new(kind, configKey, true, normalizedPath, reasonCode, message, "No root-safety action required.");

    private static LocalRootSafetyResult Unsafe(LocalRootKind kind, string configKey, string? normalizedPath, string reasonCode, string message) =>
        new(kind, configKey, false, normalizedPath, reasonCode, message, NextSafeAction);

    private static string SafeConfigKey(string? configKey, LocalRootKind kind) =>
        string.IsNullOrWhiteSpace(configKey) ? kind.ToString() : configKey.Trim();

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool ContainsTraversalSegment(string path) =>
        path.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(segment => segment == "..");

    private static bool IsRootPath(string path)
    {
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrWhiteSpace(root) && PathsEqual(root, path);
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;

    private static bool HasReparsePointInExistingAncestorChain(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null)
        {
            if (current.Exists && IsReparsePoint(current.FullName))
                return true;

            current = current.Parent;
        }

        return false;
    }

    private static string? BroadRootReason(string path)
    {
        var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userRoot) && PathsEqual(userRoot, path))
            return "UserHomeRoot";

        foreach (var special in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(userRoot, "Downloads")
        })
        {
            if (!string.IsNullOrWhiteSpace(special) && IsSameOrUnder(special, path))
                return "UserHomeRoot";
        }

        var tempRoot = Path.GetTempPath();
        if (!string.IsNullOrWhiteSpace(tempRoot) && PathsEqual(tempRoot, path))
            return "SystemRoot";

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(systemRoot) && IsSameOrUnder(systemRoot, path))
            return "SystemRoot";

        return null;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrUnder(string parent, string child)
    {
        var parentFull = Normalize(parent) + Path.DirectorySeparatorChar;
        var childFull = Normalize(child) + Path.DirectorySeparatorChar;
        return childFull.StartsWith(parentFull, StringComparison.OrdinalIgnoreCase);
    }
}
