using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("RollbackExecutionAudit")]
[TestCategory("PR207")]
public sealed class RollbackExecutionAuditTests
{
    [TestMethod]
    public void RollbackExecutionAudit_ConsistentSuccessfulReceiptAuditsCleanly()
    {
        var request = Fixture().Request;

        var report = new RollbackExecutionAuditor().Audit(request);

        Assert.IsTrue(report.EvidenceConsistent, IssueText(report));
        Assert.IsTrue(report.ReceiptHashValid);
        Assert.IsTrue(report.FileResultHashesValid);
        Assert.IsTrue(report.RollbackSucceeded);
        Assert.IsTrue(report.MutationOccurred);
        Assert.IsFalse(report.PartialRollbackOccurred);
        Assert.IsFalse(report.WorkflowBoundaryAllowsContinuation);
        Assert.IsFalse(report.ReleaseBoundaryInfersReadiness);
        Assert.IsTrue(report.HumanReviewRequired);
        Assert.AreEqual(1, report.FileResults.Count);
        Assert.IsTrue(report.FileResults[0].PlannedActionFound);
        Assert.IsTrue(report.FileResults[0].PatchArtifactChangeFound);
    }

    [TestMethod]
    public void RollbackExecutionAudit_ConsistentPartialReceiptRequiresHumanReviewAndDoesNotContinueWorkflow()
    {
        var fixture = Fixture();
        var failed = BuildReceiptFileResult(fixture, mutationApplied: false, issueCodes: ["WriteFailed"]);
        var receipt = Rehash(fixture.Request.RollbackExecutionReceipt with
        {
            RollbackSucceeded = false,
            MutationOccurred = true,
            PartialRollbackOccurred = true,
            FileResults = [failed],
            IssueCodes = ["PartialRollback"]
        });

        var report = new RollbackExecutionAuditor().Audit(fixture.Request with { RollbackExecutionReceipt = receipt });

        Assert.IsTrue(report.EvidenceConsistent, IssueText(report));
        Assert.IsFalse(report.RollbackSucceeded);
        Assert.IsTrue(report.MutationOccurred);
        Assert.IsTrue(report.PartialRollbackOccurred);
        Assert.IsFalse(report.WorkflowBoundaryAllowsContinuation);
        Assert.IsFalse(report.ReleaseBoundaryInfersReadiness);
        Assert.IsTrue(report.HumanReviewRequired);
    }

    [TestMethod]
    public void RollbackExecutionAudit_ConsistentFailedReceiptDoesNotClaimRollbackSuccess()
    {
        var fixture = Fixture();
        var failed = BuildReceiptFileResult(fixture, mutationApplied: false, issueCodes: ["PreflightFailed"]);
        var receipt = Rehash(fixture.Request.RollbackExecutionReceipt with
        {
            RollbackSucceeded = false,
            MutationOccurred = false,
            PartialRollbackOccurred = false,
            FileResults = [failed],
            IssueCodes = ["PreflightFailed"]
        });

        var report = new RollbackExecutionAuditor().Audit(fixture.Request with { RollbackExecutionReceipt = receipt });

        Assert.IsTrue(report.EvidenceConsistent, IssueText(report));
        Assert.IsFalse(report.RollbackSucceeded);
        Assert.IsFalse(report.MutationOccurred);
        Assert.IsFalse(report.PartialRollbackOccurred);
        Assert.IsTrue(report.HumanReviewRequired);
    }

    [TestMethod]
    public void RollbackExecutionAudit_DetectsReceiptHashMismatch()
    {
        var request = Fixture().Request;
        var badReceipt = request.RollbackExecutionReceipt with { RollbackExecutionReceiptHash = H("tampered-receipt-hash") };

        var report = new RollbackExecutionAuditor().Audit(request with { RollbackExecutionReceipt = badReceipt });

        Assert.IsFalse(report.EvidenceConsistent);
        Assert.IsFalse(report.ReceiptHashValid);
        AssertIssue(report, "ReceiptHashMismatch");
    }

