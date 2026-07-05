namespace IronDev.Core.Configuration;

public static class ReleaseRootSafetyGate
{
    public const string BoundaryStatement =
        "Root safety is a release precondition. It is not evidence, approval, policy satisfaction, source safety, execution authority, release readiness, and not permission to mutate.";

    private const string ConfigureDedicatedRootAction =
        "Configure a dedicated local root outside the source repository, user home, raw temp root, filesystem root, and disposable workspace.";

    private const string RunRootSafetyAction =
        "Run the root safety gate before any command writes, cleans, executes, records evidence, applies source, or treats artifacts as release evidence.";

    public static readonly IReadOnlyList<LocalRootKind> RequiredReleaseRootKinds =
    [
        LocalRootKind.LogsRoot,
        LocalRootKind.EvidenceRoot,
        LocalRootKind.WorkspaceRoot,
        LocalRootKind.DisposableWorkspaceRoot,
        LocalRootKind.SandboxRepositoryPath,
        LocalRootKind.CanaryMeasurementRoot,
        LocalRootKind.BatchMapEvidenceRoot,
        LocalRootKind.SmokeArtifactRoot
    ];

    public static ReleaseRootSafetyReport Evaluate(ReleaseRootSafetyRequest? request)
    {
        if (request is null)
        {
            return new ReleaseRootSafetyReport(
                ReleaseRootSafetyStatus.Blocked,
                [
                    new ReleaseRootSafetyRootResult(
                        LocalRootKind.WorkspaceRoot,
                        "ReleaseRootSafetyRequest",
                        ReleaseRootSafetyStatus.Blocked,
                        RedactedConfigSummaryService.NotConfiguredValue,
                        "UnsafeRootPolicyMissing",
                        "Root safety request is missing.",
                        RunRootSafetyAction)
                ],
                BoundaryStatement);
        }

        var roots = request.Roots ?? [];
        if (!request.Evaluate)
        {
            var unevaluated = roots
                .Select(root => new ReleaseRootSafetyRootResult(
                    root.Kind,
                    SafeConfigKey(root.ConfigKey, root.Kind),
                    ReleaseRootSafetyStatus.NotEvaluated,
                    RedactDisplayPath(root.ConfiguredPath, request.RepositoryRoot),
                    "UnsafeRootPolicyMissing",
                    "Root safety was not evaluated for this release preflight.",
                    RunRootSafetyAction))
                .ToArray();

            return new ReleaseRootSafetyReport(ReleaseRootSafetyStatus.NotEvaluated, unevaluated, BoundaryStatement);
        }

        var missingKindResults = RequiredReleaseRootKinds
            .Where(kind => roots.All(root => root.Kind != kind))
            .Select(kind => new ReleaseRootSafetyRootResult(
                kind,
                kind.ToString(),
                ReleaseRootSafetyStatus.Blocked,
                RedactedConfigSummaryService.NotConfiguredValue,
                "RootNotConfigured",
                $"{kind} is not present in the release root safety request.",
                ConfigureDedicatedRootAction))
            .ToArray();

        var rootSet = roots
            .Select(root => new LocalRootSafetyRequest(
                root.Kind,
                root.ConfigKey,
                root.ConfiguredPath,
                request.RepositoryRoot,
                request.EnvironmentName,
                root.Required && root.MustExist))
            .ToArray();

        var validated = LocalRootSafetyValidator.ValidateRootSet(rootSet);
        var mappedResults = validated.Results
            .Zip(roots, (result, root) => MapResult(result, root, request.RepositoryRoot))
            .Concat(missingKindResults)
            .ToArray();

        return new ReleaseRootSafetyReport(OverallStatus(mappedResults), mappedResults, BoundaryStatement);
    }

