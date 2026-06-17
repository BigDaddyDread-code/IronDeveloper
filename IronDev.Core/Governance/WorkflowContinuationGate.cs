namespace IronDev.Core.Governance;

public static class WorkflowContinuationGateStatuses
{
    public const string Satisfied = "Satisfied";
    public const string Blocked = "Blocked";
}

public static class WorkflowContinuationGateBoundaryText
{
    public const string Boundary = """
        Workflow continuation gate satisfaction is evidence only.
        Workflow continuation gate satisfaction is not workflow continuation.
        Workflow continuation gate satisfaction is not workflow state mutation.
        Workflow continuation gate satisfaction is not release readiness.
        Workflow continuation gate satisfaction is not release approval.
        Workflow continuation gate satisfaction is not source apply.
        Workflow continuation gate satisfaction is not rollback execution.
        Workflow continuation gate satisfaction does not start another workflow step.
        Workflow continuation gate satisfaction does not call agents, models, tools, git, API, CLI, UI, memory, or retrieval.
        Human review remains required before actual workflow continuation.
        """;
}

public sealed record WorkflowContinuationGateRequest
{
    public required Guid WorkflowContinuationGateRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string ExpectedWorkflowStateHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required AcceptedApprovalRecord AcceptedApproval { get; init; }
    public required PolicySatisfactionRecord PolicySatisfaction { get; init; }
    public required SourceApplyRequest SourceApplyRequest { get; init; }
    public required SourceApplyReceipt SourceApplyReceipt { get; init; }
    public RollbackExecutionReceipt? RollbackExecutionReceipt { get; init; }
    public RollbackExecutionAuditReport? RollbackExecutionAuditReport { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = WorkflowContinuationGateBoundaryText.Boundary;
}

public sealed record WorkflowContinuationGateEvaluation
{
    public required Guid WorkflowContinuationGateEvaluationId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid WorkflowContinuationGateRequestId { get; init; }
    public required string Status { get; init; }
    public required bool Satisfied { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string ExpectedWorkflowStateHash { get; init; }
    public required Guid SourceApplyRequestId { get; init; }
    public required string SourceApplyRequestHash { get; init; }
    public required Guid SourceApplyReceiptId { get; init; }
    public required string SourceApplyReceiptHash { get; init; }
    public Guid? RollbackExecutionReceiptId { get; init; }
    public string? RollbackExecutionReceiptHash { get; init; }
    public Guid? RollbackExecutionAuditReportId { get; init; }
    public required bool SourceApplySucceeded { get; init; }
    public required bool SourceApplyPartial { get; init; }
    public required bool RollbackWasExecuted { get; init; }
    public required bool RollbackSucceeded { get; init; }
    public required bool RollbackPartial { get; init; }
    public required bool RollbackAuditConsistent { get; init; }
    public required bool WorkflowStateMutated { get; init; }
    public required bool WorkflowContinuationExecuted { get; init; }
    public required bool ReleaseReadinessInferred { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public required IReadOnlyList<WorkflowContinuationGateIssue> Issues { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = WorkflowContinuationGateBoundaryText.Boundary;
}

public sealed record WorkflowContinuationGateIssue(string Code, string Field, string Message);

public interface IWorkflowContinuationGateEvaluator
{
    WorkflowContinuationGateEvaluation Evaluate(WorkflowContinuationGateRequest? request);
}

public sealed class WorkflowContinuationGateEvaluator : IWorkflowContinuationGateEvaluator
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
        "workflow continued",
        "workflow can continue",
        "safe to continue",
        "release approved",
        "release ready",
        "release readiness",
        "policy satisfied as continuation authority",
        "approval grants continuation",
        "source apply approved",
        "source applied as continuation authority",
        "rollback cleaned up",
        "crash cleaned up",
        Join("git ", "committed"),
        Join("git ", "pushed"),
        "merged",
        "pull request created",
        "memory promoted",
        "retrieval activated"
    ];

    public WorkflowContinuationGateEvaluation Evaluate(WorkflowContinuationGateRequest? request)
    {
        var issues = new List<WorkflowContinuationGateIssue>();

        if (request is null)
        {
            Add(issues, "RequestRequired", "request", "Workflow continuation gate request is required.");
            return BuildEvaluation(null, issues);
        }

        ValidateBasicRequest(request, issues);
        AddValidationIssues(AcceptedApprovalValidation.Validate(request.AcceptedApproval).Issues, "AcceptedApproval", issues);
        AddValidationIssues(PolicySatisfactionValidation.Validate(request.PolicySatisfaction).Issues, "PolicySatisfaction", issues);
        AddValidationIssues(SourceApplyRequestValidation.Validate(request.SourceApplyRequest).Issues, "SourceApplyRequest", issues);
        AddValidationIssues(SourceApplyReceiptValidation.Validate(request.SourceApplyReceipt).Issues, "SourceApplyReceipt", issues);

        if (request.RollbackExecutionReceipt is not null)
        {
            AddValidationIssues(RollbackExecutionReceiptValidation.Validate(request.RollbackExecutionReceipt).Issues, "RollbackExecutionReceipt", issues);
        }

        ValidateApprovalPolicyBinding(request, issues);
        ValidateSourceApplyBinding(request, issues);
        ValidateRollbackPath(request, issues);
        ScanTextGraph(request, issues);

        return BuildEvaluation(request, issues);
    }

    private static WorkflowContinuationGateEvaluation BuildEvaluation(WorkflowContinuationGateRequest? request, IReadOnlyList<WorkflowContinuationGateIssue> issues)
    {
        var sourceRequest = request?.SourceApplyRequest;
        var sourceReceipt = request?.SourceApplyReceipt;
        var rollbackReceipt = request?.RollbackExecutionReceipt;
        var rollbackAudit = request?.RollbackExecutionAuditReport;
        var satisfied = issues.Count == 0;

        return new WorkflowContinuationGateEvaluation
        {
            WorkflowContinuationGateEvaluationId = Guid.NewGuid(),
            ProjectId = request?.ProjectId ?? Guid.Empty,
            WorkflowContinuationGateRequestId = request?.WorkflowContinuationGateRequestId ?? Guid.Empty,
            Status = satisfied ? WorkflowContinuationGateStatuses.Satisfied : WorkflowContinuationGateStatuses.Blocked,
            Satisfied = satisfied,
            WorkflowRunId = SafeOutputText(request?.WorkflowRunId),
            WorkflowStepId = SafeOutputText(request?.WorkflowStepId),
            ExpectedWorkflowStateHash = SafeOutputText(request?.ExpectedWorkflowStateHash),
            SourceApplyRequestId = sourceRequest?.SourceApplyRequestId ?? Guid.Empty,
            SourceApplyRequestHash = SafeOutputText(sourceRequest?.SourceApplyRequestHash),
            SourceApplyReceiptId = sourceReceipt?.SourceApplyReceiptId ?? Guid.Empty,
            SourceApplyReceiptHash = SafeOutputText(sourceReceipt?.SourceApplyReceiptHash),
            RollbackExecutionReceiptId = rollbackReceipt?.RollbackExecutionReceiptId,
            RollbackExecutionReceiptHash = SafeOutputNullableText(rollbackReceipt?.RollbackExecutionReceiptHash),
            RollbackExecutionAuditReportId = rollbackAudit?.RollbackExecutionAuditReportId,
            SourceApplySucceeded = sourceReceipt?.ApplySucceeded == true,
            SourceApplyPartial = sourceReceipt?.PartialApplyOccurred == true,
            RollbackWasExecuted = rollbackReceipt is not null,
            RollbackSucceeded = rollbackReceipt?.RollbackSucceeded == true,
            RollbackPartial = rollbackReceipt?.PartialRollbackOccurred == true,
            RollbackAuditConsistent = rollbackAudit?.EvidenceConsistent == true,
            WorkflowStateMutated = false,
            WorkflowContinuationExecuted = false,
            ReleaseReadinessInferred = false,
            ReleaseApproved = false,
            HumanReviewRequired = true,
            Issues = issues,
            EvaluatedAtUtc = request?.RequestedAtUtc == default ? DateTimeOffset.UtcNow : request?.RequestedAtUtc ?? DateTimeOffset.UtcNow,
            EvidenceReferences = SafeOutputList(request?.EvidenceReferences),
            BoundaryMaxims = SafeOutputList(request?.BoundaryMaxims),
            Boundary = WorkflowContinuationGateBoundaryText.Boundary
        };
    }

    private static void ValidateBasicRequest(WorkflowContinuationGateRequest request, List<WorkflowContinuationGateIssue> issues)
    {
        RequireGuid(request.WorkflowContinuationGateRequestId, nameof(request.WorkflowContinuationGateRequestId), issues);
        RequireGuid(request.ProjectId, nameof(request.ProjectId), issues);
        RequireText(request.WorkflowRunId, nameof(request.WorkflowRunId), issues);
        RequireText(request.WorkflowStepId, nameof(request.WorkflowStepId), issues);
        RequireHash(request.ExpectedWorkflowStateHash, nameof(request.ExpectedWorkflowStateHash), issues);
        RequireText(request.SubjectKind, nameof(request.SubjectKind), issues);
        RequireText(request.SubjectId, nameof(request.SubjectId), issues);
        RequireHash(request.SubjectHash, nameof(request.SubjectHash), issues);
        RequireList(request.EvidenceReferences, nameof(request.EvidenceReferences), issues);
        RequireList(request.BoundaryMaxims, nameof(request.BoundaryMaxims), issues);
        RequireText(request.Boundary, nameof(request.Boundary), issues);

        if (request.RequestedAtUtc == default)
        {
            Add(issues, "Required", nameof(request.RequestedAtUtc), "Requested timestamp is required.");
        }
    }

    private static void ValidateApprovalPolicyBinding(WorkflowContinuationGateRequest request, List<WorkflowContinuationGateIssue> issues)
    {
        var approval = request.AcceptedApproval;
        var policy = request.PolicySatisfaction;

        Match(request.ProjectId, approval.ProjectId, nameof(approval.ProjectId), "AcceptedApprovalProjectMismatch", issues);
        Match(request.ProjectId, policy.ProjectId, nameof(policy.ProjectId), "PolicySatisfactionProjectMismatch", issues);
        Match(policy.AcceptedApprovalId, approval.AcceptedApprovalId, nameof(policy.AcceptedApprovalId), "PolicyAcceptedApprovalMismatch", issues);
        Match(request.SubjectKind, policy.SubjectKind, nameof(policy.SubjectKind), "PolicySubjectKindMismatch", issues);
        Match(request.SubjectId, policy.SubjectId, nameof(policy.SubjectId), "PolicySubjectIdMismatch", issues);
        Match(request.SubjectHash, policy.SubjectHash, nameof(policy.SubjectHash), "PolicySubjectHashMismatch", issues);
        Match(request.SubjectKind, approval.ApprovalTargetKind, nameof(approval.ApprovalTargetKind), "ApprovalSubjectKindMismatch", issues);
        Match(request.SubjectId, approval.ApprovalTargetId, nameof(approval.ApprovalTargetId), "ApprovalSubjectIdMismatch", issues);
        Match(request.SubjectHash, approval.ApprovalTargetHash, nameof(approval.ApprovalTargetHash), "ApprovalSubjectHashMismatch", issues);

        if (approval.ExpiresAtUtc.HasValue && approval.ExpiresAtUtc.Value <= request.RequestedAtUtc)
        {
            Add(issues, "AcceptedApprovalExpired", nameof(approval.ExpiresAtUtc), "Accepted approval has expired.");
        }

        if (policy.ExpiresAtUtc.HasValue && policy.ExpiresAtUtc.Value <= request.RequestedAtUtc)
        {
            Add(issues, "PolicySatisfactionExpired", nameof(policy.ExpiresAtUtc), "Policy satisfaction has expired.");
        }
    }

    private static void ValidateSourceApplyBinding(WorkflowContinuationGateRequest request, List<WorkflowContinuationGateIssue> issues)
    {
        var sourceRequest = request.SourceApplyRequest;
        var sourceReceipt = request.SourceApplyReceipt;

        Match(request.ProjectId, sourceRequest.ProjectId, nameof(sourceRequest.ProjectId), "SourceApplyRequestProjectMismatch", issues);
        Match(request.ProjectId, sourceReceipt.ProjectId, nameof(sourceReceipt.ProjectId), "SourceApplyReceiptProjectMismatch", issues);
        Match(request.SubjectKind, sourceRequest.SubjectKind, nameof(sourceRequest.SubjectKind), "SourceApplySubjectKindMismatch", issues);
        Match(request.SubjectId, sourceRequest.SubjectId, nameof(sourceRequest.SubjectId), "SourceApplySubjectIdMismatch", issues);
        Match(request.SubjectHash, sourceRequest.SubjectHash, nameof(sourceRequest.SubjectHash), "SourceApplySubjectHashMismatch", issues);
        Match(sourceRequest.SourceApplyRequestId, sourceReceipt.SourceApplyRequestId, nameof(sourceReceipt.SourceApplyRequestId), "SourceApplyRequestReceiptIdMismatch", issues);
        Match(sourceRequest.SourceApplyRequestHash, sourceReceipt.SourceApplyRequestHash, nameof(sourceReceipt.SourceApplyRequestHash), "SourceApplyRequestReceiptHashMismatch", issues);
        Match(sourceRequest.SourceApplyGateEvaluationId, sourceReceipt.SourceApplyGateEvaluationId, nameof(sourceReceipt.SourceApplyGateEvaluationId), "SourceApplyGateEvaluationIdMismatch", issues);
        Match(sourceRequest.SourceApplyGateEvaluationHash, sourceReceipt.SourceApplyGateEvaluationHash, nameof(sourceReceipt.SourceApplyGateEvaluationHash), "SourceApplyGateEvaluationHashMismatch", issues);
        Match(sourceRequest.PatchArtifactId, sourceReceipt.PatchArtifactId, nameof(sourceReceipt.PatchArtifactId), "PatchArtifactIdMismatch", issues);
        Match(sourceRequest.PatchHash, sourceReceipt.PatchHash, nameof(sourceReceipt.PatchHash), "PatchHashMismatch", issues);
        Match(sourceRequest.ChangeSetHash, sourceReceipt.ChangeSetHash, nameof(sourceReceipt.ChangeSetHash), "ChangeSetHashMismatch", issues);
        Match(sourceRequest.RollbackSupportReceiptId, sourceReceipt.RollbackSupportReceiptId, nameof(sourceReceipt.RollbackSupportReceiptId), "RollbackSupportReceiptIdMismatch", issues);
        Match(sourceRequest.RollbackSupportReceiptHash, sourceReceipt.RollbackSupportReceiptHash, nameof(sourceReceipt.RollbackSupportReceiptHash), "RollbackSupportReceiptHashMismatch", issues);

        if (!sourceReceipt.MutationOccurred)
        {
            Add(issues, "SourceApplyMutationRequired", nameof(sourceReceipt.MutationOccurred), "Workflow continuation gate requires source apply mutation evidence.");
        }

        if (!sourceReceipt.ApplySucceeded && !sourceReceipt.PartialApplyOccurred)
        {
            Add(issues, "SourceApplySuccessOrPartialRequired", nameof(sourceReceipt.ApplySucceeded), "Workflow continuation gate requires successful source apply or partial source apply with successful rollback evidence.");
        }
    }

    private static void ValidateRollbackPath(WorkflowContinuationGateRequest request, List<WorkflowContinuationGateIssue> issues)
    {
        var sourceReceipt = request.SourceApplyReceipt;
        var rollbackReceipt = request.RollbackExecutionReceipt;
        var rollbackAudit = request.RollbackExecutionAuditReport;

        if (rollbackReceipt is null && rollbackAudit is null)
        {
            if (!sourceReceipt.ApplySucceeded || sourceReceipt.PartialApplyOccurred)
            {
                Add(issues, "SourceApplyRequiresSuccessfulRollbackEvidence", nameof(request.SourceApplyReceipt), "Failed or partial source apply cannot satisfy workflow continuation gate without successful rollback evidence.");
            }

            return;
        }

        if (rollbackReceipt is null)
        {
            Add(issues, "RollbackReceiptRequired", nameof(request.RollbackExecutionReceipt), "Rollback audit report cannot satisfy the gate without rollback execution receipt evidence.");
            return;
        }

        if (rollbackAudit is null)
        {
            Add(issues, "RollbackAuditRequired", nameof(request.RollbackExecutionAuditReport), "Rollback execution receipt cannot satisfy the gate without rollback audit evidence.");
            return;
        }

        ValidateRollbackAuditReport(rollbackAudit, issues);
        Match(request.ProjectId, rollbackReceipt.ProjectId, nameof(rollbackReceipt.ProjectId), "RollbackReceiptProjectMismatch", issues);
        Match(request.ProjectId, rollbackAudit.ProjectId, nameof(rollbackAudit.ProjectId), "RollbackAuditProjectMismatch", issues);
        Match(sourceReceipt.SourceApplyReceiptId, rollbackReceipt.SourceApplyReceiptId, nameof(rollbackReceipt.SourceApplyReceiptId), "RollbackSourceApplyReceiptIdMismatch", issues);
        Match(sourceReceipt.SourceApplyReceiptHash, rollbackReceipt.SourceApplyReceiptHash, nameof(rollbackReceipt.SourceApplyReceiptHash), "RollbackSourceApplyReceiptHashMismatch", issues);
        Match(rollbackReceipt.RollbackExecutionReceiptId, rollbackAudit.RollbackExecutionReceiptId, nameof(rollbackAudit.RollbackExecutionReceiptId), "RollbackAuditReceiptIdMismatch", issues);
        Match(rollbackReceipt.RollbackExecutionReceiptHash, rollbackAudit.RollbackExecutionReceiptHash, nameof(rollbackAudit.RollbackExecutionReceiptHash), "RollbackAuditReceiptHashMismatch", issues);
        Match(sourceReceipt.SourceApplyReceiptId, rollbackAudit.SourceApplyReceiptId, nameof(rollbackAudit.SourceApplyReceiptId), "RollbackAuditSourceApplyReceiptIdMismatch", issues);
        Match(sourceReceipt.SourceApplyReceiptHash, rollbackAudit.SourceApplyReceiptHash, nameof(rollbackAudit.SourceApplyReceiptHash), "RollbackAuditSourceApplyReceiptHashMismatch", issues);

        if (!rollbackReceipt.RollbackSucceeded)
        {
            Add(issues, "RollbackReceiptFailed", nameof(rollbackReceipt.RollbackSucceeded), "Failed rollback execution cannot satisfy workflow continuation gate.");
        }

        if (rollbackReceipt.PartialRollbackOccurred)
        {
            Add(issues, "RollbackReceiptPartial", nameof(rollbackReceipt.PartialRollbackOccurred), "Partial rollback execution cannot satisfy workflow continuation gate.");
        }

        if (!rollbackReceipt.MutationOccurred && !RollbackReceiptOnlyNoop(rollbackReceipt))
        {
            Add(issues, "RollbackReceiptMutationRequired", nameof(rollbackReceipt.MutationOccurred), "Successful non-noop rollback execution requires mutation evidence.");
        }

        if (!rollbackAudit.EvidenceConsistent)
        {
            Add(issues, "RollbackAuditInconsistent", nameof(rollbackAudit.EvidenceConsistent), "Inconsistent rollback audit cannot satisfy workflow continuation gate.");
        }

        if (!rollbackAudit.RollbackSucceeded)
        {
            Add(issues, "RollbackAuditFailed", nameof(rollbackAudit.RollbackSucceeded), "Rollback audit must report rollback success.");
        }

        if (rollbackAudit.PartialRollbackOccurred)
        {
            Add(issues, "RollbackAuditPartial", nameof(rollbackAudit.PartialRollbackOccurred), "Rollback audit must not report partial rollback.");
        }

        if (!rollbackAudit.HumanReviewRequired)
        {
            Add(issues, "RollbackAuditHumanReviewRequired", nameof(rollbackAudit.HumanReviewRequired), "Rollback audit must preserve human review requirement.");
        }

        if (rollbackAudit.WorkflowBoundaryAllowsContinuation)
        {
            Add(issues, "RollbackAuditCannotAllowContinuation", nameof(rollbackAudit.WorkflowBoundaryAllowsContinuation), "Rollback audit cannot allow workflow continuation.");
        }

        if (rollbackAudit.ReleaseBoundaryInfersReadiness)
        {
            Add(issues, "RollbackAuditCannotInferReleaseReadiness", nameof(rollbackAudit.ReleaseBoundaryInfersReadiness), "Rollback audit cannot infer release readiness.");
        }

        if (rollbackAudit.Issues.Count > 0)
        {
            Add(issues, "RollbackAuditIssuesPresent", nameof(rollbackAudit.Issues), "Rollback audit issues block workflow continuation gate satisfaction.");
        }
    }

    private static void ValidateRollbackAuditReport(RollbackExecutionAuditReport report, List<WorkflowContinuationGateIssue> issues)
    {
        RequireGuid(report.RollbackExecutionAuditReportId, nameof(report.RollbackExecutionAuditReportId), issues);
        RequireGuid(report.ProjectId, nameof(report.ProjectId), issues);
        RequireGuid(report.RollbackExecutionReceiptId, nameof(report.RollbackExecutionReceiptId), issues);
        RequireHash(report.RollbackExecutionReceiptHash, nameof(report.RollbackExecutionReceiptHash), issues);
        RequireGuid(report.SourceApplyReceiptId, nameof(report.SourceApplyReceiptId), issues);
        RequireHash(report.SourceApplyReceiptHash, nameof(report.SourceApplyReceiptHash), issues);
        RequireGuid(report.RollbackPlanId, nameof(report.RollbackPlanId), issues);
        RequireHash(report.RollbackPlanHash, nameof(report.RollbackPlanHash), issues);
        RequireGuid(report.RollbackSupportReceiptId, nameof(report.RollbackSupportReceiptId), issues);
        RequireHash(report.RollbackSupportReceiptHash, nameof(report.RollbackSupportReceiptHash), issues);
        RequireGuid(report.PatchArtifactId, nameof(report.PatchArtifactId), issues);
        RequireHash(report.PatchHash, nameof(report.PatchHash), issues);
        RequireHash(report.ChangeSetHash, nameof(report.ChangeSetHash), issues);
        RequireList(report.EvidenceReferences, nameof(report.EvidenceReferences), issues);
        RequireList(report.BoundaryMaxims, nameof(report.BoundaryMaxims), issues);
        RequireText(report.Boundary, nameof(report.Boundary), issues);

        if (report.AuditedAtUtc == default)
        {
            Add(issues, "Required", nameof(report.AuditedAtUtc), "Rollback audit timestamp is required.");
        }
    }

    private static void ScanTextGraph(WorkflowContinuationGateRequest request, List<WorkflowContinuationGateIssue> issues)
    {
        ScanText(request.WorkflowRunId, nameof(request.WorkflowRunId), issues);
        ScanText(request.WorkflowStepId, nameof(request.WorkflowStepId), issues);
        ScanText(request.ExpectedWorkflowStateHash, nameof(request.ExpectedWorkflowStateHash), issues);
        ScanText(request.SubjectKind, nameof(request.SubjectKind), issues);
        ScanText(request.SubjectId, nameof(request.SubjectId), issues);
        ScanText(request.SubjectHash, nameof(request.SubjectHash), issues);
        ScanText(request.Boundary, nameof(request.Boundary), issues);
        ScanTexts(request.EvidenceReferences, nameof(request.EvidenceReferences), issues);
        ScanTexts(request.BoundaryMaxims, nameof(request.BoundaryMaxims), issues);

        var approval = request.AcceptedApproval;
        ScanText(approval.ApprovalTargetKind, nameof(approval.ApprovalTargetKind), issues);
        ScanText(approval.ApprovalTargetId, nameof(approval.ApprovalTargetId), issues);
        ScanText(approval.ApprovalTargetHash, nameof(approval.ApprovalTargetHash), issues);
        ScanText(approval.CapabilityCode, nameof(approval.CapabilityCode), issues);
        ScanText(approval.ApprovalPurpose, nameof(approval.ApprovalPurpose), issues);
        ScanText(approval.ApprovedByActorId, nameof(approval.ApprovedByActorId), issues);
        ScanText(approval.ApprovedByActorDisplayName, nameof(approval.ApprovedByActorDisplayName), issues);
        ScanTexts(approval.EvidenceReferences, nameof(approval.EvidenceReferences), issues);
        ScanTexts(approval.BoundaryMaxims, nameof(approval.BoundaryMaxims), issues);

        var policy = request.PolicySatisfaction;
        ScanText(policy.PolicyCode, nameof(policy.PolicyCode), issues);
        ScanText(policy.PolicyVersion, nameof(policy.PolicyVersion), issues);
        ScanText(policy.SubjectKind, nameof(policy.SubjectKind), issues);
        ScanText(policy.SubjectId, nameof(policy.SubjectId), issues);
        ScanText(policy.SubjectHash, nameof(policy.SubjectHash), issues);
        ScanText(policy.CapabilityCode, nameof(policy.CapabilityCode), issues);
        ScanText(policy.ApprovalRequirementHash, nameof(policy.ApprovalRequirementHash), issues);
        ScanText(policy.Boundary, nameof(policy.Boundary), issues);
        ScanTexts(policy.EvidenceReferences, nameof(policy.EvidenceReferences), issues);
        ScanTexts(policy.BoundaryMaxims, nameof(policy.BoundaryMaxims), issues);

        ScanTexts(request.SourceApplyRequest.EvidenceReferences, "SourceApplyRequest.EvidenceReferences", issues);
        ScanTexts(request.SourceApplyRequest.BoundaryMaxims, "SourceApplyRequest.BoundaryMaxims", issues);
        ScanTexts(request.SourceApplyReceipt.EvidenceReferences, "SourceApplyReceipt.EvidenceReferences", issues);
        ScanTexts(request.SourceApplyReceipt.BoundaryMaxims, "SourceApplyReceipt.BoundaryMaxims", issues);
        ScanTexts(request.SourceApplyReceipt.IssueCodes, "SourceApplyReceipt.IssueCodes", issues);

        if (request.RollbackExecutionReceipt is not null)
        {
            ScanTexts(request.RollbackExecutionReceipt.EvidenceReferences, "RollbackExecutionReceipt.EvidenceReferences", issues);
            ScanTexts(request.RollbackExecutionReceipt.BoundaryMaxims, "RollbackExecutionReceipt.BoundaryMaxims", issues);
            ScanTexts(request.RollbackExecutionReceipt.IssueCodes, "RollbackExecutionReceipt.IssueCodes", issues);
        }

        if (request.RollbackExecutionAuditReport is not null)
        {
            ScanTexts(request.RollbackExecutionAuditReport.EvidenceReferences, "RollbackExecutionAuditReport.EvidenceReferences", issues);
            ScanTexts(request.RollbackExecutionAuditReport.BoundaryMaxims, "RollbackExecutionAuditReport.BoundaryMaxims", issues);
            ScanText(request.RollbackExecutionAuditReport.Boundary, "RollbackExecutionAuditReport.Boundary", issues);
            foreach (var issue in request.RollbackExecutionAuditReport.Issues)
            {
                ScanText(issue.Code, "RollbackExecutionAuditReport.Issues.Code", issues);
                ScanText(issue.Field, "RollbackExecutionAuditReport.Issues.Field", issues);
                ScanText(issue.Message, "RollbackExecutionAuditReport.Issues.Message", issues);
            }
        }
    }

    private static bool RollbackReceiptOnlyNoop(RollbackExecutionReceipt receipt) =>
        receipt.FileResults.Count > 0 && receipt.FileResults.All(result => string.Equals(result.OperationKind, RollbackPlanFileActionKinds.Noop, StringComparison.Ordinal));

    private static void AddValidationIssues<T>(IEnumerable<T> validationIssues, string prefix, List<WorkflowContinuationGateIssue> issues)
    {
        foreach (var issue in validationIssues)
        {
            var code = issue?.GetType().GetProperty("Code")?.GetValue(issue)?.ToString() ?? "ValidationIssue";
            var field = issue?.GetType().GetProperty("Field")?.GetValue(issue)?.ToString() ?? prefix;
            var message = issue?.GetType().GetProperty("Message")?.GetValue(issue)?.ToString() ?? "Validation issue.";
            Add(issues, $"{prefix}.{code}", field, message);
        }
    }

    private static void RequireGuid(Guid value, string field, List<WorkflowContinuationGateIssue> issues)
    {
        if (value == Guid.Empty)
        {
            Add(issues, "Required", field, "Value is required.");
        }
    }

    private static void RequireText(string? value, string field, List<WorkflowContinuationGateIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, "Required", field, "Text value is required.");
            return;
        }

