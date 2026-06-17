using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public static class WorkflowTransitionKinds
{
    public const string ContinueToNextStep = "ContinueToNextStep";
    public const string MarkStepComplete = "MarkStepComplete";
    public const string BlockedNoTransition = "BlockedNoTransition";

    public static IReadOnlyList<string> Known { get; } =
    [
        ContinueToNextStep,
        MarkStepComplete,
        BlockedNoTransition
    ];
}

public static class WorkflowTransitionRecordBoundaryText
{
    public const string Boundary = """
        WorkflowTransitionRecord is workflow transition evidence.
        WorkflowTransitionRecord contract is not workflow transition.
        WorkflowTransitionRecord contract is not workflow state mutation.
        WorkflowTransitionRecord is not release readiness.
        WorkflowTransitionRecord is not release approval.
        WorkflowTransitionRecord is not source apply.
        WorkflowTransitionRecord is not rollback execution.
        WorkflowTransitionRecord does not call agents, models, tools, git, API, CLI, UI, memory, or retrieval.
        WorkflowTransitionRecord does not prove the product is releasable.
        Human review remains required for release readiness and release approval.
        """;
}

public sealed record WorkflowTransitionRecord
{
    public required Guid WorkflowTransitionRecordId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string TransitionKind { get; init; }
    public required string PreviousWorkflowStateHash { get; init; }
    public required string NewWorkflowStateHash { get; init; }
    public required string PreviousStepStateHash { get; init; }
    public required string NewStepStateHash { get; init; }
    public string? PreviousStepId { get; init; }
    public string? NextStepId { get; init; }
    public required Guid WorkflowContinuationGateEvaluationId { get; init; }
    public required string WorkflowContinuationGateEvaluationHash { get; init; }
    public required Guid SourceApplyRequestId { get; init; }
    public required string SourceApplyRequestHash { get; init; }
    public required Guid SourceApplyReceiptId { get; init; }
    public required string SourceApplyReceiptHash { get; init; }
    public Guid? RollbackExecutionReceiptId { get; init; }
    public string? RollbackExecutionReceiptHash { get; init; }
    public Guid? RollbackExecutionAuditReportId { get; init; }
    public string? RollbackExecutionAuditReportHash { get; init; }
    public required bool WorkflowStateMutated { get; init; }
    public required bool StepCompleted { get; init; }
    public required bool NextStepStarted { get; init; }
    public required bool ReleaseReadinessInferred { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool SourceApplyExecuted { get; init; }
    public required bool RollbackExecuted { get; init; }
    public required DateTimeOffset TransitionedAtUtc { get; init; }
    public required string WorkflowTransitionRecordHash { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = WorkflowTransitionRecordBoundaryText.Boundary;
}

public sealed record WorkflowTransitionRecordValidationIssue(string Code, string Field, string Message);

public sealed record WorkflowTransitionRecordValidationResult(IReadOnlyList<WorkflowTransitionRecordValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class WorkflowTransitionRecordValidation
{
    private static readonly string[] PrivateOrRawMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "private reasoning",
        "hidden reasoning",
        "system prompt",
        "developer prompt",
        "password",
        "api_key",
        "secret",
        "private key",
        "bearer"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "workflow continued by gate alone",
        "safe to continue",
        "release approved",
        "release ready",
        "release readiness",
        "source applied by transition record",
        "rollback executed by transition record",
        "rollback cleaned up",
        "crash cleaned up",
        Join("git ", "committed"),
        Join("git ", "pushed"),
        "merged",
        "pull request created",
        "memory promoted",
        "retrieval activated"
    ];

    public static WorkflowTransitionRecordValidationResult Validate(WorkflowTransitionRecord? record)
    {
        var issues = new List<WorkflowTransitionRecordValidationIssue>();

        if (record is null)
        {
            Add(issues, "RecordRequired", "record", "Workflow transition record is required.");
            return new WorkflowTransitionRecordValidationResult(issues);
        }

        ValidateRequiredFields(record, issues);
        ValidateTransitionKind(record, issues);
        ValidateRollbackReferences(record, issues);
        ValidateAuthorityFlags(record, issues);
        ValidateTruthTable(record, issues);
        ValidateRecordHash(record, issues);
        ValidateTextGraph(record, issues);

        return new WorkflowTransitionRecordValidationResult(issues);
    }

    private static void ValidateRequiredFields(WorkflowTransitionRecord record, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        RequireGuid(record.WorkflowTransitionRecordId, nameof(record.WorkflowTransitionRecordId), issues);
        RequireGuid(record.ProjectId, nameof(record.ProjectId), issues);
        RequireText(record.WorkflowRunId, nameof(record.WorkflowRunId), issues);
        RequireText(record.WorkflowStepId, nameof(record.WorkflowStepId), issues);
        RequireText(record.TransitionKind, nameof(record.TransitionKind), issues);
        RequireHash(record.PreviousWorkflowStateHash, nameof(record.PreviousWorkflowStateHash), issues);
        RequireHash(record.NewWorkflowStateHash, nameof(record.NewWorkflowStateHash), issues);
        RequireHash(record.PreviousStepStateHash, nameof(record.PreviousStepStateHash), issues);
        RequireHash(record.NewStepStateHash, nameof(record.NewStepStateHash), issues);
        RequireGuid(record.WorkflowContinuationGateEvaluationId, nameof(record.WorkflowContinuationGateEvaluationId), issues);
        RequireHash(record.WorkflowContinuationGateEvaluationHash, nameof(record.WorkflowContinuationGateEvaluationHash), issues);
        RequireGuid(record.SourceApplyRequestId, nameof(record.SourceApplyRequestId), issues);
        RequireHash(record.SourceApplyRequestHash, nameof(record.SourceApplyRequestHash), issues);
        RequireGuid(record.SourceApplyReceiptId, nameof(record.SourceApplyReceiptId), issues);
        RequireHash(record.SourceApplyReceiptHash, nameof(record.SourceApplyReceiptHash), issues);
        RequireHash(record.WorkflowTransitionRecordHash, nameof(record.WorkflowTransitionRecordHash), issues);
        RequireList(record.EvidenceReferences, nameof(record.EvidenceReferences), issues);
        RequireList(record.BoundaryMaxims, nameof(record.BoundaryMaxims), issues);
        RequireText(record.Boundary, nameof(record.Boundary), issues);

        if (record.TransitionedAtUtc == default)
        {
            Add(issues, "Required", nameof(record.TransitionedAtUtc), "Transition timestamp is required.");
        }
    }

    private static void ValidateTransitionKind(WorkflowTransitionRecord record, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        if (!WorkflowTransitionKinds.Known.Contains(record.TransitionKind, StringComparer.Ordinal))
        {
            Add(issues, "UnknownTransitionKind", nameof(record.TransitionKind), "Workflow transition kind is not known.");
        }
    }

    private static void ValidateRollbackReferences(WorkflowTransitionRecord record, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        var hasRollbackReceiptId = record.RollbackExecutionReceiptId.HasValue;
        var hasRollbackReceiptHash = !string.IsNullOrWhiteSpace(record.RollbackExecutionReceiptHash);
        var hasRollbackAuditId = record.RollbackExecutionAuditReportId.HasValue;
        var hasRollbackAuditHash = !string.IsNullOrWhiteSpace(record.RollbackExecutionAuditReportHash);

        if (hasRollbackReceiptId != hasRollbackReceiptHash)
        {
            Add(issues, "RollbackReceiptReferenceIncomplete", nameof(record.RollbackExecutionReceiptId), "Rollback execution receipt ID and hash must be supplied together.");
        }

        if (hasRollbackAuditId != hasRollbackAuditHash)
        {
            Add(issues, "RollbackAuditReferenceIncomplete", nameof(record.RollbackExecutionAuditReportId), "Rollback execution audit report ID and hash must be supplied together.");
        }

        if (hasRollbackAuditId && !hasRollbackReceiptId)
        {
            Add(issues, "RollbackAuditRequiresReceiptReference", nameof(record.RollbackExecutionAuditReportId), "Rollback audit report reference requires rollback receipt reference.");
        }

        if (hasRollbackReceiptHash)
        {
            RequireHash(record.RollbackExecutionReceiptHash, nameof(record.RollbackExecutionReceiptHash), issues);
        }

        if (hasRollbackAuditHash)
        {
            RequireHash(record.RollbackExecutionAuditReportHash, nameof(record.RollbackExecutionAuditReportHash), issues);
        }
    }

    private static void ValidateAuthorityFlags(WorkflowTransitionRecord record, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        RejectTrue(record.ReleaseReadinessInferred, nameof(record.ReleaseReadinessInferred), "ReleaseReadinessInferenceRejected", "Workflow transition record must not infer release readiness.", issues);
        RejectTrue(record.ReleaseApproved, nameof(record.ReleaseApproved), "ReleaseApprovalRejected", "Workflow transition record must not approve release.", issues);
        RejectTrue(record.SourceApplyExecuted, nameof(record.SourceApplyExecuted), "SourceApplyExecutionRejected", "Workflow transition record must not execute source apply.", issues);
        RejectTrue(record.RollbackExecuted, nameof(record.RollbackExecuted), "RollbackExecutionRejected", "Workflow transition record must not execute rollback.", issues);
    }

    private static void ValidateTruthTable(WorkflowTransitionRecord record, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        switch (record.TransitionKind)
        {
            case WorkflowTransitionKinds.ContinueToNextStep:
                RequireTrue(record.WorkflowStateMutated, nameof(record.WorkflowStateMutated), "ContinueRequiresWorkflowMutation", issues);
                RequireTrue(record.StepCompleted, nameof(record.StepCompleted), "ContinueRequiresStepCompleted", issues);
                RequireTrue(record.NextStepStarted, nameof(record.NextStepStarted), "ContinueRequiresNextStepStarted", issues);
                RequireText(record.PreviousStepId, nameof(record.PreviousStepId), issues);
                RequireText(record.NextStepId, nameof(record.NextStepId), issues);
                RequireDifferent(record.PreviousWorkflowStateHash, record.NewWorkflowStateHash, nameof(record.NewWorkflowStateHash), "WorkflowStateHashMustChange", issues);
                RequireDifferent(record.PreviousStepStateHash, record.NewStepStateHash, nameof(record.NewStepStateHash), "StepStateHashMustChange", issues);
                break;

            case WorkflowTransitionKinds.MarkStepComplete:
                RequireTrue(record.WorkflowStateMutated, nameof(record.WorkflowStateMutated), "MarkCompleteRequiresWorkflowMutation", issues);
                RequireTrue(record.StepCompleted, nameof(record.StepCompleted), "MarkCompleteRequiresStepCompleted", issues);
                RejectTrue(record.NextStepStarted, nameof(record.NextStepStarted), "MarkCompleteMustNotStartNextStep", "Mark-step-complete transition must not start the next step.", issues);
                RequireText(record.PreviousStepId, nameof(record.PreviousStepId), issues);
                RequireDifferent(record.PreviousWorkflowStateHash, record.NewWorkflowStateHash, nameof(record.NewWorkflowStateHash), "WorkflowStateHashMustChange", issues);
                RequireDifferent(record.PreviousStepStateHash, record.NewStepStateHash, nameof(record.NewStepStateHash), "StepStateHashMustChange", issues);
                break;

            case WorkflowTransitionKinds.BlockedNoTransition:
                RejectTrue(record.WorkflowStateMutated, nameof(record.WorkflowStateMutated), "BlockedMustNotMutateWorkflow", "Blocked transition record must not mutate workflow state.", issues);
                RejectTrue(record.StepCompleted, nameof(record.StepCompleted), "BlockedMustNotCompleteStep", "Blocked transition record must not complete a step.", issues);
                RejectTrue(record.NextStepStarted, nameof(record.NextStepStarted), "BlockedMustNotStartNextStep", "Blocked transition record must not start next step.", issues);
                RequireSame(record.PreviousWorkflowStateHash, record.NewWorkflowStateHash, nameof(record.NewWorkflowStateHash), "BlockedWorkflowStateHashMustNotChange", issues);
                RequireSame(record.PreviousStepStateHash, record.NewStepStateHash, nameof(record.NewStepStateHash), "BlockedStepStateHashMustNotChange", issues);
                break;
        }
    }

    private static void ValidateRecordHash(WorkflowTransitionRecord record, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(record.WorkflowTransitionRecordHash) &&
            !string.Equals(record.WorkflowTransitionRecordHash, WorkflowTransitionRecordHashing.ComputeRecordHash(record), StringComparison.Ordinal))
        {
            Add(issues, "RecordHashMismatch", nameof(record.WorkflowTransitionRecordHash), "Workflow transition record hash does not match the recomputed hash.");
        }
    }

    private static void ValidateTextGraph(WorkflowTransitionRecord record, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        ScanText(record.WorkflowRunId, nameof(record.WorkflowRunId), issues);
        ScanText(record.WorkflowStepId, nameof(record.WorkflowStepId), issues);
        ScanText(record.TransitionKind, nameof(record.TransitionKind), issues);
        ScanText(record.PreviousWorkflowStateHash, nameof(record.PreviousWorkflowStateHash), issues);
        ScanText(record.NewWorkflowStateHash, nameof(record.NewWorkflowStateHash), issues);
        ScanText(record.PreviousStepStateHash, nameof(record.PreviousStepStateHash), issues);
        ScanText(record.NewStepStateHash, nameof(record.NewStepStateHash), issues);
        ScanText(record.PreviousStepId, nameof(record.PreviousStepId), issues);
        ScanText(record.NextStepId, nameof(record.NextStepId), issues);
        ScanText(record.WorkflowContinuationGateEvaluationHash, nameof(record.WorkflowContinuationGateEvaluationHash), issues);
        ScanText(record.SourceApplyRequestHash, nameof(record.SourceApplyRequestHash), issues);
        ScanText(record.SourceApplyReceiptHash, nameof(record.SourceApplyReceiptHash), issues);
        ScanText(record.RollbackExecutionReceiptHash, nameof(record.RollbackExecutionReceiptHash), issues);
        ScanText(record.RollbackExecutionAuditReportHash, nameof(record.RollbackExecutionAuditReportHash), issues);
        ScanText(record.WorkflowTransitionRecordHash, nameof(record.WorkflowTransitionRecordHash), issues);
        ScanTexts(record.EvidenceReferences, nameof(record.EvidenceReferences), issues);
        ScanTexts(record.BoundaryMaxims, nameof(record.BoundaryMaxims), issues);
        ScanText(record.Boundary, nameof(record.Boundary), issues);
    }

    private static void RequireGuid(Guid value, string field, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        if (value == Guid.Empty)
        {
            Add(issues, "Required", field, "Value is required.");
        }
    }

    private static void RequireText(string? value, string field, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, "Required", field, "Text value is required.");
            return;
        }

