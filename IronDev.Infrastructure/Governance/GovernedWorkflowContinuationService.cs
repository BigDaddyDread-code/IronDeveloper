using Dapper;
using IronDev.Core.Governance;
using IronDev.Core.Workflow;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class GovernedWorkflowContinuationService : IGovernedWorkflowContinuationService
{
    private readonly IWorkflowRunStore _workflowRunStore;
    private readonly IControlledWorkflowStateTransitionStore _transitionStore;
    private readonly IWorkflowTransitionRecordStore _recordStore;
    private readonly IWorkflowContinuationGateEvaluator _gateEvaluator;

    public GovernedWorkflowContinuationService(
        IWorkflowRunStore workflowRunStore,
        IControlledWorkflowStateTransitionStore transitionStore,
        IWorkflowTransitionRecordStore recordStore)
        : this(workflowRunStore, transitionStore, recordStore, new WorkflowContinuationGateEvaluator())
    {
    }

    public GovernedWorkflowContinuationService(
        IWorkflowRunStore workflowRunStore,
        IControlledWorkflowStateTransitionStore transitionStore,
        IWorkflowTransitionRecordStore recordStore,
        IWorkflowContinuationGateEvaluator gateEvaluator)
    {
        _workflowRunStore = workflowRunStore ?? throw new ArgumentNullException(nameof(workflowRunStore));
        _transitionStore = transitionStore ?? throw new ArgumentNullException(nameof(transitionStore));
        _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
        _gateEvaluator = gateEvaluator ?? throw new ArgumentNullException(nameof(gateEvaluator));
    }

    public async Task<GovernedWorkflowContinuationResult> ContinueAsync(
        GovernedWorkflowContinuationRequest? request,
        CancellationToken cancellationToken = default)
    {
        var requestIssues = GovernedWorkflowContinuationValidation.ValidateRequest(request);
        if (request is null || requestIssues.Count > 0)
            return Rejected(requestIssues);

        var freshGate = _gateEvaluator.Evaluate(BuildGateRequest(request));
        var freshGateHash = GovernedWorkflowContinuationHashing.ComputeGateEvaluationHash(freshGate);
        var freshGateIssues = ValidateFreshGate(request, freshGate, freshGateHash);
        if (freshGateIssues.Count > 0)
            return Rejected(freshGateIssues);

        if (!Guid.TryParse(request.WorkflowRunId, out var workflowRunId))
            return Rejected([Issue("InvalidWorkflowRunId", nameof(request.WorkflowRunId), "WorkflowRunId must be a GUID for governed continuation.")]);

        var run = await _workflowRunStore.GetAsync(request.ProjectId, workflowRunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return Rejected([Issue("WorkflowRunNotFound", nameof(request.WorkflowRunId), "Workflow run was not found.")]);

        var currentStep = FindStep(run, request.CurrentWorkflowStepId);
        if (currentStep is null)
            return Rejected([Issue("WorkflowStepNotFound", nameof(request.CurrentWorkflowStepId), "Current workflow step was not found.")]);

        var actualWorkflowHash = GovernedWorkflowContinuationHashing.ComputeWorkflowStateHash(run);
        var actualStepHash = GovernedWorkflowContinuationHashing.ComputeStepStateHash(currentStep);
        var stateIssues = new List<GovernedWorkflowContinuationIssue>();
        if (!MatchesHash(actualWorkflowHash, request.ExpectedWorkflowStateHash))
            stateIssues.Add(Issue("WorkflowStateHashMismatch", nameof(request.ExpectedWorkflowStateHash), "Expected workflow state hash does not match current workflow state."));
        if (!MatchesHash(actualStepHash, request.ExpectedCurrentStepStateHash))
            stateIssues.Add(Issue("StepStateHashMismatch", nameof(request.ExpectedCurrentStepStateHash), "Expected current step state hash does not match current step state."));
        if (!IsContinuable(run.Status))
            stateIssues.Add(Issue("WorkflowRunStatusRejected", nameof(run.Status), "Workflow run status is not continuable."));
        if (!IsContinuable(currentStep.Status))
            stateIssues.Add(Issue("WorkflowStepStatusRejected", nameof(currentStep.Status), "Current workflow step status is not continuable."));

        var nextStep = ResolveNextStep(run, request, currentStep, stateIssues);
        if (stateIssues.Count > 0)
            return Rejected(stateIssues);

        var transitionRequest = BuildTransitionRequest(run, currentStep, nextStep, request.TransitionKind);
        var transition = await _transitionStore.TransitionAsync(transitionRequest, cancellationToken).ConfigureAwait(false);
        if (!transition.Succeeded)
            return Rejected(transition.Issues);

        var mutatedRun = await _workflowRunStore.GetAsync(request.ProjectId, workflowRunId, cancellationToken).ConfigureAwait(false);
        if (mutatedRun is null)
            return FailedAfterMutation([Issue("WorkflowRunReloadFailed", nameof(request.WorkflowRunId), "Workflow state mutation completed but workflow run could not be reloaded.")]);

        var mutatedCurrentStep = FindStep(mutatedRun, currentStep.WorkflowRunStepId.ToString("D"));
        if (mutatedCurrentStep is null)
            return FailedAfterMutation([Issue("WorkflowStepReloadFailed", nameof(request.CurrentWorkflowStepId), "Workflow state mutation completed but current workflow step could not be reloaded.")]);

        var record = BuildTransitionRecord(request, freshGate, freshGateHash, run, currentStep, mutatedRun, mutatedCurrentStep, nextStep);
        var recordValidation = WorkflowTransitionRecordValidation.Validate(record);
        if (!recordValidation.IsValid)
        {
            return FailedAfterMutation(recordValidation.Issues
                .Select(issue => Issue(issue.Code, issue.Field, issue.Message))
                .ToArray());
        }

        try
        {
            await _recordStore.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return FailedAfterMutation([Issue("TransitionRecordSaveFailed", nameof(IWorkflowTransitionRecordStore), $"Workflow state mutated but transition record save failed: {ex.Message}")]);
        }

        return new GovernedWorkflowContinuationResult
        {
            Status = GovernedWorkflowContinuationStatuses.Transitioned,
            Succeeded = true,
            WorkflowStateMutated = true,
            StepCompleted = true,
            NextStepStarted = request.TransitionKind == WorkflowTransitionKinds.ContinueToNextStep,
            ReleaseReadinessInferred = false,
            ReleaseApproved = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            WorkflowTransitionRecord = record,
            Issues = [],
            Warnings = GovernedWorkflowContinuationBoundaryText.Warnings,
            Boundary = GovernedWorkflowContinuationBoundaryText.Boundary
        };
    }

    private static ControlledWorkflowStateTransitionRequest BuildTransitionRequest(
        WorkflowRun run,
        WorkflowRunStep currentStep,
        WorkflowRunStep? nextStep,
        string transitionKind)
    {
        var continueToNext = transitionKind == WorkflowTransitionKinds.ContinueToNextStep;
        return new ControlledWorkflowStateTransitionRequest
        {
            ProjectId = run.ProjectId,
            WorkflowRunId = run.WorkflowRunId,
            CurrentWorkflowRunStepId = currentStep.WorkflowRunStepId,
            NextWorkflowRunStepId = nextStep?.WorkflowRunStepId,
            TransitionKind = transitionKind,
            ExpectedWorkflowStatus = run.Status,
            ExpectedCurrentStepStatus = currentStep.Status,
            ExpectedNextStepStatus = nextStep?.Status,
            NewWorkflowStatus = continueToNext ? WorkflowRunStatus.ReadyForReview : WorkflowRunStatus.Completed,
            NewCurrentStepStatus = WorkflowRunStatus.Completed,
            NewNextStepStatus = continueToNext ? WorkflowRunStatus.ReadyForReview : null
        };
    }

    private static WorkflowTransitionRecord BuildTransitionRecord(
        GovernedWorkflowContinuationRequest request,
        WorkflowContinuationGateEvaluation freshGate,
        string freshGateHash,
        WorkflowRun previousRun,
        WorkflowRunStep previousStep,
        WorkflowRun newRun,
        WorkflowRunStep newStep,
        WorkflowRunStep? nextStep)
    {
        var record = new WorkflowTransitionRecord
        {
            WorkflowTransitionRecordId = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            WorkflowRunId = previousRun.WorkflowRunId.ToString("D"),
            WorkflowStepId = previousStep.WorkflowRunStepId.ToString("D"),
            TransitionKind = request.TransitionKind,
            PreviousWorkflowStateHash = GovernedWorkflowContinuationHashing.ComputeWorkflowStateHash(previousRun),
            NewWorkflowStateHash = GovernedWorkflowContinuationHashing.ComputeWorkflowStateHash(newRun),
            PreviousStepStateHash = GovernedWorkflowContinuationHashing.ComputeStepStateHash(previousStep),
            NewStepStateHash = GovernedWorkflowContinuationHashing.ComputeStepStateHash(newStep),
            PreviousStepId = previousStep.WorkflowRunStepId.ToString("D"),
            NextStepId = nextStep?.WorkflowRunStepId.ToString("D"),
            WorkflowContinuationGateEvaluationId = freshGate.WorkflowContinuationGateEvaluationId,
            WorkflowContinuationGateEvaluationHash = freshGateHash,
            SourceApplyRequestId = freshGate.SourceApplyRequestId,
            SourceApplyRequestHash = freshGate.SourceApplyRequestHash,
            SourceApplyReceiptId = freshGate.SourceApplyReceiptId,
            SourceApplyReceiptHash = freshGate.SourceApplyReceiptHash,
            RollbackExecutionReceiptId = freshGate.RollbackExecutionReceiptId,
            RollbackExecutionReceiptHash = freshGate.RollbackExecutionReceiptHash,
            RollbackExecutionAuditReportId = freshGate.RollbackExecutionAuditReportId,
            RollbackExecutionAuditReportHash = freshGate.RollbackExecutionAuditReportId.HasValue ? "sha256:rollback-audit-reference-from-gate" : null,
            WorkflowStateMutated = true,
            StepCompleted = true,
            NextStepStarted = request.TransitionKind == WorkflowTransitionKinds.ContinueToNextStep,
            ReleaseReadinessInferred = false,
            ReleaseApproved = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            TransitionedAtUtc = DateTimeOffset.UtcNow,
            WorkflowTransitionRecordHash = "sha256:placeholder",
            EvidenceReferences = request.EvidenceReferences
                .Concat(freshGate.EvidenceReferences)
                .Append($"workflow-continuation-gate:{freshGate.WorkflowContinuationGateEvaluationId:D}")
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            BoundaryMaxims = request.BoundaryMaxims
                .Concat(freshGate.BoundaryMaxims)
                .Concat(GovernedWorkflowContinuationBoundaryText.Warnings)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Boundary = WorkflowTransitionRecordBoundaryText.Boundary
        };

        return record with { WorkflowTransitionRecordHash = WorkflowTransitionRecordHashing.ComputeRecordHash(record) };
    }

    private static WorkflowContinuationGateRequest BuildGateRequest(GovernedWorkflowContinuationRequest request) => new()
    {
        WorkflowContinuationGateRequestId = request.WorkflowContinuationGateEvaluation.WorkflowContinuationGateRequestId,
        ProjectId = request.ProjectId,
        WorkflowRunId = request.WorkflowRunId,
        WorkflowStepId = request.CurrentWorkflowStepId,
        ExpectedWorkflowStateHash = request.ExpectedWorkflowStateHash,
        SubjectKind = request.SourceApplyRequest.SubjectKind,
        SubjectId = request.SourceApplyRequest.SubjectId,
        SubjectHash = request.SourceApplyRequest.SubjectHash,
        AcceptedApproval = request.AcceptedApproval,
        PolicySatisfaction = request.PolicySatisfaction,
        SourceApplyRequest = request.SourceApplyRequest,
        SourceApplyReceipt = request.SourceApplyReceipt,
        RollbackExecutionReceipt = request.RollbackExecutionReceipt,
        RollbackExecutionAuditReport = request.RollbackExecutionAuditReport,
        RequestedAtUtc = request.RequestedAtUtc,
        EvidenceReferences = request.EvidenceReferences,
        BoundaryMaxims = request.BoundaryMaxims,
        Boundary = WorkflowContinuationGateBoundaryText.Boundary
    };

    private static IReadOnlyList<GovernedWorkflowContinuationIssue> ValidateFreshGate(
        GovernedWorkflowContinuationRequest request,
        WorkflowContinuationGateEvaluation freshGate,
        string freshGateHash)
    {
        var issues = new List<GovernedWorkflowContinuationIssue>();
        if (!freshGate.Satisfied || !string.Equals(freshGate.Status, WorkflowContinuationGateStatuses.Satisfied, StringComparison.Ordinal))
            issues.Add(Issue("FreshGateNotSatisfied", nameof(request.WorkflowContinuationGateEvaluation), "Recomputed workflow continuation gate is not satisfied."));
        if (freshGate.Issues.Count > 0)
            issues.AddRange(freshGate.Issues.Select(issue => Issue($"FreshGate.{issue.Code}", $"WorkflowContinuationGate.{issue.Field}", issue.Message)));
        if (!MatchesHash(freshGateHash, request.WorkflowContinuationGateEvaluationHash))
            issues.Add(Issue("FreshGateHashMismatch", nameof(request.WorkflowContinuationGateEvaluationHash), "Recomputed workflow continuation gate hash does not match the supplied gate hash."));
        if (!MatchesHash(GovernedWorkflowContinuationHashing.ComputeGateEvaluationHash(request.WorkflowContinuationGateEvaluation), request.WorkflowContinuationGateEvaluationHash))
            issues.Add(Issue("SuppliedGateHashMismatch", nameof(request.WorkflowContinuationGateEvaluation), "Supplied workflow continuation gate hash does not match the supplied gate object."));
        if (!MatchesHash(freshGate.ExpectedWorkflowStateHash, request.ExpectedWorkflowStateHash))
            issues.Add(Issue("FreshGateWorkflowStateHashMismatch", nameof(freshGate.ExpectedWorkflowStateHash), "Recomputed gate workflow state hash must match the continuation request."));
        if (freshGate.SourceApplyRequestId != request.SourceApplyRequest.SourceApplyRequestId)
            issues.Add(Issue("FreshGateSourceApplyRequestMismatch", nameof(freshGate.SourceApplyRequestId), "Recomputed gate source apply request must match supplied evidence."));
        if (freshGate.SourceApplyReceiptId != request.SourceApplyReceipt.SourceApplyReceiptId)
            issues.Add(Issue("FreshGateSourceApplyReceiptMismatch", nameof(freshGate.SourceApplyReceiptId), "Recomputed gate source apply receipt must match supplied evidence."));

        return issues;
    }

    private static WorkflowRunStep? ResolveNextStep(
        WorkflowRun run,
        GovernedWorkflowContinuationRequest request,
        WorkflowRunStep currentStep,
        List<GovernedWorkflowContinuationIssue> issues)
    {
        if (request.TransitionKind == WorkflowTransitionKinds.MarkStepComplete)
        {
            if (!string.IsNullOrWhiteSpace(request.NextWorkflowStepId))
                issues.Add(Issue("NextStepUnexpected", nameof(request.NextWorkflowStepId), "MarkStepComplete must not start a next step."));
            return null;
        }

        var nextStep = !string.IsNullOrWhiteSpace(request.NextWorkflowStepId)
            ? FindStep(run, request.NextWorkflowStepId!)
            : run.Steps.SkipWhile(step => step.WorkflowRunStepId != currentStep.WorkflowRunStepId).Skip(1).FirstOrDefault();

        if (nextStep is null)
        {
            issues.Add(Issue("NextStepRequired", nameof(request.NextWorkflowStepId), "ContinueToNextStep requires an existing next workflow step."));
            return null;
        }

        if (nextStep.WorkflowRunStepId == currentStep.WorkflowRunStepId)
            issues.Add(Issue("NextStepMustDiffer", nameof(request.NextWorkflowStepId), "Next workflow step must differ from the current step."));
        if (nextStep.Status != WorkflowRunStatus.Created)
            issues.Add(Issue("NextStepStatusRejected", nameof(nextStep.Status), "Next workflow step must be in Created status before continuation."));

        return nextStep;
    }

    private static WorkflowRunStep? FindStep(WorkflowRun run, string value)
    {
        var normalized = value.Trim();
        return run.Steps.FirstOrDefault(step =>
            string.Equals(step.WorkflowRunStepId.ToString("D"), normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(step.StepKey, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsContinuable(WorkflowRunStatus status) =>
        status is WorkflowRunStatus.Created or WorkflowRunStatus.ReadyForReview or WorkflowRunStatus.Blocked;

    private static bool MatchesHash(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static GovernedWorkflowContinuationResult Rejected(IReadOnlyList<GovernedWorkflowContinuationIssue> issues) => new()
    {
        Status = GovernedWorkflowContinuationStatuses.Rejected,
        Succeeded = false,
        WorkflowStateMutated = false,
        StepCompleted = false,
        NextStepStarted = false,
        ReleaseReadinessInferred = false,
        ReleaseApproved = false,
        SourceApplyExecuted = false,
        RollbackExecuted = false,
        WorkflowTransitionRecord = null,
        Issues = issues,
        Warnings = GovernedWorkflowContinuationBoundaryText.Warnings,
        Boundary = GovernedWorkflowContinuationBoundaryText.Boundary
    };

    private static GovernedWorkflowContinuationResult FailedAfterMutation(IReadOnlyList<GovernedWorkflowContinuationIssue> issues) => new()
    {
        Status = GovernedWorkflowContinuationStatuses.TransitionRecordSaveFailed,
        Succeeded = false,
        WorkflowStateMutated = true,
        StepCompleted = false,
        NextStepStarted = false,
        ReleaseReadinessInferred = false,
        ReleaseApproved = false,
        SourceApplyExecuted = false,
        RollbackExecuted = false,
        WorkflowTransitionRecord = null,
        Issues = issues,
        Warnings = GovernedWorkflowContinuationBoundaryText.Warnings,
        Boundary = GovernedWorkflowContinuationBoundaryText.Boundary
    };

    private static GovernedWorkflowContinuationIssue Issue(string code, string field, string message) => new(code, field, message);
}

public sealed class SqlControlledWorkflowStateTransitionStore : IControlledWorkflowStateTransitionStore
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlControlledWorkflowStateTransitionStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<ControlledWorkflowStateTransitionResult> TransitionAsync(
        ControlledWorkflowStateTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<WorkflowTransitionSqlResult>(new CommandDefinition(
            "workflow.usp_WorkflowGovernedContinuation_Transition",
            new
            {
                request.ProjectId,
                request.WorkflowRunId,
                request.CurrentWorkflowRunStepId,
                request.NextWorkflowRunStepId,
                request.TransitionKind,
                ExpectedWorkflowStatus = request.ExpectedWorkflowStatus.ToString(),
                ExpectedCurrentStepStatus = request.ExpectedCurrentStepStatus.ToString(),
                ExpectedNextStepStatus = request.ExpectedNextStepStatus?.ToString(),
                NewWorkflowStatus = request.NewWorkflowStatus.ToString(),
                NewCurrentStepStatus = request.NewCurrentStepStatus.ToString(),
                NewNextStepStatus = request.NewNextStepStatus?.ToString()
            },
            commandType: System.Data.CommandType.StoredProcedure,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var row = rows.FirstOrDefault();
        if (row is not null && row.Succeeded)
            return new ControlledWorkflowStateTransitionResult(true, []);

        return new ControlledWorkflowStateTransitionResult(false,
        [
            new GovernedWorkflowContinuationIssue(
                row?.Code ?? "ControlledTransitionFailed",
                row?.Field ?? "workflowState",
                row?.Message ?? "Controlled workflow state transition failed.")
        ]);
    }

    private sealed class WorkflowTransitionSqlResult
    {
        public bool Succeeded { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Field { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }
}