    private static ReleaseRootSafetyRootResult MapResult(
        LocalRootSafetyResult result,
        ReleaseRootSafetyRoot root,
        string repositoryRoot)
    {
        var redactedPath = RedactDisplayPath(root.ConfiguredPath ?? result.NormalizedPath, repositoryRoot);

        if (string.IsNullOrWhiteSpace(root.ConfiguredPath))
        {
            var status = root.Required ? ReleaseRootSafetyStatus.Blocked : ReleaseRootSafetyStatus.NotConfigured;
            var action = root.Required
                ? ConfigureDedicatedRootAction
                : "Configure this optional root only when the local capability is needed.";
            return new ReleaseRootSafetyRootResult(
                result.Kind,
                result.ConfigKey,
                status,
                redactedPath,
                "RootNotConfigured",
                result.Message,
                action);
        }

        if (result.IsSafe)
        {
            return new ReleaseRootSafetyRootResult(
                result.Kind,
                result.ConfigKey,
                ReleaseRootSafetyStatus.Passed,
                RedactDisplayPath(result.NormalizedPath ?? root.ConfiguredPath, repositoryRoot),
                "RootSafetyPassed",
                result.Message,
                "No root-safety action required. Continue only to the next independent release gate.");
        }

        var reasonCode = MapReasonCode(result, root, repositoryRoot);
        return new ReleaseRootSafetyRootResult(
            result.Kind,
            result.ConfigKey,
            ReleaseRootSafetyStatus.Blocked,
            RedactDisplayPath(result.NormalizedPath ?? root.ConfiguredPath, repositoryRoot),
            reasonCode,
            result.Message,
            NextSafeAction(reasonCode));
    }

    private static ReleaseRootSafetyStatus OverallStatus(IReadOnlyList<ReleaseRootSafetyRootResult> results)
    {
        if (results.Any(result => result.Status == ReleaseRootSafetyStatus.Blocked))
            return ReleaseRootSafetyStatus.Blocked;

        if (results.Any(result => result.Status == ReleaseRootSafetyStatus.NotEvaluated))
            return ReleaseRootSafetyStatus.NotEvaluated;

        if (results.Any(result => result.Status == ReleaseRootSafetyStatus.NotConfigured))
            return ReleaseRootSafetyStatus.NotConfigured;

        return ReleaseRootSafetyStatus.Passed;
    }

    private static string MapReasonCode(LocalRootSafetyResult result, ReleaseRootSafetyRoot root, string repositoryRoot)
    {
        var code = result.ReasonCode ?? string.Empty;
        return code switch
        {
            "MissingPath" or "SandboxRepoPathMissing" or "NotConfigured" => "RootNotConfigured",
            "SandboxRepositoryMissing" => "RootDoesNotResolve",
            "RelativePath" => "RootNotAbsolute",
            "InvalidPath" or "PathIsFile" => "RootDoesNotResolve",
            "PathTraversal" => "RootEscapesAllowedBase",
            "DriveRoot" => IsFilesystemRoot(result.NormalizedPath) ? "RootIsFilesystemRoot" : "RootIsDriveRoot",
            "PathContainsSymlinkOrReparsePoint" => "RootContainsReparsePoint",
            "UserHomeRoot" => "RootIsUserHome",
            "SystemRoot" => IsRawTempRoot(result.NormalizedPath) ? "RootIsRawTempRoot" : "RootIsFilesystemRoot",
            "RepositoryRoot" => "RootEqualsSourceRepo",
            "UnderRepositoryRoot" => "RootIsRepositoryChild",
            "SandboxEqualsSourceRepository" => "SandboxEqualsSourceRepo",
            "SandboxUnderSourceRepository" => "SandboxUnderSourceRepo",
            "SandboxContainsSourceRepository" => "SourceRepoUnderSandbox",
            "WorkspaceContainsEvidence" => "RootParentChildCollision",
            "EvidenceUnderWorkspace" => root.Kind == LocalRootKind.LogsRoot ? "LogsUnderDisposableWorkspace" : "EvidenceUnderDisposableWorkspace",
            "LogsUnderWorkspace" => "LogsUnderDisposableWorkspace",
            "WorkspaceUnderEvidence" => "WorkspaceUnderEvidenceRoot",
            _ when PathEquals(result.NormalizedPath, repositoryRoot) => "RootEqualsSourceRepo",
            _ => "RootDoesNotResolve"
        };
    }