    [TestMethod]
    public void RollbackExecutionAudit_DetectsFileResultHashMismatch()
    {
        var request = Fixture().Request;
        var badFile = request.RollbackExecutionReceipt.FileResults[0] with { FileResultHash = H("tampered-file-result") };
        var badReceipt = Rehash(request.RollbackExecutionReceipt with { FileResults = [badFile] });

        var report = new RollbackExecutionAuditor().Audit(request with { RollbackExecutionReceipt = badReceipt });

        Assert.IsFalse(report.EvidenceConsistent);
        Assert.IsTrue(report.ReceiptHashValid);
        Assert.IsFalse(report.FileResultHashesValid);
        AssertIssue(report, "FileResultHashMismatch");
        Assert.IsFalse(report.FileResults[0].FileResultHashValid);
    }

    [TestMethod]
    public void RollbackExecutionAudit_DetectsEvidenceBindingMismatch()
    {
        var request = Fixture().Request;
        var badReceipt = Rehash(request.RollbackExecutionReceipt with
        {
            RollbackPlanHash = H("wrong-plan"),
            RollbackSupportReceiptHash = H("wrong-support"),
            SourceApplyReceiptHash = H("wrong-source-receipt"),
            SourceApplyRequestHash = H("wrong-source-request"),
            PatchHash = H("wrong-patch"),
            ChangeSetHash = H("wrong-change-set"),
            SourceBaselineHash = H("wrong-baseline"),
            WorkspaceBoundaryHash = H("wrong-workspace"),
            ExpectedBranch = "wrong-branch",
            ExpectedCleanWorktreeHash = H("wrong-clean")
        });

        var report = new RollbackExecutionAuditor().Audit(request with { RollbackExecutionReceipt = badReceipt });

        Assert.IsFalse(report.EvidenceConsistent);
        AssertIssue(report, "RollbackPlanHashMismatch");
        AssertIssue(report, "RollbackSupportReceiptHashMismatch");
        AssertIssue(report, "SourceApplyReceiptHashMismatch");
        AssertIssue(report, "SourceApplyRequestHashMismatch");
        AssertIssue(report, "PatchHashMismatch");
        AssertIssue(report, "ChangeSetHashMismatch");
        AssertIssue(report, "SourceBaselineMismatch");
        AssertIssue(report, "WorkspaceBoundaryMismatch");
        AssertIssue(report, "ExpectedBranchMismatch");
        AssertIssue(report, "ExpectedCleanWorktreeHashMismatch");
    }

    [TestMethod]
    public void RollbackExecutionAudit_DetectsMissingRollbackPlanAction()
    {
        var request = Fixture().Request;
        var badFile = request.RollbackExecutionReceipt.FileResults[0] with { RollbackActionHash = H("missing-action") };
        badFile = badFile with { FileResultHash = RollbackExecutionReceiptHashing.ComputeFileResultHash(badFile) };
        var badReceipt = Rehash(request.RollbackExecutionReceipt with { FileResults = [badFile] });

        var report = new RollbackExecutionAuditor().Audit(request with { RollbackExecutionReceipt = badReceipt });

        Assert.IsFalse(report.EvidenceConsistent);
        AssertIssue(report, "MissingRollbackPlanAction");
        AssertIssue(report, "MissingFileResultForRollbackPlanAction");
        Assert.IsFalse(report.FileResults[0].PlannedActionFound);
    }

    [TestMethod]
    public void RollbackExecutionAudit_DetectsExtraAndDuplicateFileResult()
    {
        var request = Fixture().Request;
        var extra = request.RollbackExecutionReceipt.FileResults[0] with { Path = "src/extra.txt", RollbackActionHash = H("extra-action") };
        extra = extra with { FileResultHash = RollbackExecutionReceiptHashing.ComputeFileResultHash(extra) };
        var receipt = Rehash(request.RollbackExecutionReceipt with { FileResults = [request.RollbackExecutionReceipt.FileResults[0], request.RollbackExecutionReceipt.FileResults[0], extra] });

        var report = new RollbackExecutionAuditor().Audit(request with { RollbackExecutionReceipt = receipt });

        Assert.IsFalse(report.EvidenceConsistent);
        AssertIssue(report, "DuplicateFileResult");
        AssertIssue(report, "MissingRollbackPlanAction");
    }

