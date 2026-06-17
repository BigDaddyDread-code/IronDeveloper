using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ReleaseReadinessReport")]
[TestCategory("PR216")]
public sealed class ReleaseReadinessReportTests
{
    [TestMethod]
    public void ReleaseReadinessReport_CompleteEvidenceBuildsCompleteReportButDoesNotDeclareReady()
    {
        var report = new ReleaseReadinessReportBuilder().Build(ValidRequest());

        Assert.AreEqual(ReleaseReadinessReportStatuses.Complete, report.Status);
        AssertAuthorityFlagsFalse(report);
        Assert.IsTrue(report.ApprovalEvidencePresent);
        Assert.IsTrue(report.PolicyEvidencePresent);
        Assert.IsTrue(report.SourceApplySucceeded);
        Assert.IsFalse(report.SourceApplyPartial);
        Assert.IsFalse(report.RollbackWasExecuted);
        Assert.IsTrue(report.WorkflowContinuationSucceeded);
        Assert.IsTrue(report.WorkflowTransitionRecordValid);
        Assert.IsTrue(ReleaseReadinessReportValidation.Validate(report).IsValid);
    }

    [TestMethod]
    public void ReleaseReadinessReport_SuccessfulRollbackPathBuildsCompleteReportButDoesNotDeclareReady()
    {
        var report = new ReleaseReadinessReportBuilder().Build(ValidRequest(includeRollback: true));

        Assert.AreEqual(ReleaseReadinessReportStatuses.Complete, report.Status);
        AssertAuthorityFlagsFalse(report);
        Assert.IsFalse(report.SourceApplySucceeded);
        Assert.IsTrue(report.SourceApplyPartial);
        Assert.IsTrue(report.RollbackWasExecuted);
        Assert.IsTrue(report.RollbackSucceeded);
        Assert.IsFalse(report.RollbackPartial);
        Assert.IsTrue(report.RollbackAuditConsistent);
        Assert.IsTrue(ReleaseReadinessReportValidation.Validate(report).IsValid);
    }

    [TestMethod]
    public void ReleaseReadinessReport_HashIsDeterministic()
    {
        var request = ValidRequest();
        var first = new ReleaseReadinessReportBuilder().Build(request);
        var second = new ReleaseReadinessReportBuilder().Build(request);

        Assert.AreEqual(first.ReleaseReadinessReportHash, second.ReleaseReadinessReportHash);
        Assert.AreEqual(first.ReleaseReadinessReportHash, ReleaseReadinessReportHashing.ComputeReportHash(first));
    }