    private static string NextSafeAction(string reasonCode) =>
        reasonCode switch
        {
            "RootNotConfigured" => ConfigureDedicatedRootAction,
            "RootNotAbsolute" => "Configure an absolute local path for the root.",
            "RootDoesNotResolve" => "Configure a resolvable directory path; do not point roots at files or malformed paths.",
            "RootIsRepositoryRoot" or "RootEqualsSourceRepo" => "Move the root outside the source repository before any release preflight writes artifacts.",
            "RootIsRepositoryChild" or "SandboxUnderSourceRepo" => "Move the root outside the source repository tree before any release preflight writes artifacts.",
            "RootIsFilesystemRoot" or "RootIsDriveRoot" => "Configure a dedicated child directory, not a drive or filesystem root.",
            "RootIsUserHome" => "Configure a dedicated child directory, not the user home or broad personal folders.",
            "RootIsRawTempRoot" => "Configure a dedicated child directory under temp or local app data, not the raw temp root.",
            "RootEscapesAllowedBase" => "Remove traversal segments and configure a stable dedicated root.",
            "RootContainsSymlink" or "RootContainsReparsePoint" => "Move the root away from symlink/reparse-point ancestors.",
            "RootParentChildCollision" => "Separate workspace, logs, evidence, smoke, canary, and batch-map roots so cleanup cannot destroy evidence.",
            "EvidenceUnderDisposableWorkspace" => "Move evidence roots outside disposable workspace cleanup trees.",
            "LogsUnderDisposableWorkspace" => "Move logs outside disposable workspace cleanup trees.",
            "WorkspaceUnderEvidenceRoot" => "Move workspace roots outside durable evidence/log roots.",
            "SourceRepoUnderSandbox" or "SandboxEqualsSourceRepo" => "Use a sandbox repository path that is separate from the source checkout.",
            _ => ConfigureDedicatedRootAction
        };

    private static string RedactDisplayPath(string? path, string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
            return RedactedConfigSummaryService.NotConfiguredValue;

        var candidate = path.Trim();
        try
        {
            candidate = Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return RedactedConfigSummaryService.RedactPath(candidate);
        }

        var redacted = RedactPathPrefix(candidate, repositoryRoot, "<source-repo>");
        redacted = RedactPathPrefix(redacted, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<user>");
        redacted = RedactPathPrefix(redacted, Path.GetTempPath(), "<temp>");
        return RedactedConfigSummaryService.RedactPath(redacted);
    }

    private static string RedactPathPrefix(string path, string? prefix, string replacement)
    {
        if (path.StartsWith("<", StringComparison.Ordinal))
            return path;

        if (string.IsNullOrWhiteSpace(prefix))
            return path;

        string normalizedPrefix;
        try
        {
            normalizedPrefix = Normalize(prefix);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Normalize(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
        if (string.Equals(normalizedPath, normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            return replacement;

        var prefixed = normalizedPrefix + Path.DirectorySeparatorChar;
        if (!normalizedPath.StartsWith(prefixed, StringComparison.OrdinalIgnoreCase))
            return path;

        return replacement + Path.DirectorySeparatorChar + normalizedPath[prefixed.Length..];
    }

    private static string SafeConfigKey(string? configKey, LocalRootKind kind) =>
        string.IsNullOrWhiteSpace(configKey) ? kind.ToString() : configKey.Trim();

    private static bool IsRawTempRoot(string? path) =>
        !string.IsNullOrWhiteSpace(path) && PathEquals(path, Path.GetTempPath());

    private static bool IsFilesystemRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
            return false;

        var normalizedRoot = Normalize(root);
        return normalizedRoot is "/" or "\\";
    }

    private static bool PathEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