    [TestMethod]
    public void RollbackExecutionAudit_DetectsWrongPatchArtifactChangeHash()
    {
        var request = Fixture().Request;
        var badFile = request.RollbackExecutionReceipt.FileResults[0] with { PatchArtifactChangeHash = H("wrong-patch-change") };
        badFile = badFile with { FileResultHash = RollbackExecutionReceiptHashing.ComputeFileResultHash(badFile) };
        var receipt = Rehash(request.RollbackExecutionReceipt with { FileResults = [badFile] });

        var report = new RollbackExecutionAuditor().Audit(request with { RollbackExecutionReceipt = receipt });

        Assert.IsFalse(report.EvidenceConsistent);
        AssertIssue(report, "PatchArtifactChangeHashMismatch");
        Assert.IsFalse(report.FileResults[0].PatchArtifactChangeFound);
    }

    [TestMethod]
    public void RollbackExecutionAudit_DetectsOperationFlagMismatch()
    {
        var request = Fixture().Request;
        var badFile = request.RollbackExecutionReceipt.FileResults[0] with
        {
            Restored = false,
            Deleted = true
        };
        badFile = badFile with { FileResultHash = RollbackExecutionReceiptHashing.ComputeFileResultHash(badFile) };
        var receipt = Rehash(request.RollbackExecutionReceipt with { FileResults = [badFile] });

        var report = new RollbackExecutionAuditor().Audit(request with { RollbackExecutionReceipt = receipt });

        Assert.IsFalse(report.EvidenceConsistent);
        AssertIssue(report, "OperationFlagMismatch");
        Assert.IsFalse(report.FileResults[0].FlagsConsistentWithOperation);
    }

    [TestMethod]
    public void RollbackExecutionAudit_DetectsPartialReceiptMissingUnappliedOperation()
    {
        var fixture = Fixture();
        var secondAction = new RollbackPlanFileAction
        {
            Path = "src/second.txt",
            PlannedActionKind = RollbackPlanFileActionKinds.RestoreModifiedFile,
            RestoreContentHash = H("second-before"),
            DeleteContentHash = null,
            ExpectedCurrentContentHash = H("second-after"),
            RollbackActionHash = H("rollback-second"),
            IsBinary = false
        };
        var plan = fixture.Request.RollbackPlan with { FileActions = [.. fixture.Request.RollbackPlan.FileActions, secondAction] };
        var receipt = Rehash(fixture.Request.RollbackExecutionReceipt with
        {
            RollbackSucceeded = false,
            MutationOccurred = true,
            PartialRollbackOccurred = true,
            IssueCodes = ["PartialRollback"]
        });

        var report = new RollbackExecutionAuditor().Audit(fixture.Request with { RollbackPlan = plan, RollbackExecutionReceipt = receipt });

        Assert.IsFalse(report.EvidenceConsistent);
        AssertIssue(report, "MissingFileResultForRollbackPlanAction");
        AssertIssue(report, "PartialRollbackMissingPlannedOperation");
    }

    [TestMethod]
    public void RollbackExecutionAudit_RejectsWorkflowReleaseGitMemoryRetrievalAuthorityClaims()
    {
        var request = Fixture().Request;
        var receipt = Rehash(request.RollbackExecutionReceipt with { EvidenceReferences = ["workflow can continue after rollback"] });

        var report = new RollbackExecutionAuditor().Audit(request with { RollbackExecutionReceipt = receipt });

        Assert.IsFalse(report.EvidenceConsistent);
        AssertIssue(report, "AuthorityClaim");
    }

