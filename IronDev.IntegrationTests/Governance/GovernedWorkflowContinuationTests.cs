using IronDev.Core.Governance;
using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Workflow;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("GovernedWorkflowContinuation")]
[TestCategory("PR214")]
public sealed class GovernedWorkflowContinuationTests
{
    [TestMethod]
    public async Task GovernedContinuation_ContinueToNextStep_MutatesStateAndWritesTransitionRecord()
    {
        var run = ValidRun();
        var store = new FakeWorkflowRunStore(run);
        var transitionStore = new FakeTransitionStore(store);
        var recordStore = new FakeTransitionRecordStore();
        var service = new GovernedWorkflowContinuationService(store, transitionStore, recordStore);
        var request = ValidRequest(run);

        var result = await service.ContinueAsync(request);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(GovernedWorkflowContinuationStatuses.Transitioned, result.Status);
        Assert.IsTrue(result.WorkflowStateMutated);
        Assert.IsTrue(result.StepCompleted);
        Assert.IsTrue(result.NextStepStarted);
        Assert.IsFalse(result.ReleaseReadinessInferred);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsFalse(result.SourceApplyExecuted);
        Assert.IsFalse(result.RollbackExecuted);
        Assert.AreEqual(1, transitionStore.CallCount);
        Assert.AreEqual(1, recordStore.Saved.Count);
        Assert.IsNotNull(result.WorkflowTransitionRecord);
        Assert.AreEqual(WorkflowTransitionKinds.ContinueToNextStep, result.WorkflowTransitionRecord!.TransitionKind);
        Assert.IsTrue(WorkflowTransitionRecordValidation.Validate(result.WorkflowTransitionRecord).IsValid);
        Assert.AreEqual(WorkflowRunStatus.Completed, store.Current.Steps[0].Status);
        Assert.AreEqual(WorkflowRunStatus.ReadyForReview, store.Current.Steps[1].Status);
    }

