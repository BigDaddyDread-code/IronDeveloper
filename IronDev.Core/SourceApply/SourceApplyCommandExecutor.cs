using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace IronDev.Core.SourceApply;

public static class SourceApplyCommandExecutor
{
    public static async Task<SourceApplyCommandResult> ApplyAsync(SourceApplyExecutionRequest request, string outputDirectory, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(outputDirectory);
        var stdoutPath = Path.Combine(outputDirectory, "apply.stdout.txt");
        var stderrPath = Path.Combine(outputDirectory, "apply.stderr.txt");
        var combinedPath = Path.Combine(outputDirectory, "apply.combined.txt");

        var check = await RunGitAsync(request.SourceRepoPath, ["apply", "--ignore-space-change", "--check", request.PatchPath], cancellationToken).ConfigureAwait(false);
        if (check.ExitCode != 0)
        {
            await WriteOutputsAsync(stdoutPath, stderrPath, combinedPath, check.Stdout, check.Stderr, cancellationToken).ConfigureAwait(false);
            return Result(request, check.ExitCode, false, stdoutPath, stderrPath, combinedPath, started, DateTimeOffset.UtcNow);
        }

        var apply = await RunGitAsync(request.SourceRepoPath, ["apply", "--ignore-space-change", request.PatchPath], cancellationToken).ConfigureAwait(false);
        await WriteOutputsAsync(stdoutPath, stderrPath, combinedPath, check.Stdout + apply.Stdout, check.Stderr + apply.Stderr, cancellationToken).ConfigureAwait(false);
        return Result(request, apply.ExitCode, apply.ExitCode == 0, stdoutPath, stderrPath, combinedPath, started, DateTimeOffset.UtcNow);
    }

    private static SourceApplyCommandResult Result(SourceApplyExecutionRequest request, int exitCode, bool applied, string stdoutPath, string stderrPath, string combinedPath, DateTimeOffset started, DateTimeOffset finished) =>
        new()
        {
            SourceApplyCommandResultId = $"source_apply_cmd_{Guid.NewGuid():N}",
            RunId = request.RunId,
            SourceApplyExecutionRequestId = request.SourceApplyExecutionRequestId,
            Command = "git apply --ignore-space-change --check && git apply --ignore-space-change <patch.diff>",
            ExitCode = exitCode,
            StdoutPath = stdoutPath,
            StderrPath = stderrPath,
            CombinedOutputPath = combinedPath,
            SourceAppliedToWorkingTree = applied,
            GitCommitCreated = false,
            GitPushPerformed = false,
            PullRequestCreated = false,
            StartedAtUtc = started,
            FinishedAtUtc = finished,
            Boundary = new SourceApplyBoundary { SourceRepoMutated = applied, SourceApplied = applied }
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
