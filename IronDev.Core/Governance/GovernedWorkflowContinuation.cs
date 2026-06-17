using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Workflow;

namespace IronDev.Core.Governance;

public static class GovernedWorkflowContinuationStatuses
{
    public const string Transitioned = "Transitioned";
    public const string Rejected = "Rejected";
    public const string TransitionRecordSaveFailed = "TransitionRecordSaveFailed";
}

public static class GovernedWorkflowContinuationBoundaryText
{
    public const string Boundary = """
        Governed workflow continuation mutates workflow state only after a satisfied continuation gate and current state hash check.
        Governed workflow continuation is not release readiness.
        Governed workflow continuation is not release approval.
        Governed workflow continuation is not source apply.
        Governed workflow continuation is not rollback execution.
        Governed workflow continuation is not policy satisfaction.
        Governed workflow continuation does not call agents, models, tools, git, memory, or retrieval.
        Governed workflow continuation writes a WorkflowTransitionRecord receipt for human review.
        Human review remains required for release readiness and release approval.
        """;

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Workflow continuation is not release readiness.",
        "Workflow continuation is not release approval.",
        "Workflow continuation does not execute source apply or rollback.",
        "Workflow continuation does not call agents, models, tools, git, memory, or retrieval.",
        "WorkflowTransitionRecord is mutation evidence, not release permission."
    ];
}

public sealed record GovernedWorkflowContinuationRequest
{
    public required Guid GovernedWorkflowContinuationRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string CurrentWorkflowStepId { get; init; }
    public string? NextWorkflowStepId { get; init; }
    public required string TransitionKind { get; init; }
    public required string ExpectedWorkflowStateHash { get; init; }
    public required string ExpectedCurrentStepStateHash { get; init; }
    public required WorkflowContinuationGateEvaluation WorkflowContinuationGateEvaluation { get; init; }
    public required string WorkflowContinuationGateEvaluationHash { get; init; }
    public DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = GovernedWorkflowContinuationBoundaryText.Boundary;
}

public sealed record GovernedWorkflowContinuationResult
{
    public required string Status { get; init; }
    public required bool Succeeded { get; init; }
    public required bool WorkflowStateMutated { get; init; }
    public required bool StepCompleted { get; init; }
    public required bool NextStepStarted { get; init; }
    public required bool ReleaseReadinessInferred { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool SourceApplyExecuted { get; init; }
    public required bool RollbackExecuted { get; init; }
    public WorkflowTransitionRecord? WorkflowTransitionRecord { get; init; }
    public required IReadOnlyList<GovernedWorkflowContinuationIssue> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public string Boundary { get; init; } = GovernedWorkflowContinuationBoundaryText.Boundary;
}

public sealed record GovernedWorkflowContinuationIssue(string Code, string Field, string Message);

public sealed record ControlledWorkflowStateTransitionRequest
{
    public required Guid ProjectId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid CurrentWorkflowRunStepId { get; init; }
    public Guid? NextWorkflowRunStepId { get; init; }
    public required string TransitionKind { get; init; }
    public required WorkflowRunStatus ExpectedWorkflowStatus { get; init; }
    public required WorkflowRunStatus ExpectedCurrentStepStatus { get; init; }
    public WorkflowRunStatus? ExpectedNextStepStatus { get; init; }
    public required WorkflowRunStatus NewWorkflowStatus { get; init; }
    public required WorkflowRunStatus NewCurrentStepStatus { get; init; }
    public WorkflowRunStatus? NewNextStepStatus { get; init; }
}

public sealed record ControlledWorkflowStateTransitionResult(
    bool Succeeded,
    IReadOnlyList<GovernedWorkflowContinuationIssue> Issues);

public interface IControlledWorkflowStateTransitionStore
{
    Task<ControlledWorkflowStateTransitionResult> TransitionAsync(
        ControlledWorkflowStateTransitionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IGovernedWorkflowContinuationService
{
    Task<GovernedWorkflowContinuationResult> ContinueAsync(
        GovernedWorkflowContinuationRequest? request,
        CancellationToken cancellationToken = default);
}

public static class GovernedWorkflowContinuationValidation
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
        "entire patch",
        "entirepatch",
        "patch payload",
        "patchpayload"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "release ready",
        "release readiness",
        "release approved",
        "approve release",
        "source applied by continuation",
        "rollback executed by continuation",
        "policy satisfied by continuation",
        "git committed",
        "git pushed",
        "pull request created",
        "memory promoted",
        "retrieval activated",
        "agent dispatched",
        "tool executed",
        "model called"
    ];

