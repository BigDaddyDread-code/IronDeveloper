using System.Diagnostics;
using System.Text;

namespace IronDev.Infrastructure.Services.Sandbox;

/// <summary>
/// Runs only the explicitly supplied absolute Docker executable. It never invokes a
/// shell and starts from an empty host environment so user/cloud/Git credentials cannot
/// leak into the container-engine client process.
/// </summary>
public sealed class DockerCommandRunner : IDockerCommandRunner
{
    public async Task<DockerCommandResult> RunAsync(
        DockerCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        var startInfo = new ProcessStartInfo(request.ExecutablePath)
        {
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment.Clear();
        foreach (var item in request.Environment.OrderBy(item => item.Key, StringComparer.Ordinal))
            startInfo.Environment[item.Key] = item.Value;
        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("The configured Docker executable did not start.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new HcsContainerRuntimeException(
                "SandboxRuntimeUnavailable",
                "The configured Windows container runtime could not be started.",
                exception);
        }

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, request.MaximumOutputCharacters);
        var stderrTask = ReadBoundedAsync(process.StandardError, request.MaximumOutputCharacters);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            if (!KillProcess(process))
                throw new HcsContainerRuntimeException(
                    "SandboxRuntimeUnavailable",
                    "The timed-out Docker client process could not be terminated within the fixed cleanup bound.");
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            throw;
        }

        if (!process.HasExited && !KillProcess(process))
            throw new HcsContainerRuntimeException(
                "SandboxRuntimeUnavailable",
                "The Docker client process did not terminate within the fixed cleanup bound.");

        BoundedText stdout;
        BoundedText stderr;
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new HcsContainerRuntimeException(
                "SandboxRuntimeUnavailable",
                "The Docker client output streams did not close within the fixed cleanup bound.",
                exception);
        }
        return new DockerCommandResult
        {
            ExitCode = timedOut ? -1 : process.ExitCode,
            StandardOutput = stdout.Text,
            StandardError = stderr.Text,
            TimedOut = timedOut,
            StandardOutputTruncated = stdout.Truncated,
            StandardErrorTruncated = stderr.Truncated,
            DurationMilliseconds = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds
        };
    }

    private static void Validate(DockerCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Path.IsPathFullyQualified(request.ExecutablePath) || !File.Exists(request.ExecutablePath))
            throw new HcsContainerRuntimeException(
                "SandboxRuntimeUnavailable",
                "The configured Docker executable must be an existing absolute path.");
        if (!string.Equals(Path.GetFileName(request.ExecutablePath), "docker.exe", StringComparison.OrdinalIgnoreCase))
            throw new HcsContainerRuntimeException(
                "SandboxRuntimeUnavailable",
                "Only the configured docker.exe container-engine client may be invoked.");
        if (!Path.IsPathFullyQualified(request.WorkingDirectory) || !Directory.Exists(request.WorkingDirectory))
            throw new HcsContainerRuntimeException(
                "SandboxRuntimeUnavailable",
                "The Docker client configuration directory is unavailable.");
        if (request.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "Docker command timeout must be positive.");
        if (request.MaximumOutputCharacters is < 1 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(request), "Docker output bound is invalid.");
        if (request.Arguments.Any(ContainsControlCharacters))
            throw new HcsContainerRuntimeException("SandboxExecutionRejected", "A Docker argument contains control characters.");
        if (request.Environment.Keys.Any(ContainsControlCharacters) || request.Environment.Values.Any(ContainsControlCharacters))
            throw new HcsContainerRuntimeException("SandboxExecutionRejected", "A Docker host-environment value contains control characters.");
    }

    private static bool ContainsControlCharacters(string value) =>
        value.Any(character => character is '\0' or '\r' or '\n');

    private static bool KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            return process.HasExited;
        }
        catch
        {
            // Container cleanup is performed independently by the runtime facade.
            return false;
        }
    }

    private static async Task<BoundedText> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 16_384));
        var buffer = new char[4_096];
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            var remaining = maximumCharacters - builder.Length;
            if (remaining > 0)
                builder.Append(buffer, 0, Math.Min(remaining, read));
            if (read > remaining)
                truncated = true;
        }

        return new BoundedText(builder.ToString(), truncated || builder.Length == maximumCharacters);
    }

    private sealed record BoundedText(string Text, bool Truncated);
}