    [TestMethod]
    public void RollbackExecutionAudit_RejectsPrivateRawMaterial()
    {
        var request = Fixture().Request;
        var receipt = Rehash(request.RollbackExecutionReceipt with { EvidenceReferences = ["raw prompt leaked"] });

        var report = new RollbackExecutionAuditor().Audit(request with { RollbackExecutionReceipt = receipt });

        Assert.IsFalse(report.EvidenceConsistent);
        Assert.IsTrue(report.Issues.Any(issue => issue.Code.Contains("PrivateOrRawMaterial", StringComparison.Ordinal) || issue.Code.Contains("UnsafeText", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void RollbackExecutionAudit_DoesNotAddMutationApiCliUiRuntimeWorkflowReleaseGitAgentsMemoryRetrieval()
    {
        var root = RepositoryRoot();
        var productionFiles = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackExecutionAudit.cs")
        };

        foreach (var file in productionFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in new[]
            {
                "File.WriteAllText",
                "File.WriteAllBytes",
                "File.Delete",
                "File.Move",
                "Directory.CreateDirectory",
                "Process.Start",
                "ProcessStartInfo",
                "git commit",
                "git push",
                "git merge",
                "gh pr",
                "ControllerBase",
                "HttpPost",
                "HttpPut",
                "HttpPatch",
                "HttpDelete",
                "IHostedService",
                "BackgroundService",
                "Scheduler",
                "WorkflowContinuation",
                "ReleaseReadiness",
                "RollbackExecutor.RollbackAsync",
                "ControlledRollbackExecutor",
                "ControlledSourceApplyExecutor",
                "AgentDispatch",
                "ModelProvider",
                "ToolInvoker",
                "PromoteMemory",
                "ActivateRetrieval",
                "Weaviate",
                "Embedding"
            })
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden production token found in {file}: {token}");
            }
        }
    }

    [TestMethod]
    public void RollbackExecutionAudit_ReceiptDocumentsBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR207_ROLLBACK_EXECUTION_AUDIT.md"));

        StringAssert.Contains(receipt, "PR207 adds rollback execution audit only.");
        StringAssert.Contains(receipt, "PR207 does not execute rollback.");
        StringAssert.Contains(receipt, "PR207 does not mutate source.");
        StringAssert.Contains(receipt, "PR207 does not add SQL.");
        StringAssert.Contains(receipt, "PR207 does not add API.");
        StringAssert.Contains(receipt, "PR207 does not add CLI.");
        StringAssert.Contains(receipt, "PR207 does not add UI.");
        StringAssert.Contains(receipt, "PR207 does not continue workflow.");
        StringAssert.Contains(receipt, "PR207 does not approve release.");
        StringAssert.Contains(receipt, "PR207 does not infer release readiness.");
        StringAssert.Contains(receipt, "Rollback execution audit is evidence inspection.");
        StringAssert.Contains(receipt, "EvidenceConsistent is not WorkflowCanContinue.");
        StringAssert.Contains(receipt, "RollbackSucceeded is not ReleaseReady.");
        StringAssert.Contains(receipt, "Human review remains required.");
    }

