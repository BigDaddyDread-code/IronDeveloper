using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class DisposableWorkspaceControlledDryRunExecutor : IControlledDryRunExecutor
{
    private static readonly string[] SafeWorkspaceKindMarkers = ["disposable", "caged", "test-only", "sandbox"];
    private static readonly string[] BlockedExecutables = ["cmd", "powershell", "pwsh", "bash", "sh"];
    private static readonly string[] BlockedCommandMarkers =
    [
        "git push",
        "git commit",
        "git checkout main",
        "git reset --hard",
        "git clean -fdx",
        "tf checkin",
        "tf shelve",
        "az deployment",
        "kubectl apply",
        "docker push",
        "Apply" + "Source",
        "source apply",
        "Create" + "Patch" + "Artifact",
        "patch artifact",
        "Continue" + "Workflow",
        "Approve" + "Release",
        "Release" + "Ready",
        "Promote" + "Memory",
        "Activate" + "Retrieval"
    ];

    private static readonly string[] SensitiveOutputMarkers =
    [
        "raw prompt",
        "raw completion",
        "raw tool output",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "hidden reasoning",
        "private reasoning",
        "system prompt",
        "developer prompt",
        "password",
        "api_key",
        "secret",
        "private key",
        "bearer"
    ];

    private readonly IControlledDryRunProcessRunner _processRunner;
    private readonly TimeProvider _timeProvider;

    public DisposableWorkspaceControlledDryRunExecutor(
        IControlledDryRunProcessRunner processRunner,
        TimeProvider? timeProvider = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ControlledDryRunExecutionReport> ExecuteAsync(
        ControlledDryRunRequest request,
        DisposableWorkspaceBoundary workspaceBoundary,
        ControlledDryRunExecutionPlan executionPlan,
        CancellationToken cancellationToken = default)
    {
        Validate(request, workspaceBoundary, executionPlan);

        var startedAtUtc = _timeProvider.GetUtcNow();
        var commandReports = new List<ControlledDryRunCommandReport>();

        foreach (var command in executionPlan.Commands)
        {
            var result = await _processRunner.RunAsync(
                new ControlledDryRunProcessRequest(
                    command.CommandId.Trim(),
                    NormalizePath(command.WorkingDirectory),
                    command.Executable.Trim(),
                    command.Arguments.Select(argument => argument.Trim()).ToArray(),
                    command.TimeoutSeconds),
                cancellationToken).ConfigureAwait(false);

            commandReports.Add(new ControlledDryRunCommandReport
            {
                CommandId = result.CommandId,
                ExitCode = result.ExitCode,
                TimedOut = result.TimedOut,
                StandardOutputSummary = SanitizeSummary(result.StandardOutput),
                StandardErrorSummary = SanitizeSummary(result.StandardError)
            });
        }

        var completedAtUtc = _timeProvider.GetUtcNow();
        var succeeded = commandReports.All(report => report.ExitCode == 0 && !report.TimedOut);

        return new ControlledDryRunExecutionReport
        {
            ControlledDryRunRequestId = request.ControlledDryRunRequestId,
            ProjectId = request.ProjectId,
            WorkspaceId = workspaceBoundary.WorkspaceId.Trim(),
            WorkspaceBoundaryHash = workspaceBoundary.WorkspaceBoundaryHash.Trim(),
            ValidationPlanId = executionPlan.ValidationPlanId.Trim(),
            ValidationPlanHash = executionPlan.ValidationPlanHash.Trim(),
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            CommandReports = commandReports,
            DryRunCompleted = true,
            DryRunSucceeded = succeeded,
            Boundary = ControlledDryRunExecutionBoundaryText.Boundary,
            Warnings =
            [
                "Controlled dry-run report is in-memory only.",
                "Controlled dry-run execution does not authorize source mutation.",
                "Controlled dry-run execution does not create downstream artifacts."
            ]
        };
    }

    private static void Validate(
        ControlledDryRunRequest request,
        DisposableWorkspaceBoundary workspaceBoundary,
        ControlledDryRunExecutionPlan executionPlan)
    {
        var requestValidation = ControlledDryRunRequestValidation.Validate(request);
        if (!requestValidation.IsValid)
        {
            throw new InvalidOperationException($"Controlled dry-run request is invalid: {string.Join(", ", requestValidation.Issues.Select(issue => issue.Code))}");
        }

        ValidateWorkspaceBoundary(workspaceBoundary);
        ValidateExecutionPlan(executionPlan);

        if (request.ProjectId != workspaceBoundary.ProjectId)
        {
            throw new InvalidOperationException("Controlled dry-run project mismatch.");
        }

        if (!StringEquals(request.WorkspaceId, workspaceBoundary.WorkspaceId))
        {
            throw new InvalidOperationException("Controlled dry-run workspace ID mismatch.");
        }

        if (!StringEquals(request.WorkspaceBoundaryHash, workspaceBoundary.WorkspaceBoundaryHash))
        {
            throw new InvalidOperationException("Controlled dry-run workspace boundary hash mismatch.");
        }

        if (!StringEquals(request.ValidationPlanId, executionPlan.ValidationPlanId))
        {
            throw new InvalidOperationException("Controlled dry-run validation plan ID mismatch.");
        }

        if (!StringEquals(request.ValidationPlanHash, executionPlan.ValidationPlanHash))
        {
            throw new InvalidOperationException("Controlled dry-run validation plan hash mismatch.");
        }

        var workspaceRoot = NormalizePath(workspaceBoundary.WorkspaceRootPath);
        var writeRoot = NormalizePath(workspaceBoundary.AllowedWriteRootPath);

        if (!IsInsidePath(writeRoot, workspaceRoot))
        {
            throw new InvalidOperationException("Controlled dry-run allowed write root must be inside the workspace root.");
        }

        foreach (var command in executionPlan.Commands)
        {
            ValidateCommand(command, workspaceRoot, writeRoot);
        }
    }

    private static void ValidateWorkspaceBoundary(DisposableWorkspaceBoundary boundary)
    {
        if (boundary.ProjectId == Guid.Empty)
        {
            throw new InvalidOperationException("Controlled dry-run workspace project ID is required.");
        }

        ValidateRequiredText(boundary.WorkspaceId, "Controlled dry-run workspace ID is required.");
        ValidateRequiredText(boundary.WorkspaceKind, "Controlled dry-run workspace kind is required.");
        ValidateRequiredText(boundary.WorkspaceRootPath, "Controlled dry-run workspace root path is required.");
        ValidateRequiredText(boundary.AllowedWriteRootPath, "Controlled dry-run allowed write root path is required.");
        ValidateRequiredText(boundary.SourceSnapshotReference, "Controlled dry-run source snapshot reference is required.");
        ValidateRequiredText(boundary.WorkspaceBoundaryHash, "Controlled dry-run workspace boundary hash is required.");

        if (boundary.PreparedAtUtc == default)
        {
            throw new InvalidOperationException("Controlled dry-run workspace prepared timestamp is required.");
        }

        if (boundary.ExpiresAtUtc.HasValue && boundary.ExpiresAtUtc.Value <= boundary.PreparedAtUtc)
        {
            throw new InvalidOperationException("Controlled dry-run workspace expiry must be after prepared timestamp.");
        }

        if (boundary.BoundaryMaxims is null || boundary.BoundaryMaxims.Count == 0 || boundary.BoundaryMaxims.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Controlled dry-run workspace boundary maxims are required.");
        }

        if (!SafeWorkspaceKindMarkers.Any(marker => boundary.WorkspaceKind.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Controlled dry-run workspace kind must be disposable, caged, sandbox, or test-only.");
        }
    }

    private static void ValidateExecutionPlan(ControlledDryRunExecutionPlan plan)
    {
        ValidateRequiredText(plan.ValidationPlanId, "Controlled dry-run validation plan ID is required.");
        ValidateRequiredText(plan.ValidationPlanHash, "Controlled dry-run validation plan hash is required.");

        if (plan.Commands is null || plan.Commands.Count == 0)
        {
            throw new InvalidOperationException("Controlled dry-run execution plan requires at least one command.");
        }

        if (plan.ExpectedOutputArtifacts is null)
        {
            throw new InvalidOperationException("Controlled dry-run expected output artifacts list is required.");
        }

        if (plan.BoundaryMaxims is null || plan.BoundaryMaxims.Count == 0 || plan.BoundaryMaxims.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Controlled dry-run execution plan boundary maxims are required.");
        }
    }

    private static void ValidateCommand(ControlledDryRunCommand command, string workspaceRoot, string writeRoot)
    {
        ValidateRequiredText(command.CommandId, "Controlled dry-run command ID is required.");
        ValidateRequiredText(command.WorkingDirectory, "Controlled dry-run command working directory is required.");
        ValidateRequiredText(command.Executable, "Controlled dry-run command executable is required.");

        if (command.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Controlled dry-run command timeout must be positive.");
        }

        if (command.AllowSourceWorkspaceWrite)
        {
            throw new InvalidOperationException("Controlled dry-run command cannot allow source workspace writes.");
        }

        if (BlockedExecutables.Any(executable => StringEquals(command.Executable, executable)))
        {
            throw new InvalidOperationException("Controlled dry-run command executable is not allowed.");
        }

        var workingDirectory = NormalizePath(command.WorkingDirectory);
        if (!IsInsidePath(workingDirectory, workspaceRoot))
        {
            throw new InvalidOperationException("Controlled dry-run command working directory must stay inside workspace root.");
        }

        if (!IsInsidePath(workingDirectory, writeRoot))
        {
            throw new InvalidOperationException("Controlled dry-run command working directory must stay inside allowed write root.");
        }

        var commandText = $"{command.Executable} {string.Join(" ", command.Arguments ?? [])}";
        foreach (var marker in BlockedCommandMarkers)
        {
            if (commandText.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Controlled dry-run command contains blocked marker: {marker}.");
            }
        }
    }

    private static string SanitizeSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var summary = value.Length > 512 ? value[..512] : value;
        foreach (var marker in SensitiveOutputMarkers)
        {
            summary = summary.Replace(marker, "[redacted]", StringComparison.OrdinalIgnoreCase);
        }

        return summary;
    }

    private static void ValidateRequiredText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsInsidePath(string candidatePath, string rootPath)
    {
        var root = NormalizePath(rootPath) + Path.DirectorySeparatorChar;
        var candidate = NormalizePath(candidatePath) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