    [TestMethod]
    public void ReleaseReadinessReport_MissingApprovalBlocksReport()
    {
        var report = new ReleaseReadinessReportBuilder().Build(ValidRequest() with { AcceptedApproval = null! });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByMissingEvidence, report.Status);
        AssertHasBlocking(report, "AcceptedApproval");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_MissingPolicySatisfactionBlocksReport()
    {
        var report = new ReleaseReadinessReportBuilder().Build(ValidRequest() with { PolicySatisfaction = null! });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByMissingEvidence, report.Status);
        AssertHasBlocking(report, "PolicySatisfaction");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_MissingSourceApplyReceiptBlocksReport()
    {
        var report = new ReleaseReadinessReportBuilder().Build(ValidRequest() with { SourceApplyReceipt = null! });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByMissingEvidence, report.Status);
        AssertHasBlocking(report, "SourceApplyReceipt");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_MissingWorkflowTransitionRecordBlocksReport()
    {
        var report = new ReleaseReadinessReportBuilder().Build(ValidRequest() with { WorkflowTransitionRecord = null! });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByMissingEvidence, report.Status);
        AssertHasBlocking(report, "WorkflowTransitionRecord");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_FailedSourceApplyBlocksReport()
    {
        var request = ValidRequest();
        var receipt = Rehash(request.SourceApplyReceipt with { ApplySucceeded = false, PartialApplyOccurred = false });
        var report = new ReleaseReadinessReportBuilder().Build(request with { SourceApplyReceipt = receipt });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "SourceApplyFailed");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_PartialSourceApplyWithoutRollbackBlocksReport()
    {
        var request = ValidRequest();
        var receipt = Rehash(request.SourceApplyReceipt with { ApplySucceeded = false, PartialApplyOccurred = true });
        var report = new ReleaseReadinessReportBuilder().Build(request with { SourceApplyReceipt = receipt });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "PartialSourceApplyRequiresRollback");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_FailedRollbackBlocksReport()
    {
        var request = ValidRequest(includeRollback: true);
        var rollback = Rehash(request.RollbackExecutionReceipt! with { RollbackSucceeded = false });
        var report = new ReleaseReadinessReportBuilder().Build(request with { RollbackExecutionReceipt = rollback });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "RollbackFailed");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_InconsistentRollbackAuditBlocksReport()
    {
        var request = ValidRequest(includeRollback: true);
        var audit = request.RollbackExecutionAuditReport! with { EvidenceConsistent = false };
        var transition = Rehash(request.WorkflowTransitionRecord with { RollbackExecutionAuditReportHash = ReleaseReadinessReportHashing.ComputeRollbackExecutionAuditReportHash(audit) });
        var report = new ReleaseReadinessReportBuilder().Build(request with { RollbackExecutionAuditReport = audit, WorkflowTransitionRecord = transition });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "RollbackAuditInconsistent");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_UnsatisfiedContinuationGateBlocksReport()
    {
        var request = ValidRequest();
        var gate = request.WorkflowContinuationGateEvaluation with { Satisfied = false, Status = WorkflowContinuationGateStatuses.Blocked };
        var transition = Rehash(request.WorkflowTransitionRecord with { WorkflowContinuationGateEvaluationHash = GovernedWorkflowContinuationHashing.ComputeGateEvaluationHash(gate) });
        var report = new ReleaseReadinessReportBuilder().Build(request with { WorkflowContinuationGateEvaluation = gate, WorkflowTransitionRecord = transition });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "WorkflowContinuationGateUnsatisfied");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_InvalidWorkflowTransitionRecordBlocksReport()
    {
        var request = ValidRequest();
        var transition = Rehash(request.WorkflowTransitionRecord with { ReleaseApproved = true });
        var report = new ReleaseReadinessReportBuilder().Build(request with { WorkflowTransitionRecord = transition });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "WorkflowTransitionRecordReleaseApprovalRejected");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_ProjectMismatchBlocksReport()
    {
        var request = ValidRequest();
        var approval = request.AcceptedApproval with { ProjectId = Guid.NewGuid() };
        var report = new ReleaseReadinessReportBuilder().Build(request with { AcceptedApproval = approval });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "ProjectMismatch");
    }

    [TestMethod]
    public void ReleaseReadinessReport_SubjectMismatchBlocksReport()
    {
        var request = ValidRequest();
        var policy = request.PolicySatisfaction with { SubjectHash = H("other-subject") };
        var report = new ReleaseReadinessReportBuilder().Build(request with { PolicySatisfaction = policy });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "SubjectHashMismatch");
    }

    [TestMethod]
    public void ReleaseReadinessReport_SourceApplyReceiptRequestMismatchBlocksReport()
    {
        var request = ValidRequest();
        var receipt = Rehash(request.SourceApplyReceipt with { SourceApplyRequestHash = H("other-source-request") });
        var report = new ReleaseReadinessReportBuilder().Build(request with { SourceApplyReceipt = receipt });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "SourceApplyReceiptRequestHashMismatch");
    }

    [TestMethod]
    public void ReleaseReadinessReport_WorkflowTransitionRecordGateMismatchBlocksReport()
    {
        var request = ValidRequest();
        var transition = Rehash(request.WorkflowTransitionRecord with { WorkflowContinuationGateEvaluationId = Guid.NewGuid() });
        var report = new ReleaseReadinessReportBuilder().Build(request with { WorkflowTransitionRecord = transition });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "WorkflowTransitionRecordGateMismatch");
    }

    [TestMethod]
    public void ReleaseReadinessReport_WorkflowTransitionRecordSourceApplyMismatchBlocksReport()
    {
        var request = ValidRequest();
        var transition = Rehash(request.WorkflowTransitionRecord with { SourceApplyReceiptHash = H("other-source-receipt") });
        var report = new ReleaseReadinessReportBuilder().Build(request with { WorkflowTransitionRecord = transition });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "WorkflowTransitionRecordSourceApplyReceiptHashMismatch");
    }

    [TestMethod]
    public void ReleaseReadinessReport_WorkflowTransitionRecordRollbackMismatchBlocksReport()
    {
        var request = ValidRequest(includeRollback: true);
        var transition = Rehash(request.WorkflowTransitionRecord with { RollbackExecutionReceiptHash = H("other-rollback") });
        var report = new ReleaseReadinessReportBuilder().Build(request with { WorkflowTransitionRecord = transition });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "WorkflowTransitionRecordRollbackHashMismatch");
    }

    [TestMethod]
    public void ReleaseReadinessReport_RejectsPrivateRawMaterial()
    {
        var report = new ReleaseReadinessReportBuilder().Build(ValidRequest() with { EvidenceReferences = ["raw prompt leaked"] });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "PrivateOrRawMaterial");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_RejectsReleaseAuthorityClaims()
    {
        var report = new ReleaseReadinessReportBuilder().Build(ValidRequest() with { BoundaryMaxims = ["release approved"] });

        Assert.AreEqual(ReleaseReadinessReportStatuses.BlockedByFailedEvidence, report.Status);
        AssertHasBlocking(report, "AuthorityClaim");
        AssertAuthorityFlagsFalse(report);
    }

    [TestMethod]
    public void ReleaseReadinessReport_StaticNoGoTermsAreNotProductionCapabilities()
    {
        var source = ReadRepositoryFile("IronDev.Core", "Governance", "ReleaseReadinessReport.cs");
        var forbidden = new[]
        {
            "ReleaseReady = true",
            "ReleaseApproved = true",
            "DeploymentApproved = true",
            "MergeApproved = true",
            "ReleaseReadinessDecided = true",
            "SourceApplyExecutedByReport = true",
            "RollbackExecutedByReport = true",
            "WorkflowMutatedByReport = true",
            "GitOperationExecutedByReport = true",
            "HttpPost",
            "HttpPut",
            "HttpPatch",
            "HttpDelete",
            "ControllerBase",
            "SaveAsync",
            "ExecuteAsync",
            "Process.Start",
            "ProcessStartInfo",
            "WorkflowContinuationExecutor",
            "GovernedWorkflowContinuationService",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "AgentDispatch",
            "ModelProvider",
            "ToolInvoker",
            "PromoteMemory",
            "ActivateRetrieval",
            "Weaviate",
            "Embedding"
        };

        foreach (var term in forbidden)
            Assert.IsFalse(source.Contains(term, StringComparison.Ordinal), $"Production report file must not contain {term}.");
    }

    [TestMethod]
    public void ReleaseReadinessReport_ReceiptStatesBoundary()
    {
        var receipt = ReadRepositoryFile("Docs", "receipts", "PR216_RELEASE_READINESS_REPORT.md");

        StringAssert.Contains(receipt, "PR216 adds release-readiness report generation only.");
        StringAssert.Contains(receipt, "PR216 does not decide release readiness.");
        StringAssert.Contains(receipt, "PR216 does not approve release.");
        StringAssert.Contains(receipt, "Report status Complete does not mean ReleaseReady.");
        StringAssert.Contains(receipt, "PR216 writes the release-readiness briefing. It does not declare release readiness.");
    }

    private static void AssertAuthorityFlagsFalse(ReleaseReadinessReport report)
    {
        Assert.IsFalse(report.ReleaseReadinessDecided);
        Assert.IsFalse(report.ReleaseReady);
        Assert.IsFalse(report.ReleaseApproved);
        Assert.IsFalse(report.DeploymentApproved);
        Assert.IsFalse(report.MergeApproved);
        Assert.IsFalse(report.SourceApplyExecutedByReport);
        Assert.IsFalse(report.RollbackExecutedByReport);
        Assert.IsFalse(report.WorkflowMutatedByReport);
        Assert.IsFalse(report.GitOperationExecutedByReport);
        Assert.IsTrue(report.HumanReviewRequiredForReadiness);
        Assert.IsTrue(report.HumanReviewRequiredForReleaseApproval);
    }

    private static void AssertHasBlocking(ReleaseReadinessReport report, string code) =>
        Assert.IsTrue(report.Findings.Any(finding => finding.Severity == ReleaseReadinessFindingSeverities.Blocking && finding.Code.Contains(code, StringComparison.Ordinal)), string.Join("; ", report.Findings.Select(finding => finding.Code)));

    private static ReleaseReadinessReportRequest ValidRequest(bool includeRollback = false)
    {
        var now = new DateTimeOffset(2026, 6, 17, 15, 0, 0, TimeSpan.Zero);
        var projectId = Guid.NewGuid();
        var workflowRunId = Guid.NewGuid().ToString("D");
        var workflowStepId = Guid.NewGuid().ToString("D");
        var subjectKind = AcceptedApprovalTargetKinds.SourceApplyRequest;
        var subjectId = Guid.NewGuid().ToString("D");
        var subjectHash = H("subject");
        var acceptedApprovalId = Guid.NewGuid();
        var acceptedApprovalHash = H("accepted-approval");
        var policySatisfactionId = Guid.NewGuid();
        var policySatisfactionHash = H("policy-satisfaction");
        var sourceApplyRequestId = Guid.NewGuid();
        var sourceApplyReceiptId = Guid.NewGuid();
        var sourceApplyRequestHash = H("source-apply-request");
        var patchArtifactId = Guid.NewGuid();
        var patchHash = H("patch");
        var changeSetHash = H("change-set");
        var rollbackSupportReceiptId = Guid.NewGuid();
        var rollbackSupportReceiptHash = H("rollback-support");
        var rollbackPlanId = Guid.NewGuid();
        var rollbackPlanHash = H("rollback-plan");
        var sourceGateEvaluationId = Guid.NewGuid();
        var sourceGateHash = H("source-gate");
        var operation = new SourceApplyRequestFileOperation
        {
            Path = "src/file.txt",
            OperationKind = SourceApplyRequestFileOperationKinds.ModifyFile,
            PreviousPath = null,
            BeforeContentHash = H("old"),
            AfterContentHash = H("new"),
            DiffHash = H("diff"),
            PatchArtifactChangeHash = H("patch-change"),
            OperationHash = H("operation")
        };
        var sourceGate = new SourceApplyRequestGateEvaluationEvidence
        {
            SourceApplyGateEvaluationId = sourceGateEvaluationId,
            SourceApplyGateEvaluationHash = sourceGateHash,
            Satisfied = true,
            ProjectId = projectId,
            AcceptedApprovalId = acceptedApprovalId,
            AcceptedApprovalHash = acceptedApprovalHash,
            PolicySatisfactionId = policySatisfactionId,
            PolicySatisfactionHash = policySatisfactionHash,
            ControlledDryRunRequestId = Guid.NewGuid(),
            DryRunExecutionAuditId = Guid.NewGuid(),
            DryRunAuditHash = H("dry-run-audit"),
            DryRunReceiptHash = H("dry-run-receipt"),
            PatchArtifactId = patchArtifactId,
            PatchHash = patchHash,
            ChangeSetHash = changeSetHash,
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            RollbackSupportReceiptHash = rollbackSupportReceiptHash,
            RollbackPlanId = rollbackPlanId,
            RollbackPlanHash = rollbackPlanHash,
            RollbackGateEvaluationHash = H("rollback-gate"),
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            SourceSnapshotReference = "snapshot-main",
            SourceBaselineHash = H("baseline"),
            WorkspaceBoundaryHash = H("workspace"),
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = H("clean-before"),
            ExpiresAtUtc = now.AddHours(1),
            EvidenceReferences = ["source-gate-evidence"],
            BoundaryMaxims = ["Source apply gate evidence is not execution."]
        };
        var sourceRequest = new SourceApplyRequest
        {
            SourceApplyRequestId = sourceApplyRequestId,
            ProjectId = projectId,
            SourceApplyGateEvaluationId = sourceGateEvaluationId,
            SourceApplyGateEvaluationHash = sourceGateHash,
            SourceApplyGateSatisfied = true,
            SourceApplyGateEvaluation = sourceGate,
            AcceptedApprovalId = acceptedApprovalId,
            AcceptedApprovalHash = acceptedApprovalHash,
            PolicySatisfactionId = policySatisfactionId,
            PolicySatisfactionHash = policySatisfactionHash,
            ControlledDryRunRequestId = sourceGate.ControlledDryRunRequestId,
            DryRunExecutionAuditId = sourceGate.DryRunExecutionAuditId,
            DryRunAuditHash = sourceGate.DryRunAuditHash,
            DryRunReceiptHash = sourceGate.DryRunReceiptHash,
            PatchArtifactId = patchArtifactId,
            PatchHash = patchHash,
            ChangeSetHash = changeSetHash,
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            RollbackSupportReceiptHash = rollbackSupportReceiptHash,
            RollbackPlanId = rollbackPlanId,
            RollbackPlanHash = rollbackPlanHash,
            RollbackGateEvaluationHash = sourceGate.RollbackGateEvaluationHash,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            SourceSnapshotReference = sourceGate.SourceSnapshotReference,
            SourceBaselineHash = sourceGate.SourceBaselineHash,
            WorkspaceBoundaryHash = sourceGate.WorkspaceBoundaryHash,
            ExpectedBranch = sourceGate.ExpectedBranch,
            ExpectedCleanWorktreeHash = sourceGate.ExpectedCleanWorktreeHash,
            FileOperations = [operation],
            RequestedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            SourceApplyRequestHash = sourceApplyRequestHash,
            EvidenceReferences = ["source-apply-request-evidence"],
            BoundaryMaxims = ["Source apply request is not apply."]
        };
        var sourceReceipt = ValidSourceApplyReceipt(projectId, sourceRequest, sourceApplyReceiptId, includeRollback);
        var acceptedApproval = new AcceptedApprovalRecord
        {
            AcceptedApprovalId = acceptedApprovalId,
            ProjectId = projectId,
            ApprovalTargetKind = subjectKind,
            ApprovalTargetId = subjectId,
            ApprovalTargetHash = subjectHash,
            CapabilityCode = "release-readiness-report-input",
            ApprovalPurpose = AcceptedApprovalPurposes.ReleaseReadinessInput,
            ApprovedByActorId = "human-reviewer",
            ApprovedByActorDisplayName = "Human Reviewer",
            AcceptedAtUtc = now.AddMinutes(-30),
            ExpiresAtUtc = now.AddHours(1),
            CorrelationId = "correlation-pr216",
            CausationId = "cause-pr216",
            EvidenceReferences = ["accepted-approval-evidence"],
            BoundaryMaxims = ["Accepted approval is not release approval."]
        };
        var policy = new PolicySatisfactionRecord
        {
            PolicySatisfactionId = policySatisfactionId,
            ProjectId = projectId,
            PolicyCode = "release-readiness-input-policy",
            PolicyVersion = "v1",
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            CapabilityCode = acceptedApproval.CapabilityCode,
            AcceptedApprovalId = acceptedApprovalId,
            ApprovalRequirementHash = H("approval-requirement"),
            ApprovalEvaluatedAtUtc = now.AddMinutes(-20),
            SatisfiedAtUtc = now.AddMinutes(-19),
            ExpiresAtUtc = now.AddHours(1),
            CorrelationId = "correlation-pr216",
            CausationId = "cause-policy-pr216",
            EvidenceReferences = ["policy-satisfaction-evidence"],
            BoundaryMaxims = ["Policy satisfaction is not release readiness."]
        };
        RollbackExecutionReceipt? rollbackReceipt = null;
        RollbackExecutionAuditReport? rollbackAudit = null;
        if (includeRollback)
        {
            rollbackReceipt = ValidRollbackReceipt(projectId, sourceRequest, sourceReceipt, now);
            rollbackAudit = ValidRollbackAudit(projectId, rollbackReceipt, sourceReceipt, sourceRequest, now);
        }

        var gateRequest = new WorkflowContinuationGateRequest
        {
            WorkflowContinuationGateRequestId = Guid.NewGuid(),
            ProjectId = projectId,
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            ExpectedWorkflowStateHash = H("workflow-state"),
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            AcceptedApproval = acceptedApproval,
            PolicySatisfaction = policy,
            SourceApplyRequest = sourceRequest,
            SourceApplyReceipt = sourceReceipt,
            RollbackExecutionReceipt = rollbackReceipt,
            RollbackExecutionAuditReport = rollbackAudit,
            RequestedAtUtc = now,
            EvidenceReferences = ["workflow-continuation-gate-evidence"],
            BoundaryMaxims = ["Workflow continuation gate satisfaction is evidence only."]
        };
        var gate = new WorkflowContinuationGateEvaluator().Evaluate(gateRequest);
        var transition = ValidTransitionRecord(projectId, workflowRunId, workflowStepId, sourceRequest, sourceReceipt, gate, rollbackReceipt, rollbackAudit, now);
        return new ReleaseReadinessReportRequest
        {
            ReleaseReadinessReportRequestId = Guid.NewGuid(),
            ProjectId = projectId,
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            AcceptedApproval = acceptedApproval,
            PolicySatisfaction = policy,
            SourceApplyRequest = sourceRequest,
            SourceApplyReceipt = sourceReceipt,
            RollbackExecutionReceipt = rollbackReceipt,
            RollbackExecutionAuditReport = rollbackAudit,
            WorkflowContinuationGateEvaluation = gate,
            WorkflowTransitionRecord = transition,
            RequestedAtUtc = now,
            EvidenceReferences = ["release-readiness-report-evidence"],
            BoundaryMaxims = ["Release readiness report is evidence summary only."]
        };
    }

    private static SourceApplyReceipt ValidSourceApplyReceipt(Guid projectId, SourceApplyRequest sourceRequest, Guid receiptId, bool partial)
    {
        var fileResult = new SourceApplyReceiptFileResult
        {
            Path = "src/file.txt",
            PreviousPath = null,
            OperationKind = SourceApplyRequestFileOperationKinds.ModifyFile,
            PatchArtifactChangeHash = sourceRequest.FileOperations[0].PatchArtifactChangeHash,
            OperationHash = sourceRequest.FileOperations[0].OperationHash,
            BeforeContentHash = sourceRequest.FileOperations[0].BeforeContentHash,
            AfterContentHash = sourceRequest.FileOperations[0].AfterContentHash,
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
        return Rehash(new SourceApplyReceipt
        {
            SourceApplyReceiptId = receiptId,
            ProjectId = projectId,
            ControlledSourceApplyRequestId = Guid.NewGuid(),
            SourceApplyRequestId = sourceRequest.SourceApplyRequestId,
            SourceApplyRequestHash = sourceRequest.SourceApplyRequestHash,
            SourceApplyDryRunReceiptId = Guid.NewGuid(),
            SourceApplyDryRunReceiptHash = H("source-apply-dry-run-receipt"),
            SourceApplyGateEvaluationId = sourceRequest.SourceApplyGateEvaluationId,
            SourceApplyGateEvaluationHash = sourceRequest.SourceApplyGateEvaluationHash,
            PatchArtifactId = sourceRequest.PatchArtifactId,
            PatchHash = sourceRequest.PatchHash,
            ChangeSetHash = sourceRequest.ChangeSetHash,
            RollbackSupportReceiptId = sourceRequest.RollbackSupportReceiptId,
            RollbackSupportReceiptHash = sourceRequest.RollbackSupportReceiptHash,
            SourceBaselineHash = sourceRequest.SourceBaselineHash,
            WorkspaceBoundaryHash = sourceRequest.WorkspaceBoundaryHash,
            ExpectedBranch = sourceRequest.ExpectedBranch,
            ExpectedCleanWorktreeHash = sourceRequest.ExpectedCleanWorktreeHash,
            ObservedBranch = sourceRequest.ExpectedBranch,
            ObservedCleanWorktreeHashBeforeApply = sourceRequest.ExpectedCleanWorktreeHash,
            ObservedCleanWorktreeHashAfterApply = H("clean-after"),
            MutationOccurred = true,
            ApplySucceeded = !partial,
            PartialApplyOccurred = partial,
            FileResults = [fileResult],
            IssueCodes = [],
            AppliedAtUtc = sourceRequest.RequestedAtUtc.AddMinutes(1),
            SourceApplyReceiptHash = "sha256:pending",
            EvidenceReferences = ["source-apply-receipt-evidence"],
            BoundaryMaxims = ["Source apply receipt is mutation evidence, not release approval."]
        });
    }

    private static RollbackExecutionReceipt ValidRollbackReceipt(Guid projectId, SourceApplyRequest request, SourceApplyReceipt receipt, DateTimeOffset now)
    {
        var fileResult = new RollbackExecutionReceiptFileResult
        {
            Path = "src/file.txt",
            PreviousPath = null,
            OperationKind = RollbackPlanFileActionKinds.RestoreModifiedFile,
            PatchArtifactChangeHash = request.FileOperations[0].PatchArtifactChangeHash,
            RollbackActionHash = H("rollback-action"),
            BeforeContentHash = request.FileOperations[0].AfterContentHash,
            AfterContentHash = request.FileOperations[0].BeforeContentHash,
            PreconditionsSatisfied = true,
            MutationApplied = true,
            Restored = true,
            Deleted = false,
            Recreated = false,
            RenamedBack = false,
            Noop = false,
            IssueCodes = [],
            FileResultHash = "sha256:pending"
        };
        fileResult = fileResult with { FileResultHash = RollbackExecutionReceiptHashing.ComputeFileResultHash(fileResult) };
        return Rehash(new RollbackExecutionReceipt
        {
            RollbackExecutionReceiptId = Guid.NewGuid(),
            ProjectId = projectId,
            ControlledRollbackExecutionRequestId = Guid.NewGuid(),
            RollbackPlanId = request.RollbackPlanId,
            RollbackPlanHash = request.RollbackPlanHash,
            RollbackSupportReceiptId = request.RollbackSupportReceiptId,
            RollbackSupportReceiptHash = request.RollbackSupportReceiptHash,
            SourceApplyRequestId = request.SourceApplyRequestId,
            SourceApplyRequestHash = request.SourceApplyRequestHash,
            SourceApplyReceiptId = receipt.SourceApplyReceiptId,
            SourceApplyReceiptHash = receipt.SourceApplyReceiptHash,
            PatchArtifactId = request.PatchArtifactId,
            PatchHash = request.PatchHash,
            ChangeSetHash = request.ChangeSetHash,
            SourceBaselineHash = request.SourceBaselineHash,
            WorkspaceBoundaryHash = request.WorkspaceBoundaryHash,
            ExpectedBranch = request.ExpectedBranch,
            ExpectedCleanWorktreeHash = request.ExpectedCleanWorktreeHash,
            ObservedBranch = request.ExpectedBranch,
            ObservedSourceBaselineHash = request.SourceBaselineHash,
            ObservedCleanWorktreeHashBeforeRollback = H("clean-before-rollback"),
            ObservedCleanWorktreeHashAfterRollback = H("clean-after-rollback"),
            MutationOccurred = true,
            RollbackSucceeded = true,
            PartialRollbackOccurred = false,
            FileResults = [fileResult],
            IssueCodes = [],
            RolledBackAtUtc = now.AddMinutes(2),
            RollbackExecutionReceiptHash = "sha256:pending",
            EvidenceReferences = ["rollback-receipt-evidence"],
            BoundaryMaxims = ["Rollback receipt is mutation evidence, not release approval."]
        });
    }

    private static RollbackExecutionAuditReport ValidRollbackAudit(Guid projectId, RollbackExecutionReceipt rollback, SourceApplyReceipt sourceReceipt, SourceApplyRequest request, DateTimeOffset now) => new()
    {
        RollbackExecutionAuditReportId = Guid.NewGuid(),
        ProjectId = projectId,
        RollbackExecutionReceiptId = rollback.RollbackExecutionReceiptId,
        RollbackExecutionReceiptHash = rollback.RollbackExecutionReceiptHash,
        SourceApplyReceiptId = sourceReceipt.SourceApplyReceiptId,
        SourceApplyReceiptHash = sourceReceipt.SourceApplyReceiptHash,
        RollbackPlanId = request.RollbackPlanId,
        RollbackPlanHash = request.RollbackPlanHash,
        RollbackSupportReceiptId = request.RollbackSupportReceiptId,
        RollbackSupportReceiptHash = request.RollbackSupportReceiptHash,
        PatchArtifactId = request.PatchArtifactId,
        PatchHash = request.PatchHash,
        ChangeSetHash = request.ChangeSetHash,
        EvidenceConsistent = true,
        ReceiptHashValid = true,
        FileResultHashesValid = true,
        RollbackSucceeded = true,
        MutationOccurred = true,
        PartialRollbackOccurred = false,
        WorkflowBoundaryAllowsContinuation = false,
        ReleaseBoundaryInfersReadiness = false,
        HumanReviewRequired = true,
        FileResults = [],
        Issues = [],
        AuditedAtUtc = now.AddMinutes(3),
        EvidenceReferences = ["rollback-audit-evidence"],
        BoundaryMaxims = ["Rollback audit is inspection, not release readiness."]
    };

    private static WorkflowTransitionRecord ValidTransitionRecord(
        Guid projectId,
        string workflowRunId,
        string workflowStepId,
        SourceApplyRequest sourceRequest,
        SourceApplyReceipt sourceReceipt,
        WorkflowContinuationGateEvaluation gate,
        RollbackExecutionReceipt? rollbackReceipt,
        RollbackExecutionAuditReport? rollbackAudit,
        DateTimeOffset now) =>
        Rehash(new WorkflowTransitionRecord
        {
            WorkflowTransitionRecordId = Guid.NewGuid(),
            ProjectId = projectId,
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            TransitionKind = WorkflowTransitionKinds.MarkStepComplete,
            PreviousWorkflowStateHash = H("previous-workflow"),
            NewWorkflowStateHash = H("new-workflow"),
            PreviousStepStateHash = H("previous-step"),
            NewStepStateHash = H("new-step"),
            PreviousStepId = workflowStepId,
            NextStepId = null,
            WorkflowContinuationGateEvaluationId = gate.WorkflowContinuationGateEvaluationId,
            WorkflowContinuationGateEvaluationHash = GovernedWorkflowContinuationHashing.ComputeGateEvaluationHash(gate),
            SourceApplyRequestId = sourceRequest.SourceApplyRequestId,
            SourceApplyRequestHash = sourceRequest.SourceApplyRequestHash,
            SourceApplyReceiptId = sourceReceipt.SourceApplyReceiptId,
            SourceApplyReceiptHash = sourceReceipt.SourceApplyReceiptHash,
            RollbackExecutionReceiptId = rollbackReceipt?.RollbackExecutionReceiptId,
            RollbackExecutionReceiptHash = rollbackReceipt?.RollbackExecutionReceiptHash,
            RollbackExecutionAuditReportId = rollbackAudit?.RollbackExecutionAuditReportId,
            RollbackExecutionAuditReportHash = rollbackAudit is null ? null : ReleaseReadinessReportHashing.ComputeRollbackExecutionAuditReportHash(rollbackAudit),
            WorkflowStateMutated = true,
            StepCompleted = true,
            NextStepStarted = false,
            ReleaseReadinessInferred = false,
            ReleaseApproved = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            TransitionedAtUtc = now.AddMinutes(4),
            WorkflowTransitionRecordHash = "sha256:pending",
            EvidenceReferences = ["workflow-transition-record-evidence"],
            BoundaryMaxims = ["Workflow transition record is evidence, not release readiness."]
        });

    private static SourceApplyReceipt Rehash(SourceApplyReceipt receipt) =>
        receipt with { SourceApplyReceiptHash = SourceApplyReceiptHashing.ComputeReceiptHash(receipt) };

    private static RollbackExecutionReceipt Rehash(RollbackExecutionReceipt receipt) =>
        receipt with { RollbackExecutionReceiptHash = RollbackExecutionReceiptHashing.ComputeReceiptHash(receipt) };

    private static WorkflowTransitionRecord Rehash(WorkflowTransitionRecord record) =>
        record with { WorkflowTransitionRecordHash = WorkflowTransitionRecordHashing.ComputeRecordHash(record) };

    private static string H(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string ReadRepositoryFile(params string[] path)
    {
        var root = RepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(path).ToArray()));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
