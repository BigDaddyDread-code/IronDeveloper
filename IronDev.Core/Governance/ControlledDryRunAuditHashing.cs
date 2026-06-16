using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IronDev.Core.Governance;

public static class ControlledDryRunAuditHashing
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string ComputeTextHash(string? value) =>
        ComputeHash(new { value = Normalize(value) });

    public static string ComputeCommandHash(ControlledDryRunCommand command) =>
        ComputeHash(new
        {
            commandId = Normalize(command.CommandId),
            workingDirectory = Normalize(command.WorkingDirectory),
            executable = Normalize(command.Executable),
            arguments = command.Arguments?.Select(Normalize).ToArray() ?? [],
            command.TimeoutSeconds,
            command.AllowNetwork,
            command.AllowSourceWorkspaceWrite
        });

    public static string ComputeExecutionReportHash(
        ControlledDryRunRequest request,
        DisposableWorkspaceBoundary workspaceBoundary,
        ControlledDryRunExecutionPlan executionPlan,
        ControlledDryRunExecutionReport report) =>
        ComputeHash(new
        {
            request.ControlledDryRunRequestId,
            request.ProjectId,
            workspaceId = Normalize(workspaceBoundary.WorkspaceId),
            workspaceBoundaryHash = Normalize(workspaceBoundary.WorkspaceBoundaryHash),
            validationPlanId = Normalize(executionPlan.ValidationPlanId),
            validationPlanHash = Normalize(executionPlan.ValidationPlanHash),
            report.StartedAtUtc,
            report.CompletedAtUtc,
            report.DryRunCompleted,
            report.DryRunSucceeded,
            commandReports = report.CommandReports.Select(command => new
            {
                commandId = Normalize(command.CommandId),
                command.ExitCode,
                command.TimedOut,
                standardOutputSummary = Normalize(command.StandardOutputSummary),
                standardErrorSummary = Normalize(command.StandardErrorSummary)
            }).ToArray()
        });

    public static string ComputeAuditHash(ControlledDryRunExecutionAudit audit) =>
        ComputeHash(new
        {
            audit.DryRunExecutionAuditId,
            audit.ProjectId,
            audit.ControlledDryRunRequestId,
            audit.PolicySatisfactionId,
            policySatisfactionHash = Normalize(audit.PolicySatisfactionHash),
            subjectKind = Normalize(audit.SubjectKind),
            subjectId = Normalize(audit.SubjectId),
            subjectHash = Normalize(audit.SubjectHash),
            workspaceId = Normalize(audit.WorkspaceId),
            workspaceKind = Normalize(audit.WorkspaceKind),
            workspaceBoundaryHash = Normalize(audit.WorkspaceBoundaryHash),
            sourceSnapshotReference = Normalize(audit.SourceSnapshotReference),
            validationPlanId = Normalize(audit.ValidationPlanId),
            validationPlanHash = Normalize(audit.ValidationPlanHash),
            audit.StartedAtUtc,
            audit.CompletedAtUtc,
            audit.DryRunCompleted,
            audit.DryRunSucceeded,
            executionReportHash = Normalize(audit.ExecutionReportHash),
            commandAudits = audit.CommandAudits.Select(command => new
            {
                commandId = Normalize(command.CommandId),
                workingDirectory = Normalize(command.WorkingDirectory),
                executable = Normalize(command.Executable),
                commandHash = Normalize(command.CommandHash),
                command.ExitCode,
                command.TimedOut,
                standardOutputSummaryHash = Normalize(command.StandardOutputSummaryHash),
                standardErrorSummaryHash = Normalize(command.StandardErrorSummaryHash),
                standardOutputSummary = Normalize(command.StandardOutputSummary),
                standardErrorSummary = Normalize(command.StandardErrorSummary)
            }).ToArray(),
            evidenceReferences = audit.EvidenceReferences.Select(Normalize).ToArray(),
            boundaryMaxims = audit.BoundaryMaxims.Select(Normalize).ToArray(),
            boundary = Normalize(audit.Boundary)
        });

    private static string ComputeHash(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