    private static AuditFixture Fixture()
    {
        var projectId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 17, 14, 0, 0, TimeSpan.Zero);
        var patchArtifactId = Guid.NewGuid();
        var sourceApplyRequestId = Guid.NewGuid();
        var sourceApplyReceiptId = Guid.NewGuid();
        var rollbackPlanId = Guid.NewGuid();
        var rollbackSupportReceiptId = Guid.NewGuid();
        var baselineHash = H("baseline");
        var workspaceHash = H("workspace");
        var beforeApplyCleanHash = H("clean-before-apply");
        var afterApplyCleanHash = H("clean-after-apply");
        var beforeContentHash = H("old");
        var afterContentHash = H("new");
        var change = new PatchArtifactFileChange
        {
            Path = "src/file.txt",
            PreviousPath = null,
            ChangeKind = "Modify",
            BeforeContentHash = beforeContentHash,
            AfterContentHash = afterContentHash,
            DiffHash = H("diff"),
            NormalizedDiff = "diff -- safe",
            IsBinary = false
        };
        var changeHash = PatchArtifactHashing.ComputeFileChangeHash(change);
        var changeSetHash = PatchArtifactHashing.ComputeChangeSetHash([change]);
        var patch = new PatchArtifact
        {
            PatchArtifactId = patchArtifactId,
            ProjectId = projectId,
            PatchArtifactKind = "SourcePatch",
            ControlledDryRunRequestId = Guid.NewGuid(),
            DryRunExecutionAuditId = Guid.NewGuid(),
            DryRunAuditHash = H("dry-run-audit"),
            DryRunReceiptHash = H("dry-run-receipt"),
            PolicySatisfactionId = Guid.NewGuid(),
            PolicySatisfactionHash = H("policy"),
            SubjectKind = "SourceApplyRequest",
            SubjectId = sourceApplyRequestId.ToString("D"),
            SubjectHash = H("subject"),
            SourceSnapshotReference = "snapshot-main",
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ValidationPlanId = "validation-plan",
            ValidationPlanHash = H("validation-plan"),
            PatchHash = "sha256:pending",
            ChangeSetHash = changeSetHash,
            FileChanges = [change],
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            EvidenceReferences = ["patch-evidence"],
            BoundaryMaxims = ["Patch artifact is not source apply."],
            Boundary = PatchArtifactBoundaryText.Boundary
        };
        patch = patch with { PatchHash = PatchArtifactHashing.ComputePatchHash(patch, changeSetHash) };

