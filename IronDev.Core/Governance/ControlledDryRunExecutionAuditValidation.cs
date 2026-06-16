namespace IronDev.Core.Governance;

public sealed record ControlledDryRunExecutionAuditValidationIssue(string Code, string Field, string Message);

public sealed record ControlledDryRunExecutionAuditValidationResult(IReadOnlyList<ControlledDryRunExecutionAuditValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class ControlledDryRunExecutionAuditValidation
{
    private static readonly string[] PrivateMaterialMarkers =
    [
        "raw prompt",
        "raw completion",
        "raw tool output",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "system prompt",
        "developer prompt",
        "password",
        "api_key",
        "secret",
        "private key",
        "bearer"
    ];

    private static readonly string[] AuthorityClaimMarkers =
    [
        "creates patch artifact",
        "patch artifact created",
        "applies source",
        "source applied",
        "continues workflow",
        "workflow continued",
        "approves release",
        "release approved",
        "release ready",
        "rollback executed"
    ];

    public static ControlledDryRunExecutionAuditValidationResult Validate(ControlledDryRunExecutionAudit? audit)
    {
        var issues = new List<ControlledDryRunExecutionAuditValidationIssue>();

        if (audit is null)
        {
            Add(issues, "DRY_RUN_EXECUTION_AUDIT_REQUIRED", "audit", "Dry-run execution audit is required.");
            return new ControlledDryRunExecutionAuditValidationResult(issues);
        }

        if (audit.DryRunExecutionAuditId == Guid.Empty)
        {
            Add(issues, "DRY_RUN_EXECUTION_AUDIT_ID_REQUIRED", nameof(audit.DryRunExecutionAuditId), "Dry-run execution audit ID is required.");
        }

        if (audit.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(audit.ProjectId), "Project ID is required.");
        }

        if (audit.ControlledDryRunRequestId == Guid.Empty)
        {
            Add(issues, "CONTROLLED_DRY_RUN_REQUEST_ID_REQUIRED", nameof(audit.ControlledDryRunRequestId), "Controlled dry-run request ID is required.");
        }

        if (audit.PolicySatisfactionId == Guid.Empty)
        {
            Add(issues, "POLICY_SATISFACTION_ID_REQUIRED", nameof(audit.PolicySatisfactionId), "Policy satisfaction ID is required.");
        }

        ValidateText(issues, audit.PolicySatisfactionHash, nameof(audit.PolicySatisfactionHash), "POLICY_SATISFACTION_HASH_REQUIRED", "Policy satisfaction hash is required.");
        ValidateText(issues, audit.SubjectKind, nameof(audit.SubjectKind), "SUBJECT_KIND_REQUIRED", "Subject kind is required.");
        ValidateText(issues, audit.SubjectId, nameof(audit.SubjectId), "SUBJECT_ID_REQUIRED", "Subject ID is required.");
        ValidateText(issues, audit.SubjectHash, nameof(audit.SubjectHash), "SUBJECT_HASH_REQUIRED", "Subject hash is required.");
        ValidateText(issues, audit.WorkspaceId, nameof(audit.WorkspaceId), "WORKSPACE_ID_REQUIRED", "Workspace ID is required.");
        ValidateText(issues, audit.WorkspaceKind, nameof(audit.WorkspaceKind), "WORKSPACE_KIND_REQUIRED", "Workspace kind is required.");
        ValidateText(issues, audit.WorkspaceBoundaryHash, nameof(audit.WorkspaceBoundaryHash), "WORKSPACE_BOUNDARY_HASH_REQUIRED", "Workspace boundary hash is required.");
        ValidateText(issues, audit.SourceSnapshotReference, nameof(audit.SourceSnapshotReference), "SOURCE_SNAPSHOT_REFERENCE_REQUIRED", "Source snapshot reference is required.");
        ValidateText(issues, audit.ValidationPlanId, nameof(audit.ValidationPlanId), "VALIDATION_PLAN_ID_REQUIRED", "Validation plan ID is required.");
        ValidateText(issues, audit.ValidationPlanHash, nameof(audit.ValidationPlanHash), "VALIDATION_PLAN_HASH_REQUIRED", "Validation plan hash is required.");
        ValidateText(issues, audit.ExecutionReportHash, nameof(audit.ExecutionReportHash), "EXECUTION_REPORT_HASH_REQUIRED", "Execution report hash is required.");
        ValidateText(issues, audit.AuditHash, nameof(audit.AuditHash), "AUDIT_HASH_REQUIRED", "Audit hash is required.");
        ValidateText(issues, audit.Boundary, nameof(audit.Boundary), "BOUNDARY_REQUIRED", "Boundary text is required.");

        if (audit.StartedAtUtc == default)
        {
            Add(issues, "STARTED_AT_UTC_REQUIRED", nameof(audit.StartedAtUtc), "Started timestamp is required.");
        }

        if (audit.CompletedAtUtc == default)
        {
            Add(issues, "COMPLETED_AT_UTC_REQUIRED", nameof(audit.CompletedAtUtc), "Completed timestamp is required.");
        }

        if (audit.StartedAtUtc != default && audit.CompletedAtUtc != default && audit.CompletedAtUtc < audit.StartedAtUtc)
        {
            Add(issues, "COMPLETED_AT_UTC_INVALID", nameof(audit.CompletedAtUtc), "Completed timestamp must be at or after started timestamp.");
        }

        ValidateRequiredList(issues, audit.EvidenceReferences, nameof(audit.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "At least one evidence reference is required.");
        ValidateRequiredList(issues, audit.BoundaryMaxims, nameof(audit.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "At least one boundary maxim is required.");

        if (audit.CommandAudits is null || audit.CommandAudits.Count == 0)
        {
            Add(issues, "COMMAND_AUDITS_REQUIRED", nameof(audit.CommandAudits), "At least one command audit is required.");
        }
        else
        {
            for (var i = 0; i < audit.CommandAudits.Count; i++)
            {
                ValidateCommandAudit(issues, audit.CommandAudits[i], $"{nameof(audit.CommandAudits)}[{i}]");
            }
        }

        ValidateSafeText(issues, audit.PolicySatisfactionHash, nameof(audit.PolicySatisfactionHash));
        ValidateSafeText(issues, audit.SubjectKind, nameof(audit.SubjectKind));
        ValidateSafeText(issues, audit.SubjectId, nameof(audit.SubjectId));
        ValidateSafeText(issues, audit.SubjectHash, nameof(audit.SubjectHash));
        ValidateSafeText(issues, audit.WorkspaceId, nameof(audit.WorkspaceId));
        ValidateSafeText(issues, audit.WorkspaceKind, nameof(audit.WorkspaceKind));
        ValidateSafeText(issues, audit.WorkspaceBoundaryHash, nameof(audit.WorkspaceBoundaryHash));
        ValidateSafeText(issues, audit.SourceSnapshotReference, nameof(audit.SourceSnapshotReference));
        ValidateSafeText(issues, audit.ValidationPlanId, nameof(audit.ValidationPlanId));
        ValidateSafeText(issues, audit.ValidationPlanHash, nameof(audit.ValidationPlanHash));
        ValidateSafeText(issues, audit.ExecutionReportHash, nameof(audit.ExecutionReportHash));
        ValidateSafeText(issues, audit.AuditHash, nameof(audit.AuditHash));
        ValidateSafeText(issues, audit.Boundary, nameof(audit.Boundary));
        ValidateSafeList(issues, audit.EvidenceReferences, nameof(audit.EvidenceReferences));
        ValidateSafeList(issues, audit.BoundaryMaxims, nameof(audit.BoundaryMaxims));

        return new ControlledDryRunExecutionAuditValidationResult(issues);
    }

    private static void ValidateCommandAudit(
        List<ControlledDryRunExecutionAuditValidationIssue> issues,
        ControlledDryRunCommandAudit commandAudit,
        string fieldPrefix)
    {
        ValidateText(issues, commandAudit.CommandId, $"{fieldPrefix}.{nameof(commandAudit.CommandId)}", "COMMAND_ID_REQUIRED", "Command ID is required.");
        ValidateText(issues, commandAudit.WorkingDirectory, $"{fieldPrefix}.{nameof(commandAudit.WorkingDirectory)}", "COMMAND_WORKING_DIRECTORY_REQUIRED", "Command working directory is required.");
        ValidateText(issues, commandAudit.Executable, $"{fieldPrefix}.{nameof(commandAudit.Executable)}", "COMMAND_EXECUTABLE_REQUIRED", "Command executable is required.");
        ValidateText(issues, commandAudit.CommandHash, $"{fieldPrefix}.{nameof(commandAudit.CommandHash)}", "COMMAND_HASH_REQUIRED", "Command hash is required.");
        ValidateText(issues, commandAudit.StandardOutputSummaryHash, $"{fieldPrefix}.{nameof(commandAudit.StandardOutputSummaryHash)}", "STANDARD_OUTPUT_SUMMARY_HASH_REQUIRED", "Standard output summary hash is required.");
        ValidateText(issues, commandAudit.StandardErrorSummaryHash, $"{fieldPrefix}.{nameof(commandAudit.StandardErrorSummaryHash)}", "STANDARD_ERROR_SUMMARY_HASH_REQUIRED", "Standard error summary hash is required.");
        ValidateText(issues, commandAudit.StandardOutputSummary, $"{fieldPrefix}.{nameof(commandAudit.StandardOutputSummary)}", "STANDARD_OUTPUT_SUMMARY_REQUIRED", "Standard output summary is required.");
        ValidateText(issues, commandAudit.StandardErrorSummary, $"{fieldPrefix}.{nameof(commandAudit.StandardErrorSummary)}", "STANDARD_ERROR_SUMMARY_REQUIRED", "Standard error summary is required.");

        ValidateSafeText(issues, commandAudit.CommandId, $"{fieldPrefix}.{nameof(commandAudit.CommandId)}");
        ValidateSafeText(issues, commandAudit.WorkingDirectory, $"{fieldPrefix}.{nameof(commandAudit.WorkingDirectory)}");
        ValidateSafeText(issues, commandAudit.Executable, $"{fieldPrefix}.{nameof(commandAudit.Executable)}");
        ValidateSafeText(issues, commandAudit.CommandHash, $"{fieldPrefix}.{nameof(commandAudit.CommandHash)}");
        ValidateSafeText(issues, commandAudit.StandardOutputSummaryHash, $"{fieldPrefix}.{nameof(commandAudit.StandardOutputSummaryHash)}");
        ValidateSafeText(issues, commandAudit.StandardErrorSummaryHash, $"{fieldPrefix}.{nameof(commandAudit.StandardErrorSummaryHash)}");
        ValidateSafeText(issues, commandAudit.StandardOutputSummary, $"{fieldPrefix}.{nameof(commandAudit.StandardOutputSummary)}");
        ValidateSafeText(issues, commandAudit.StandardErrorSummary, $"{fieldPrefix}.{nameof(commandAudit.StandardErrorSummary)}");
    }

    private static void ValidateText(
        List<ControlledDryRunExecutionAuditValidationIssue> issues,
        string? value,
        string field,
        string code,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateRequiredList(
        List<ControlledDryRunExecutionAuditValidationIssue> issues,
        IReadOnlyList<string>? values,
        string field,
        string code,
        string message)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateSafeList(
        List<ControlledDryRunExecutionAuditValidationIssue> issues,
        IReadOnlyList<string>? values,
        string field)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            ValidateSafeText(issues, value, field);
        }
    }

    private static void ValidateSafeText(
        List<ControlledDryRunExecutionAuditValidationIssue> issues,
        string? value,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var marker in PrivateMaterialMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, $"Dry-run execution audit text must not contain private or raw material: {marker}.");
            }
        }

        foreach (var marker in AuthorityClaimMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "AUTHORITY_CLAIM_REJECTED", field, $"Dry-run execution audit text must not claim authority: {marker}.");
            }
        }
    }

    private static void Add(List<ControlledDryRunExecutionAuditValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new ControlledDryRunExecutionAuditValidationIssue(code, field, message));
}
