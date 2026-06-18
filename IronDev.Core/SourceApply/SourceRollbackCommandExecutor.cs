using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace IronDev.Core.SourceApply;

public static class SourceRollbackCommandExecutor
{
    public static async Task<bool> ReverseCheckAsync(SourceRollbackRequest request, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(request.SourceRepoPath, ["apply", "--reverse", "--ignore-space-change", "--check", request.PatchPath], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    public static async Task<SourceRollbackCommandResult> RollbackAsync(SourceRollbackRequest request, string outputDirectory, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(outputDirectory);
        var stdoutPath = Path.Combine(outputDirectory, "rollback.stdout.txt");
        var stderrPath = Path.Combine(outputDirectory, "rollback.stderr.txt");
        var combinedPath = Path.Combine(outputDirectory, "rollback.combined.txt");

        var check = await RunGitAsync(request.SourceRepoPath, ["apply", "--reverse", "--ignore-space-change", "--check", request.PatchPath], cancellationToken).ConfigureAwait(false);
        if (check.ExitCode != 0)
        {
            await WriteOutputsAsync(stdoutPath, stderrPath, combinedPath, check.Stdout, check.Stderr, cancellationToken).ConfigureAwait(false);
            return Result(request, check.ExitCode, false, stdoutPath, stderrPath, combinedPath, started, DateTimeOffset.UtcNow);
        }

        var rollback = await RunGitAsync(request.SourceRepoPath, ["apply", "--reverse", "--ignore-space-change", request.PatchPath], cancellationToken).ConfigureAwait(false);
        await WriteOutputsAsync(stdoutPath, stderrPath, combinedPath, check.Stdout + rollback.Stdout, check.Stderr + rollback.Stderr, cancellationToken).ConfigureAwait(false);
        return Result(request, rollback.ExitCode, rollback.ExitCode == 0, stdoutPath, stderrPath, combinedPath, started, DateTimeOffset.UtcNow);
    }

    private static SourceRollbackCommandResult Result(SourceRollbackRequest request, int exitCode, bool rolledBack, string stdoutPath, string stderrPath, string combinedPath, DateTimeOffset started, DateTimeOffset finished) =>
        new()
        {
            SourceRollbackCommandResultId = $"source_rollback_cmd_{Guid.NewGuid():N}",
            RunId = request.RunId,
            SourceRollbackRequestId = request.SourceRollbackRequestId,
            Command = "git apply --reverse --ignore-space-change --check && git apply --reverse --ignore-space-change <patch.diff>",
            ExitCode = exitCode,
            StdoutPath = stdoutPath,
            StderrPath = stderrPath,
            CombinedOutputPath = combinedPath,
            RolledBackWorkingTree = rolledBack,
            GitCommitCreated = false,
            GitPushPerformed = false,
            PullRequestCreated = false,
            StartedAtUtc = started,
            FinishedAtUtc = finished,
            Boundary = new SourceApplyBoundary { SourceRepoMutated = rolledBack, RollbackExecuted = rolledBack }
        };

    private static async Task WriteOutputsAsync(string stdoutPath, string stderrPath, string combinedPath, string stdout, string stderr, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(stdoutPath, stdout, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(stderrPath, stderr, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(combinedPath, stdout + stderr, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessResult> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var output = new StringBuilder();
        var error = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, item) => { if (item.Data is not null) output.AppendLine(item.Data); };
        process.ErrorDataReceived += (_, item) => { if (item.Data is not null) error.AppendLine(item.Data); };

        try
        {
            if (!process.Start())
                return new ProcessResult(-1, string.Empty, "could not start git");
        }
        catch (Win32Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