        var gate = new SourceApplyRequestGateEvaluationEvidence
        {
            SourceApplyGateEvaluationId = Guid.NewGuid(),
            SourceApplyGateEvaluationHash = H("source-apply-gate"),
            Satisfied = true,
            ProjectId = projectId,
            AcceptedApprovalId = Guid.NewGuid(),
            AcceptedApprovalHash = H("approval"),
            PolicySatisfactionId = patch.PolicySatisfactionId,
            PolicySatisfactionHash = patch.PolicySatisfactionHash,
            ControlledDryRunRequestId = patch.ControlledDryRunRequestId,
            DryRunExecutionAuditId = patch.DryRunExecutionAuditId,
            DryRunAuditHash = patch.DryRunAuditHash,
            DryRunReceiptHash = patch.DryRunReceiptHash,
            PatchArtifactId = patch.PatchArtifactId,
            PatchHash = patch.PatchHash,
            ChangeSetHash = patch.ChangeSetHash,
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            RollbackSupportReceiptHash = H("rollback-support"),
            RollbackPlanId = rollbackPlanId,
            RollbackPlanHash = H("rollback-plan"),
            RollbackGateEvaluationHash = H("rollback-gate"),
            SubjectKind = patch.SubjectKind,
            SubjectId = patch.SubjectId,
            SubjectHash = patch.SubjectHash,
            SourceSnapshotReference = patch.SourceSnapshotReference,
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = beforeApplyCleanHash,
            ExpiresAtUtc = now.AddHours(1),
            EvidenceReferences = ["gate-evidence"],
            BoundaryMaxims = ["Gate is not executor."]
        };
        var operation = new SourceApplyRequestFileOperation
        {
            Path = change.Path,
            PreviousPath = null,
            OperationKind = SourceApplyRequestFileOperationKinds.ModifyFile,
            BeforeContentHash = beforeContentHash,
            AfterContentHash = afterContentHash,
            DiffHash = change.DiffHash,
            PatchArtifactChangeHash = changeHash,
            OperationHash = H("source-operation")
        };
        var sourceRequest = new SourceApplyRequest
        {
            SourceApplyRequestId = sourceApplyRequestId,
            ProjectId = projectId,
            SourceApplyGateEvaluationId = gate.SourceApplyGateEvaluationId,
            SourceApplyGateEvaluationHash = gate.SourceApplyGateEvaluationHash,
            SourceApplyGateSatisfied = true,
            SourceApplyGateEvaluation = gate,
            AcceptedApprovalId = gate.AcceptedApprovalId,
            AcceptedApprovalHash = gate.AcceptedApprovalHash,
            PolicySatisfactionId = patch.PolicySatisfactionId,
            PolicySatisfactionHash = patch.PolicySatisfactionHash,
            ControlledDryRunRequestId = patch.ControlledDryRunRequestId,
            DryRunExecutionAuditId = patch.DryRunExecutionAuditId,
            DryRunAuditHash = patch.DryRunAuditHash,
            DryRunReceiptHash = patch.DryRunReceiptHash,
            PatchArtifactId = patch.PatchArtifactId,
            PatchHash = patch.PatchHash,
            ChangeSetHash = patch.ChangeSetHash,
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            RollbackSupportReceiptHash = gate.RollbackSupportReceiptHash,
            RollbackPlanId = rollbackPlanId,
            RollbackPlanHash = gate.RollbackPlanHash,
            RollbackGateEvaluationHash = gate.RollbackGateEvaluationHash,
            SubjectKind = patch.SubjectKind,
            SubjectId = patch.SubjectId,
            SubjectHash = patch.SubjectHash,
            SourceSnapshotReference = patch.SourceSnapshotReference,
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = beforeApplyCleanHash,
            FileOperations = [operation],
            RequestedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            SourceApplyRequestHash = H("source-apply-request"),
            EvidenceReferences = ["source-apply-request-evidence"],
            BoundaryMaxims = ["Source apply request is not apply."],
            Boundary = SourceApplyRequestBoundaryText.Boundary
        };
        var action = new RollbackPlanFileAction
        {
            Path = change.Path,
            PreviousPath = null,
            PlannedActionKind = RollbackPlanFileActionKinds.RestoreModifiedFile,
            RestoreContentHash = beforeContentHash,
            DeleteContentHash = null,
            ExpectedCurrentContentHash = afterContentHash,
            RollbackActionHash = H("rollback-action"),
            IsBinary = false
        };
        var plan = new RollbackPlan
        {
            RollbackPlanId = rollbackPlanId,
            ProjectId = projectId,
            RollbackPlanKind = "PatchArtifactRollbackPlan",
            PatchArtifactId = patch.PatchArtifactId,
            PatchHash = patch.PatchHash,
            ChangeSetHash = patch.ChangeSetHash,
            ControlledDryRunRequestId = patch.ControlledDryRunRequestId,
            DryRunExecutionAuditId = patch.DryRunExecutionAuditId,
            DryRunAuditHash = patch.DryRunAuditHash,
            DryRunReceiptHash = patch.DryRunReceiptHash,
            PolicySatisfactionId = patch.PolicySatisfactionId,
            PolicySatisfactionHash = patch.PolicySatisfactionHash,
            SubjectKind = patch.SubjectKind,
            SubjectId = patch.SubjectId,
            SubjectHash = patch.SubjectHash,
            SourceSnapshotReference = patch.SourceSnapshotReference,
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = beforeApplyCleanHash,
            RollbackPlanHash = gate.RollbackPlanHash,
            FileActions = [action],
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            EvidenceReferences = ["rollback-plan-evidence"],
            BoundaryMaxims = ["Rollback plan is not rollback execution."],
            Boundary = RollbackPlanBoundaryText.Boundary
        };
        var support = new RollbackSupportReceipt
        {
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            ProjectId = projectId,
            RollbackPlanId = plan.RollbackPlanId,
            RollbackPlanHash = plan.RollbackPlanHash,
            RollbackGateSatisfied = true,
            RollbackGateEvaluationHash = gate.RollbackGateEvaluationHash,
            PatchArtifactId = patch.PatchArtifactId,
            PatchHash = patch.PatchHash,
            ChangeSetHash = patch.ChangeSetHash,
            ControlledDryRunRequestId = patch.ControlledDryRunRequestId,
            DryRunExecutionAuditId = patch.DryRunExecutionAuditId,
            DryRunAuditHash = patch.DryRunAuditHash,
            DryRunReceiptHash = patch.DryRunReceiptHash,
            PolicySatisfactionId = patch.PolicySatisfactionId,
            PolicySatisfactionHash = patch.PolicySatisfactionHash,
            SubjectKind = patch.SubjectKind,
            SubjectId = patch.SubjectId,
            SubjectHash = patch.SubjectHash,
            SourceSnapshotReference = patch.SourceSnapshotReference,
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = beforeApplyCleanHash,
            RollbackSupportReceiptHash = gate.RollbackSupportReceiptHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            EvidenceReferences = ["rollback-support-evidence"],
            BoundaryMaxims = ["Rollback support receipt is not rollback execution."],
            Boundary = RollbackSupportReceiptBoundaryText.Boundary
        };
        var sourceFile = new SourceApplyReceiptFileResult
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
        sourceFile = sourceFile with { FileResultHash = SourceApplyReceiptHashing.ComputeFileResultHash(sourceFile) };
        var sourceReceipt = new SourceApplyReceipt
        {
            SourceApplyReceiptId = sourceApplyReceiptId,
            ProjectId = projectId,
            ControlledSourceApplyRequestId = Guid.NewGuid(),
            SourceApplyRequestId = sourceRequest.SourceApplyRequestId,
            SourceApplyRequestHash = sourceRequest.SourceApplyRequestHash,
            SourceApplyDryRunReceiptId = Guid.NewGuid(),
            SourceApplyDryRunReceiptHash = H("source-apply-dry-run-receipt"),
            SourceApplyGateEvaluationId = sourceRequest.SourceApplyGateEvaluationId,
            SourceApplyGateEvaluationHash = sourceRequest.SourceApplyGateEvaluationHash,
            PatchArtifactId = patch.PatchArtifactId,
            PatchHash = patch.PatchHash,
            ChangeSetHash = patch.ChangeSetHash,
            RollbackSupportReceiptId = support.RollbackSupportReceiptId,
            RollbackSupportReceiptHash = support.RollbackSupportReceiptHash,
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = beforeApplyCleanHash,
            ObservedBranch = "main",
            ObservedCleanWorktreeHashBeforeApply = beforeApplyCleanHash,
            ObservedCleanWorktreeHashAfterApply = afterApplyCleanHash,
            MutationOccurred = true,
            ApplySucceeded = true,
            PartialApplyOccurred = false,
            FileResults = [sourceFile],
            IssueCodes = ["NoIssues"],
            AppliedAtUtc = now.AddMinutes(5),
            SourceApplyReceiptHash = "sha256:pending",
            EvidenceReferences = ["source-apply-receipt-evidence"],
            BoundaryMaxims = ["SourceApplyReceipt is mutation evidence, not release approval."],
            Boundary = SourceApplyReceiptBoundaryText.Boundary
        };
        sourceReceipt = sourceReceipt with { SourceApplyReceiptHash = SourceApplyReceiptHashing.ComputeReceiptHash(sourceReceipt) };
        var rollbackFile = new RollbackExecutionReceiptFileResult
        {
            Path = action.Path,
            PreviousPath = null,
            OperationKind = action.PlannedActionKind,
            PatchArtifactChangeHash = changeHash,
            RollbackActionHash = action.RollbackActionHash,
            BeforeContentHash = afterContentHash,
            AfterContentHash = beforeContentHash,
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
        rollbackFile = rollbackFile with { FileResultHash = RollbackExecutionReceiptHashing.ComputeFileResultHash(rollbackFile) };
        var rollbackReceipt = new RollbackExecutionReceipt
        {
            RollbackExecutionReceiptId = Guid.NewGuid(),
            ProjectId = projectId,
            ControlledRollbackExecutionRequestId = Guid.NewGuid(),
            RollbackPlanId = plan.RollbackPlanId,
            RollbackPlanHash = plan.RollbackPlanHash,
            RollbackSupportReceiptId = support.RollbackSupportReceiptId,
            RollbackSupportReceiptHash = support.RollbackSupportReceiptHash,
            SourceApplyRequestId = sourceRequest.SourceApplyRequestId,
            SourceApplyRequestHash = sourceRequest.SourceApplyRequestHash,
            SourceApplyReceiptId = sourceReceipt.SourceApplyReceiptId,
            SourceApplyReceiptHash = sourceReceipt.SourceApplyReceiptHash,
            PatchArtifactId = patch.PatchArtifactId,
            PatchHash = patch.PatchHash,
            ChangeSetHash = patch.ChangeSetHash,
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = afterApplyCleanHash,
            ObservedBranch = "main",
            ObservedSourceBaselineHash = baselineHash,
            ObservedCleanWorktreeHashBeforeRollback = afterApplyCleanHash,
            ObservedCleanWorktreeHashAfterRollback = H("clean-after-rollback"),
            MutationOccurred = true,
            RollbackSucceeded = true,
            PartialRollbackOccurred = false,
            FileResults = [rollbackFile],
            IssueCodes = ["NoIssues"],
            RolledBackAtUtc = now.AddMinutes(10),
            RollbackExecutionReceiptHash = "sha256:pending",
            EvidenceReferences = ["rollback-execution-evidence"],
            BoundaryMaxims = ["RollbackExecutionReceipt is mutation evidence, not release approval."],
            Boundary = RollbackExecutionBoundaryText.Boundary
        };
        rollbackReceipt = Rehash(rollbackReceipt);
        var request = new RollbackExecutionAuditRequest
        {
            RollbackExecutionAuditRequestId = Guid.NewGuid(),
            ProjectId = projectId,
            RollbackExecutionReceipt = rollbackReceipt,
            RollbackPlan = plan,
            RollbackSupportReceipt = support,
            SourceApplyReceipt = sourceReceipt,
            SourceApplyRequest = sourceRequest,
            PatchArtifact = patch,
            AuditedAtUtc = now.AddMinutes(15),
            EvidenceReferences = ["rollback-execution-audit-evidence"],
            BoundaryMaxims = ["Rollback execution audit is not rollback execution."],
            Boundary = RollbackExecutionAuditBoundaryText.Boundary
        };
        return new AuditFixture(request, action, changeHash);
    }