        ScanText(value, field, issues);
    }

    private static void RequireHash(string? value, string field, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        RequireText(value, field, issues);
        if (!string.IsNullOrWhiteSpace(value) && !value.Trim().StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            Add(issues, "InvalidHash", field, "Hash must use sha256: prefix.");
        }
    }

    private static void RequireList(IReadOnlyList<string>? values, string field, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, "Required", field, "At least one non-empty value is required.");
            return;
        }

        ScanTexts(values, field, issues);
    }

    private static void RejectTrue(bool value, string field, string code, string message, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        if (value)
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireTrue(bool value, string field, string code, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        if (!value)
        {
            Add(issues, code, field, "Workflow transition truth table requirement failed.");
        }
    }

    private static void RequireDifferent(string? left, string? right, string field, string code, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        if (string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal))
        {
            Add(issues, code, field, "Workflow transition hash must change for this transition kind.");
        }
    }

    private static void RequireSame(string? left, string? right, string field, string code, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        if (!string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal))
        {
            Add(issues, code, field, "Blocked no-transition hash must remain unchanged.");
        }
    }

    private static void ScanTexts(IEnumerable<string>? values, string field, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            ScanText(value, field, issues);
        }
    }

    private static void ScanText(string? value, string field, List<WorkflowTransitionRecordValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (PrivateOrRawMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
        {
            Add(issues, "PrivateOrRawMaterial", field, "Workflow transition record must not contain private/raw material.");
        }

        foreach (var marker in AuthorityMarkers)
        {
            if (ContainsForbiddenAuthorityMarker(normalized, marker))
            {
                Add(issues, "AuthorityClaim", field, "Workflow transition record must not contain release, source, rollback, git, memory, or retrieval authority claims.");
                return;
            }
        }
    }

    private static bool ContainsForbiddenAuthorityMarker(string normalized, string marker)
    {
        if (!normalized.Contains(marker, StringComparison.Ordinal))
        {
            return false;
        }

        return !(normalized.Contains($"not {marker}", StringComparison.Ordinal) ||
                 normalized.Contains($"is not {marker}", StringComparison.Ordinal) ||
                 normalized.Contains($"does not {marker}", StringComparison.Ordinal) ||
                 normalized.Contains($"must not {marker}", StringComparison.Ordinal));
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static void Add(List<WorkflowTransitionRecordValidationIssue> issues, string code, string field, string message) =>
        issues.Add(new WorkflowTransitionRecordValidationIssue(code, field, message));

    private static string Join(string left, string right) => left + right;
}

public static class WorkflowTransitionRecordHashing
{
    public static string ComputeRecordHash(WorkflowTransitionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return Sha256Hex(Canonicalize(
            ("ProjectId", record.ProjectId.ToString("D")),
            ("WorkflowRunId", record.WorkflowRunId),
            ("WorkflowStepId", record.WorkflowStepId),
            ("TransitionKind", record.TransitionKind),
            ("PreviousWorkflowStateHash", record.PreviousWorkflowStateHash),
            ("NewWorkflowStateHash", record.NewWorkflowStateHash),
            ("PreviousStepStateHash", record.PreviousStepStateHash),
            ("NewStepStateHash", record.NewStepStateHash),
            ("PreviousStepId", record.PreviousStepId),
            ("NextStepId", record.NextStepId),
            ("WorkflowContinuationGateEvaluationId", record.WorkflowContinuationGateEvaluationId.ToString("D")),
            ("WorkflowContinuationGateEvaluationHash", record.WorkflowContinuationGateEvaluationHash),
            ("SourceApplyRequestId", record.SourceApplyRequestId.ToString("D")),
            ("SourceApplyRequestHash", record.SourceApplyRequestHash),
            ("SourceApplyReceiptId", record.SourceApplyReceiptId.ToString("D")),
            ("SourceApplyReceiptHash", record.SourceApplyReceiptHash),
            ("RollbackExecutionReceiptId", record.RollbackExecutionReceiptId?.ToString("D")),
            ("RollbackExecutionReceiptHash", record.RollbackExecutionReceiptHash),
            ("RollbackExecutionAuditReportId", record.RollbackExecutionAuditReportId?.ToString("D")),
            ("RollbackExecutionAuditReportHash", record.RollbackExecutionAuditReportHash),
            ("WorkflowStateMutated", record.WorkflowStateMutated ? "true" : "false"),
            ("StepCompleted", record.StepCompleted ? "true" : "false"),
            ("NextStepStarted", record.NextStepStarted ? "true" : "false"),
            ("ReleaseReadinessInferred", record.ReleaseReadinessInferred ? "true" : "false"),
            ("ReleaseApproved", record.ReleaseApproved ? "true" : "false"),
            ("SourceApplyExecuted", record.SourceApplyExecuted ? "true" : "false"),
            ("RollbackExecuted", record.RollbackExecuted ? "true" : "false"),
            ("TransitionedAtUtc", record.TransitionedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("EvidenceReferences", string.Join("\u001f", record.EvidenceReferences.Select(Normalize).Order(StringComparer.Ordinal))),
            ("BoundaryMaxims", string.Join("\u001f", record.BoundaryMaxims.Select(Normalize).Order(StringComparer.Ordinal))),
            ("Boundary", record.Boundary)));
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string Canonicalize(params (string Key, string? Value)[] values) =>
        string.Join("\n", values.Select(value => $"{value.Key}={Normalize(value.Value)}"));

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
