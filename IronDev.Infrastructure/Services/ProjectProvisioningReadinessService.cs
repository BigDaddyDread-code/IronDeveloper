using IronDev.Core.Interfaces;
using IronDev.Core.Provisioning;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.Extensions.Logging;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// PROJECT-1/3: gathers the readiness evaluator's input from stored truth (project,
/// profile, commands) and scan evidence (filesystem, root safety, detection). All
/// judgment lives in the pure evaluator; this class only looks and reports. Detection
/// failures become warnings, never fabricated facts.
/// </summary>
public sealed class ProjectProvisioningReadinessService : IProjectProvisioningReadinessService
{
    private readonly IProjectService _projects;
    private readonly IProjectProfileService _profiles;
    private readonly IProjectProfileDetectionService _detector;
    private readonly ILogger<ProjectProvisioningReadinessService> _logger;

    public ProjectProvisioningReadinessService(
        IProjectService projects,
        IProjectProfileService profiles,
        IProjectProfileDetectionService detector,
        ILogger<ProjectProvisioningReadinessService> logger)
    {
        _projects = projects;
        _profiles = profiles;
        _detector = detector;
        _logger = logger;
    }

    public async Task<ProjectProvisioningReadiness?> EvaluateAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var repoPath = project.LocalPath?.Trim();
        var repoPathExists = !string.IsNullOrWhiteSpace(repoPath) && Directory.Exists(repoPath);
        var (isSafe, safetyDetail) = repoPathExists ? CheckRootSafety(repoPath!) : (false, string.Empty);
        var isGitRepository = repoPathExists && Directory.Exists(Path.Combine(repoPath!, ".git"));

        var storedProfile = await _profiles.GetProjectProfileAsync(projectId, cancellationToken);
        var storedBuild = await _profiles.GetDefaultCommandAsync(projectId, "Build", cancellationToken);
        var storedTest = await _profiles.GetDefaultCommandAsync(projectId, "Test", cancellationToken);

        var detectedBuild = string.Empty;
        var detectedTest = string.Empty;
        ProjectProfile? detectedProfile = null;
        var detectionFacts = new List<string>();
        var detectionWarnings = new List<string>();

        // Detection only runs against a usable, safe path — and only when something
        // is still unconfirmed; confirmed truth is never re-litigated by a scan.
        var needsDetection = storedBuild is null || storedTest is null || storedProfile is null;
        if (repoPathExists && isSafe && needsDetection)
        {
            try
            {
                var detection = await _detector.DetectAsync(repoPath!, projectId, cancellationToken);
                detectedBuild = detection.BuildCommand?.CommandText ?? string.Empty;
                detectedTest = detection.TestCommand?.CommandText ?? string.Empty;
                detectedProfile = detection.Profile;
                detectionFacts.AddRange(detection.DetectedFacts);
                detectionWarnings.AddRange(detection.Warnings);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Profile detection failed for project {ProjectId}", projectId);
                detectionWarnings.Add($"Detection failed and produced no proposals: {ex.Message}");
            }
        }

        return ProvisioningReadinessEvaluator.Evaluate(new ProvisioningEvaluationInput
        {
            ProjectId = projectId,
            RepoPath = repoPath,
            RepoPathExists = repoPathExists,
            RepoPathIsSafe = isSafe,
            RepoPathSafetyDetail = safetyDetail,
            IsGitRepository = isGitRepository,
            StoredProfile = storedProfile,
            StoredBuildCommand = storedBuild,
            StoredTestCommand = storedTest,
            DetectedBuildCommand = detectedBuild,
            DetectedTestCommand = detectedTest,
            DetectedProfile = detectedProfile,
            DetectionFacts = detectionFacts,
            DetectionWarnings = detectionWarnings
        });
    }

    /// <summary>
    /// Root safety: IronDev must never treat a drive root, a user-profile root, or a
    /// system directory as a repository. Conservative on purpose — a false "unsafe"
    /// costs a path change; a false "safe" costs someone's home directory.
    /// </summary>
    internal static (bool IsSafe, string Detail) CheckRootSafety(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? string.Empty);
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"{path} is a drive root.");
        }

        var unsafeRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.System)
            }
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)));

        foreach (var unsafeRoot in unsafeRoots)
        {
            if (string.Equals(full, unsafeRoot, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"{path} is a protected system or user-profile root.");
            }

            // A system directory's children are also off limits; the user profile's
            // children (e.g. ~/source/repos/...) are fine — only the root itself is unsafe.
            var isSystemish = !string.Equals(
                unsafeRoot,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))),
                StringComparison.OrdinalIgnoreCase);
            if (isSystemish && full.StartsWith(unsafeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"{path} is inside a protected system directory.");
            }
        }

        return (true, string.Empty);
    }
}
