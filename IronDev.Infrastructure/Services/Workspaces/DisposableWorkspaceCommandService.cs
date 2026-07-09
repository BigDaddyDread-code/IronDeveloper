using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceCommandService : IDisposableWorkspaceCommandService
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, WorkspaceCommandDefinition> CommandDefinitions =
        new Dictionary<string, WorkspaceCommandDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            // DOGFOOD-2 finding F-M: a fresh workspace copy has no restore state,
            // so validation must restore explicitly — as its own evidenced step,
            // never implicitly inside build. Without this, every normal repo
            // failed apply-validate with NETSDK1004 unless it committed restore
            // scaffolding (hidden local knowledge the product must not require).
            ["dotnet-restore"] = new("dotnet", ["restore"]),
            ["dotnet-build"] = new("dotnet", ["build", "--no-restore"]),
            ["dotnet-test"] = new("dotnet", ["test", "--no-build"])
        };

    private readonly TimeSpan _commandTimeout;

    public DisposableWorkspaceCommandService(TimeSpan? commandTimeout = null)
    {
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
    }

    public async Task<DisposableWorkspaceCommandExecutionResult> RunAsync(
        DisposableWorkspaceCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = NormalizePath(request.WorkspacePath);
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!CommandDefinitions.TryGetValue(request.CommandId, out var command))
        {
            errors.Add($"Workspace command '{request.CommandId}' is not allowlisted.");
            return Blocked(request, workspacePath, errors, warnings);
        }

        if (!Directory.Exists(workspacePath))
        {
            errors.Add($"Workspace path does not exist: {workspacePath}");
            return Failed(request, workspacePath, -1, errors, warnings);
        }

        if (PathContainsSegment(workspacePath, ".git"))
        {
            errors.Add("Workspace path must not be inside a .git directory.");
            return Blocked(request, workspacePath, errors, warnings);
        }

        var metadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        if (!File.Exists(metadataPath))
        {
            errors.Add("Workspace preparation metadata was not found. Run 'irondev workspace prepare' before executing commands.");
            return Blocked(request, workspacePath, errors, warnings);
        }

        DisposableWorkspaceMetadata? metadata;
        try
        {
            await using var metadataStream = File.OpenRead(metadataPath);
            metadata = await JsonSerializer.DeserializeAsync<DisposableWorkspaceMetadata>(
                metadataStream,
                MetadataJsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            errors.Add($"Workspace preparation metadata could not be read: {ex.Message}");
            return Blocked(request, workspacePath, errors, warnings);
        }

        if (metadata is null)
        {
            errors.Add("Workspace preparation metadata was empty.");
            return Blocked(request, workspacePath, errors, warnings);
        }

        if (!string.Equals(metadata.RunId, request.RunId, StringComparison.Ordinal))
        {
            errors.Add($"Workspace runId mismatch. Metadata runId '{metadata.RunId}' does not match requested runId '{request.RunId}'.");
            return Blocked(request, workspacePath, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(metadata.SourceRepo))
        {
            errors.Add("Workspace metadata is missing sourceRepo.");
            return Blocked(request, workspacePath, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(metadata.WorkspacePath))
        {
            errors.Add("Workspace metadata is missing workspacePath.");
            return Blocked(request, workspacePath, errors, warnings);
        }

        var metadataWorkspacePath = NormalizePath(metadata.WorkspacePath);
        if (!PathsEqual(workspacePath, metadataWorkspacePath))
        {
            errors.Add("Workspace path does not match the prepared workspace metadata.");
            return Blocked(request, workspacePath, errors, warnings);
        }

        var sourceRepo = NormalizePath(metadata.SourceRepo);
        if (PathsEqual(workspacePath, sourceRepo) || IsSameOrInside(sourceRepo, workspacePath))
        {
            errors.Add("Workspace path must be isolated from the source repository.");
            return Blocked(request, workspacePath, errors, warnings);
        }

        if (IsSameOrInside(Path.Combine(sourceRepo, ".git"), workspacePath))
        {
            errors.Add("Workspace path must not be inside the source repository .git directory.");
            return Blocked(request, workspacePath, errors, warnings);
        }

        return await RunCommandAsync(request, workspacePath, command, _commandTimeout, errors, warnings, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<DisposableWorkspaceCommandExecutionResult> RunCommandAsync(
        DisposableWorkspaceCommandRequest request,
        string workspacePath,
        WorkspaceCommandDefinition command,
        TimeSpan commandTimeout,
        List<string> errors,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var evidenceDirectory = Path.Combine(workspacePath, ".irondev", "runs", request.RunId, request.CommandId);
        var stdoutPath = Path.Combine(evidenceDirectory, "stdout.log");
        var stderrPath = Path.Combine(evidenceDirectory, "stderr.log");
        var commandMetadataPath = Path.Combine(evidenceDirectory, "command.json");
        var startedUtc = DateTimeOffset.UtcNow;
        DateTimeOffset completedUtc;
        int processExitCode;
        string stdout;
        string stderr;

        try
        {
            var startInfo = new ProcessStartInfo(command.Executable)
            {
                WorkingDirectory = workspacePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in command.Arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                errors.Add("Workspace command process could not be started.");
                return Failed(request, workspacePath, -1, errors, warnings);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(commandTimeout);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    warnings.Add($"Workspace command process kill failed after timeout: {ex.Message}");
                }

                errors.Add($"Workspace command '{request.CommandId}' timed out after {commandTimeout}.");
                return Failed(request, workspacePath, -1, errors, warnings);
            }

            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
            processExitCode = process.ExitCode;
            completedUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
            errors.Add($"Workspace command could not be started: {ex.Message}");
            return Failed(request, workspacePath, -1, errors, warnings);
        }

        try
        {
            Directory.CreateDirectory(evidenceDirectory);
            await File.WriteAllTextAsync(stdoutPath, stdout, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(stderrPath, stderr, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                commandMetadataPath,
                JsonSerializer.Serialize(
                    new WorkspaceCommandEvidence
                    {
                        RunId = request.RunId,
                        CommandId = request.CommandId,
                        WorkingDirectory = workspacePath,
                        Executable = command.Executable,
                        Arguments = command.Arguments,
                        StartedUtc = startedUtc,
                        CompletedUtc = completedUtc,
                        ExitCode = processExitCode
                    },
                    MetadataJsonOptions),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add($"Workspace command evidence could not be written: {ex.Message}");
            return Failed(request, workspacePath, processExitCode, errors, warnings);
        }

        var evidencePaths = new[] { stdoutPath, stderrPath, commandMetadataPath };
        var succeeded = processExitCode == 0;
        if (!succeeded)
            errors.Add($"Workspace command '{request.CommandId}' completed with non-zero exit code {processExitCode}.");

        var data = new DisposableWorkspaceCommandData
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            CommandId = request.CommandId,
            WorkingDirectory = workspacePath,
            ExitCode = processExitCode,
            Succeeded = succeeded,
            StdoutPath = stdoutPath,
            StderrPath = stderrPath,
            CommandMetadataPath = commandMetadataPath,
            EvidencePaths = evidencePaths,
            Errors = errors,
            Warnings = warnings
        };

        return new DisposableWorkspaceCommandExecutionResult
        {
            Status = succeeded ? "succeeded" : "failed",
            Summary = succeeded ? "Workspace command completed." : "Workspace command failed.",
            ExitCode = succeeded ? 0 : 1,
            Data = data,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static DisposableWorkspaceCommandExecutionResult Blocked(
        DisposableWorkspaceCommandRequest request,
        string workspacePath,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        return new DisposableWorkspaceCommandExecutionResult
        {
            Status = "blocked",
            Summary = "Workspace command execution was blocked.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, -1, false, errors, warnings),
            Errors = errors,
            Warnings = warnings
        };
    }

    private static DisposableWorkspaceCommandExecutionResult Failed(
        DisposableWorkspaceCommandRequest request,
        string workspacePath,
        int commandExitCode,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        return new DisposableWorkspaceCommandExecutionResult
        {
            Status = "failed",
            Summary = "Workspace command failed.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, commandExitCode, false, errors, warnings),
            Errors = errors,
            Warnings = warnings
        };
    }

    private static DisposableWorkspaceCommandData BuildData(
        DisposableWorkspaceCommandRequest request,
        string workspacePath,
        int commandExitCode,
        bool succeeded,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        return new DisposableWorkspaceCommandData
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            CommandId = request.CommandId,
            WorkingDirectory = workspacePath,
            ExitCode = commandExitCode,
            Succeeded = succeeded,
            StdoutPath = null,
            StderrPath = null,
            CommandMetadataPath = null,
            EvidencePaths = [],
            Errors = errors,
            Warnings = warnings
        };
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim());

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            TrimEndingDirectorySeparator(NormalizePath(left)),
            TrimEndingDirectorySeparator(NormalizePath(right)),
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

    private static bool PathContainsSegment(string path, string segment)
    {
        return NormalizePath(path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string TrimEndingDirectorySeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed record WorkspaceCommandDefinition(string Executable, IReadOnlyList<string> Arguments);

    private sealed record DisposableWorkspaceMetadata
    {
        public string? RunId { get; init; }
        public string? SourceRepo { get; init; }
        public string? WorkspacePath { get; init; }
    }

    private sealed record WorkspaceCommandEvidence
    {
        public required string RunId { get; init; }
        public required string CommandId { get; init; }
        public required string WorkingDirectory { get; init; }
        public required string Executable { get; init; }
        public required IReadOnlyList<string> Arguments { get; init; }
        public required DateTimeOffset StartedUtc { get; init; }
        public required DateTimeOffset CompletedUtc { get; init; }
        public required int ExitCode { get; init; }
    }
}
