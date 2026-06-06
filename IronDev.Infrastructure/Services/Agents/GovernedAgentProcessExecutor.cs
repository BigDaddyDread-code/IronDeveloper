using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using IronDev.Core.Agents;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class GovernedAgentProcessExecutor : IGovernedAgentProcessExecutor
{
    private const string DefaultEvidenceRoot = "IronDev.AgentProcessEvidence";
    private const string DefaultPolicyDecision = "allowed";
    private static readonly HashSet<string> AllowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell",
        "powershell.exe",
        "dotnet",
        "dotnet.exe"
    };

    private static readonly Regex NonIdSafeChars = new("[^a-zA-Z0-9._-]", RegexOptions.Compiled);

    public async Task<GovernedAgentProcessResult> ExecuteAsync(
        GovernedAgentProcessRequest request,
        CancellationToken ct = default)
    {
        ValidateRequest(request);

        var command = BuildCommand(request.FileName, request.Arguments);
        var started = DateTimeOffset.UtcNow;
        var processStart = CreateProcessStartInfo(request.FileName, request.Arguments, request.WorkingDirectory);

        using var process = Process.Start(processStart) ?? throw new InvalidOperationException("Failed to start governed process execution.");

        var timeout = request.Timeout ?? TimeSpan.FromSeconds(GetTimeoutSeconds());
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var stdout = string.Empty;
        var stderr = string.Empty;
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Intentionally ignore cleanup failures.
            }

            var timeoutSeconds = (int)Math.Ceiling(timeout.TotalSeconds);
            stderr = $"{request.FileName} subprocess timed out after {timeoutSeconds}s and was killed.";
        }

        var evidencePaths = await WriteEvidenceAsync(
            request,
            processStart.FileName,
            command,
            started,
            DateTimeOffset.UtcNow,
            stdout,
            stderr).ConfigureAwait(false);

        var completed = DateTimeOffset.UtcNow;
        return new GovernedAgentProcessResult
        {
            ToolCallId = request.ToolCallId,
            Command = command,
            ExitCode = timedOut ? -1 : process.ExitCode,
            Stdout = stdout,
            Stderr = stderr,
            TimedOut = timedOut,
            EvidencePaths = evidencePaths,
            DurationMs = (long)(completed - started).TotalMilliseconds
        };
    }

    private static int GetTimeoutSeconds() =>
        int.TryParse(Environment.GetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS"), out var parsed)
            ? parsed
            : 300;

    private static void ValidateRequest(GovernedAgentProcessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ToolCallId))
            throw new InvalidOperationException("Governed process request is missing ToolCallId.");

        if (string.IsNullOrWhiteSpace(request.FileName))
            throw new InvalidOperationException("Governed process request is missing file name.");

        var executable = Path.GetFileName(request.FileName);
        if (!AllowedExecutables.Contains(executable))
            throw new InvalidOperationException($"Governed process execution rejected for '{executable}'. The command is not on the allow-list.");

        if (request.Arguments.Count == 0)
            throw new InvalidOperationException($"Governed process execution for '{executable}' requires arguments.");
    }

    private static ProcessStartInfo CreateProcessStartInfo(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        return info;
    }

    private static string BuildCommand(string fileName, IReadOnlyList<string> arguments) =>
        $"{fileName} {string.Join(" ", arguments.Select(QuoteIfNeeded))}".Trim();

    private static string QuoteIfNeeded(string value) =>
        string.IsNullOrWhiteSpace(value) ? "\"\"" : value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private async Task<IReadOnlyList<string>> WriteEvidenceAsync(
        GovernedAgentProcessRequest request,
        string fileName,
        string command,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string stdout,
        string stderr)
    {
        var evidenceRoot = ResolveEvidenceRoot(request.EvidenceRoot);
        var evidenceSessionRoot = Path.Combine(evidenceRoot, SanitizeForFilePath(
            string.IsNullOrWhiteSpace(request.TraceId)
                ? request.ToolCallId
                : request.TraceId));
        Directory.CreateDirectory(evidenceSessionRoot);

        var commandLabel = string.IsNullOrWhiteSpace(request.Purpose)
            ? $"{fileName}-{startedAtUtc:yyyyMMddHHmmss}"
            : SanitizeForFilePath(request.Purpose);

        var stdoutPath = Path.Combine(evidenceSessionRoot, $"{commandLabel}.stdout.log");
        var stderrPath = Path.Combine(evidenceSessionRoot, $"{commandLabel}.stderr.log");
        await File.WriteAllTextAsync(stdoutPath, stdout).ConfigureAwait(false);
        await File.WriteAllTextAsync(stderrPath, stderr).ConfigureAwait(false);

        var metadata = new StringBuilder();
        metadata.AppendLine($"tool_call_id={request.ToolCallId}");
        metadata.AppendLine($"command={command}");
        metadata.AppendLine($"purpose={request.Purpose}");
        metadata.AppendLine($"default_policy={DefaultPolicyDecision}");
        metadata.AppendLine($"started_utc={startedAtUtc:O}");
        metadata.AppendLine($"completed_utc={completedAtUtc:O}");
        metadata.AppendLine($"duration_ms={(long)(completedAtUtc - startedAtUtc).TotalMilliseconds}");

        foreach (var item in request.EvidenceMetadata)
            metadata.AppendLine($"{item.Key}={item.Value}");

        var metadataPath = Path.Combine(evidenceSessionRoot, $"{commandLabel}.metadata.txt");
        await File.WriteAllTextAsync(metadataPath, metadata.ToString()).ConfigureAwait(false);

        return [stdoutPath, stderrPath, metadataPath];
    }

    private static string ResolveEvidenceRoot(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var root = Path.GetTempPath();
        var evidenceRoot = Path.Combine(root, DefaultEvidenceRoot);
        Directory.CreateDirectory(evidenceRoot);
        return evidenceRoot;
    }

    private static string SanitizeForFilePath(string value) =>
        NonIdSafeChars.Replace(value, "_").Trim('_');
}
