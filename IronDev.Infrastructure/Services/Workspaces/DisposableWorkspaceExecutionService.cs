using System.Diagnostics;
using System.Text;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceExecutionService : IDisposableWorkspaceExecutionService
{
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;

    public DisposableWorkspaceExecutionService(IRunStore runs, IRunEventStore events)
    {
        _runs = runs;
        _events = events;
    }

    public async Task<DisposableWorkspaceRunResult> RunAsync(
        DisposableWorkspaceRunRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
            throw new ArgumentException("Run id is required.", nameof(request));

        var sourcePath = Path.GetFullPath(request.SourcePath);
        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException($"Disposable workspace source path not found: {sourcePath}");

        var workspaceRoot = Path.GetFullPath(request.WorkspaceRoot);
        Directory.CreateDirectory(workspaceRoot);
        var workspacePath = Path.Combine(workspaceRoot, request.RunId);
        if (Directory.Exists(workspacePath))
            Directory.Delete(workspacePath, recursive: true);

        var commandResults = new List<DisposableWorkspaceCommandResult>();
        var usedGitWorktree = false;
        DisposableWorkspaceRunResult? result = null;

        await _runs.TransitionAsync(new RunStateTransition
        {
            RunId = request.RunId,
            State = RunLifecycleState.Running,
            Summary = "Disposable workspace execution started."
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            usedGitWorktree = await TryCreateGitWorktreeAsync(sourcePath, workspacePath, cancellationToken).ConfigureAwait(false);
            if (!usedGitWorktree)
                CopyDirectory(sourcePath, workspacePath);

            await PublishAsync(request.RunId, "DisposableWorkspaceCreated", $"Disposable workspace created at {workspacePath}.", new Dictionary<string, string>
            {
                ["workspacePath"] = workspacePath,
                ["usedGitWorktree"] = usedGitWorktree.ToString().ToLowerInvariant()
            }, cancellationToken).ConfigureAwait(false);

            foreach (var command in request.Commands)
            {
                var commandResult = await RunCommandAsync(request.RunId, workspacePath, command, cancellationToken).ConfigureAwait(false);
                commandResults.Add(commandResult);
                if (commandResult.ExitCode != 0)
                    throw new InvalidOperationException($"Disposable command '{commandResult.DisplayName}' failed with exit code {commandResult.ExitCode}.");
            }

            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = request.RunId,
                State = RunLifecycleState.Completed,
                Summary = "Disposable workspace execution completed."
            }, cancellationToken).ConfigureAwait(false);
            await PublishAsync(request.RunId, "RunCompleted", "Disposable workspace execution completed.", new Dictionary<string, string>
            {
                ["status"] = RunLifecycleState.Completed.ToString()
            }, cancellationToken).ConfigureAwait(false);

            result = new DisposableWorkspaceRunResult
            {
                RunId = request.RunId,
                WorkspacePath = workspacePath,
                Succeeded = true,
                UsedGitWorktree = usedGitWorktree,
                CleanedUp = false,
                Summary = "Disposable workspace execution completed.",
                Commands = commandResults
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = request.RunId,
                State = RunLifecycleState.Failed,
                Summary = "Disposable workspace execution failed.",
                FailureReason = ex.Message
            }, cancellationToken).ConfigureAwait(false);
            await PublishAsync(request.RunId, "RunFailed", ex.Message, new Dictionary<string, string>
            {
                ["status"] = RunLifecycleState.Failed.ToString(),
                ["failureReason"] = ex.Message
            }, cancellationToken).ConfigureAwait(false);

            result = new DisposableWorkspaceRunResult
            {
                RunId = request.RunId,
                WorkspacePath = workspacePath,
                Succeeded = false,
                UsedGitWorktree = usedGitWorktree,
                CleanedUp = false,
                Summary = ex.Message,
                Commands = commandResults
            };
        }
        finally
        {
            var cleanedUp = await CleanupWorkspaceAsync(sourcePath, workspacePath, usedGitWorktree, CancellationToken.None).ConfigureAwait(false);
            await PublishAsync(request.RunId, "DisposableWorkspaceCleaned", cleanedUp
                ? "Disposable workspace cleaned up."
                : "Disposable workspace cleanup did not complete.",
                new Dictionary<string, string>
                {
                    ["workspacePath"] = workspacePath,
                    ["cleanedUp"] = cleanedUp.ToString().ToLowerInvariant()
                },
                CancellationToken.None).ConfigureAwait(false);
            if (result is not null)
                result = result with { CleanedUp = cleanedUp };
        }

        return result ?? new DisposableWorkspaceRunResult
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            Succeeded = false,
            UsedGitWorktree = usedGitWorktree,
            CleanedUp = !Directory.Exists(workspacePath),
            Summary = "Disposable workspace execution did not produce a result.",
            Commands = commandResults
        };
    }

    private async Task<DisposableWorkspaceCommandResult> RunCommandAsync(
        string runId,
        string workspacePath,
        DisposableWorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        var displayName = string.IsNullOrWhiteSpace(command.DisplayName)
            ? command.FileName
            : command.DisplayName;
        var started = DateTimeOffset.UtcNow;
        await PublishAsync(runId, "DisposableCommandStarted", $"Started disposable command {displayName}.", new Dictionary<string, string>
        {
            ["command"] = displayName
        }, cancellationToken).ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(command.Timeout);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(command.FileName)
            {
                WorkingDirectory = workspacePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var arg in command.Arguments)
            process.StartInfo.ArgumentList.Add(arg);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

        var completed = DateTimeOffset.UtcNow;
        var result = new DisposableWorkspaceCommandResult
        {
            DisplayName = displayName,
            FileName = command.FileName,
            Arguments = command.Arguments,
            ExitCode = process.ExitCode,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString(),
            StartedUtc = started,
            CompletedUtc = completed
        };

        await PublishAsync(runId, result.ExitCode == 0 ? "DisposableCommandCompleted" : "DisposableCommandFailed", $"{displayName} exited with code {result.ExitCode}.", new Dictionary<string, string>
        {
            ["command"] = displayName,
            ["exitCode"] = result.ExitCode.ToString()
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }

    private static async Task<bool> TryCreateGitWorktreeAsync(
        string sourcePath,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(sourcePath, ".git")))
            return false;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = sourcePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.StartInfo.ArgumentList.Add("worktree");
        process.StartInfo.ArgumentList.Add("add");
        process.StartInfo.ArgumentList.Add("--detach");
        process.StartInfo.ArgumentList.Add(workspacePath);
        process.StartInfo.ArgumentList.Add("HEAD");

        try
        {
            process.Start();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 && Directory.Exists(workspacePath);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> CleanupWorkspaceAsync(
        string sourcePath,
        string workspacePath,
        bool usedGitWorktree,
        CancellationToken cancellationToken)
    {
        try
        {
            if (usedGitWorktree)
                await RemoveGitWorktreeAsync(sourcePath, workspacePath, cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(workspacePath))
                Directory.Delete(workspacePath, recursive: true);

            return !Directory.Exists(workspacePath);
        }
        catch
        {
            return false;
        }
    }

    private static async Task RemoveGitWorktreeAsync(
        string sourcePath,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = sourcePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.StartInfo.ArgumentList.Add("worktree");
        process.StartInfo.ArgumentList.Add("remove");
        process.StartInfo.ArgumentList.Add("--force");
        process.StartInfo.ArgumentList.Add(workspacePath);
        process.Start();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void CopyDirectory(string sourcePath, string workspacePath)
    {
        Directory.CreateDirectory(workspacePath);
        foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkip(directory))
                continue;

            Directory.CreateDirectory(Path.Combine(workspacePath, Path.GetRelativePath(sourcePath, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkip(file))
                continue;

            var targetPath = Path.Combine(workspacePath, Path.GetRelativePath(sourcePath, file));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath);
        }

        static bool ShouldSkip(string path)
        {
            var normalized = path.Replace('\\', '/');
            return normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.EndsWith("/.git", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase);
        }
    }

    private Task PublishAsync(
        string runId,
        string eventType,
        string message,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken) =>
        _events.PublishAsync(new RunEventDto
        {
            RunId = runId,
            EventType = eventType,
            Message = message,
            Payload = payload
        }, cancellationToken);
}
