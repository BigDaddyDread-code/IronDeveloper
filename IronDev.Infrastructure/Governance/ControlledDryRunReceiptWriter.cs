using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class ControlledDryRunReceiptWriter : IControlledDryRunReceiptWriter
{
    private readonly IControlledDryRunExecutor _executor;
    private readonly IControlledDryRunReceiptStore _receiptStore;
    private readonly TimeProvider _timeProvider;

    public ControlledDryRunReceiptWriter(
        IControlledDryRunExecutor executor,
        IControlledDryRunReceiptStore receiptStore,
        TimeProvider? timeProvider = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _receiptStore = receiptStore ?? throw new ArgumentNullException(nameof(receiptStore));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ControlledDryRunReceiptWriteResult> ExecuteAndWriteAsync(
        ControlledDryRunRequest request,
        DisposableWorkspaceBoundary workspaceBoundary,
        ControlledDryRunExecutionPlan executionPlan,
        CancellationToken cancellationToken = default)
    {
        ValidateInputs(request, workspaceBoundary, executionPlan);

        var report = await _executor.ExecuteAsync(request, workspaceBoundary, executionPlan, cancellationToken).ConfigureAwait(false);
        ValidateReport(request, workspaceBoundary, executionPlan, report);

        var audit = BuildAudit(request, workspaceBoundary, executionPlan, report);
        var auditValidation = ControlledDryRunExecutionAuditValidation.Validate(audit);
        if (!auditValidation.IsValid)
        {
            throw new InvalidOperationException($"Controlled dry-run execution audit is invalid: {string.Join(", ", auditValidation.Issues.Select(issue => issue.Code))}");
        }

        await _receiptStore.SaveAsync(audit, cancellationToken).ConfigureAwait(false);

        return new ControlledDryRunReceiptWriteResult
        {
            DryRunExecutionAuditId = audit.DryRunExecutionAuditId,
            ProjectId = audit.ProjectId,
            ControlledDryRunRequestId = audit.ControlledDryRunRequestId,
            ExecutionReportHash = audit.ExecutionReportHash,
            AuditHash = audit.AuditHash,
            DryRunCompleted = audit.DryRunCompleted,
            DryRunSucceeded = audit.DryRunSucceeded,
            Audit = audit,
            Boundary = ControlledDryRunReceiptWriteBoundaryText.Boundary,
            Warnings =
            [
                "Dry-run receipt write integration records evidence only.",
                "Dry-run receipt write integration does not create patch artifacts.",
                "Dry-run receipt write integration does not authorize source mutation."
            ]
        };
    }

    private void ValidateInputs(
        ControlledDryRunRequest request,
        DisposableWorkspaceBoundary workspaceBoundary,
        ControlledDryRunExecutionPlan executionPlan)
    {
        var requestValidation = ControlledDryRunRequestValidation.Validate(request);
        if (!requestValidation.IsValid)
        {
            throw new InvalidOperationException($"Controlled dry-run request is invalid: {string.Join(", ", requestValidation.Issues.Select(issue => issue.Code))}");
        }

        ValidateWorkspaceBoundary(request, workspaceBoundary);
        ValidateExecutionPlan(request, executionPlan);
    }

    private void ValidateWorkspaceBoundary(ControlledDryRunRequest request, DisposableWorkspaceBoundary boundary)
    {
        if (boundary.ProjectId == Guid.Empty)
        {
            throw new InvalidOperationException("Disposable workspace project ID is required.");
        }

        RequireText(boundary.WorkspaceId, "Disposable workspace ID is required.");
        RequireText(boundary.WorkspaceKind, "Disposable workspace kind is required.");
        RequireText(boundary.WorkspaceRootPath, "Disposable workspace root path is required.");
        RequireText(boundary.AllowedWriteRootPath, "Disposable workspace allowed write root path is required.");
        RequireText(boundary.SourceSnapshotReference, "Disposable workspace source snapshot reference is required.");
        RequireText(boundary.WorkspaceBoundaryHash, "Disposable workspace boundary hash is required.");

        if (boundary.PreparedAtUtc == default)
        {
            throw new InvalidOperationException("Disposable workspace prepared timestamp is required.");
        }

        if (boundary.ExpiresAtUtc.HasValue && boundary.ExpiresAtUtc.Value <= _timeProvider.GetUtcNow())
        {
            throw new InvalidOperationException("Disposable workspace boundary is expired.");
        }

        if (boundary.BoundaryMaxims is null || boundary.BoundaryMaxims.Count == 0 || boundary.BoundaryMaxims.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Disposable workspace boundary maxims are required.");
        }

        if (request.ProjectId != boundary.ProjectId)
        {
            throw new InvalidOperationException("Controlled dry-run project mismatch.");
        }

        if (!Same(request.WorkspaceId, boundary.WorkspaceId))
        {
            throw new InvalidOperationException("Controlled dry-run workspace ID mismatch.");
        }

        if (!Same(request.WorkspaceKind, boundary.WorkspaceKind))
        {
            throw new InvalidOperationException("Controlled dry-run workspace kind mismatch.");
        }

        if (!Same(request.WorkspaceBoundaryHash, boundary.WorkspaceBoundaryHash))
        {
            throw new InvalidOperationException("Controlled dry-run workspace boundary hash mismatch.");
        }
    }

    private static void ValidateExecutionPlan(ControlledDryRunRequest request, ControlledDryRunExecutionPlan plan)
    {
        RequireText(plan.ValidationPlanId, "Controlled dry-run validation plan ID is required.");
        RequireText(plan.ValidationPlanHash, "Controlled dry-run validation plan hash is required.");

        if (!Same(request.ValidationPlanId, plan.ValidationPlanId))
        {
            throw new InvalidOperationException("Controlled dry-run validation plan ID mismatch.");
        }

        if (!Same(request.ValidationPlanHash, plan.ValidationPlanHash))
        {
            throw new InvalidOperationException("Controlled dry-run validation plan hash mismatch.");
        }

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

        foreach (var command in plan.Commands)
        {
            RequireText(command.CommandId, "Controlled dry-run command ID is required.");
            RequireText(command.WorkingDirectory, "Controlled dry-run command working directory is required.");
            RequireText(command.Executable, "Controlled dry-run command executable is required.");

            if (command.Arguments is null)
            {
                throw new InvalidOperationException("Controlled dry-run command arguments are required.");
            }

            if (command.TimeoutSeconds <= 0)
            {
                throw new InvalidOperationException("Controlled dry-run command timeout must be positive.");
            }

            if (command.AllowNetwork)
            {
                throw new InvalidOperationException("Controlled dry-run command cannot allow network access in receipt write integration.");
            }

            if (command.AllowSourceWorkspaceWrite)
            {
                throw new InvalidOperationException("Controlled dry-run command cannot allow source workspace writes.");
            }
        }
    }

    private static void ValidateReport(
        ControlledDryRunRequest request,
        DisposableWorkspaceBoundary workspaceBoundary,
        ControlledDryRunExecutionPlan executionPlan,
        ControlledDryRunExecutionReport report)
    {
        if (report.ControlledDryRunRequestId != request.ControlledDryRunRequestId)
        {
            throw new InvalidOperationException("Controlled dry-run report request ID mismatch.");
        }

        if (report.ProjectId != request.ProjectId)
        {
            throw new InvalidOperationException("Controlled dry-run report project mismatch.");
        }

        if (!Same(report.WorkspaceId, workspaceBoundary.WorkspaceId))
        {
            throw new InvalidOperationException("Controlled dry-run report workspace ID mismatch.");
        }

        if (!Same(report.WorkspaceBoundaryHash, workspaceBoundary.WorkspaceBoundaryHash))
        {
            throw new InvalidOperationException("Controlled dry-run report workspace boundary hash mismatch.");
        }

        if (!Same(report.ValidationPlanId, executionPlan.ValidationPlanId))
        {
            throw new InvalidOperationException("Controlled dry-run report validation plan ID mismatch.");
        }

        if (!Same(report.ValidationPlanHash, executionPlan.ValidationPlanHash))
        {
            throw new InvalidOperationException("Controlled dry-run report validation plan hash mismatch.");
        }

        if (report.StartedAtUtc == default || report.CompletedAtUtc == default || report.CompletedAtUtc < report.StartedAtUtc)
        {
            throw new InvalidOperationException("Controlled dry-run report timestamps are invalid.");
        }

        if (report.CommandReports is null || report.CommandReports.Count == 0)
        {
            throw new InvalidOperationException("Controlled dry-run report command reports are required.");
        }

        foreach (var commandReport in report.CommandReports)
        {
            if (!executionPlan.Commands.Any(command => Same(command.CommandId, commandReport.CommandId)))
            {
                throw new InvalidOperationException("Controlled dry-run report contains an unknown command report.");
            }
        }
    }

    private static ControlledDryRunExecutionAudit BuildAudit(
        ControlledDryRunRequest request,
        DisposableWorkspaceBoundary workspaceBoundary,
        ControlledDryRunExecutionPlan executionPlan,
        ControlledDryRunExecutionReport report)
    {
        var executionReportHash = ControlledDryRunAuditHashing.ComputeExecutionReportHash(request, workspaceBoundary, executionPlan, report);
        var commandAudits = report.CommandReports
            .Select(commandReport => BuildCommandAudit(commandReport, executionPlan.Commands.Single(command => Same(command.CommandId, commandReport.CommandId))))
            .ToArray();

        var evidenceReferences = DistinctNonBlank(
            request.EvidenceReferences,
            [$"controlled-dry-run-request:{request.ControlledDryRunRequestId:D}", $"controlled-dry-run-execution-report:{executionReportHash}"]);

        var boundaryMaxims = DistinctNonBlank(
            request.BoundaryMaxims,
            workspaceBoundary.BoundaryMaxims,
            executionPlan.BoundaryMaxims,
            report.Warnings ?? [],
            ControlledDryRunReceiptWriteBoundaryText.Boundary.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var auditWithoutHash = new ControlledDryRunExecutionAudit
        {
            DryRunExecutionAuditId = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            ControlledDryRunRequestId = request.ControlledDryRunRequestId,
            PolicySatisfactionId = request.PolicySatisfactionId,
            PolicySatisfactionHash = request.PolicySatisfactionHash.Trim(),
            SubjectKind = request.SubjectKind.Trim(),
            SubjectId = request.SubjectId.Trim(),
            SubjectHash = request.SubjectHash.Trim(),
            WorkspaceId = workspaceBoundary.WorkspaceId.Trim(),
            WorkspaceKind = workspaceBoundary.WorkspaceKind.Trim(),
            WorkspaceBoundaryHash = workspaceBoundary.WorkspaceBoundaryHash.Trim(),
            SourceSnapshotReference = workspaceBoundary.SourceSnapshotReference.Trim(),
            ValidationPlanId = executionPlan.ValidationPlanId.Trim(),
            ValidationPlanHash = executionPlan.ValidationPlanHash.Trim(),
            StartedAtUtc = report.StartedAtUtc,
            CompletedAtUtc = report.CompletedAtUtc,
            DryRunCompleted = report.DryRunCompleted,
            DryRunSucceeded = report.DryRunSucceeded,
            ExecutionReportHash = executionReportHash,
            AuditHash = "sha256:pending",
            CommandAudits = commandAudits,
            EvidenceReferences = evidenceReferences,
            BoundaryMaxims = boundaryMaxims,
            Boundary = ControlledDryRunExecutionAuditBoundaryText.Boundary
        };

        return auditWithoutHash with
        {
            AuditHash = ControlledDryRunAuditHashing.ComputeAuditHash(auditWithoutHash)
        };
    }

    private static ControlledDryRunCommandAudit BuildCommandAudit(
        ControlledDryRunCommandReport commandReport,
        ControlledDryRunCommand command) =>
        new()
        {
            CommandId = command.CommandId.Trim(),
            WorkingDirectory = command.WorkingDirectory.Trim(),
            Executable = command.Executable.Trim(),
            CommandHash = ControlledDryRunAuditHashing.ComputeCommandHash(command),
            ExitCode = commandReport.ExitCode,
            TimedOut = commandReport.TimedOut,
            StandardOutputSummaryHash = ControlledDryRunAuditHashing.ComputeTextHash(commandReport.StandardOutputSummary),
            StandardErrorSummaryHash = ControlledDryRunAuditHashing.ComputeTextHash(commandReport.StandardErrorSummary),
            StandardOutputSummary = commandReport.StandardOutputSummary.Trim(),
            StandardErrorSummary = commandReport.StandardErrorSummary.Trim()
        };

    private static IReadOnlyList<string> DistinctNonBlank(params IEnumerable<string>[] groups) =>
        groups
            .SelectMany(group => group)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

    private static void RequireText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }
    }
}
