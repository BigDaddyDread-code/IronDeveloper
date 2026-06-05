using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IronDev.Infrastructure.Services.Agents;

public sealed record AgentProcessRunResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut,
    string Command);

public interface IAgentProcessRunner
{
    Task<AgentProcessRunResult> RunAsync(
        string fileName,
        string[] arguments,
        string workingDirectory,
        CancellationToken ct = default);
}

public sealed class AgentProcessRunner : IAgentProcessRunner
{
    private static int GetTimeoutSeconds() =>
        int.TryParse(Environment.GetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS"), out var parsed)
            ? parsed
            : 300;

    public async Task<AgentProcessRunResult> RunAsync(
        string fileName,
        string[] arguments,
        string workingDirectory,
        CancellationToken ct = default)
    {
        var command = fileName + " " + string.Join(" ", arguments.Select(QuoteIfNeeded));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();

        var timeoutSeconds = GetTimeoutSeconds();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new AgentProcessRunResult(process.ExitCode, stdout, stderr, false, command);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout — kill the process tree
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch
            {
                // best effort cleanup
            }
            return new AgentProcessRunResult(
                -1,
                string.Empty,
                $"{fileName} subprocess timed out after {timeoutSeconds}s and was killed.",
                true,
                command);
        }
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (arg.Contains(' ') || arg.Contains('\t') || arg.Contains('"'))
        {
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }
        return arg;
    }
}
