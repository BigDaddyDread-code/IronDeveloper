using System.Diagnostics;
using System.Text;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceExecutionService : IDisposableWorkspaceExecutionService
{
    private static readonly HashSet<string> AllowedCommandFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe",
        "dotnet",
        "dotnet.exe"
    };

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
        if (IsSameOrUnder(sourcePath, workspaceRoot))
            throw new InvalidOperationException("Disposable workspace root must not be inside the source repository.");

        foreach (var command in request.Commands)
            ValidateAllowedCommand(command);

        Directory.CreateDirectory(workspaceRoot);
        var workspacePath = Path.Combine(workspaceRoot, request.RunId);
        workspacePath = Path.GetFullPath(workspacePath);
        if (!IsSameOrUnder(workspaceRoot, workspacePath) || string.Equals(workspaceRoot, workspacePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable workspace path must stay under the configured workspace root.");
        if (PathsOverlap(sourcePath, workspacePath))
            throw new InvalidOperationException("Disposable workspace path must not overlap the source repository.");

        if (Directory.Exists(workspacePath))
            Directory.Delete(workspacePath, recursive: true);

        var evidenceRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(request.EvidenceRoot)
            ? Path.Combine(workspaceRoot, "_evidence")
            : request.EvidenceRoot);
        Directory.CreateDirectory(evidenceRoot);
        var evidencePath = Path.Combine(evidenceRoot, request.RunId);
        evidencePath = Path.GetFullPath(evidencePath);
        if (!IsSameOrUnder(evidenceRoot, evidencePath) || string.Equals(evidenceRoot, evidencePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable evidence path must stay under the configured evidence root.");

        Directory.CreateDirectory(evidencePath);

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
                ["evidencePath"] = evidencePath,
                ["usedGitWorktree"] = usedGitWorktree.ToString().ToLowerInvariant()
            }, cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < request.Commands.Count; index++)
            {
                var command = request.Commands[index];
                var commandResult = await RunCommandAsync(request.RunId, workspacePath, evidencePath, index + 1, command, cancellationToken).ConfigureAwait(false);
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
                WorkspacePreserved = false,
                Summary = "Disposable workspace execution completed.",
                EvidencePath = evidencePath,
                Commands = commandResults
            };
        }
        catch (OperationCanceledException ex)
        {
            await _runs.TransitionAsync(new RunStateTransition
            {
                RunId = request.RunId,
                State = RunLifecycleState.Cancelled,
                Summary = "Disposable workspace execution cancelled.",
                FailureReason = ex.Message
            }, CancellationToken.None).ConfigureAwait(false);
            await PublishAsync(request.RunId, "RunCancelled", "Disposable workspace execution cancelled.", new Dictionary<string, string>
            {
                ["status"] = RunLifecycleState.Cancelled.ToString(),
                ["workspacePath"] = workspacePath,
                ["evidencePath"] = evidencePath
            }, CancellationToken.None).ConfigureAwait(false);

            result = new DisposableWorkspaceRunResult
            {
                RunId = request.RunId,
                WorkspacePath = workspacePath,
                Succeeded = false,
                UsedGitWorktree = usedGitWorktree,
                CleanedUp = false,
                WorkspacePreserved = request.PreserveWorkspaceOnCancellation,
                Cancelled = true,
                Summary = "Disposable workspace execution cancelled.",
                EvidencePath = evidencePath,
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
                ["failureReason"] = ex.Message,
                ["workspacePath"] = workspacePath,
                ["evidencePath"] = evidencePath
            }, cancellationToken).ConfigureAwait(false);

            result = new DisposableWorkspaceRunResult
            {
                RunId = request.RunId,
                WorkspacePath = workspacePath,
                Succeeded = false,
                UsedGitWorktree = usedGitWorktree,
                CleanedUp = false,
                WorkspacePreserved = request.PreserveWorkspaceOnFailure,
                Summary = ex.Message,
                EvidencePath = evidencePath,
                Commands = commandResults
            };
        }
        finally
        {
            var shouldCleanup = result is null ||
                result.Succeeded && request.CleanWorkspaceOnSuccess ||
                !result.Succeeded && !result.Cancelled && !request.PreserveWorkspaceOnFailure ||
                result.Cancelled && !request.PreserveWorkspaceOnCancellation;

            var cleanedUp = false;
            if (shouldCleanup)
            {
                cleanedUp = await CleanupWorkspaceAsync(sourcePath, workspacePath, usedGitWorktree, CancellationToken.None).ConfigureAwait(false);
                await PublishAsync(request.RunId, "DisposableWorkspaceCleaned", cleanedUp
                    ? "Disposable workspace cleaned up."
                    : "Disposable workspace cleanup did not complete.",
                    new Dictionary<string, string>
                    {
                        ["workspacePath"] = workspacePath,
                        ["evidencePath"] = evidencePath,
                        ["cleanedUp"] = cleanedUp.ToString().ToLowerInvariant()
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await PublishAsync(request.RunId, "DisposableWorkspacePreserved", "Disposable workspace preserved for failure evidence.", new Dictionary<string, string>
                {
                    ["workspacePath"] = workspacePath,
                    ["evidencePath"] = evidencePath,
                    ["cleanedUp"] = "false"
                }, CancellationToken.None).ConfigureAwait(false);
            }

            if (result is not null)
                result = result with { CleanedUp = cleanedUp, WorkspacePreserved = !cleanedUp && Directory.Exists(workspacePath) };
        }

        return result ?? new DisposableWorkspaceRunResult
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            Succeeded = false,
            UsedGitWorktree = usedGitWorktree,
            CleanedUp = !Directory.Exists(workspacePath),
            WorkspacePreserved = Directory.Exists(workspacePath),
            Summary = "Disposable workspace execution did not produce a result.",
            EvidencePath = evidencePath,
            Commands = commandResults
        };
    }

    private async Task<DisposableWorkspaceCommandResult> RunCommandAsync(
        string runId,
        string workspacePath,
        string evidencePath,
        int commandNumber,
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
        var safeName = MakeSafeFileName(displayName);
        var stdoutPath = Path.Combine(evidencePath, $"{commandNumber:00}-{safeName}.stdout.log");
        var stderrPath = Path.Combine(evidencePath, $"{commandNumber:00}-{safeName}.stderr.log");
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
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var completed = DateTimeOffset.UtcNow;
        var exitCode = timedOut ? -1 : process.ExitCode;
        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();
        if (timedOut)
            stderrText += $"{Environment.NewLine}Command timed out after {command.Timeout}.";

        await File.WriteAllTextAsync(stdoutPath, stdoutText, CancellationToken.None).ConfigureAwait(false);
        await File.WriteAllTextAsync(stderrPath, stderrText, CancellationToken.None).ConfigureAwait(false);
        var result = new DisposableWorkspaceCommandResult
        {
            DisplayName = displayName,
            FileName = command.FileName,
            Arguments = command.Arguments,
            ExitCode = exitCode,
            StandardOutput = stdoutText,
            StandardError = stderrText,
            StandardOutputPath = stdoutPath,
            StandardErrorPath = stderrPath,
            StartedUtc = started,
            CompletedUtc = completed,
            DurationMs = (long)(completed - started).TotalMilliseconds,
            TimedOut = timedOut
        };

        await PublishAsync(runId, result.ExitCode == 0 ? "DisposableCommandCompleted" : "DisposableCommandFailed", $"{displayName} exited with code {result.ExitCode}.", new Dictionary<string, string>
        {
            ["command"] = displayName,
            ["fileName"] = command.FileName,
            ["exitCode"] = result.ExitCode.ToString(),
            ["durationMs"] = result.DurationMs.ToString(),
            ["timedOut"] = result.TimedOut.ToString().ToLowerInvariant(),
            ["stdoutPath"] = stdoutPath,
            ["stderrPath"] = stderrPath
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }

    private static void ValidateAllowedCommand(DisposableWorkspaceCommand command)
    {
        var fileName = Path.GetFileName(command.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || !AllowedCommandFileNames.Contains(fileName))
            throw new InvalidOperationException($"Disposable command '{command.FileName}' is not allow-listed.");
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        var safe = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "command" : safe;
    }

    private static bool IsSameOrUnder(string parent, string child)
    {
        var parentFull = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var childFull = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return childFull.StartsWith(parentFull, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsOverlap(string first, string second) =>
        IsSameOrUnder(first, second) || IsSameOrUnder(second, first);

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
