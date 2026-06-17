using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowContinuationGate")]
[TestCategory("PR210")]
public sealed class WorkflowContinuationGateTests
{
    private readonly WorkflowContinuationGateEvaluator _evaluator = new();

    [TestMethod]
    public void WorkflowContinuationGate_SourceApplySuccessWithoutRollbackCanSatisfyGateButDoesNotContinueWorkflow()
    {
        var result = _evaluator.Evaluate(ValidRequest());

        AssertSatisfiedWithoutMovement(result);
        Assert.IsTrue(result.SourceApplySucceeded);
        Assert.IsFalse(result.SourceApplyPartial);
        Assert.IsFalse(result.RollbackWasExecuted);
    }

    [TestMethod]
    public void WorkflowContinuationGate_SuccessfulRollbackWithConsistentAuditCanSatisfyGateButDoesNotContinueWorkflow()
    {
        var result = _evaluator.Evaluate(ValidRequest(includeRollback: true));

        AssertSatisfiedWithoutMovement(result);
        Assert.IsTrue(result.RollbackWasExecuted);
        Assert.IsTrue(result.RollbackSucceeded);
        Assert.IsFalse(result.RollbackPartial);
        Assert.IsTrue(result.RollbackAuditConsistent);
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksSourceApplyReceiptWithoutMutation()
    {
        var request = ValidRequest();

        var result = _evaluator.Evaluate(request with
        {
            SourceApplyReceipt = Rehash(request.SourceApplyReceipt with { MutationOccurred = false })
        });

        AssertBlocked(result, "SourceApplyMutationRequired");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksFailedSourceApplyWithoutRollback()
    {
        var request = ValidRequest();

        var result = _evaluator.Evaluate(request with
        {
            SourceApplyReceipt = Rehash(request.SourceApplyReceipt with
            {
                MutationOccurred = false,
                ApplySucceeded = false,
                PartialApplyOccurred = false,
                IssueCodes = ["ApplyFailed"]
            })
        });

        AssertBlocked(result, "SourceApplyRequiresSuccessfulRollbackEvidence");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksPartialSourceApplyWithoutRollback()
    {
        var request = ValidRequest();

        var result = _evaluator.Evaluate(request with
        {
            SourceApplyReceipt = Rehash(request.SourceApplyReceipt with
            {
                ApplySucceeded = false,
                PartialApplyOccurred = true,
                IssueCodes = ["PartialApply"]
            })
        });

        AssertBlocked(result, "SourceApplyRequiresSuccessfulRollbackEvidence");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksSourceApplyRequestReceiptMismatch()
    {
        var request = ValidRequest();

        var result = _evaluator.Evaluate(request with
        {
            SourceApplyReceipt = Rehash(request.SourceApplyReceipt with { SourceApplyRequestHash = H("wrong-source-request") })
        });

        AssertBlocked(result, "SourceApplyRequestReceiptHashMismatch");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksApprovalPolicySubjectMismatch()
    {
        var request = ValidRequest();

        var result = _evaluator.Evaluate(request with
        {
            PolicySatisfaction = request.PolicySatisfaction with { SubjectId = "other-subject" }
        });

        AssertBlocked(result, "PolicySubjectIdMismatch");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksExpiredPolicyEvidence()
    {
        var request = ValidRequest();

        var result = _evaluator.Evaluate(request with
        {
            PolicySatisfaction = request.PolicySatisfaction with { ExpiresAtUtc = request.RequestedAtUtc.AddMinutes(-1) }
        });

        AssertBlocked(result, "PolicySatisfactionExpired");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksRollbackReceiptWithoutAudit()
    {
        var request = ValidRequest(includeRollback: true);

        var result = _evaluator.Evaluate(request with { RollbackExecutionAuditReport = null });

        AssertBlocked(result, "RollbackAuditRequired");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksRollbackAuditWithoutReceipt()
    {
        var request = ValidRequest(includeRollback: true);

        var result = _evaluator.Evaluate(request with { RollbackExecutionReceipt = null });

        AssertBlocked(result, "RollbackReceiptRequired");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksRollbackReceiptSourceApplyMismatch()
    {
        var request = ValidRequest(includeRollback: true);

        var result = _evaluator.Evaluate(request with
        {
            RollbackExecutionReceipt = Rehash(request.RollbackExecutionReceipt! with { SourceApplyReceiptHash = H("wrong-source-receipt") })
        });

        AssertBlocked(result, "RollbackSourceApplyReceiptHashMismatch");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksRollbackAuditReceiptMismatch()
    {
        var request = ValidRequest(includeRollback: true);

        var result = _evaluator.Evaluate(request with
        {
            RollbackExecutionAuditReport = request.RollbackExecutionAuditReport! with { RollbackExecutionReceiptHash = H("wrong-rollback-receipt") }
        });

        AssertBlocked(result, "RollbackAuditReceiptHashMismatch");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksInconsistentRollbackAudit()
    {
        var request = ValidRequest(includeRollback: true);

        var result = _evaluator.Evaluate(request with
        {
            RollbackExecutionAuditReport = request.RollbackExecutionAuditReport! with { EvidenceConsistent = false }
        });

        AssertBlocked(result, "RollbackAuditInconsistent");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksRollbackAuditWithIssues()
    {
        var request = ValidRequest(includeRollback: true);

        var result = _evaluator.Evaluate(request with
        {
            RollbackExecutionAuditReport = request.RollbackExecutionAuditReport! with
            {
                Issues = [new RollbackExecutionAuditIssue("ObservedMismatch", "receipt", "Rollback audit found mismatched evidence.")]
            }
        });

        AssertBlocked(result, "RollbackAuditIssuesPresent");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksPartialRollback()
    {
        var request = ValidRequest(includeRollback: true);

        var result = _evaluator.Evaluate(request with
        {
            RollbackExecutionReceipt = Rehash(request.RollbackExecutionReceipt! with
            {
                RollbackSucceeded = false,
                PartialRollbackOccurred = true,
                IssueCodes = ["PartialRollback"]
            }),
            RollbackExecutionAuditReport = request.RollbackExecutionAuditReport! with
            {
                RollbackSucceeded = false,
                PartialRollbackOccurred = true,
                EvidenceConsistent = false
            }
        });

        AssertBlocked(result, "RollbackReceiptPartial");
        AssertIssue(result, "RollbackAuditPartial");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksFailedRollback()
    {
        var request = ValidRequest(includeRollback: true);

        var result = _evaluator.Evaluate(request with
        {
            RollbackExecutionReceipt = Rehash(request.RollbackExecutionReceipt! with
            {
                RollbackSucceeded = false,
                MutationOccurred = false,
                IssueCodes = ["RollbackFailed"]
            }),
            RollbackExecutionAuditReport = request.RollbackExecutionAuditReport! with { RollbackSucceeded = false }
        });

        AssertBlocked(result, "RollbackReceiptFailed");
        AssertIssue(result, "RollbackAuditFailed");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksRollbackReceiptNoMutationForNonNoopRollback()
    {
        var request = ValidRequest(includeRollback: true);

        var result = _evaluator.Evaluate(request with
        {
            RollbackExecutionReceipt = Rehash(request.RollbackExecutionReceipt! with { MutationOccurred = false })
        });

        AssertBlocked(result, "RollbackReceiptMutationRequired");
    }

    [TestMethod]
    public void WorkflowContinuationGate_BlocksRollbackAuditClaimsWorkflowContinuation()
    {
        var request = ValidRequest(includeRollback: true);

        var result = _evaluator.Evaluate(request with
        {
            RollbackExecutionAuditReport = request.RollbackExecutionAuditReport! with { WorkflowBoundaryAllowsContinuation = true }
        });

        AssertBlocked(result, "RollbackAuditCannotAllowContinuation");
    }

    [TestMethod]
    public void WorkflowContinuationGate_RejectsPrivateRawMaterialWithoutEchoingIt()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { EvidenceReferences = ["raw prompt leaked"] });
        var serialized = JsonSerializer.Serialize(result);

        AssertBlocked(result, "PrivateOrRawMaterial");
        Assert.IsFalse(serialized.Contains("raw prompt leaked", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void WorkflowContinuationGate_RejectsWorkflowReleaseGitMemoryRetrievalAuthorityClaimsWithoutEchoingThem()
    {
        var result = _evaluator.Evaluate(ValidRequest() with { BoundaryMaxims = ["memory promoted by continuation gate"] });
        var serialized = JsonSerializer.Serialize(result);

        AssertBlocked(result, "AuthorityClaim");
        Assert.IsFalse(serialized.Contains("memory promoted by continuation gate", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void WorkflowContinuationGate_InterfaceExposesEvaluationOnly()
    {
        var names = typeof(IWorkflowContinuationGateEvaluator).GetMethods().Select(method => method.Name).ToArray();

        CollectionAssert.AreEqual(new[] { "Evaluate" }, names);
        AssertDoesNotContainAny(names, "Continue", "Run", "Execute", "Dispatch", "Mutate", "Approve", "Release");
    }

    [TestMethod]
    public void WorkflowContinuationGate_DoesNotAddApiCliUiRuntimeWorkflowReleaseGitAgentsMemoryRetrieval()
    {
        var production = ReadRepoFile("IronDev.Core/Governance/WorkflowContinuationGate.cs");

        AssertDoesNotContainAny(
            production,
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
            "WorkflowContinuationExecutor",
            "ContinueWorkflow",
            "AdvanceWorkflow",
            "CompleteStep",
            "StartNextStep",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "AgentDispatch",
            "ModelProvider",
            "ToolInvoker",
            "PromoteMemory",
            "ActivateRetrieval",
            "Weaviate",
            "Embedding");
    }

    [TestMethod]
    public void WorkflowContinuationGate_NoApiCliOrSqlSurfaceAdded()
    {
        var root = RepositoryRoot();
        var apiText = ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Api"));
        var cliText = ReadAllTextIfDirectoryExists(Path.Combine(root, "tools", "IronDev.Cli")) +
                      ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Cli"));
        var databaseText = ReadAllTextIfDirectoryExists(Path.Combine(root, "Database"));

        AssertDoesNotContainAny(
            apiText,
            "WorkflowContinuationExecutor",
            "ContinueWorkflow(",
            "AdvanceWorkflow(",
            "CompleteStep(",
            "StartNextStep(",
            "usp_WorkflowContinuationGate_Save",
            "usp_WorkflowContinuationGate_Create",
            "usp_WorkflowContinuationGate_Evaluate");
        AssertDoesNotContainAny(cliText, "WorkflowContinuationGate");
        AssertDoesNotContainAny(
            databaseText,
            "CREATE TABLE governance.WorkflowContinuationGate",
            "usp_WorkflowContinuationGate_Save",
            "usp_WorkflowContinuationGate_Create",
            "usp_WorkflowContinuationGate_Evaluate");
    }

    [TestMethod]
    public void WorkflowContinuationGate_ReceiptDocumentsBoundary()
    {
        var receipt = ReadRepoFile("Docs/receipts/PR210_WORKFLOW_CONTINUATION_GATE.md");

        StringAssert.Contains(receipt, "PR210 adds workflow continuation gate evaluation only.");
        StringAssert.Contains(receipt, "PR210 does not continue workflow.");
        StringAssert.Contains(receipt, "PR210 does not mutate workflow state.");
        StringAssert.Contains(receipt, "PR210 does not add SQL.");
        StringAssert.Contains(receipt, "PR210 does not add API.");
        StringAssert.Contains(receipt, "PR210 does not add CLI.");
        StringAssert.Contains(receipt, "PR210 does not add UI.");
        StringAssert.Contains(receipt, "PR210 does not approve release.");
        StringAssert.Contains(receipt, "PR210 does not infer release readiness.");
        StringAssert.Contains(receipt, "PR210 does not execute rollback.");
        StringAssert.Contains(receipt, "PR210 does not promote memory.");
        StringAssert.Contains(receipt, "PR210 does not activate retrieval.");
        StringAssert.Contains(receipt, "Workflow continuation gate satisfaction is evidence only.");
        StringAssert.Contains(receipt, "EvidenceConsistent is not WorkflowContinued.");
        StringAssert.Contains(receipt, "GateSatisfied is not WorkflowContinued.");
        StringAssert.Contains(receipt, "RollbackSucceeded is not ReleaseReady.");
        StringAssert.Contains(receipt, "SourceApplySucceeded is not ReleaseReady.");
        StringAssert.Contains(receipt, "Human review remains required.");
    }

    private static void AssertSatisfiedWithoutMovement(WorkflowContinuationGateEvaluation result)
    {
        Assert.IsTrue(result.Satisfied, IssueText(result));
        Assert.AreEqual(WorkflowContinuationGateStatuses.Satisfied, result.Status);
        Assert.IsFalse(result.WorkflowStateMutated);
        Assert.IsFalse(result.WorkflowContinuationExecuted);
        Assert.IsFalse(result.ReleaseReadinessInferred);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsTrue(result.HumanReviewRequired);
        Assert.AreEqual(0, result.Issues.Count, IssueText(result));
    }

    private static void AssertBlocked(WorkflowContinuationGateEvaluation result, string issueCode)
    {
        Assert.IsFalse(result.Satisfied);
        Assert.AreEqual(WorkflowContinuationGateStatuses.Blocked, result.Status);
        Assert.IsFalse(result.WorkflowStateMutated);
        Assert.IsFalse(result.WorkflowContinuationExecuted);
        Assert.IsFalse(result.ReleaseReadinessInferred);
        Assert.IsFalse(result.ReleaseApproved);
        Assert.IsTrue(result.HumanReviewRequired);
        AssertIssue(result, issueCode);
    }

    private static void AssertIssue(WorkflowContinuationGateEvaluation result, string issueCode) =>
        Assert.IsTrue(
            result.Issues.Any(issue => issue.Code == issueCode || issue.Code.EndsWith("." + issueCode, StringComparison.Ordinal)),
            $"Expected issue {issueCode}. Actual: {IssueText(result)}");

    private static string IssueText(WorkflowContinuationGateEvaluation result) =>
        string.Join("; ", result.Issues.Select(issue => $"{issue.Code}:{issue.Field}"));

    private static WorkflowContinuationGateRequest ValidRequest(bool includeRollback = false)
    {
        var projectId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 17, 15, 0, 0, TimeSpan.Zero);
        var sourceApplyRequestId = Guid.NewGuid();
        var sourceApplyReceiptId = Guid.NewGuid();
        var sourceApplyRequestHash = H("source-apply-request");
        var subjectKind = "SourceApplyRequest";
        var subjectId = sourceApplyRequestId.ToString("D");
        var subjectHash = H("subject");
        var baselineHash = H("baseline");
        var workspaceHash = H("workspace");
        var cleanBefore = H("clean-before");
        var cleanAfter = H("clean-after");
        var patchArtifactId = Guid.NewGuid();
        var rollbackSupportReceiptId = Guid.NewGuid();
        var rollbackPlanId = Guid.NewGuid();
        var acceptedApprovalId = Guid.NewGuid();
        var policySatisfactionId = Guid.NewGuid();
        var sourceGateId = Guid.NewGuid();
        var patchHash = H("patch");
        var changeSetHash = H("change-set");
        var rollbackSupportHash = H("rollback-support");
        var rollbackPlanHash = H("rollback-plan");
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
            SourceApplyGateEvaluationId = sourceGateId,
            SourceApplyGateEvaluationHash = sourceGateHash,
            Satisfied = true,
            ProjectId = projectId,
            AcceptedApprovalId = acceptedApprovalId,
            AcceptedApprovalHash = H("accepted-approval"),
            PolicySatisfactionId = policySatisfactionId,
            PolicySatisfactionHash = H("policy-satisfaction"),
            ControlledDryRunRequestId = Guid.NewGuid(),
            DryRunExecutionAuditId = Guid.NewGuid(),
            DryRunAuditHash = H("dry-run-audit"),
            DryRunReceiptHash = H("dry-run-receipt"),
            PatchArtifactId = patchArtifactId,
            PatchHash = patchHash,
            ChangeSetHash = changeSetHash,
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            RollbackSupportReceiptHash = rollbackSupportHash,
            RollbackPlanId = rollbackPlanId,
            RollbackPlanHash = rollbackPlanHash,
            RollbackGateEvaluationHash = H("rollback-gate"),
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            SourceSnapshotReference = "snapshot-main",
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = cleanBefore,
            ExpiresAtUtc = now.AddHours(1),
            EvidenceReferences = ["source-gate-evidence"],
            BoundaryMaxims = ["Gate is not executor."]
        };
        var sourceRequest = new SourceApplyRequest
        {
            SourceApplyRequestId = sourceApplyRequestId,
            ProjectId = projectId,
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
            EvidenceReferences = ["source-apply-request-evidence"],
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
            ProjectId = projectId,
            ControlledSourceApplyRequestId = Guid.NewGuid(),
            SourceApplyRequestId = sourceApplyRequestId,
            SourceApplyRequestHash = sourceApplyRequestHash,
            SourceApplyDryRunReceiptId = Guid.NewGuid(),
            SourceApplyDryRunReceiptHash = H("source-apply-dry-run-receipt"),
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
            EvidenceReferences = ["source-apply-receipt-evidence"],
            BoundaryMaxims = ["SourceApplyReceipt is mutation evidence, not release approval."],
            Boundary = SourceApplyReceiptBoundaryText.Boundary
        };
        sourceReceipt = Rehash(sourceReceipt);
        var approval = new AcceptedApprovalRecord
        {
            AcceptedApprovalId = acceptedApprovalId,
            ProjectId = projectId,
            ApprovalTargetKind = subjectKind,
            ApprovalTargetId = subjectId,
            ApprovalTargetHash = subjectHash,
            CapabilityCode = "workflow-continuation-gate-input",
            ApprovalPurpose = AcceptedApprovalPurposes.WorkflowContinuationInput,
            ApprovedByActorId = "human-reviewer",
            ApprovedByActorDisplayName = "Human Reviewer",
            AcceptedAtUtc = now.AddMinutes(-20),
            ExpiresAtUtc = now.AddHours(1),
            CorrelationId = "correlation-001",
            CausationId = "cause-001",
            EvidenceReferences = ["accepted-approval-evidence"],
            BoundaryMaxims = ["Accepted approval is not workflow continuation."]
        };
        var policy = new PolicySatisfactionRecord
        {
            PolicySatisfactionId = policySatisfactionId,
            ProjectId = projectId,
            PolicyCode = "source-apply-policy",
            PolicyVersion = "v1",
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            CapabilityCode = approval.CapabilityCode,
            AcceptedApprovalId = acceptedApprovalId,
            ApprovalRequirementHash = H("approval-requirement"),
            ApprovalEvaluatedAtUtc = now.AddMinutes(-15),
            SatisfiedAtUtc = now.AddMinutes(-14),
            ExpiresAtUtc = now.AddHours(1),
            CorrelationId = "correlation-001",
            CausationId = "cause-002",
            EvidenceReferences = ["policy-satisfaction-evidence"],
            BoundaryMaxims = ["Policy satisfaction is not workflow continuation."],
            Boundary = PolicySatisfactionBoundaryText.Boundary
        };
        var request = new WorkflowContinuationGateRequest
        {
            WorkflowContinuationGateRequestId = Guid.NewGuid(),
            ProjectId = projectId,
            WorkflowRunId = "workflow-run-001",
            WorkflowStepId = "workflow-step-001",
            ExpectedWorkflowStateHash = H("workflow-state"),
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectHash = subjectHash,
            AcceptedApproval = approval,
            PolicySatisfaction = policy,
            SourceApplyRequest = sourceRequest,
            SourceApplyReceipt = sourceReceipt,
            RequestedAtUtc = now,
            EvidenceReferences = ["workflow-continuation-gate-evidence"],
            BoundaryMaxims = ["Workflow continuation gate satisfaction is evidence only."],
            Boundary = WorkflowContinuationGateBoundaryText.Boundary
        };

        if (!includeRollback)
        {
            return request;
        }

        var rollbackFile = new RollbackExecutionReceiptFileResult
        {
            Path = operation.Path,
            PreviousPath = null,
            OperationKind = RollbackPlanFileActionKinds.RestoreModifiedFile,
            PatchArtifactChangeHash = operation.PatchArtifactChangeHash,
            RollbackActionHash = H("rollback-action"),
            BeforeContentHash = operation.AfterContentHash,
            AfterContentHash = operation.BeforeContentHash,
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
            RollbackPlanId = rollbackPlanId,
            RollbackPlanHash = rollbackPlanHash,
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            RollbackSupportReceiptHash = rollbackSupportHash,
            SourceApplyRequestId = sourceApplyRequestId,
            SourceApplyRequestHash = sourceApplyRequestHash,
            SourceApplyReceiptId = sourceApplyReceiptId,
            SourceApplyReceiptHash = sourceReceipt.SourceApplyReceiptHash,
            PatchArtifactId = patchArtifactId,
            PatchHash = patchHash,
            ChangeSetHash = changeSetHash,
            SourceBaselineHash = baselineHash,
            WorkspaceBoundaryHash = workspaceHash,
            ExpectedBranch = "main",
            ExpectedCleanWorktreeHash = cleanAfter,
            ObservedBranch = "main",
            ObservedSourceBaselineHash = baselineHash,
            ObservedCleanWorktreeHashBeforeRollback = cleanAfter,
            ObservedCleanWorktreeHashAfterRollback = H("clean-after-rollback"),
            MutationOccurred = true,
            RollbackSucceeded = true,
            PartialRollbackOccurred = false,
            FileResults = [rollbackFile],
            IssueCodes = [],
            RolledBackAtUtc = now.AddMinutes(3),
            RollbackExecutionReceiptHash = "sha256:pending",
            EvidenceReferences = ["rollback-execution-receipt-evidence"],
            BoundaryMaxims = ["RollbackExecutionReceipt is mutation evidence, not release approval."],
            Boundary = RollbackExecutionBoundaryText.Boundary
        };
        rollbackReceipt = Rehash(rollbackReceipt);
        var auditFile = new RollbackExecutionAuditFileResult
        {
            Path = rollbackFile.Path,
            PreviousPath = null,
            OperationKind = rollbackFile.OperationKind,
            RollbackActionHash = rollbackFile.RollbackActionHash,
            PatchArtifactChangeHash = rollbackFile.PatchArtifactChangeHash,
            PlannedActionFound = true,
            PatchArtifactChangeFound = true,
            FileResultHashValid = true,
            MutationApplied = true,
            FlagsConsistentWithOperation = true,
            Issues = []
        };
        var audit = new RollbackExecutionAuditReport
        {
            RollbackExecutionAuditReportId = Guid.NewGuid(),
            ProjectId = projectId,
            RollbackExecutionReceiptId = rollbackReceipt.RollbackExecutionReceiptId,
            RollbackExecutionReceiptHash = rollbackReceipt.RollbackExecutionReceiptHash,
            SourceApplyReceiptId = sourceReceipt.SourceApplyReceiptId,
            SourceApplyReceiptHash = sourceReceipt.SourceApplyReceiptHash,
            RollbackPlanId = rollbackPlanId,
            RollbackPlanHash = rollbackPlanHash,
            RollbackSupportReceiptId = rollbackSupportReceiptId,
            RollbackSupportReceiptHash = rollbackSupportHash,
            PatchArtifactId = patchArtifactId,
            PatchHash = patchHash,
            ChangeSetHash = changeSetHash,
            EvidenceConsistent = true,
            ReceiptHashValid = true,
            FileResultHashesValid = true,
            RollbackSucceeded = true,
            MutationOccurred = true,
            PartialRollbackOccurred = false,
            WorkflowBoundaryAllowsContinuation = false,
            ReleaseBoundaryInfersReadiness = false,
            HumanReviewRequired = true,
            FileResults = [auditFile],
            Issues = [],
            AuditedAtUtc = now.AddMinutes(4),
            EvidenceReferences = ["rollback-execution-audit-evidence"],
            BoundaryMaxims = ["Rollback execution audit is not rollback execution."],
            Boundary = RollbackExecutionAuditBoundaryText.Boundary
        };

        return request with { RollbackExecutionReceipt = rollbackReceipt, RollbackExecutionAuditReport = audit };
    }

    private static SourceApplyReceipt Rehash(SourceApplyReceipt receipt) =>
        receipt with { SourceApplyReceiptHash = SourceApplyReceiptHashing.ComputeReceiptHash(receipt) };

    private static RollbackExecutionReceipt Rehash(RollbackExecutionReceipt receipt) =>
        receipt with { RollbackExecutionReceiptHash = RollbackExecutionReceiptHashing.ComputeReceiptHash(receipt) };

    private static string H(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static void AssertDoesNotContainAny(IEnumerable<string> values, params string[] forbidden) =>
        AssertDoesNotContainAny(string.Join("\n", values), forbidden);

    private static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
        }
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ReadAllTextIfDirectoryExists(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
