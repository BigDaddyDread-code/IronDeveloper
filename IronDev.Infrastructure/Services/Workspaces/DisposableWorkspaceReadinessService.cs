using System.ComponentModel;
using System.Diagnostics;

using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceReadinessService : IDisposableWorkspaceReadinessService
{
    private const string Summary = "Disposable workspace readiness check completed.";

    public async Task<DisposableWorkspaceReadinessResult> CheckAsync(
        DisposableWorkspaceReadinessRequest request,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<DisposableWorkspaceReadinessCheck>();
        var errors = new List<string>();
        var warnings = new List<string>();

        var sourceRepo = NormalizePath(request.SourceRepo);
        var workspaceRoot = NormalizePath(request.WorkspaceRoot);
        var workspacePath = NormalizePath(Path.Combine(workspaceRoot, request.RunId));

        var sourceRepoExists = Directory.Exists(sourceRepo);
        var workspaceRootExists = Directory.Exists(workspaceRoot);
        var workspacePathExists = Directory.Exists(workspacePath);
        var workspacePathNonEmpty = workspacePathExists && Directory.EnumerateFileSystemEntries(workspacePath).Any();

        var workspaceRootSameAsSourceRepo = PathsEqual(workspaceRoot, sourceRepo);
        var workspaceRootUnderGitDirectory = IsSameOrInside(Path.Combine(sourceRepo, ".git"), workspaceRoot);
        var workspacePathEscapedWorkspaceRoot = !IsSameOrInside(workspaceRoot, workspacePath) || PathsEqual(workspaceRoot, workspacePath);
        var isInsideSourceRepo = sourceRepoExists && IsSameOrInside(sourceRepo, workspacePath);

        AddCheck(
            checks,
            errors,
            "source_repo_exists",
            sourceRepoExists,
            "Source repository exists.",
            $"Source repository does not exist: {sourceRepo}",
            "failed");

        AddCheck(
            checks,
            errors,
            "workspace_root_exists",
            workspaceRootExists,
            "Workspace root exists.",
            $"Workspace root does not exist: {workspaceRoot}",
            "failed");

        AddCheck(
            checks,
            errors,
            "workspace_root_not_source_repo",
            !workspaceRootSameAsSourceRepo,
            "Workspace root is separate from the source repository.",
            "Workspace root must not be the same directory as the source repository.",
            "blocked");

        AddCheck(
            checks,
            errors,
            "workspace_root_not_under_git",
            !workspaceRootUnderGitDirectory,
            "Workspace root is not under the source repository .git directory.",
            "Workspace root must not be under the source repository .git directory.",
            "blocked");

        AddCheck(
            checks,
            errors,
            "workspace_path_under_workspace_root",
            !workspacePathEscapedWorkspaceRoot,
            "Workspace path stays under the workspace root.",
            "Workspace path escaped the workspace root.",
            "blocked");

        AddCheck(
            checks,
            errors,
            "workspace_path_not_inside_source_repo",
            !isInsideSourceRepo,
            "Workspace path is isolated from the source repository.",
            "Workspace path must not be inside the source repository.",
            "blocked");

        AddCheck(
            checks,
            errors,
            "workspace_path_empty_or_absent",
            !workspacePathNonEmpty,
            workspacePathExists
                ? "Workspace path already exists and is empty."
                : "Workspace path does not exist yet.",
            "Workspace path already exists and is not empty.",
            "blocked");

        var sourceRepoIsGitRepo = false;
        var gitStatusClean = false;
        if (sourceRepoExists)
        {
            var gitRepo = await RunGitAsync(sourceRepo, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
            sourceRepoIsGitRepo = gitRepo.ExitCode == 0 &&
                string.Equals(gitRepo.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase);

            AddCheck(
                checks,
                errors,
                "source_repo_is_git_repo",
                sourceRepoIsGitRepo,
                "Source repository is a git work tree.",
                "Source repository is not a readable git work tree.",
                "failed");

            if (sourceRepoIsGitRepo)
            {
                var gitStatus = await RunGitAsync(sourceRepo, ["status", "--porcelain"], cancellationToken);
                if (gitStatus.ExitCode == 0)
                {
                    gitStatusClean = string.IsNullOrWhiteSpace(gitStatus.StandardOutput);
                    AddCheck(
                        checks,
                        errors,
                        "source_repo_git_status_clean",
                        gitStatusClean,
                        "Source repository git status is clean.",
                        "Source repository has uncommitted or untracked changes.",
                        "blocked");
                }
                else
                {
                    AddCheck(
                        checks,
                        errors,
                        "source_repo_git_status_readable",
                        false,
                        "Source repository git status is readable.",
                        string.IsNullOrWhiteSpace(gitStatus.StandardError)
                            ? "Source repository git status could not be read."
                            : $"Source repository git status could not be read: {gitStatus.StandardError.Trim()}",
                        "failed");
                }
            }
        }

        var canCreateWorkspaceDirectory = false;
        if (workspaceRootExists &&
            !workspaceRootSameAsSourceRepo &&
            !workspaceRootUnderGitDirectory &&
            !workspacePathEscapedWorkspaceRoot &&
            !isInsideSourceRepo &&
            !workspacePathNonEmpty)
        {
            canCreateWorkspaceDirectory = ProbeWorkspaceRoot(workspaceRoot, errors, checks);
        }
        else
        {
            checks.Add(new DisposableWorkspaceReadinessCheck
            {
                Name = "workspace_directory_creation_probe",
                Status = "blocked",
                Message = "Workspace directory creation probe was skipped because an earlier path safety check failed."
            });
        }

        var hasBlockedCheck = checks.Any(check => string.Equals(check.Status, "blocked", StringComparison.OrdinalIgnoreCase));
        var hasFailedCheck = checks.Any(check => string.Equals(check.Status, "failed", StringComparison.OrdinalIgnoreCase));
        var status = hasBlockedCheck ? "blocked" : hasFailedCheck ? "failed" : "succeeded";
        var ready = string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase);

        var data = new DisposableWorkspaceReadinessData
        {
            RunId = request.RunId,
            SourceRepo = sourceRepo,
            WorkspaceRoot = workspaceRoot,
            WorkspacePath = workspacePath,
            SourceRepoExists = sourceRepoExists,
            WorkspaceRootExists = workspaceRootExists,
            WorkspacePathExists = workspacePathExists,
            IsInsideSourceRepo = isInsideSourceRepo,
            GitStatusClean = gitStatusClean,
            CanCreateWorkspaceDirectory = canCreateWorkspaceDirectory,
            Checks = checks,
            Ready = ready,
            SourceRepoIsGitRepo = sourceRepoIsGitRepo,
            WorkspaceRootSameAsSourceRepo = workspaceRootSameAsSourceRepo,
            WorkspaceRootUnderGitDirectory = workspaceRootUnderGitDirectory,
            WorkspacePathEscapedWorkspaceRoot = workspacePathEscapedWorkspaceRoot
        };

        return new DisposableWorkspaceReadinessResult
        {
            Status = status,
            Summary = Summary,
            ExitCode = ready ? 0 : 1,
            Data = data,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static bool ProbeWorkspaceRoot(
        string workspaceRoot,
        List<string> errors,
        List<DisposableWorkspaceReadinessCheck> checks)
    {
        var probePath = Path.Combine(workspaceRoot, $".irondev-readiness-probe-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(probePath);
            Directory.Delete(probePath, recursive: true);
            checks.Add(new DisposableWorkspaceReadinessCheck
            {
                Name = "workspace_directory_creation_probe",
                Status = "passed",
                Message = "Workspace root allowed creation and cleanup of a temporary probe directory."
            });
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            var message = $"Workspace root did not allow a temporary probe directory: {ex.Message}";
            errors.Add(message);
            checks.Add(new DisposableWorkspaceReadinessCheck
            {
                Name = "workspace_directory_creation_probe",
                Status = "failed",
                Message = message
            });
            return false;
        }
    }

    private static void AddCheck(
        List<DisposableWorkspaceReadinessCheck> checks,
        List<string> errors,
        string name,
        bool passed,
        string passedMessage,
        string failedMessage,
        string failedStatus)
    {
        checks.Add(new DisposableWorkspaceReadinessCheck
        {
            Name = name,
            Status = passed ? "passed" : failedStatus,
            Message = passed ? passedMessage : failedMessage
        });

        if (!passed)
            errors.Add(failedMessage);
    }

    private static async Task<GitResult> RunGitAsync(
        string sourceRepo,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(sourceRepo);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return new GitResult(-1, string.Empty, "git process could not be started.");

            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new GitResult(process.ExitCode, await stdout, await stderr);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            return new GitResult(-1, string.Empty, ex.Message);
        }
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

    private sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);
}

