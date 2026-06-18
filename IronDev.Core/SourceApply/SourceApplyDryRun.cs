using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace IronDev.Core.SourceApply;

public static class SourceApplyDryRun
{
    public static async Task<SourceApplyDryRunResult> RunAsync(SourceApplyDryRunPlan plan, string outputDirectory, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(outputDirectory);

        var stdoutPath = Path.Combine(outputDirectory, "dry-run.stdout.txt");
        var stderrPath = Path.Combine(outputDirectory, "dry-run.stderr.txt");
        var combinedPath = Path.Combine(outputDirectory, "dry-run.combined.txt");

        if (IsSameOrUnderPath(plan.ApplyRehearsalWorkspacePath, plan.SourceRepoPath))
        {
            var message = "Apply rehearsal workspace must be outside the source repository.";
            await WriteOutputsAsync(stdoutPath, stderrPath, combinedPath, string.Empty, message, cancellationToken).ConfigureAwait(false);
            return Result(plan, -1, false, string.Empty, stdoutPath, stderrPath, combinedPath, started, DateTimeOffset.UtcNow);
        }

        if (Directory.Exists(plan.ApplyRehearsalWorkspacePath))
            Directory.Delete(plan.ApplyRehearsalWorkspacePath, recursive: true);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(plan.ApplyRehearsalWorkspacePath)) ?? Directory.GetCurrentDirectory());

        var clone = await RunProcessAsync("git", ["clone", "--no-hardlinks", "--quiet", plan.SourceRepoPath, plan.ApplyRehearsalWorkspacePath], Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);
        if (clone.ExitCode != 0)
        {
            await WriteOutputsAsync(stdoutPath, stderrPath, combinedPath, clone.Stdout, clone.Stderr, cancellationToken).ConfigureAwait(false);
            return Result(plan, clone.ExitCode, false, string.Empty, stdoutPath, stderrPath, combinedPath, started, DateTimeOffset.UtcNow);
        }

        var checkout = await RunProcessAsync("git", ["checkout", "--detach", plan.BaseCommit], plan.ApplyRehearsalWorkspacePath, cancellationToken).ConfigureAwait(false);
        if (checkout.ExitCode != 0)
        {
            var stderr = "BaseCommitCheckoutFailed" + Environment.NewLine + checkout.Stderr;
            await WriteOutputsAsync(stdoutPath, stderrPath, combinedPath, checkout.Stdout, stderr, cancellationToken).ConfigureAwait(false);
            return Result(plan, checkout.ExitCode, false, string.Empty, stdoutPath, stderrPath, combinedPath, started, DateTimeOffset.UtcNow);
        }

        var head = await RunProcessAsync("git", ["rev-parse", "HEAD"], plan.ApplyRehearsalWorkspacePath, cancellationToken).ConfigureAwait(false);
        var rehearsalHead = head.ExitCode == 0 ? head.Stdout.Trim() : string.Empty;
        if (!string.Equals(rehearsalHead, plan.BaseCommit, StringComparison.OrdinalIgnoreCase))
        {
            var stderr = "RehearsalBaseCommitMismatch" + Environment.NewLine + $"expected: {plan.BaseCommit}" + Environment.NewLine + $"actual: {rehearsalHead}";
            await WriteOutputsAsync(stdoutPath, stderrPath, combinedPath, head.Stdout, stderr, cancellationToken).ConfigureAwait(false);
            return Result(plan, -1, false, rehearsalHead, stdoutPath, stderrPath, combinedPath, started, DateTimeOffset.UtcNow);
        }

        var check = await RunProcessAsync("git", ["apply", "--check", plan.PatchPath], plan.ApplyRehearsalWorkspacePath, cancellationToken).ConfigureAwait(false);
        if (check.ExitCode != 0)
        {
            await WriteOutputsAsync(stdoutPath, stderrPath, combinedPath, check.Stdout, check.Stderr, cancellationToken).ConfigureAwait(false);
            return Result(plan, check.ExitCode, false, rehearsalHead, stdoutPath, stderrPath, combinedPath, started, DateTimeOffset.UtcNow);
        }

        var apply = await RunProcessAsync("git", ["apply", plan.PatchPath], plan.ApplyRehearsalWorkspacePath, cancellationToken).ConfigureAwait(false);
        await WriteOutputsAsync(stdoutPath, stderrPath, combinedPath, check.Stdout + apply.Stdout, check.Stderr + apply.Stderr, cancellationToken).ConfigureAwait(false);

        return Result(plan, apply.ExitCode, apply.ExitCode == 0, rehearsalHead, stdoutPath, stderrPath, combinedPath, started, DateTimeOffset.UtcNow);
    }

    private static SourceApplyDryRunResult Result(SourceApplyDryRunPlan plan, int exitCode, bool applied, string rehearsalHead, string stdoutPath, string stderrPath, string combinedPath, DateTimeOffset started, DateTimeOffset finished) =>
        new()
        {
            SourceApplyDryRunResultId = $"source_apply_dry_run_{Guid.NewGuid():N}",
            RunId = plan.RunId,
            SourceApplyDryRunPlanId = plan.SourceApplyDryRunPlanId,
            RehearsalWorkspacePath = plan.ApplyRehearsalWorkspacePath,
            RehearsalBaseCommit = plan.BaseCommit,
            RehearsalHeadCommit = rehearsalHead,
            Command = "git apply --check && git apply (rehearsal workspace only)",
            ExitCode = exitCode,
            StdoutPath = stdoutPath,
            StderrPath = stderrPath,
            CombinedOutputPath = combinedPath,
            PatchAppliedInRehearsalWorkspace = applied,
            SourceRepoMutated = false,
            StartedAtUtc = started,
            FinishedAtUtc = finished,
            Boundary = applied ? SourceApplyBoundary.RehearsalApplied : SourceApplyBoundary.None
        };

    private static async Task WriteOutputsAsync(string stdoutPath, string stderrPath, string combinedPath, string stdout, string stderr, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(stdoutPath, stdout, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(stderrPath, stderr, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(combinedPath, stdout + stderr, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, item) => { if (item.Data is not null) output.AppendLine(item.Data); };
        process.ErrorDataReceived += (_, item) => { if (item.Data is not null) error.AppendLine(item.Data); };

        try
        {
            if (!process.Start())
                return new ProcessResult(-1, string.Empty, $"could not start process: {fileName}");
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

    private static bool IsSameOrUnderPath(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedCandidate = NormalizeForPathComparison(candidate);
        var normalizedRoot = NormalizeForPathComparison(root);
        return string.Equals(normalizedCandidate, normalizedRoot, comparison) || normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static string NormalizeForPathComparison(string path) =>
        Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
}