    [TestMethod]
    public async Task GovernedContinuation_RejectsUnsatisfiedGateBeforeMutation()
    {
        var run = ValidRun();
        var store = new FakeWorkflowRunStore(run);
        var transitionStore = new FakeTransitionStore(store);
        var recordStore = new FakeTransitionRecordStore();
        var service = new GovernedWorkflowContinuationService(store, transitionStore, recordStore);
        var request = ValidRequest(run);
        var gate = request.WorkflowContinuationGateEvaluation with { Satisfied = false, Status = WorkflowContinuationGateStatuses.Blocked };
        request = request with
        {
            WorkflowContinuationGateEvaluation = gate,
            WorkflowContinuationGateEvaluationHash = GovernedWorkflowContinuationHashing.ComputeGateEvaluationHash(gate)
        };

        var result = await service.ContinueAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GovernedWorkflowContinuationStatuses.Rejected, result.Status);
        Assert.IsFalse(result.WorkflowStateMutated);
        Assert.AreEqual(0, transitionStore.CallCount);
        Assert.AreEqual(0, recordStore.Saved.Count);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "GateNotSatisfied"));
    }

    [TestMethod]
    public async Task GovernedContinuation_RejectsFabricatedGateEvenWhenSuppliedHashMatches()
    {
        var run = ValidRun();
        var store = new FakeWorkflowRunStore(run);
        var transitionStore = new FakeTransitionStore(store);
        var recordStore = new FakeTransitionRecordStore();
        var service = new GovernedWorkflowContinuationService(store, transitionStore, recordStore);
        var request = ValidRequest(run);
        var fabricatedGate = request.WorkflowContinuationGateEvaluation with
        {
            SourceApplyReceiptHash = H("fabricated-source-apply-receipt")
        };
        request = request with
        {
            WorkflowContinuationGateEvaluation = fabricatedGate,
            WorkflowContinuationGateEvaluationHash = GovernedWorkflowContinuationHashing.ComputeGateEvaluationHash(fabricatedGate)
        };

        var result = await service.ContinueAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.WorkflowStateMutated);
        Assert.AreEqual(0, transitionStore.CallCount);
        Assert.AreEqual(0, recordStore.Saved.Count);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "FreshGateHashMismatch"), string.Join("; ", result.Issues.Select(issue => issue.Code)));
    }

    [TestMethod]
    public async Task GovernedContinuation_RejectsStateHashMismatchBeforeMutation()
    {
        var run = ValidRun();
        var store = new FakeWorkflowRunStore(run);
        var transitionStore = new FakeTransitionStore(store);
        var recordStore = new FakeTransitionRecordStore();
        var service = new GovernedWorkflowContinuationService(store, transitionStore, recordStore);
        var request = ValidRequest(run) with { ExpectedWorkflowStateHash = "sha256:wrong" };
        var gate = request.WorkflowContinuationGateEvaluation with { ExpectedWorkflowStateHash = "sha256:wrong" };
        request = request with
        {
            WorkflowContinuationGateEvaluation = gate,
            WorkflowContinuationGateEvaluationHash = GovernedWorkflowContinuationHashing.ComputeGateEvaluationHash(gate)
        };

        var result = await service.ContinueAsync(request);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.WorkflowStateMutated);
        Assert.AreEqual(0, transitionStore.CallCount);
        Assert.AreEqual(0, recordStore.Saved.Count);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "WorkflowStateHashMismatch"));
    }

    [TestMethod]
    public async Task GovernedContinuation_RecordSaveFailureAfterMutationFailsLoudly()
    {
        var run = ValidRun();
        var store = new FakeWorkflowRunStore(run);
        var transitionStore = new FakeTransitionStore(store);
        var recordStore = new FakeTransitionRecordStore { ThrowOnSave = true };
        var service = new GovernedWorkflowContinuationService(store, transitionStore, recordStore);

        var result = await service.ContinueAsync(ValidRequest(run));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(GovernedWorkflowContinuationStatuses.TransitionRecordSaveFailed, result.Status);
        Assert.IsTrue(result.WorkflowStateMutated);
        Assert.IsNull(result.WorkflowTransitionRecord);
        Assert.AreEqual(1, transitionStore.CallCount);
        Assert.AreEqual(0, recordStore.Saved.Count);
        Assert.AreEqual(WorkflowRunStatus.Completed, store.Current.Steps[0].Status);
    }

    [TestMethod]
    public void GovernedContinuation_SqlMigrationAddsControlledProcedureAndKeepsDirectUpdatesBlocked()
    {
        var root = RepositoryRoot();
        var runSql = File.ReadAllText(Path.Combine(root, "Database", "migrate_workflow_run.sql"));
        var stepSql = File.ReadAllText(Path.Combine(root, "Database", "migrate_workflow_step_store.sql"));

        StringAssert.Contains(stepSql, "workflow.usp_WorkflowGovernedContinuation_Transition");
        StringAssert.Contains(stepSql, "sp_set_session_context @key = N'IronDevGovernedWorkflowContinuation'");
        StringAssert.Contains(runSql, "SESSION_CONTEXT(N'IronDevGovernedWorkflowContinuation')");
        StringAssert.Contains(runSql, "Workflow run records are append-only");
        StringAssert.Contains(runSql, "Workflow run steps are append-only");
    }

    [TestMethod]
    public void GovernedContinuation_ApiControllerDependsOnlyOnGovernedService()
    {
        var controller = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "GovernedWorkflowContinuationController.cs"));
        StringAssert.Contains(controller, "IGovernedWorkflowContinuationService");
        Assert.IsFalse(controller.Contains("IWorkflowRunStore", StringComparison.Ordinal));
        Assert.IsFalse(controller.Contains("IWorkflowStepStore", StringComparison.Ordinal));
        Assert.IsFalse(controller.Contains("IWorkflowTransitionRecordStore", StringComparison.Ordinal));
        StringAssert.Contains(controller, "WorkflowContinuationApprovesRelease: false");
        StringAssert.Contains(controller, "WorkflowContinuationExecutesSourceApply: false");
        StringAssert.Contains(controller, "WorkflowContinuationExecutesRollback: false");
    }

    [TestMethod]
    public void GovernedContinuation_CliPostsOnlyToGovernedContinuationApi()
    {
        var cli = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli", "CliWorkflowContinuation.cs"));
        StringAssert.Contains(cli, "workflow continue governed");
        StringAssert.Contains(cli, "CreateGovernedWorkflowContinuationAsync");
        StringAssert.Contains(cli, "--request-file");
        Assert.IsFalse(cli.Contains("IWorkflowRunStore", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("IWorkflowTransitionRecordStore", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("Sql", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("release-ready", StringComparison.OrdinalIgnoreCase) && !cli.Contains("Unsupported governed continuation option", StringComparison.Ordinal));
    }

    private static GovernedWorkflowContinuationRequest ValidRequest(WorkflowRun run)
    {
        var gateRequest = ValidGateRequest(run);
        var gate = new WorkflowContinuationGateEvaluator().Evaluate(gateRequest);
        return new GovernedWorkflowContinuationRequest
        {
            GovernedWorkflowContinuationRequestId = Guid.NewGuid(),
            ProjectId = run.ProjectId,
            WorkflowRunId = run.WorkflowRunId.ToString("D"),
            CurrentWorkflowStepId = run.Steps[0].WorkflowRunStepId.ToString("D"),
            NextWorkflowStepId = run.Steps[1].WorkflowRunStepId.ToString("D"),
            TransitionKind = WorkflowTransitionKinds.ContinueToNextStep,
            ExpectedWorkflowStateHash = GovernedWorkflowContinuationHashing.ComputeWorkflowStateHash(run),
            ExpectedCurrentStepStateHash = GovernedWorkflowContinuationHashing.ComputeStepStateHash(run.Steps[0]),
            WorkflowContinuationGateEvaluation = gate,
            WorkflowContinuationGateEvaluationHash = GovernedWorkflowContinuationHashing.ComputeGateEvaluationHash(gate),
            AcceptedApproval = gateRequest.AcceptedApproval,
            PolicySatisfaction = gateRequest.PolicySatisfaction,
            SourceApplyRequest = gateRequest.SourceApplyRequest,
            SourceApplyReceipt = gateRequest.SourceApplyReceipt,
            RollbackExecutionReceipt = gateRequest.RollbackExecutionReceipt,
            RollbackExecutionAuditReport = gateRequest.RollbackExecutionAuditReport,
            RequestedAtUtc = gateRequest.RequestedAtUtc,
            EvidenceReferences = gateRequest.EvidenceReferences,
            BoundaryMaxims = gateRequest.BoundaryMaxims
        };
    }

    private static WorkflowContinuationGateRequest ValidGateRequest(WorkflowRun run)
    {
        var now = new DateTimeOffset(2026, 6, 17, 15, 0, 0, TimeSpan.Zero);
        var sourceApplyRequestId = Guid.NewGuid();
        var sourceApplyReceiptId = Guid.NewGuid();
        var sourceApplyRequestHash = H("source-apply-request-pr214");
        var subjectKind = "SourceApplyRequest";
        var subjectId = sourceApplyRequestId.ToString("D");
        var subjectHash = H("subject-pr214");
        var baselineHash = H("baseline-pr214");
        var workspaceHash = H("workspace-pr214");
        var cleanBefore = H("clean-before-pr214");
        var cleanAfter = H("clean-after-pr214");
        var patchArtifactId = Guid.NewGuid();
        var rollbackSupportReceiptId = Guid.NewGuid();
        var rollbackPlanId = Guid.NewGuid();
        var acceptedApprovalId = Guid.NewGuid();
        var policySatisfactionId = Guid.NewGuid();
        var sourceGateId = Guid.NewGuid();
        var patchHash = H("patch-pr214");
        var changeSetHash = H("change-set-pr214");
        var rollbackSupportHash = H("rollback-support-pr214");
        var rollbackPlanHash = H("rollback-plan-pr214");
        var sourceGateHash = H("source-gate-pr214");
        var operation = new SourceApplyRequestFileOperation
        {
            Path = "src/file.txt",
            OperationKind = SourceApplyRequestFileOperationKinds.ModifyFile,
            PreviousPath = null,
            BeforeContentHash = H("old-pr214"),
            AfterContentHash = H("new-pr214"),
            DiffHash = H("diff-pr214"),
            PatchArtifactChangeHash = H("patch-change-pr214"),
            OperationHash = H("operation-pr214")
        };
        var sourceGate = new SourceApplyRequestGateEvaluationEvidence
        {
            SourceApplyGateEvaluationId = sourceGateId,
            SourceApplyGateEvaluationHash = sourceGateHash,
            Satisfied = true,
            ProjectId = run.ProjectId,
            AcceptedApprovalId = acceptedApprovalId,
            AcceptedApprovalHash = H("accepted-approval-pr214"),
            PolicySatisfactionId = policySatisfactionId,
            PolicySatisfactionHash = H("policy-satisfaction-pr214"),
            ControlledDryRunRequestId = Guid.NewGuid(),
            DryRunExecutionAuditId = Guid.NewGuid(),
            DryRunAuditHash = H("dry-run-audit-pr214"),
            DryRunReceiptHash = H("dry-run-receipt-pr214"),
            PatchArtifactId = patchArtifactId,
            PatchHash = patchHash,
            ChangeSetHash = changeSetHash,
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            RollbackSupportReceiptHash = rollbackSupportHash,
            RollbackPlanId = rollbackPlanId,
            RollbackPlanHash = rollbackPlanHash,
            RollbackGateEvaluationHash = H("rollback-gate-pr214"),
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            SourceSnapshotReference = "snapshot-main",
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = cleanBefore,
            ExpiresAtUtc = now.AddHours(1),
            EvidenceReferences = ["source-gate-evidence-pr214"],
            BoundaryMaxims = ["Gate evidence is not execution."]
        };
        var sourceRequest = new SourceApplyRequest
        {
            SourceApplyRequestId = sourceApplyRequestId,
            ProjectId = run.ProjectId,
            SourceApplyGateEvaluationId = sourceGateId,
            SourceApplyGateEvaluationHash = sourceGateHash,
            SourceApplyGateSatisfied = true,
            SourceApplyGateEvaluation = sourceGate,
            AcceptedApprovalId = acceptedApprovalId,
            AcceptedApprovalHash = sourceGate.AcceptedApprovalHash,
            PolicySatisfactionId = policySatisfactionId,
            PolicySatisfactionHash = sourceGate.PolicySatisfactionHash,
            ControlledDryRunRequestId = sourceGate.ControlledDryRunRequestId,
            DryRunExecutionAuditId = sourceGate.DryRunExecutionAuditId,
            DryRunAuditHash = sourceGate.DryRunAuditHash,
            DryRunReceiptHash = sourceGate.DryRunReceiptHash,
            PatchArtifactId = patchArtifactId,
            PatchHash = patchHash,
            ChangeSetHash = changeSetHash,
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            RollbackSupportReceiptHash = rollbackSupportHash,
            RollbackPlanId = rollbackPlanId,
            RollbackPlanHash = rollbackPlanHash,
            RollbackGateEvaluationHash = sourceGate.RollbackGateEvaluationHash,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            SourceSnapshotReference = sourceGate.SourceSnapshotReference,
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = cleanBefore,
            FileOperations = [operation],
            RequestedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            SourceApplyRequestHash = sourceApplyRequestHash,
            EvidenceReferences = ["source-apply-request-evidence-pr214"],
            BoundaryMaxims = ["Source apply request is not apply."],
            Boundary = SourceApplyRequestBoundaryText.Boundary
        };
        var fileResult = new SourceApplyReceiptFileResult
        {
            Path = operation.Path,
            PreviousPath = null,
            OperationKind = operation.OperationKind,
            PatchArtifactChangeHash = operation.PatchArtifactChangeHash,
            OperationHash = operation.OperationHash,
            BeforeContentHash = operation.BeforeContentHash,
            AfterContentHash = operation.AfterContentHash,
            PreconditionsSatisfied = true,
            MutationApplied = true,
            Created = false,
            Modified = true,
            Deleted = false,
            Renamed = false,
            Noop = false,
            IssueCodes = [],
            FileResultHash = "sha256:pending"
        };
        fileResult = fileResult with { FileResultHash = SourceApplyReceiptHashing.ComputeFileResultHash(fileResult) };
        var sourceReceipt = new SourceApplyReceipt
        {
            SourceApplyReceiptId = sourceApplyReceiptId,
            ProjectId = run.ProjectId,
            ControlledSourceApplyRequestId = Guid.NewGuid(),
            SourceApplyRequestId = sourceApplyRequestId,
            SourceApplyRequestHash = sourceApplyRequestHash,
            SourceApplyDryRunReceiptId = Guid.NewGuid(),
            SourceApplyDryRunReceiptHash = H("source-apply-dry-run-receipt-pr214"),
            SourceApplyGateEvaluationId = sourceGateId,
            SourceApplyGateEvaluationHash = sourceGateHash,
            PatchArtifactId = patchArtifactId,
            PatchHash = patchHash,
            ChangeSetHash = changeSetHash,
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            RollbackSupportReceiptHash = rollbackSupportHash,
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = cleanBefore,
            ObservedBranch = "main",
            ObservedCleanWorktreeHashBeforeApply = cleanBefore,
            ObservedCleanWorktreeHashAfterApply = cleanAfter,
            MutationOccurred = true,
            ApplySucceeded = true,
            PartialApplyOccurred = false,
            FileResults = [fileResult],
            IssueCodes = [],
            AppliedAtUtc = now.AddMinutes(1),
            SourceApplyReceiptHash = "sha256:pending",
            EvidenceReferences = ["source-apply-receipt-evidence-pr214"],
            BoundaryMaxims = ["SourceApplyReceipt is mutation evidence, not release approval."],
            Boundary = SourceApplyReceiptBoundaryText.Boundary
        };
        sourceReceipt = sourceReceipt with { SourceApplyReceiptHash = SourceApplyReceiptHashing.ComputeReceiptHash(sourceReceipt) };
        var approval = new AcceptedApprovalRecord
        {
            AcceptedApprovalId = acceptedApprovalId,
            ProjectId = run.ProjectId,
            ApprovalTargetKind = subjectKind,
            ApprovalTargetId = subjectId,
            ApprovalTargetHash = subjectHash,
            CapabilityCode = "workflow-continuation-gate-input",
            ApprovalPurpose = AcceptedApprovalPurposes.WorkflowContinuationInput,
            ApprovedByActorId = "human-reviewer",
            ApprovedByActorDisplayName = "Human Reviewer",
            AcceptedAtUtc = now.AddMinutes(-20),
            ExpiresAtUtc = now.AddHours(1),
            CorrelationId = "correlation-pr214",
            CausationId = "cause-pr214",
            EvidenceReferences = ["accepted-approval-evidence-pr214"],
            BoundaryMaxims = ["Accepted approval is not workflow continuation."]
        };
        var policy = new PolicySatisfactionRecord
        {
            PolicySatisfactionId = policySatisfactionId,
            ProjectId = run.ProjectId,
            PolicyCode = "source-apply-policy",
            PolicyVersion = "v1",
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            CapabilityCode = approval.CapabilityCode,
            AcceptedApprovalId = acceptedApprovalId,
            ApprovalRequirementHash = H("approval-requirement-pr214"),
            ApprovalEvaluatedAtUtc = now.AddMinutes(-15),
            SatisfiedAtUtc = now.AddMinutes(-14),
            ExpiresAtUtc = now.AddHours(1),
            CorrelationId = "correlation-pr214",
            CausationId = "cause-policy-pr214",
            EvidenceReferences = ["policy-satisfaction-evidence-pr214"],
            BoundaryMaxims = ["Policy satisfaction is not workflow continuation."],
            Boundary = PolicySatisfactionBoundaryText.Boundary
        };

        return new WorkflowContinuationGateRequest
        {
            WorkflowContinuationGateRequestId = Guid.NewGuid(),
            ProjectId = run.ProjectId,
            WorkflowRunId = run.WorkflowRunId.ToString("D"),
            WorkflowStepId = run.Steps[0].WorkflowRunStepId.ToString("D"),
            ExpectedWorkflowStateHash = GovernedWorkflowContinuationHashing.ComputeWorkflowStateHash(run),
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            AcceptedApproval = approval,
            PolicySatisfaction = policy,
            SourceApplyRequest = sourceRequest,
            SourceApplyReceipt = sourceReceipt,
            RequestedAtUtc = now,
            EvidenceReferences = ["workflow-continuation-gate-evidence-pr214"],
            BoundaryMaxims = ["Workflow continuation gate satisfaction is evidence only."],
            Boundary = WorkflowContinuationGateBoundaryText.Boundary
        };
    }

    private static string H(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static WorkflowRun ValidRun()
    {
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        return new WorkflowRun
        {
            WorkflowRunId = runId,
            ProjectId = projectId,
            WorkflowType = "SourceApplyReview",
            WorkflowName = "PR214 governed continuation test",
            Status = WorkflowRunStatus.ReadyForReview,
            SubjectType = "source-apply-receipt",
            SubjectId = "source-apply-receipt-pr214",
            SubjectSummary = "Safe workflow continuation test fixture.",
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            CreatedByActorType = "test",
            CreatedByActorId = "pr214",
            MetadataVersion = 1,
            MetadataJson = "{\"schemaVersion\":1}",
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            ContinuesWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            ApprovesRelease = false,
            CreatesAcceptedMemory = false,
            Steps =
            [
                Step(runId, projectId, "step-1", WorkflowRunStatus.ReadyForReview, now),
                Step(runId, projectId, "step-2", WorkflowRunStatus.Created, now)
            ],
            EvidenceReferences = [],
            GroundingReferences = [],
            CreatedUtc = now
        };
    }

    private static WorkflowRunStep Step(Guid runId, Guid projectId, string key, WorkflowRunStatus status, DateTimeOffset createdUtc) => new()
    {
        WorkflowRunStepId = Guid.NewGuid(),
        WorkflowRunId = runId,
        ProjectId = projectId,
        StepKey = key,
        StepName = key,
        StepType = WorkflowRunStepType.Review,
        Status = status,
        AgentRole = null,
        AgentId = null,
        SubjectType = "source-apply-receipt",
        SubjectId = "source-apply-receipt-pr214",
        SafeSummary = "Safe workflow step fixture.",
        MetadataVersion = 1,
        MetadataJson = "{\"schemaVersion\":1}",
        GrantsApproval = false,
        GrantsExecution = false,
        MutatesSource = false,
        PromotesMemory = false,
        StartsWorkflow = false,
        ContinuesWorkflow = false,
        SatisfiesPolicy = false,
        TransfersAuthority = false,
        ApprovesRelease = false,
        CreatesAcceptedMemory = false,
        CreatedUtc = createdUtc
    };

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class FakeWorkflowRunStore : IWorkflowRunStore
    {
        public WorkflowRun Current { get; private set; }

        public FakeWorkflowRunStore(WorkflowRun current) => Current = current;

        public void Apply(ControlledWorkflowStateTransitionRequest request)
        {
            var steps = Current.Steps.Select(step =>
            {
                if (step.WorkflowRunStepId == request.CurrentWorkflowRunStepId)
                    return step with { Status = request.NewCurrentStepStatus };
                if (request.NextWorkflowRunStepId.HasValue && step.WorkflowRunStepId == request.NextWorkflowRunStepId.Value && request.NewNextStepStatus.HasValue)
                    return step with { Status = request.NewNextStepStatus.Value };
                return step;
            }).ToArray();
            Current = Current with { Status = request.NewWorkflowStatus, Steps = steps };
        }

        public Task<WorkflowRun> CreateAsync(WorkflowRunCreateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowRun?> GetAsync(Guid projectId, Guid workflowRunId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowRun?>(Current.ProjectId == projectId && Current.WorkflowRunId == workflowRunId ? Current : null);
        public Task<IReadOnlyList<WorkflowRunSummary>> ListByProjectAsync(Guid projectId, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowRunSummary>>([]);
        public Task<IReadOnlyList<WorkflowRunSummary>> ListByCorrelationAsync(Guid projectId, Guid correlationId, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowRunSummary>>([]);
        public Task<IReadOnlyList<WorkflowRunSummary>> ListBySubjectAsync(Guid projectId, string subjectType, string subjectId, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowRunSummary>>([]);
    }

    private sealed class FakeTransitionStore : IControlledWorkflowStateTransitionStore
    {
        private readonly FakeWorkflowRunStore _store;
        public int CallCount { get; private set; }
        public FakeTransitionStore(FakeWorkflowRunStore store) => _store = store;

        public Task<ControlledWorkflowStateTransitionResult> TransitionAsync(ControlledWorkflowStateTransitionRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            _store.Apply(request);
            return Task.FromResult(new ControlledWorkflowStateTransitionResult(true, []));
        }
    }

    private sealed class FakeTransitionRecordStore : IWorkflowTransitionRecordStore
    {
        public bool ThrowOnSave { get; init; }
        public List<WorkflowTransitionRecord> Saved { get; } = [];

        public Task SaveAsync(WorkflowTransitionRecord record, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
                throw new InvalidOperationException("simulated save failure");
            Saved.Add(record);
            return Task.CompletedTask;
        }

        public Task<WorkflowTransitionRecord?> GetAsync(Guid projectId, Guid workflowTransitionRecordId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowTransitionRecord?>(null);
        public Task<WorkflowTransitionRecord?> GetByRecordHashAsync(Guid projectId, string workflowTransitionRecordHash, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowTransitionRecord?>(null);
        public Task<IReadOnlyList<WorkflowTransitionRecord>> ListByWorkflowRunAsync(Guid projectId, string workflowRunId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowTransitionRecord>>([]);
        public Task<IReadOnlyList<WorkflowTransitionRecord>> ListByWorkflowStepAsync(Guid projectId, string workflowRunId, string workflowStepId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowTransitionRecord>>([]);
        public Task<IReadOnlyList<WorkflowTransitionRecord>> ListByContinuationGateEvaluationAsync(Guid projectId, Guid workflowContinuationGateEvaluationId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowTransitionRecord>>([]);
        public Task<IReadOnlyList<WorkflowTransitionRecord>> ListBySourceApplyReceiptAsync(Guid projectId, Guid sourceApplyReceiptId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowTransitionRecord>>([]);
        public Task<IReadOnlyList<WorkflowTransitionRecord>> ListByRollbackExecutionReceiptAsync(Guid projectId, Guid rollbackExecutionReceiptId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowTransitionRecord>>([]);
    }
}
