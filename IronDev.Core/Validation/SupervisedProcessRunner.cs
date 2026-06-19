using System.Diagnostics;
using System.Text;

namespace IronDev.Core.Validation;

public sealed class SupervisedProcessRunner
{
    public async Task<ValidationProcessResult> RunAsync(ValidationCommandSpec spec, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(spec.StdoutPath) ?? spec.WorkingDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(spec.StderrPath) ?? spec.WorkingDirectory);

        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var stdout = string.Empty;
        var stderr = string.Empty;
        int? exitCode = null;
        var timedOut = false;
        var cancelled = false;
        var killAttempted = false;
        var killSucceeded = false;

        try
        {
            using var process = new Process
            {
                StartInfo = CreateStartInfo(spec),
                EnableRaisingEvents = true
            };

            if (!process.Start())
            {
                stderr = "Process failed to start.";
                return Finish(spec, started, stopwatch, null, false, false, false, false, stdout, stderr, ValidationFailureKind.HarnessException);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(spec.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                (killAttempted, killSucceeded) = await KillProcessTreeAsync(process).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                (killAttempted, killSucceeded) = await KillProcessTreeAsync(process).ConfigureAwait(false);
            }

            await WaitForReadersAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            stdout = CompletedText(stdoutTask);
            stderr = CompletedText(stderrTask);
            if (process.HasExited)
                exitCode = process.ExitCode;
        }
        catch (Exception ex)
        {
            stderr = ex.ToString();
            return Finish(spec, started, stopwatch, null, false, false, false, false, stdout, stderr, ValidationFailureKind.HarnessException);
        }

        var classification = ValidationFailureClassifier.Classify(spec.CommandKind, exitCode, timedOut, cancelled, stdout, stderr);
        return Finish(spec, started, stopwatch, exitCode, timedOut, cancelled, killAttempted, killSucceeded, stdout, stderr, classification);
    }

    private static ProcessStartInfo CreateStartInfo(ValidationCommandSpec spec)
    {
        var startInfo = new ProcessStartInfo(spec.Command)
        {
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in spec.Arguments)
            startInfo.ArgumentList.Add(argument);

        foreach (var item in spec.Environment)
            startInfo.Environment[item.Key] = item.Value;

        return startInfo;
    }

    private static async Task<(bool Attempted, bool Succeeded)> KillProcessTreeAsync(Process process)
    {
        if (process.HasExited)
            return (false, false);

        try
        {
            process.Kill(entireProcessTree: true);
            var exited = await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            return (true, exited is Task { IsCompleted: true } && process.HasExited);
        }
        catch
        {
            return (true, false);
        }
    }

    private static async Task WaitForReadersAsync(Task<string> stdoutTask, Task<string> stderrTask)
    {
        await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
    }

    private static string CompletedText(Task<string> task) =>
        task.IsCompletedSuccessfully ? task.Result : string.Empty;

    private static ValidationProcessResult Finish(
        ValidationCommandSpec spec,
        DateTimeOffset started,
        Stopwatch stopwatch,
        int? exitCode,
        bool timedOut,
        bool cancelled,
        bool killAttempted,
        bool killSucceeded,
        string stdout,
        string stderr,
        ValidationFailureKind classification)
    {
        stopwatch.Stop();
        File.WriteAllText(spec.StdoutPath, stdout, Encoding.UTF8);
        File.WriteAllText(spec.StderrPath, stderr, Encoding.UTF8);
        var finished = DateTimeOffset.UtcNow;
        return new ValidationProcessResult
        {
            LaneName = spec.LaneName,
            Command = spec.Command,
            Arguments = spec.Arguments,
            WorkingDirectory = spec.WorkingDirectory,
            StartedUtc = started,
            FinishedUtc = finished,
            DurationMs = stopwatch.ElapsedMilliseconds,
            ExitCode = exitCode,
            TimedOut = timedOut,
            Cancelled = cancelled,
            ProcessTreeKillAttempted = killAttempted,
            ProcessTreeKillSucceeded = killSucceeded,
            StdoutPath = spec.StdoutPath,
            StderrPath = spec.StderrPath,
            StdoutTail = Tail(stdout),
            StderrTail = Tail(stderr),
            FailureClassification = classification
        };
    }

    private static string Tail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.None);
        return string.Join(Environment.NewLine, lines.TakeLast(40)).Trim();
    }
}

public static class ValidationFailureClassifier
{
    public static ValidationFailureKind Classify(ValidationCommandKind commandKind, int? exitCode, bool timedOut, bool cancelled, string stdout, string stderr)
    {
        if (cancelled)
            return ValidationFailureKind.Cancelled;
        if (timedOut)
            return ValidationFailureKind.Timeout;
        if (exitCode == 0)
            return ValidationFailureKind.Passed;

        var combined = string.Join(Environment.NewLine, stdout, stderr);
        if (Contains(combined, "NuGet.Config") && (Contains(combined, "unauthorized access") || Contains(combined, "access is denied")))
            return ValidationFailureKind.EnvironmentAccessDenied;
        if (Contains(combined, "project.assets.json") && Contains(combined, "offline packages"))
            return ValidationFailureKind.RestoreFailed;

        return commandKind switch
        {
            ValidationCommandKind.Restore => ValidationFailureKind.RestoreFailed,
            ValidationCommandKind.Build => ValidationFailureKind.BuildFailed,
            ValidationCommandKind.Test => ValidationFailureKind.TestFailed,
            ValidationCommandKind.DiffCheck => ValidationFailureKind.DiffCheckFailed,
            _ => ValidationFailureKind.ProcessExitNonZero
        };
    }

    private static bool Contains(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