    public static IReadOnlyList<GovernedWorkflowContinuationIssue> ValidateRequest(GovernedWorkflowContinuationRequest? request)
    {
        var issues = new List<GovernedWorkflowContinuationIssue>();
        if (request is null)
        {
            Add(issues, "RequestRequired", "request", "Governed workflow continuation request is required.");
            return issues;
        }

        RequireGuid(request.GovernedWorkflowContinuationRequestId, nameof(request.GovernedWorkflowContinuationRequestId), issues);
        RequireGuid(request.ProjectId, nameof(request.ProjectId), issues);
        RequireText(request.WorkflowRunId, nameof(request.WorkflowRunId), issues);
        RequireText(request.CurrentWorkflowStepId, nameof(request.CurrentWorkflowStepId), issues);
        ScanText(request.NextWorkflowStepId, nameof(request.NextWorkflowStepId), issues);
        RequireText(request.TransitionKind, nameof(request.TransitionKind), issues);
        RequireHash(request.ExpectedWorkflowStateHash, nameof(request.ExpectedWorkflowStateHash), issues);
        RequireHash(request.ExpectedCurrentStepStateHash, nameof(request.ExpectedCurrentStepStateHash), issues);
        RequireHash(request.WorkflowContinuationGateEvaluationHash, nameof(request.WorkflowContinuationGateEvaluationHash), issues);
        RequireList(request.EvidenceReferences, nameof(request.EvidenceReferences), issues);
        RequireList(request.BoundaryMaxims, nameof(request.BoundaryMaxims), issues);
        RequireText(request.Boundary, nameof(request.Boundary), issues);
        ScanText(request.Boundary, nameof(request.Boundary), issues);

        if (request.TransitionKind is not WorkflowTransitionKinds.ContinueToNextStep and not WorkflowTransitionKinds.MarkStepComplete)
        {
            Add(issues, "UnsupportedTransitionKind", nameof(request.TransitionKind), "Only ContinueToNextStep and MarkStepComplete are supported by the governed continuation API.");
        }

        ValidateGateEvaluation(request, issues);
        return issues;
    }

    public static void ScanExternalText(string? value, string field, List<GovernedWorkflowContinuationIssue> issues) => ScanText(value, field, issues);