    private static RollbackExecutionReceiptFileResult BuildReceiptFileResult(AuditFixture fixture, bool mutationApplied, IReadOnlyList<string> issueCodes)
    {
        var action = fixture.Action;
        var result = new RollbackExecutionReceiptFileResult
        {
            Path = action.Path,
            PreviousPath = action.PreviousPath,
            OperationKind = action.PlannedActionKind,
            PatchArtifactChangeHash = fixture.PatchArtifactChangeHash,
            RollbackActionHash = action.RollbackActionHash,
            BeforeContentHash = action.ExpectedCurrentContentHash,
            AfterContentHash = action.RestoreContentHash,
            PreconditionsSatisfied = issueCodes.Count == 0,
            MutationApplied = mutationApplied,
            Restored = mutationApplied,
            Deleted = false,
            Recreated = false,
            RenamedBack = false,
            Noop = false,
            IssueCodes = issueCodes,
            FileResultHash = "sha256:pending"
        };
        return result with { FileResultHash = RollbackExecutionReceiptHashing.ComputeFileResultHash(result) };
    }

    private static RollbackExecutionReceipt Rehash(RollbackExecutionReceipt receipt) =>
        receipt with { RollbackExecutionReceiptHash = RollbackExecutionReceiptHashing.ComputeReceiptHash(receipt) };

    private static void AssertIssue(RollbackExecutionAuditReport report, string code) =>
        Assert.IsTrue(report.Issues.Any(issue => issue.Code == code || issue.Code.EndsWith("." + code, StringComparison.Ordinal)), $"Expected issue {code}. Actual: {IssueText(report)}");

    private static string IssueText(RollbackExecutionAuditReport report) =>
        string.Join("; ", report.Issues.Select(issue => $"{issue.Code}:{issue.Field}"));

    private static string H(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record AuditFixture(RollbackExecutionAuditRequest Request, RollbackPlanFileAction Action, string PatchArtifactChangeHash);
}
