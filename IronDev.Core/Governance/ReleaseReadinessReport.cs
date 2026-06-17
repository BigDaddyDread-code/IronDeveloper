using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public static class ReleaseReadinessReportStatuses
{
    public const string Complete = "Complete";
    public const string BlockedByMissingEvidence = "BlockedByMissingEvidence";
    public const string BlockedByFailedEvidence = "BlockedByFailedEvidence";
}

public static class ReleaseReadinessFindingSeverities
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Blocking = "Blocking";
}

public static class ReleaseReadinessReportBoundaryText
{
    public const string Boundary = """
        Release readiness report is evidence summary only.
        Release readiness report is not release readiness.
        Release readiness report is not release approval.
        Release readiness report is not deployment approval.
        Release readiness report is not merge approval.
        Release readiness report is not source apply.
        Release readiness report is not rollback execution.
        Release readiness report is not workflow continuation.
        Release readiness report does not mutate workflow state.
        Release readiness report does not call agents, models, tools, git, API, CLI, UI, memory, or retrieval.
        Human review remains required for release readiness and release approval.
        """;
}

public sealed record ReleaseReadinessReportRequest
{
    public required Guid ReleaseReadinessReportRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required AcceptedApprovalRecord AcceptedApproval { get; init; }
    public required PolicySatisfactionRecord PolicySatisfaction { get; init; }
    public required SourceApplyRequest SourceApplyRequest { get; init; }
    public required SourceApplyReceipt SourceApplyReceipt { get; init; }
    public RollbackExecutionReceipt? RollbackExecutionReceipt { get; init; }
    public RollbackExecutionAuditReport? RollbackExecutionAuditReport { get; init; }
    public required WorkflowContinuationGateEvaluation WorkflowContinuationGateEvaluation { get; init; }
    public required WorkflowTransitionRecord WorkflowTransitionRecord { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = ReleaseReadinessReportBoundaryText.Boundary;
}

public sealed record ReleaseReadinessReport
{
    public required Guid ReleaseReadinessReportId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ReleaseReadinessReportRequestId { get; init; }
    public required string Status { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required Guid AcceptedApprovalId { get; init; }
    public required string AcceptedApprovalHash { get; init; }
    public required Guid PolicySatisfactionId { get; init; }
    public required string PolicySatisfactionHash { get; init; }
    public required Guid SourceApplyRequestId { get; init; }
    public required string SourceApplyRequestHash { get; init; }
    public required Guid SourceApplyReceiptId { get; init; }
    public required string SourceApplyReceiptHash { get; init; }
    public Guid? RollbackExecutionReceiptId { get; init; }
    public string? RollbackExecutionReceiptHash { get; init; }
    public Guid? RollbackExecutionAuditReportId { get; init; }
    public string? RollbackExecutionAuditReportHash { get; init; }
    public required Guid WorkflowContinuationGateEvaluationId { get; init; }
    public required string WorkflowContinuationGateEvaluationHash { get; init; }
    public required Guid WorkflowTransitionRecordId { get; init; }
    public required string WorkflowTransitionRecordHash { get; init; }
    public required bool ApprovalEvidencePresent { get; init; }
    public required bool PolicyEvidencePresent { get; init; }
    public required bool SourceApplySucceeded { get; init; }
    public required bool SourceApplyPartial { get; init; }
    public required bool RollbackWasExecuted { get; init; }
    public required bool RollbackSucceeded { get; init; }
    public required bool RollbackPartial { get; init; }
    public required bool RollbackAuditConsistent { get; init; }
    public required bool WorkflowContinuationSucceeded { get; init; }
    public required bool WorkflowTransitionRecordValid { get; init; }
    public required bool ReleaseReadinessDecided { get; init; }
    public required bool ReleaseReady { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool DeploymentApproved { get; init; }
    public required bool MergeApproved { get; init; }
    public required bool SourceApplyExecutedByReport { get; init; }
    public required bool RollbackExecutedByReport { get; init; }
    public required bool WorkflowMutatedByReport { get; init; }
    public required bool GitOperationExecutedByReport { get; init; }
    public required bool HumanReviewRequiredForReadiness { get; init; }
    public required bool HumanReviewRequiredForReleaseApproval { get; init; }
    public required IReadOnlyList<ReleaseReadinessReportFinding> Findings { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public required DateTimeOffset ReportedAtUtc { get; init; }
    public required string ReleaseReadinessReportHash { get; init; }
    public string Boundary { get; init; } = ReleaseReadinessReportBoundaryText.Boundary;
}

public sealed record ReleaseReadinessReportFinding
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public sealed record ReleaseReadinessReportValidationIssue(string Code, string Field, string Message);

public sealed record ReleaseReadinessReportValidationResult(IReadOnlyList<ReleaseReadinessReportValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public sealed class ReleaseReadinessReportBuilder
{
    public ReleaseReadinessReport Build(ReleaseReadinessReportRequest? request)
    {
        var findings = new List<ReleaseReadinessReportFinding>();
        if (request is null)
        {
            Add(findings, "RequestRequired", ReleaseReadinessFindingSeverities.Blocking, "request", "Release readiness report request is required.");
            return BuildReport(null, findings);
        }

        ValidateRequestShape(request, findings);
        AddValidationIssues(AcceptedApprovalValidation.Validate(request.AcceptedApproval).Issues, "AcceptedApproval", findings);
        AddValidationIssues(PolicySatisfactionValidation.Validate(request.PolicySatisfaction).Issues, "PolicySatisfaction", findings);
        AddValidationIssues(SourceApplyRequestValidation.Validate(request.SourceApplyRequest).Issues, "SourceApplyRequest", findings);
        AddValidationIssues(SourceApplyReceiptValidation.Validate(request.SourceApplyReceipt).Issues, "SourceApplyReceipt", findings);
        AddValidationIssues(WorkflowTransitionRecordValidation.Validate(request.WorkflowTransitionRecord).Issues, "WorkflowTransitionRecord", findings);
        if (request.RollbackExecutionReceipt is not null)
            AddValidationIssues(RollbackExecutionReceiptValidation.Validate(request.RollbackExecutionReceipt).Issues, "RollbackExecutionReceipt", findings);

        ValidateEvidenceBinding(request, findings);
        ValidateSourceApplyPath(request, findings);
        ValidateRollbackPath(request, findings);
        ValidateContinuationGate(request, findings);
        ValidateTransitionRecordBinding(request, findings);
        ScanTextGraph(request, findings);

        if (!findings.Any(finding => finding.Severity == ReleaseReadinessFindingSeverities.Blocking))
        {
            Add(findings, "ApprovalEvidencePresent", ReleaseReadinessFindingSeverities.Info, nameof(request.AcceptedApproval), "Accepted approval evidence is present.");
            Add(findings, "PolicySatisfactionPresent", ReleaseReadinessFindingSeverities.Info, nameof(request.PolicySatisfaction), "Policy satisfaction evidence is present.");
            Add(findings, "SourceApplySucceeded", ReleaseReadinessFindingSeverities.Info, nameof(request.SourceApplyReceipt), "Source apply receipt reports successful source apply.");
            Add(findings, "WorkflowTransitionRecordValid", ReleaseReadinessFindingSeverities.Info, nameof(request.WorkflowTransitionRecord), "Workflow transition record is valid and bound.");
            Add(findings, "HumanReviewRequired", ReleaseReadinessFindingSeverities.Info, nameof(ReleaseReadinessReport.HumanReviewRequiredForReadiness), "Human review remains required for release readiness and release approval.");
        }

        return BuildReport(request, findings);
    }

    private static ReleaseReadinessReport BuildReport(ReleaseReadinessReportRequest? request, IReadOnlyList<ReleaseReadinessReportFinding> findings)
    {
        var missing = findings.Any(finding => finding.Code.Contains("Required", StringComparison.OrdinalIgnoreCase) || finding.Code.Contains("Missing", StringComparison.OrdinalIgnoreCase));
        var blocking = findings.Any(finding => finding.Severity == ReleaseReadinessFindingSeverities.Blocking);
        var report = new ReleaseReadinessReport
        {
            ReleaseReadinessReportId = Guid.NewGuid(),
            ProjectId = request?.ProjectId ?? Guid.Empty,
            ReleaseReadinessReportRequestId = request?.ReleaseReadinessReportRequestId ?? Guid.Empty,
            Status = blocking ? missing ? ReleaseReadinessReportStatuses.BlockedByMissingEvidence : ReleaseReadinessReportStatuses.BlockedByFailedEvidence : ReleaseReadinessReportStatuses.Complete,
            WorkflowRunId = Safe(request?.WorkflowRunId),
            WorkflowStepId = Safe(request?.WorkflowStepId),
            SubjectKind = Safe(request?.SubjectKind),
            SubjectId = Safe(request?.SubjectId),
            SubjectHash = Safe(request?.SubjectHash),
            AcceptedApprovalId = request?.AcceptedApproval?.AcceptedApprovalId ?? Guid.Empty,
            AcceptedApprovalHash = Safe(request?.SourceApplyRequest?.AcceptedApprovalHash),
            PolicySatisfactionId = request?.PolicySatisfaction?.PolicySatisfactionId ?? Guid.Empty,
            PolicySatisfactionHash = Safe(request?.SourceApplyRequest?.PolicySatisfactionHash),
            SourceApplyRequestId = request?.SourceApplyRequest?.SourceApplyRequestId ?? Guid.Empty,
            SourceApplyRequestHash = Safe(request?.SourceApplyRequest?.SourceApplyRequestHash),
            SourceApplyReceiptId = request?.SourceApplyReceipt?.SourceApplyReceiptId ?? Guid.Empty,
            SourceApplyReceiptHash = Safe(request?.SourceApplyReceipt?.SourceApplyReceiptHash),
            RollbackExecutionReceiptId = request?.RollbackExecutionReceipt?.RollbackExecutionReceiptId,
            RollbackExecutionReceiptHash = SafeNullable(request?.RollbackExecutionReceipt?.RollbackExecutionReceiptHash),
            RollbackExecutionAuditReportId = request?.RollbackExecutionAuditReport?.RollbackExecutionAuditReportId,
            RollbackExecutionAuditReportHash = request?.RollbackExecutionAuditReport is null ? null : ReleaseReadinessReportHashing.ComputeRollbackExecutionAuditReportHash(request.RollbackExecutionAuditReport),
            WorkflowContinuationGateEvaluationId = request?.WorkflowContinuationGateEvaluation?.WorkflowContinuationGateEvaluationId ?? Guid.Empty,
            WorkflowContinuationGateEvaluationHash = Safe(request?.WorkflowTransitionRecord?.WorkflowContinuationGateEvaluationHash ?? (request?.WorkflowContinuationGateEvaluation is null ? null : GovernedWorkflowContinuationHashing.ComputeGateEvaluationHash(request.WorkflowContinuationGateEvaluation))),
            WorkflowTransitionRecordId = request?.WorkflowTransitionRecord?.WorkflowTransitionRecordId ?? Guid.Empty,
            WorkflowTransitionRecordHash = Safe(request?.WorkflowTransitionRecord?.WorkflowTransitionRecordHash),
            ApprovalEvidencePresent = request?.AcceptedApproval is not null,
            PolicyEvidencePresent = request?.PolicySatisfaction is not null,
            SourceApplySucceeded = request?.SourceApplyReceipt?.ApplySucceeded == true,
            SourceApplyPartial = request?.SourceApplyReceipt?.PartialApplyOccurred == true,
            RollbackWasExecuted = request?.RollbackExecutionReceipt is not null,
            RollbackSucceeded = request?.RollbackExecutionReceipt?.RollbackSucceeded == true,
            RollbackPartial = request?.RollbackExecutionReceipt?.PartialRollbackOccurred == true,
            RollbackAuditConsistent = request?.RollbackExecutionAuditReport?.EvidenceConsistent == true,
            WorkflowContinuationSucceeded = request?.WorkflowContinuationGateEvaluation?.Satisfied == true,
            WorkflowTransitionRecordValid = request?.WorkflowTransitionRecord is not null && WorkflowTransitionRecordValidation.Validate(request.WorkflowTransitionRecord).IsValid,
            ReleaseReadinessDecided = false,
            ReleaseReady = false,
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            SourceApplyExecutedByReport = false,
            RollbackExecutedByReport = false,
            WorkflowMutatedByReport = false,
            GitOperationExecutedByReport = false,
            HumanReviewRequiredForReadiness = true,
            HumanReviewRequiredForReleaseApproval = true,
            Findings = findings,
            EvidenceReferences = SafeList(request?.EvidenceReferences),
            BoundaryMaxims = SafeList(request?.BoundaryMaxims),
            ReportedAtUtc = request?.RequestedAtUtc == default ? DateTimeOffset.UtcNow : request?.RequestedAtUtc ?? DateTimeOffset.UtcNow,
            ReleaseReadinessReportHash = "sha256:pending",
            Boundary = ReleaseReadinessReportBoundaryText.Boundary
        };

        return report with { ReleaseReadinessReportHash = ReleaseReadinessReportHashing.ComputeReportHash(report) };
    }

    private static void ValidateRequestShape(ReleaseReadinessReportRequest request, List<ReleaseReadinessReportFinding> findings)
    {
        RequireGuid(request.ReleaseReadinessReportRequestId, nameof(request.ReleaseReadinessReportRequestId), findings);
        RequireGuid(request.ProjectId, nameof(request.ProjectId), findings);
        RequireText(request.WorkflowRunId, nameof(request.WorkflowRunId), findings);
        RequireText(request.WorkflowStepId, nameof(request.WorkflowStepId), findings);
        RequireText(request.SubjectKind, nameof(request.SubjectKind), findings);
        RequireText(request.SubjectId, nameof(request.SubjectId), findings);
        RequireHash(request.SubjectHash, nameof(request.SubjectHash), findings);
        RequireList(request.EvidenceReferences, nameof(request.EvidenceReferences), findings);
        RequireList(request.BoundaryMaxims, nameof(request.BoundaryMaxims), findings);
        RequireText(request.Boundary, nameof(request.Boundary), findings);
        if (request.RequestedAtUtc == default)
            Add(findings, "RequestedAtRequired", nameof(request.RequestedAtUtc), "Requested timestamp is required.");
    }

    private static void ValidateEvidenceBinding(ReleaseReadinessReportRequest request, List<ReleaseReadinessReportFinding> findings)
    {
        Match(request.ProjectId, request.AcceptedApproval?.ProjectId, "ProjectMismatch", "AcceptedApproval.ProjectId", findings);
        Match(request.ProjectId, request.PolicySatisfaction?.ProjectId, "ProjectMismatch", "PolicySatisfaction.ProjectId", findings);
        Match(request.ProjectId, request.SourceApplyRequest?.ProjectId, "ProjectMismatch", "SourceApplyRequest.ProjectId", findings);
        Match(request.ProjectId, request.SourceApplyReceipt?.ProjectId, "ProjectMismatch", "SourceApplyReceipt.ProjectId", findings);
        Match(request.ProjectId, request.WorkflowContinuationGateEvaluation?.ProjectId, "ProjectMismatch", "WorkflowContinuationGateEvaluation.ProjectId", findings);
        Match(request.ProjectId, request.WorkflowTransitionRecord?.ProjectId, "ProjectMismatch", "WorkflowTransitionRecord.ProjectId", findings);
        if (request.RollbackExecutionReceipt is not null) Match(request.ProjectId, request.RollbackExecutionReceipt.ProjectId, "ProjectMismatch", "RollbackExecutionReceipt.ProjectId", findings);
        if (request.RollbackExecutionAuditReport is not null) Match(request.ProjectId, request.RollbackExecutionAuditReport.ProjectId, "ProjectMismatch", "RollbackExecutionAuditReport.ProjectId", findings);

        Match(request.SubjectKind, request.AcceptedApproval?.ApprovalTargetKind, "SubjectKindMismatch", "AcceptedApproval.ApprovalTargetKind", findings);
        Match(request.SubjectId, request.AcceptedApproval?.ApprovalTargetId, "SubjectIdMismatch", "AcceptedApproval.ApprovalTargetId", findings);
        Match(request.SubjectHash, request.AcceptedApproval?.ApprovalTargetHash, "SubjectHashMismatch", "AcceptedApproval.ApprovalTargetHash", findings);
        Match(request.SubjectKind, request.PolicySatisfaction?.SubjectKind, "SubjectKindMismatch", "PolicySatisfaction.SubjectKind", findings);
        Match(request.SubjectId, request.PolicySatisfaction?.SubjectId, "SubjectIdMismatch", "PolicySatisfaction.SubjectId", findings);
        Match(request.SubjectHash, request.PolicySatisfaction?.SubjectHash, "SubjectHashMismatch", "PolicySatisfaction.SubjectHash", findings);
        Match(request.SubjectKind, request.SourceApplyRequest?.SubjectKind, "SubjectKindMismatch", "SourceApplyRequest.SubjectKind", findings);
        Match(request.SubjectId, request.SourceApplyRequest?.SubjectId, "SubjectIdMismatch", "SourceApplyRequest.SubjectId", findings);
        Match(request.SubjectHash, request.SourceApplyRequest?.SubjectHash, "SubjectHashMismatch", "SourceApplyRequest.SubjectHash", findings);
        Match(request.AcceptedApproval?.AcceptedApprovalId, request.PolicySatisfaction?.AcceptedApprovalId, "PolicyAcceptedApprovalMismatch", "PolicySatisfaction.AcceptedApprovalId", findings);
        Match(request.AcceptedApproval?.AcceptedApprovalId, request.SourceApplyRequest?.AcceptedApprovalId, "SourceApplyRequestAcceptedApprovalMismatch", "SourceApplyRequest.AcceptedApprovalId", findings);
        Match(request.PolicySatisfaction?.PolicySatisfactionId, request.SourceApplyRequest?.PolicySatisfactionId, "SourceApplyRequestPolicySatisfactionMismatch", "SourceApplyRequest.PolicySatisfactionId", findings);
    }

    private static void ValidateSourceApplyPath(ReleaseReadinessReportRequest request, List<ReleaseReadinessReportFinding> findings)
    {
        var sourceRequest = request.SourceApplyRequest;
        var sourceReceipt = request.SourceApplyReceipt;
        if (sourceRequest is null)
        {
            Add(findings, "SourceApplyRequestRequired", nameof(request.SourceApplyRequest), "Source apply request evidence is required.");
            return;
        }

        if (sourceReceipt is null)
        {
            Add(findings, "SourceApplyReceiptRequired", nameof(request.SourceApplyReceipt), "Source apply receipt evidence is required.");
            return;
        }

        Match(sourceRequest.SourceApplyRequestId, sourceReceipt.SourceApplyRequestId, "SourceApplyReceiptRequestMismatch", nameof(sourceReceipt.SourceApplyRequestId), findings);
        Match(sourceRequest.SourceApplyRequestHash, sourceReceipt.SourceApplyRequestHash, "SourceApplyReceiptRequestHashMismatch", nameof(sourceReceipt.SourceApplyRequestHash), findings);
        var rollbackRecovered = request.RollbackExecutionReceipt?.RollbackSucceeded == true &&
                                request.RollbackExecutionReceipt.PartialRollbackOccurred == false &&
                                request.RollbackExecutionAuditReport?.EvidenceConsistent == true &&
                                request.RollbackExecutionAuditReport.RollbackSucceeded &&
                                request.RollbackExecutionAuditReport.PartialRollbackOccurred == false &&
                                request.RollbackExecutionAuditReport.Issues.Count == 0;
        if (!sourceReceipt.MutationOccurred) Add(findings, "SourceApplyMutationMissing", nameof(sourceReceipt.MutationOccurred), "Source apply receipt must record mutation evidence.");
        if (!sourceReceipt.ApplySucceeded && !rollbackRecovered) Add(findings, "SourceApplyFailed", nameof(sourceReceipt.ApplySucceeded), "Source apply receipt must report success or have successful rollback evidence.");
        if (sourceReceipt.PartialApplyOccurred && !rollbackRecovered) Add(findings, "PartialSourceApplyRequiresRollback", nameof(sourceReceipt.PartialApplyOccurred), "Partial source apply requires rollback evidence.");
    }

    private static void ValidateRollbackPath(ReleaseReadinessReportRequest request, List<ReleaseReadinessReportFinding> findings)
    {
        var rollbackReceipt = request.RollbackExecutionReceipt;
        var rollbackAudit = request.RollbackExecutionAuditReport;
        if (rollbackReceipt is null && rollbackAudit is null) return;
        if (rollbackReceipt is null) { Add(findings, "RollbackReceiptRequired", nameof(request.RollbackExecutionReceipt), "Rollback audit cannot stand without rollback receipt."); return; }
        if (rollbackAudit is null) { Add(findings, "RollbackAuditRequired", nameof(request.RollbackExecutionAuditReport), "Rollback receipt requires rollback audit."); return; }

        Match(request.SourceApplyReceipt?.SourceApplyReceiptId, rollbackReceipt.SourceApplyReceiptId, "RollbackSourceApplyReceiptMismatch", nameof(rollbackReceipt.SourceApplyReceiptId), findings);
        Match(request.SourceApplyReceipt?.SourceApplyReceiptHash, rollbackReceipt.SourceApplyReceiptHash, "RollbackSourceApplyReceiptHashMismatch", nameof(rollbackReceipt.SourceApplyReceiptHash), findings);
        Match(rollbackReceipt.RollbackExecutionReceiptId, rollbackAudit.RollbackExecutionReceiptId, "RollbackAuditReceiptMismatch", nameof(rollbackAudit.RollbackExecutionReceiptId), findings);
        Match(rollbackReceipt.RollbackExecutionReceiptHash, rollbackAudit.RollbackExecutionReceiptHash, "RollbackAuditReceiptHashMismatch", nameof(rollbackAudit.RollbackExecutionReceiptHash), findings);
        if (!rollbackReceipt.RollbackSucceeded) Add(findings, "RollbackFailed", nameof(rollbackReceipt.RollbackSucceeded), "Rollback receipt must report success.");
        if (rollbackReceipt.PartialRollbackOccurred) Add(findings, "RollbackPartial", nameof(rollbackReceipt.PartialRollbackOccurred), "Rollback receipt must not be partial.");
        if (!rollbackAudit.EvidenceConsistent) Add(findings, "RollbackAuditInconsistent", nameof(rollbackAudit.EvidenceConsistent), "Rollback audit must be consistent.");
        if (!rollbackAudit.RollbackSucceeded) Add(findings, "RollbackAuditFailed", nameof(rollbackAudit.RollbackSucceeded), "Rollback audit must report success.");
        if (rollbackAudit.PartialRollbackOccurred) Add(findings, "RollbackAuditPartial", nameof(rollbackAudit.PartialRollbackOccurred), "Rollback audit must not report partial rollback.");
        if (rollbackAudit.Issues.Count > 0) Add(findings, "RollbackAuditHasIssues", nameof(rollbackAudit.Issues), "Rollback audit issues block release readiness report completeness.");
        if (rollbackAudit.WorkflowBoundaryAllowsContinuation) Add(findings, "RollbackAuditContinuationAuthorityRejected", nameof(rollbackAudit.WorkflowBoundaryAllowsContinuation), "Rollback audit must not allow workflow continuation.");
        if (rollbackAudit.ReleaseBoundaryInfersReadiness) Add(findings, "RollbackAuditReadinessAuthorityRejected", nameof(rollbackAudit.ReleaseBoundaryInfersReadiness), "Rollback audit must not infer release readiness.");
    }

    private static void ValidateContinuationGate(ReleaseReadinessReportRequest request, List<ReleaseReadinessReportFinding> findings)
    {
        var gate = request.WorkflowContinuationGateEvaluation;
        if (gate is null)
        {
            Add(findings, "WorkflowContinuationGateRequired", nameof(request.WorkflowContinuationGateEvaluation), "Workflow continuation gate evidence is required.");
            return;
        }

        if (!gate.Satisfied || gate.Status != WorkflowContinuationGateStatuses.Satisfied) Add(findings, "WorkflowContinuationGateUnsatisfied", nameof(gate.Status), "Workflow continuation gate must be satisfied.");
        if (gate.Issues.Count > 0) Add(findings, "WorkflowContinuationGateHasIssues", nameof(gate.Issues), "Workflow continuation gate must not contain issues.");
        if (gate.WorkflowStateMutated) Add(findings, "GateMustNotMutateWorkflow", nameof(gate.WorkflowStateMutated), "Gate evidence must not mutate workflow state.");
        if (gate.WorkflowContinuationExecuted) Add(findings, "GateMustNotContinueWorkflow", nameof(gate.WorkflowContinuationExecuted), "Gate evidence must not execute workflow continuation.");
        if (gate.ReleaseReadinessInferred) Add(findings, "GateMustNotInferReleaseReadiness", nameof(gate.ReleaseReadinessInferred), "Gate evidence must not infer release readiness.");
        if (gate.ReleaseApproved) Add(findings, "GateMustNotApproveRelease", nameof(gate.ReleaseApproved), "Gate evidence must not approve release.");
    }

    private static void ValidateTransitionRecordBinding(ReleaseReadinessReportRequest request, List<ReleaseReadinessReportFinding> findings)
    {
        var record = request.WorkflowTransitionRecord;
        if (record is null)
        {
            Add(findings, "WorkflowTransitionRecordRequired", nameof(request.WorkflowTransitionRecord), "Workflow transition record evidence is required.");
            return;
        }

        Match(request.WorkflowRunId, record.WorkflowRunId, "WorkflowRunMismatch", nameof(record.WorkflowRunId), findings);
        Match(request.WorkflowStepId, record.WorkflowStepId, "WorkflowStepMismatch", nameof(record.WorkflowStepId), findings);
        Match(request.WorkflowContinuationGateEvaluation?.WorkflowContinuationGateEvaluationId, record.WorkflowContinuationGateEvaluationId, "WorkflowTransitionRecordGateMismatch", nameof(record.WorkflowContinuationGateEvaluationId), findings);
        Match(request.WorkflowContinuationGateEvaluation is null ? null : GovernedWorkflowContinuationHashing.ComputeGateEvaluationHash(request.WorkflowContinuationGateEvaluation), record.WorkflowContinuationGateEvaluationHash, "WorkflowTransitionRecordGateHashMismatch", nameof(record.WorkflowContinuationGateEvaluationHash), findings);
        Match(request.SourceApplyRequest?.SourceApplyRequestId, record.SourceApplyRequestId, "WorkflowTransitionRecordSourceApplyRequestMismatch", nameof(record.SourceApplyRequestId), findings);
        Match(request.SourceApplyRequest?.SourceApplyRequestHash, record.SourceApplyRequestHash, "WorkflowTransitionRecordSourceApplyRequestHashMismatch", nameof(record.SourceApplyRequestHash), findings);
        Match(request.SourceApplyReceipt?.SourceApplyReceiptId, record.SourceApplyReceiptId, "WorkflowTransitionRecordSourceApplyReceiptMismatch", nameof(record.SourceApplyReceiptId), findings);
        Match(request.SourceApplyReceipt?.SourceApplyReceiptHash, record.SourceApplyReceiptHash, "WorkflowTransitionRecordSourceApplyReceiptHashMismatch", nameof(record.SourceApplyReceiptHash), findings);
        if (request.RollbackExecutionReceipt is not null)
        {
            Match(request.RollbackExecutionReceipt.RollbackExecutionReceiptId, record.RollbackExecutionReceiptId, "WorkflowTransitionRecordRollbackMismatch", nameof(record.RollbackExecutionReceiptId), findings);
            Match(request.RollbackExecutionReceipt.RollbackExecutionReceiptHash, record.RollbackExecutionReceiptHash, "WorkflowTransitionRecordRollbackHashMismatch", nameof(record.RollbackExecutionReceiptHash), findings);
            Match(request.RollbackExecutionAuditReport is null ? null : ReleaseReadinessReportHashing.ComputeRollbackExecutionAuditReportHash(request.RollbackExecutionAuditReport), record.RollbackExecutionAuditReportHash, "WorkflowTransitionRecordRollbackAuditHashMismatch", nameof(record.RollbackExecutionAuditReportHash), findings);
        }

        if (record.ReleaseReadinessInferred) Add(findings, "WorkflowTransitionRecordReadinessRejected", nameof(record.ReleaseReadinessInferred), "Workflow transition record must not infer release readiness.");
        if (record.ReleaseApproved) Add(findings, "WorkflowTransitionRecordReleaseApprovalRejected", nameof(record.ReleaseApproved), "Workflow transition record must not approve release.");
        if (record.SourceApplyExecuted) Add(findings, "WorkflowTransitionRecordSourceApplyRejected", nameof(record.SourceApplyExecuted), "Workflow transition record must not execute source apply.");
        if (record.RollbackExecuted) Add(findings, "WorkflowTransitionRecordRollbackRejected", nameof(record.RollbackExecuted), "Workflow transition record must not execute rollback.");
    }

    private static void ScanTextGraph(ReleaseReadinessReportRequest request, List<ReleaseReadinessReportFinding> findings)
    {
        ScanText(request.WorkflowRunId, nameof(request.WorkflowRunId), findings);
        ScanText(request.WorkflowStepId, nameof(request.WorkflowStepId), findings);
        ScanText(request.SubjectKind, nameof(request.SubjectKind), findings);
        ScanText(request.SubjectId, nameof(request.SubjectId), findings);
        ScanText(request.SubjectHash, nameof(request.SubjectHash), findings);
        ScanText(request.Boundary, nameof(request.Boundary), findings);
        ScanTexts(request.EvidenceReferences, nameof(request.EvidenceReferences), findings);
        ScanTexts(request.BoundaryMaxims, nameof(request.BoundaryMaxims), findings);
    }

    private static void AddValidationIssues<T>(IEnumerable<T> validationIssues, string prefix, List<ReleaseReadinessReportFinding> findings)
    {
        foreach (var issue in validationIssues)
        {
            var code = issue?.GetType().GetProperty("Code")?.GetValue(issue)?.ToString() ?? "ValidationIssue";
            var field = issue?.GetType().GetProperty("Field")?.GetValue(issue)?.ToString() ?? prefix;
            var message = issue?.GetType().GetProperty("Message")?.GetValue(issue)?.ToString() ?? "Validation issue.";
            Add(findings, $"{prefix}.{code}", ReleaseReadinessFindingSeverities.Blocking, $"{prefix}.{field}", message);
        }
    }

    private static void RequireGuid(Guid value, string field, List<ReleaseReadinessReportFinding> findings)
    {
        if (value == Guid.Empty) Add(findings, "Required", field, "Value is required.");
    }

    private static void RequireText(string? value, string field, List<ReleaseReadinessReportFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(value)) { Add(findings, "Required", field, "Text value is required."); return; }
        ScanText(value, field, findings);
    }

    private static void RequireHash(string? value, string field, List<ReleaseReadinessReportFinding> findings)
    {
        RequireText(value, field, findings);
        if (!string.IsNullOrWhiteSpace(value) && !Safe(value).StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            Add(findings, "InvalidHash", field, "Hash must use sha256: prefix.");
    }

    private static void RequireList(IReadOnlyList<string>? values, string field, List<ReleaseReadinessReportFinding> findings)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace)) { Add(findings, "Required", field, "At least one non-empty value is required."); return; }
        ScanTexts(values, field, findings);
    }

    private static void Match(Guid? expected, Guid? actual, string code, string field, List<ReleaseReadinessReportFinding> findings)
    {
        if (!expected.HasValue || !actual.HasValue || expected.Value != actual.Value) Add(findings, code, field, "Evidence ID binding mismatch.");
    }

    private static void Match(string? expected, string? actual, string code, string field, List<ReleaseReadinessReportFinding> findings)
    {
        if (!string.Equals(Safe(expected), Safe(actual), StringComparison.Ordinal)) Add(findings, code, field, "Evidence text/hash binding mismatch.");
    }

    private static void ScanTexts(IEnumerable<string>? values, string field, List<ReleaseReadinessReportFinding> findings)
    {
        if (values is null) return;
        foreach (var value in values) ScanText(value, field, findings);
    }

    private static void ScanText(string? value, string field, List<ReleaseReadinessReportFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var normalized = value.Trim().ToLowerInvariant();
        if (ReleaseReadinessReportTextSafety.PrivateOrRawMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
            Add(findings, "PrivateOrRawMaterial", field, "Release readiness report material must not contain private/raw material.");
        foreach (var marker in ReleaseReadinessReportTextSafety.AuthorityMarkers)
        {
            if (ReleaseReadinessReportTextSafety.ContainsForbiddenAuthorityMarker(normalized, marker))
            {
                Add(findings, "AuthorityClaim", field, "Release readiness report material must not claim release, deployment, merge, source, rollback, workflow, git, memory, retrieval, agent, tool, or model authority.");
                return;
            }
        }
    }

    private static void Add(List<ReleaseReadinessReportFinding> findings, string code, string field, string message) =>
        Add(findings, code, ReleaseReadinessFindingSeverities.Blocking, field, message);

    private static void Add(List<ReleaseReadinessReportFinding> findings, string code, string severity, string field, string message) =>
        findings.Add(new ReleaseReadinessReportFinding { Code = code, Severity = severity, Field = field, Message = message });

    private static string Safe(string? value) => value?.Trim() ?? string.Empty;
    private static string? SafeNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IReadOnlyList<string> SafeList(IReadOnlyList<string>? values) => values?.Select(Safe).Where(value => value.Length > 0).ToArray() ?? [];
}

public static class ReleaseReadinessReportValidation
{
    public static ReleaseReadinessReportValidationResult Validate(ReleaseReadinessReport? report)
    {
        var issues = new List<ReleaseReadinessReportValidationIssue>();
        if (report is null)
        {
            issues.Add(new("ReportRequired", "report", "Release readiness report is required."));
            return new(issues);
        }

        if (report.ReleaseReadinessDecided) issues.Add(new("ReleaseReadinessDecisionRejected", nameof(report.ReleaseReadinessDecided), "Report must not decide release readiness."));
        if (report.ReleaseReady) issues.Add(new("ReleaseReadyRejected", nameof(report.ReleaseReady), "Report must not mark release ready."));
        if (report.ReleaseApproved) issues.Add(new("ReleaseApprovalRejected", nameof(report.ReleaseApproved), "Report must not approve release."));
        if (report.DeploymentApproved) issues.Add(new("DeploymentApprovalRejected", nameof(report.DeploymentApproved), "Report must not approve deployment."));
        if (report.MergeApproved) issues.Add(new("MergeApprovalRejected", nameof(report.MergeApproved), "Report must not approve merge."));
        if (report.SourceApplyExecutedByReport) issues.Add(new("SourceApplyExecutionRejected", nameof(report.SourceApplyExecutedByReport), "Report must not execute source apply."));
        if (report.RollbackExecutedByReport) issues.Add(new("RollbackExecutionRejected", nameof(report.RollbackExecutedByReport), "Report must not execute rollback."));
        if (report.WorkflowMutatedByReport) issues.Add(new("WorkflowMutationRejected", nameof(report.WorkflowMutatedByReport), "Report must not mutate workflow state."));
        if (report.GitOperationExecutedByReport) issues.Add(new("GitOperationRejected", nameof(report.GitOperationExecutedByReport), "Report must not execute git operations."));
        if (!report.HumanReviewRequiredForReadiness) issues.Add(new("HumanReviewForReadinessRequired", nameof(report.HumanReviewRequiredForReadiness), "Human review remains required for readiness."));
        if (!report.HumanReviewRequiredForReleaseApproval) issues.Add(new("HumanReviewForReleaseApprovalRequired", nameof(report.HumanReviewRequiredForReleaseApproval), "Human review remains required for release approval."));
        if (!string.Equals(report.ReleaseReadinessReportHash, ReleaseReadinessReportHashing.ComputeReportHash(report), StringComparison.Ordinal))
            issues.Add(new("ReportHashMismatch", nameof(report.ReleaseReadinessReportHash), "Report hash does not match recomputed hash."));
        if (report.Findings.Any(finding => finding.Code.Contains("ReleaseReady", StringComparison.OrdinalIgnoreCase) ||
                                           finding.Code.Contains("ApprovedForRelease", StringComparison.OrdinalIgnoreCase) ||
                                           finding.Code.Contains("SafeToShip", StringComparison.OrdinalIgnoreCase) ||
                                           finding.Code.Contains("CanDeploy", StringComparison.OrdinalIgnoreCase) ||
                                           finding.Code.Contains("CanMerge", StringComparison.OrdinalIgnoreCase)))
            issues.Add(new("FindingAuthorityNameRejected", nameof(report.Findings), "Findings must be evidence facts, not release decisions."));
        return new(issues);
    }
}

public static class ReleaseReadinessReportHashing
{
    public static string ComputeRollbackExecutionAuditReportHash(RollbackExecutionAuditReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Sha256Hex(Canonicalize(
            ("RollbackExecutionAuditReportId", report.RollbackExecutionAuditReportId.ToString("D")),
            ("ProjectId", report.ProjectId.ToString("D")),
            ("RollbackExecutionReceiptId", report.RollbackExecutionReceiptId.ToString("D")),
            ("RollbackExecutionReceiptHash", report.RollbackExecutionReceiptHash),
            ("SourceApplyReceiptId", report.SourceApplyReceiptId.ToString("D")),
            ("SourceApplyReceiptHash", report.SourceApplyReceiptHash),
            ("RollbackPlanId", report.RollbackPlanId.ToString("D")),
            ("RollbackPlanHash", report.RollbackPlanHash),
            ("RollbackSupportReceiptId", report.RollbackSupportReceiptId.ToString("D")),
            ("RollbackSupportReceiptHash", report.RollbackSupportReceiptHash),
            ("PatchArtifactId", report.PatchArtifactId.ToString("D")),
            ("PatchHash", report.PatchHash),
            ("ChangeSetHash", report.ChangeSetHash),
            ("EvidenceConsistent", report.EvidenceConsistent ? "true" : "false"),
            ("ReceiptHashValid", report.ReceiptHashValid ? "true" : "false"),
            ("FileResultHashesValid", report.FileResultHashesValid ? "true" : "false"),
            ("RollbackSucceeded", report.RollbackSucceeded ? "true" : "false"),
            ("MutationOccurred", report.MutationOccurred ? "true" : "false"),
            ("PartialRollbackOccurred", report.PartialRollbackOccurred ? "true" : "false"),
            ("WorkflowBoundaryAllowsContinuation", report.WorkflowBoundaryAllowsContinuation ? "true" : "false"),
            ("ReleaseBoundaryInfersReadiness", report.ReleaseBoundaryInfersReadiness ? "true" : "false"),
            ("HumanReviewRequired", report.HumanReviewRequired ? "true" : "false"),
            ("Issues", string.Join("\u001f", report.Issues.Select(issue => $"{Normalize(issue.Code)}:{Normalize(issue.Field)}:{Normalize(issue.Message)}").Order(StringComparer.Ordinal))),
            ("EvidenceReferences", string.Join("\u001f", report.EvidenceReferences.Select(Normalize).Order(StringComparer.Ordinal))),
            ("BoundaryMaxims", string.Join("\u001f", report.BoundaryMaxims.Select(Normalize).Order(StringComparer.Ordinal))),
            ("AuditedAtUtc", report.AuditedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("Boundary", report.Boundary)));
    }

    public static string ComputeReportHash(ReleaseReadinessReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Sha256Hex(Canonicalize(
            ("ProjectId", report.ProjectId.ToString("D")),
            ("ReleaseReadinessReportRequestId", report.ReleaseReadinessReportRequestId.ToString("D")),
            ("Status", report.Status),
            ("WorkflowRunId", report.WorkflowRunId),
            ("WorkflowStepId", report.WorkflowStepId),
            ("SubjectKind", report.SubjectKind),
            ("SubjectId", report.SubjectId),
            ("SubjectHash", report.SubjectHash),
            ("AcceptedApprovalId", report.AcceptedApprovalId.ToString("D")),
            ("AcceptedApprovalHash", report.AcceptedApprovalHash),
            ("PolicySatisfactionId", report.PolicySatisfactionId.ToString("D")),
            ("PolicySatisfactionHash", report.PolicySatisfactionHash),
            ("SourceApplyRequestId", report.SourceApplyRequestId.ToString("D")),
            ("SourceApplyRequestHash", report.SourceApplyRequestHash),
            ("SourceApplyReceiptId", report.SourceApplyReceiptId.ToString("D")),
            ("SourceApplyReceiptHash", report.SourceApplyReceiptHash),
            ("RollbackExecutionReceiptId", report.RollbackExecutionReceiptId?.ToString("D")),
            ("RollbackExecutionReceiptHash", report.RollbackExecutionReceiptHash),
            ("RollbackExecutionAuditReportId", report.RollbackExecutionAuditReportId?.ToString("D")),
            ("RollbackExecutionAuditReportHash", report.RollbackExecutionAuditReportHash),
            ("WorkflowContinuationGateEvaluationId", report.WorkflowContinuationGateEvaluationId.ToString("D")),
            ("WorkflowContinuationGateEvaluationHash", report.WorkflowContinuationGateEvaluationHash),
            ("WorkflowTransitionRecordId", report.WorkflowTransitionRecordId.ToString("D")),
            ("WorkflowTransitionRecordHash", report.WorkflowTransitionRecordHash),
            ("EvidenceBooleans", string.Join("|", report.ApprovalEvidencePresent, report.PolicyEvidencePresent, report.SourceApplySucceeded, report.SourceApplyPartial, report.RollbackWasExecuted, report.RollbackSucceeded, report.RollbackPartial, report.RollbackAuditConsistent, report.WorkflowContinuationSucceeded, report.WorkflowTransitionRecordValid)),
            ("AuthorityBooleans", string.Join("|", report.ReleaseReadinessDecided, report.ReleaseReady, report.ReleaseApproved, report.DeploymentApproved, report.MergeApproved, report.SourceApplyExecutedByReport, report.RollbackExecutedByReport, report.WorkflowMutatedByReport, report.GitOperationExecutedByReport, report.HumanReviewRequiredForReadiness, report.HumanReviewRequiredForReleaseApproval)),
            ("Findings", string.Join("\u001f", report.Findings.Select(finding => $"{Normalize(finding.Code)}:{Normalize(finding.Severity)}:{Normalize(finding.Field)}:{Normalize(finding.Message)}").Order(StringComparer.Ordinal))),
            ("EvidenceReferences", string.Join("\u001f", report.EvidenceReferences.Select(Normalize).Order(StringComparer.Ordinal))),
            ("BoundaryMaxims", string.Join("\u001f", report.BoundaryMaxims.Select(Normalize).Order(StringComparer.Ordinal))),
            ("ReportedAtUtc", report.ReportedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("Boundary", report.Boundary)));
    }

    private static string Canonicalize(params (string Key, string? Value)[] values) =>
        string.Join("\n", values.Select(value => $"{value.Key}={Normalize(value.Value)}"));

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}

internal static class ReleaseReadinessReportTextSafety
{
    public static readonly string[] PrivateOrRawMarkers =
    [
        "raw prompt", "rawprompt", "raw completion", "rawcompletion", "raw tool output", "rawtooloutput",
        "chain-of-thought", "chain of thought", "chainofthought", "scratchpad", "private reasoning", "hidden reasoning",
        "system prompt", "developer prompt", "entire patch", "entirepatch", "patch payload", "patchpayload",
        "password", "api_key", "secret", "private key", "bearer"
    ];

    public static readonly string[] AuthorityMarkers =
    [
        "release ready", "ready to release", "safe to release", "approved for release", "release approved",
        "deployment approved", "merge approved", "can deploy", "can merge", "source applied by report",
        "rollback executed by report", "workflow continued by report", "git committed", "git pushed",
        "tag created", "pull request created", "memory promoted", "retrieval activated", "agent dispatched",
        "tool executed", "model called"
    ];

    public static bool ContainsForbiddenAuthorityMarker(string normalized, string marker)
    {
        if (!normalized.Contains(marker, StringComparison.Ordinal)) return false;
        return !(normalized.Contains($"not {marker}", StringComparison.Ordinal) ||
                 normalized.Contains($"is not {marker}", StringComparison.Ordinal) ||
                 normalized.Contains($"does not {marker}", StringComparison.Ordinal) ||
                 normalized.Contains($"must not {marker}", StringComparison.Ordinal));
    }
}