    private static void ValidateGateEvaluation(GovernedWorkflowContinuationRequest request, List<GovernedWorkflowContinuationIssue> issues)
    {
        var gate = request.WorkflowContinuationGateEvaluation;
        if (gate is null)
        {
            Add(issues, "GateEvaluationRequired", nameof(request.WorkflowContinuationGateEvaluation), "Workflow continuation gate evaluation is required.");
            return;
        }

        RequireGuid(gate.WorkflowContinuationGateEvaluationId, nameof(gate.WorkflowContinuationGateEvaluationId), issues);
        RequireGuid(gate.ProjectId, nameof(gate.ProjectId), issues);
        RequireGuid(gate.WorkflowContinuationGateRequestId, nameof(gate.WorkflowContinuationGateRequestId), issues);
        RequireText(gate.Status, nameof(gate.Status), issues);
        RequireText(gate.WorkflowRunId, nameof(gate.WorkflowRunId), issues);
        RequireText(gate.WorkflowStepId, nameof(gate.WorkflowStepId), issues);
        RequireHash(gate.ExpectedWorkflowStateHash, nameof(gate.ExpectedWorkflowStateHash), issues);
        RequireHash(gate.SourceApplyRequestHash, nameof(gate.SourceApplyRequestHash), issues);
        RequireHash(gate.SourceApplyReceiptHash, nameof(gate.SourceApplyReceiptHash), issues);
        RequireList(gate.EvidenceReferences, nameof(gate.EvidenceReferences), issues);
        RequireList(gate.BoundaryMaxims, nameof(gate.BoundaryMaxims), issues);
        RequireText(gate.Boundary, nameof(gate.Boundary), issues);
        ScanText(gate.Boundary, nameof(gate.Boundary), issues);

        if (gate.ProjectId != request.ProjectId)
            Add(issues, "GateProjectMismatch", nameof(gate.ProjectId), "Gate evaluation project must match continuation request project.");
        if (!string.Equals(Normalize(gate.WorkflowRunId), Normalize(request.WorkflowRunId), StringComparison.OrdinalIgnoreCase))
            Add(issues, "GateWorkflowRunMismatch", nameof(gate.WorkflowRunId), "Gate evaluation workflow run must match continuation request.");
        if (!string.Equals(Normalize(gate.WorkflowStepId), Normalize(request.CurrentWorkflowStepId), StringComparison.OrdinalIgnoreCase))
            Add(issues, "GateWorkflowStepMismatch", nameof(gate.WorkflowStepId), "Gate evaluation workflow step must match continuation request.");
        if (!string.Equals(Normalize(gate.ExpectedWorkflowStateHash), Normalize(request.ExpectedWorkflowStateHash), StringComparison.OrdinalIgnoreCase))
            Add(issues, "GateWorkflowStateHashMismatch", nameof(gate.ExpectedWorkflowStateHash), "Gate evaluation expected workflow state hash must match continuation request.");
        if (!string.Equals(GovernedWorkflowContinuationHashing.ComputeGateEvaluationHash(gate), Normalize(request.WorkflowContinuationGateEvaluationHash), StringComparison.OrdinalIgnoreCase))
            Add(issues, "GateEvaluationHashMismatch", nameof(request.WorkflowContinuationGateEvaluationHash), "Gate evaluation hash does not match the supplied gate evaluation.");
        if (!string.Equals(gate.Status, WorkflowContinuationGateStatuses.Satisfied, StringComparison.Ordinal) || !gate.Satisfied)
            Add(issues, "GateNotSatisfied", nameof(gate.Status), "Workflow continuation gate must be satisfied before governed continuation.");
        if (gate.Issues.Count > 0)
            Add(issues, "GateHasIssues", nameof(gate.Issues), "Workflow continuation gate must not contain issues.");
        if (!gate.SourceApplySucceeded || gate.SourceApplyPartial)
            Add(issues, "SourceApplyEvidenceRejected", nameof(gate.SourceApplySucceeded), "Source apply evidence must show successful non-partial apply before continuation.");
        if (!gate.HumanReviewRequired)
            Add(issues, "HumanReviewRequired", nameof(gate.HumanReviewRequired), "Gate evidence must preserve human review requirement.");

        RejectTrue(gate.WorkflowStateMutated, nameof(gate.WorkflowStateMutated), "GateMustNotMutateWorkflow", "Gate evaluation must not mutate workflow state.", issues);
        RejectTrue(gate.WorkflowContinuationExecuted, nameof(gate.WorkflowContinuationExecuted), "GateMustNotContinueWorkflow", "Gate evaluation must not execute continuation.", issues);
        RejectTrue(gate.ReleaseReadinessInferred, nameof(gate.ReleaseReadinessInferred), "ReleaseReadinessRejected", "Gate evaluation must not infer release readiness.", issues);
        RejectTrue(gate.ReleaseApproved, nameof(gate.ReleaseApproved), "ReleaseApprovalRejected", "Gate evaluation must not approve release.", issues);

        if (gate.RollbackWasExecuted)
        {
            if (!gate.RollbackExecutionReceiptId.HasValue || string.IsNullOrWhiteSpace(gate.RollbackExecutionReceiptHash))
                Add(issues, "RollbackReceiptRequired", nameof(gate.RollbackExecutionReceiptId), "Rollback receipt ID and hash are required when rollback was executed.");
            if (!gate.RollbackExecutionAuditReportId.HasValue || !gate.RollbackAuditConsistent)
                Add(issues, "RollbackAuditRequired", nameof(gate.RollbackExecutionAuditReportId), "Consistent rollback audit is required when rollback was executed.");
            RequireHash(gate.RollbackExecutionReceiptHash, nameof(gate.RollbackExecutionReceiptHash), issues);
        }
        else
        {
            if (gate.RollbackExecutionReceiptId.HasValue || !string.IsNullOrWhiteSpace(gate.RollbackExecutionReceiptHash) || gate.RollbackExecutionAuditReportId.HasValue)
                Add(issues, "RollbackReferenceUnexpected", nameof(gate.RollbackWasExecuted), "Rollback references must not be present when rollback was not executed.");
            RejectTrue(gate.RollbackSucceeded, nameof(gate.RollbackSucceeded), "RollbackSucceededUnexpected", "Rollback cannot be marked succeeded when rollback was not executed.", issues);
            RejectTrue(gate.RollbackPartial, nameof(gate.RollbackPartial), "RollbackPartialUnexpected", "Rollback cannot be marked partial when rollback was not executed.", issues);
        }
    }