        ScanText(value, field, issues);
    }

    private static void RequireHash(string? value, string field, List<WorkflowContinuationGateIssue> issues)
    {
        RequireText(value, field, issues);
        if (!string.IsNullOrWhiteSpace(value) && !value.Trim().StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            Add(issues, "InvalidHash", field, "Hash must use sha256: prefix.");
        }
    }

    private static void RequireList(IReadOnlyList<string>? values, string field, List<WorkflowContinuationGateIssue> issues)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, "Required", field, "At least one non-empty value is required.");
            return;
        }

        ScanTexts(values, field, issues);
    }

    private static void Match(Guid expected, Guid actual, string field, string code, List<WorkflowContinuationGateIssue> issues)
    {
        if (expected != actual)
        {
            Add(issues, code, field, "Evidence id binding mismatch.");
        }
    }

    private static void Match(string? expected, string? actual, string field, string code, List<WorkflowContinuationGateIssue> issues)
    {
        if (!string.Equals(Normalize(expected), Normalize(actual), StringComparison.Ordinal))
        {
            Add(issues, code, field, "Evidence text/hash binding mismatch.");
        }
    }

    private static void ScanTexts(IEnumerable<string>? values, string field, List<WorkflowContinuationGateIssue> issues)
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

    private static void ScanText(string? value, string field, List<WorkflowContinuationGateIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (PrivateOrRawMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
        {
            Add(issues, "PrivateOrRawMaterial", field, "Workflow continuation gate material must not contain private/raw markers.");
        }

        foreach (var marker in AuthorityMarkers)
        {
            if (ContainsForbiddenAuthorityMarker(normalized, marker))
            {
                Add(issues, "AuthorityClaim", field, "Workflow continuation gate material must not contain continuation, release, git, memory, or retrieval authority claims.");
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
                 normalized.Contains($"does not infer {marker}", StringComparison.Ordinal) ||
                 normalized.Contains("does not declare the crash cleaned up", StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> SafeOutputList(IReadOnlyList<string>? values) =>
        values?.Select(SafeOutputText).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];

    private static string? SafeOutputNullableText(string? value)
    {
        var safe = SafeOutputText(value);
        return string.IsNullOrWhiteSpace(safe) ? null : safe;
    }

    private static string SafeOutputText(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (PrivateOrRawMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return string.Empty;
        }

        foreach (var marker in AuthorityMarkers)
        {
            if (ContainsForbiddenAuthorityMarker(normalized.ToLowerInvariant(), marker))
            {
                return string.Empty;
            }
        }

        return normalized;
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static void Add(List<WorkflowContinuationGateIssue> issues, string code, string field, string message) =>
        issues.Add(new WorkflowContinuationGateIssue(code, field, message));

    private static string Join(string left, string right) => left + right;
}