    private static void RequireGuid(Guid value, string field, List<GovernedWorkflowContinuationIssue> issues)
    {
        if (value == Guid.Empty) Add(issues, "Required", field, "Value is required.");
    }

    private static void RequireText(string? value, string field, List<GovernedWorkflowContinuationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, "Required", field, "Text value is required.");
            return;
        }

        ScanText(value, field, issues);
    }

    private static void RequireHash(string? value, string field, List<GovernedWorkflowContinuationIssue> issues)
    {
        RequireText(value, field, issues);
        if (!string.IsNullOrWhiteSpace(value) && !Normalize(value).StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            Add(issues, "InvalidHash", field, "Hash must use sha256: prefix.");
    }

    private static void RequireList(IReadOnlyList<string>? values, string field, List<GovernedWorkflowContinuationIssue> issues)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, "Required", field, "At least one non-empty value is required.");
            return;
        }

        foreach (var value in values) ScanText(value, field, issues);
    }

    private static void RejectTrue(bool value, string field, string code, string message, List<GovernedWorkflowContinuationIssue> issues)
    {
        if (value) Add(issues, code, field, message);
    }

    private static void ScanText(string? value, string field, List<GovernedWorkflowContinuationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var normalized = value.Trim().ToLowerInvariant();
        if (PrivateOrRawMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
            Add(issues, "PrivateOrRawMaterial", field, "Governed workflow continuation must not contain private/raw material.");
        foreach (var marker in AuthorityMarkers)
        {
            if (ContainsForbiddenAuthorityMarker(normalized, marker))
            {
                Add(issues, "AuthorityClaim", field, "Governed workflow continuation must not claim release, source, rollback, policy, git, memory, retrieval, tool, model, or agent authority.");
                return;
            }
        }
    }

    private static bool ContainsForbiddenAuthorityMarker(string normalized, string marker)
    {
        if (!normalized.Contains(marker, StringComparison.Ordinal)) return false;
        return !(normalized.Contains($"not {marker}", StringComparison.Ordinal) ||
                 normalized.Contains($"is not {marker}", StringComparison.Ordinal) ||
                 normalized.Contains($"does not {marker}", StringComparison.Ordinal) ||
                 normalized.Contains($"must not {marker}", StringComparison.Ordinal));
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static void Add(List<GovernedWorkflowContinuationIssue> issues, string code, string field, string message) =>
        issues.Add(new GovernedWorkflowContinuationIssue(code, field, message));
}

public static class GovernedWorkflowContinuationHashing
{
    public static string ComputeGateEvaluationHash(WorkflowContinuationGateEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        return Sha256Hex(Canonicalize(
            ("ProjectId", evaluation.ProjectId.ToString("D")),
            ("WorkflowContinuationGateEvaluationId", evaluation.WorkflowContinuationGateEvaluationId.ToString("D")),
            ("WorkflowContinuationGateRequestId", evaluation.WorkflowContinuationGateRequestId.ToString("D")),
            ("Status", evaluation.Status),
            ("Satisfied", evaluation.Satisfied ? "true" : "false"),
            ("WorkflowRunId", evaluation.WorkflowRunId),
            ("WorkflowStepId", evaluation.WorkflowStepId),
            ("ExpectedWorkflowStateHash", evaluation.ExpectedWorkflowStateHash),
            ("SourceApplyRequestId", evaluation.SourceApplyRequestId.ToString("D")),
            ("SourceApplyRequestHash", evaluation.SourceApplyRequestHash),
            ("SourceApplyReceiptId", evaluation.SourceApplyReceiptId.ToString("D")),
            ("SourceApplyReceiptHash", evaluation.SourceApplyReceiptHash),
            ("RollbackExecutionReceiptId", evaluation.RollbackExecutionReceiptId?.ToString("D")),
            ("RollbackExecutionReceiptHash", evaluation.RollbackExecutionReceiptHash),
            ("RollbackExecutionAuditReportId", evaluation.RollbackExecutionAuditReportId?.ToString("D")),
            ("SourceApplySucceeded", evaluation.SourceApplySucceeded ? "true" : "false"),
            ("SourceApplyPartial", evaluation.SourceApplyPartial ? "true" : "false"),
            ("RollbackWasExecuted", evaluation.RollbackWasExecuted ? "true" : "false"),
            ("RollbackSucceeded", evaluation.RollbackSucceeded ? "true" : "false"),
            ("RollbackPartial", evaluation.RollbackPartial ? "true" : "false"),
            ("RollbackAuditConsistent", evaluation.RollbackAuditConsistent ? "true" : "false"),
            ("WorkflowStateMutated", evaluation.WorkflowStateMutated ? "true" : "false"),
            ("WorkflowContinuationExecuted", evaluation.WorkflowContinuationExecuted ? "true" : "false"),
            ("ReleaseReadinessInferred", evaluation.ReleaseReadinessInferred ? "true" : "false"),
            ("ReleaseApproved", evaluation.ReleaseApproved ? "true" : "false"),
            ("HumanReviewRequired", evaluation.HumanReviewRequired ? "true" : "false"),
            ("EvidenceReferences", JoinSorted(evaluation.EvidenceReferences)),
            ("BoundaryMaxims", JoinSorted(evaluation.BoundaryMaxims)),
            ("Boundary", evaluation.Boundary)));
    }

    public static string ComputeWorkflowStateHash(WorkflowRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var steps = run.Steps
            .OrderBy(step => step.StepKey, StringComparer.Ordinal)
            .ThenBy(step => step.WorkflowRunStepId)
            .Select(step => $"{step.WorkflowRunStepId:D}:{Normalize(step.StepKey)}:{step.Status}");
        return Sha256Hex(Canonicalize(
            ("ProjectId", run.ProjectId.ToString("D")),
            ("WorkflowRunId", run.WorkflowRunId.ToString("D")),
            ("Status", run.Status.ToString()),
            ("Steps", string.Join("\u001f", steps))));
    }

    public static string ComputeStepStateHash(WorkflowRunStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return Sha256Hex(Canonicalize(
            ("ProjectId", step.ProjectId.ToString("D")),
            ("WorkflowRunId", step.WorkflowRunId.ToString("D")),
            ("WorkflowRunStepId", step.WorkflowRunStepId.ToString("D")),
            ("StepKey", step.StepKey),
            ("Status", step.Status.ToString())));
    }

    private static string Canonicalize(params (string Key, string? Value)[] values) =>
        string.Join("\n", values.Select(value => $"{value.Key}={Normalize(value.Value)}"));

    private static string JoinSorted(IEnumerable<string> values) =>
        string.Join("\u001f", values.Select(Normalize).Order(StringComparer.Ordinal));

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}
