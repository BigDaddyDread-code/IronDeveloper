using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowTransitionRecord")]
[TestCategory("PR211")]
public sealed class WorkflowTransitionRecordTests
{
    [TestMethod]
    public void WorkflowTransitionRecord_ValidContinueToNextStepPassesValidation()
    {
        var record = ValidRecord(WorkflowTransitionKinds.ContinueToNextStep);
        var result = WorkflowTransitionRecordValidation.Validate(record);

        Assert.IsTrue(result.IsValid, IssueText(result));
        Assert.IsTrue(record.WorkflowStateMutated);
        Assert.IsTrue(record.StepCompleted);
        Assert.IsTrue(record.NextStepStarted);
        AssertNoExternalAuthority(record);
    }

    [TestMethod]
    public void WorkflowTransitionRecord_ValidMarkStepCompletePassesValidation()
    {
        var record = ValidRecord(WorkflowTransitionKinds.MarkStepComplete);
        var result = WorkflowTransitionRecordValidation.Validate(record);

        Assert.IsTrue(result.IsValid, IssueText(result));
        Assert.IsTrue(record.WorkflowStateMutated);
        Assert.IsTrue(record.StepCompleted);
        Assert.IsFalse(record.NextStepStarted);
        AssertNoExternalAuthority(record);
    }

    [TestMethod]
    public void WorkflowTransitionRecord_ValidBlockedNoTransitionPassesValidation()
    {
        var record = ValidRecord(WorkflowTransitionKinds.BlockedNoTransition);
        var result = WorkflowTransitionRecordValidation.Validate(record);

        Assert.IsTrue(result.IsValid, IssueText(result));
        Assert.IsFalse(record.WorkflowStateMutated);
        Assert.IsFalse(record.StepCompleted);
        Assert.IsFalse(record.NextStepStarted);
        Assert.AreEqual(record.PreviousWorkflowStateHash, record.NewWorkflowStateHash);
        Assert.AreEqual(record.PreviousStepStateHash, record.NewStepStateHash);
        AssertNoExternalAuthority(record);
    }

    [TestMethod]
    public void WorkflowTransitionRecord_HashIsDeterministic()
    {
        var record = ValidRecord(WorkflowTransitionKinds.ContinueToNextStep);

        Assert.AreEqual(
            WorkflowTransitionRecordHashing.ComputeRecordHash(record),
            WorkflowTransitionRecordHashing.ComputeRecordHash(record));

        var reordered = Rehash(record with { EvidenceReferences = record.EvidenceReferences.Reverse().ToArray() });
        Assert.AreEqual(record.WorkflowTransitionRecordHash, reordered.WorkflowTransitionRecordHash);
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsMissingRequiredIdentifiersAndHashes()
    {
        var result = WorkflowTransitionRecordValidation.Validate(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            WorkflowTransitionRecordId = Guid.Empty,
            ProjectId = Guid.Empty,
            WorkflowRunId = "",
            WorkflowStepId = "",
            PreviousWorkflowStateHash = "",
            NewWorkflowStateHash = "not-a-hash",
            PreviousStepStateHash = "",
            NewStepStateHash = "not-a-hash",
            WorkflowTransitionRecordHash = "",
            EvidenceReferences = [],
            BoundaryMaxims = [],
            TransitionedAtUtc = default
        });

        AssertIssue(result, "Required");
        AssertIssue(result, "InvalidHash");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsUnknownTransitionKind()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            TransitionKind = "AutoContinue"
        }));

        AssertIssue(result, "UnknownTransitionKind");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsMissingGateEvidence()
    {
        var result = WorkflowTransitionRecordValidation.Validate(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            WorkflowContinuationGateEvaluationId = Guid.Empty,
            WorkflowContinuationGateEvaluationHash = ""
        });

        AssertIssue(result, "Required");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsMissingSourceApplyEvidence()
    {
        var result = WorkflowTransitionRecordValidation.Validate(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            SourceApplyRequestId = Guid.Empty,
            SourceApplyRequestHash = "",
            SourceApplyReceiptId = Guid.Empty,
            SourceApplyReceiptHash = ""
        });

        AssertIssue(result, "Required");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsPartialRollbackReceiptReference()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            RollbackExecutionReceiptId = Guid.NewGuid(),
            RollbackExecutionReceiptHash = null
        }));

        AssertIssue(result, "RollbackReceiptReferenceIncomplete");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsPartialRollbackAuditReference()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            RollbackExecutionReceiptId = Guid.NewGuid(),
            RollbackExecutionReceiptHash = H("rollback-receipt"),
            RollbackExecutionAuditReportId = Guid.NewGuid(),
            RollbackExecutionAuditReportHash = null
        }));

        AssertIssue(result, "RollbackAuditReferenceIncomplete");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsAuditReferenceWithoutRollbackReceiptReference()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            RollbackExecutionReceiptId = null,
            RollbackExecutionReceiptHash = null,
            RollbackExecutionAuditReportId = Guid.NewGuid(),
            RollbackExecutionAuditReportHash = H("rollback-audit")
        }));

        AssertIssue(result, "RollbackAuditRequiresReceiptReference");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsReleaseReadinessInference()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            ReleaseReadinessInferred = true
        }));

        AssertIssue(result, "ReleaseReadinessInferenceRejected");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsReleaseApproval()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            ReleaseApproved = true
        }));

        AssertIssue(result, "ReleaseApprovalRejected");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsSourceApplyExecutedFlag()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            SourceApplyExecuted = true
        }));

        AssertIssue(result, "SourceApplyExecutionRejected");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsRollbackExecutedFlag()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            RollbackExecuted = true
        }));

        AssertIssue(result, "RollbackExecutionRejected");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_ContinueToNextStepRequiresStepCompletedAndNextStarted()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            StepCompleted = false,
            NextStepStarted = false,
            NextStepId = null
        }));

        AssertIssue(result, "ContinueRequiresStepCompleted");
        AssertIssue(result, "ContinueRequiresNextStepStarted");
        AssertIssue(result, "Required");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_MarkStepCompleteMustNotStartNextStep()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.MarkStepComplete) with
        {
            NextStepStarted = true,
            NextStepId = "workflow-step-002"
        }));

        AssertIssue(result, "MarkCompleteMustNotStartNextStep");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_BlockedNoTransitionMustNotMutateState()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.BlockedNoTransition) with
        {
            WorkflowStateMutated = true,
            StepCompleted = true,
            NextStepStarted = true
        }));

        AssertIssue(result, "BlockedMustNotMutateWorkflow");
        AssertIssue(result, "BlockedMustNotCompleteStep");
        AssertIssue(result, "BlockedMustNotStartNextStep");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_TransitionRequiresStateHashChange()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            NewWorkflowStateHash = H("previous-workflow"),
            NewStepStateHash = H("previous-step")
        }));

        AssertIssue(result, "WorkflowStateHashMustChange");
        AssertIssue(result, "StepStateHashMustChange");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsPrivateRawMaterial()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            EvidenceReferences = ["raw prompt leaked"]
        }));

        AssertIssue(result, "PrivateOrRawMaterial");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_RejectsWorkflowReleaseGitMemoryRetrievalAuthorityClaims()
    {
        var result = WorkflowTransitionRecordValidation.Validate(Rehash(ValidRecord(WorkflowTransitionKinds.ContinueToNextStep) with
        {
            BoundaryMaxims = ["memory promoted by transition record"]
        }));

        AssertIssue(result, "AuthorityClaim");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_InterfaceSurfaceDoesNotExposeExecutionStoreOrMutation()
    {
        var names = typeof(WorkflowTransitionRecordValidation).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .Concat(typeof(WorkflowTransitionRecordHashing).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(method => method.Name))
            .ToArray();

        AssertDoesNotContainAny(names, "Save", "Store", "Execute", "Transition", "Continue", "Advance", "Complete", "Start", "Approve", "Release");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_DoesNotAddApiCliUiRuntimeWorkflowReleaseGitAgentsMemoryRetrieval()
    {
        var production = ReadRepoFile("IronDev.Core/Governance/WorkflowTransitionRecord.cs");

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
            "WorkflowTransitionExecutor",
            "WorkflowContinuationExecutor",
            "ContinueWorkflow",
            "AdvanceWorkflow",
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
    public void WorkflowTransitionRecord_NoApiCliOrSqlSurfaceAdded()
    {
        var root = RepositoryRoot();
        var apiText = ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Api"));
        var cliText = ReadAllTextIfDirectoryExists(Path.Combine(root, "tools", "IronDev.Cli")) +
                      ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Cli"));
        var databaseText = ReadAllTextIfDirectoryExists(Path.Combine(root, "Database"));

        AssertDoesNotContainAny(apiText, "WorkflowTransitionRecord");
        AssertDoesNotContainAny(cliText, "WorkflowTransitionRecord");
        AssertDoesNotContainAny(databaseText, "WorkflowTransitionRecord");
    }

    [TestMethod]
    public void WorkflowTransitionRecord_ReceiptDocumentsBoundary()
    {
        var receipt = ReadRepoFile("Docs/receipts/PR211_WORKFLOW_TRANSITION_RECORD_CONTRACT.md");

        StringAssert.Contains(receipt, "PR211 adds the workflow transition record contract only.");
        StringAssert.Contains(receipt, "PR211 does not transition workflow.");
        StringAssert.Contains(receipt, "PR211 does not mutate workflow state.");
        StringAssert.Contains(receipt, "PR211 does not continue workflow.");
        StringAssert.Contains(receipt, "PR211 does not add SQL.");
        StringAssert.Contains(receipt, "PR211 does not add API.");
        StringAssert.Contains(receipt, "PR211 does not add CLI.");
        StringAssert.Contains(receipt, "PR211 does not add UI.");
        StringAssert.Contains(receipt, "PR211 does not approve release.");
        StringAssert.Contains(receipt, "PR211 does not infer release readiness.");
        StringAssert.Contains(receipt, "PR211 does not execute rollback.");
        StringAssert.Contains(receipt, "PR211 does not promote memory.");
        StringAssert.Contains(receipt, "PR211 does not activate retrieval.");
        StringAssert.Contains(receipt, "WorkflowTransitionRecord contract is evidence shape only.");
        StringAssert.Contains(receipt, "WorkflowTransitionRecord contract is not workflow transition.");
        StringAssert.Contains(receipt, "GateSatisfied is not WorkflowTransitioned.");
        StringAssert.Contains(receipt, "WorkflowTransitionRecord is not ReleaseReady.");
        StringAssert.Contains(receipt, "WorkflowTransitionRecord is not ReleaseApproved.");
        StringAssert.Contains(receipt, "Human review remains required for release readiness and release approval.");
    }

    private static WorkflowTransitionRecord ValidRecord(string transitionKind)
    {
        var record = new WorkflowTransitionRecord
        {
            WorkflowTransitionRecordId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            WorkflowRunId = "workflow-run-001",
            WorkflowStepId = "workflow-step-001",
            TransitionKind = transitionKind,
            PreviousWorkflowStateHash = H("previous-workflow"),
            NewWorkflowStateHash = transitionKind == WorkflowTransitionKinds.BlockedNoTransition ? H("previous-workflow") : H("new-workflow"),
            PreviousStepStateHash = H("previous-step"),
            NewStepStateHash = transitionKind == WorkflowTransitionKinds.BlockedNoTransition ? H("previous-step") : H("new-step"),
            PreviousStepId = transitionKind == WorkflowTransitionKinds.BlockedNoTransition ? null : "workflow-step-001",
            NextStepId = transitionKind == WorkflowTransitionKinds.ContinueToNextStep ? "workflow-step-002" : null,
            WorkflowContinuationGateEvaluationId = Guid.NewGuid(),
            WorkflowContinuationGateEvaluationHash = H("continuation-gate"),
            SourceApplyRequestId = Guid.NewGuid(),
            SourceApplyRequestHash = H("source-apply-request"),
            SourceApplyReceiptId = Guid.NewGuid(),
            SourceApplyReceiptHash = H("source-apply-receipt"),
            RollbackExecutionReceiptId = Guid.NewGuid(),
            RollbackExecutionReceiptHash = H("rollback-receipt"),
            RollbackExecutionAuditReportId = Guid.NewGuid(),
            RollbackExecutionAuditReportHash = H("rollback-audit"),
            WorkflowStateMutated = transitionKind != WorkflowTransitionKinds.BlockedNoTransition,
            StepCompleted = transitionKind is WorkflowTransitionKinds.ContinueToNextStep or WorkflowTransitionKinds.MarkStepComplete,
            NextStepStarted = transitionKind == WorkflowTransitionKinds.ContinueToNextStep,
            ReleaseReadinessInferred = false,
            ReleaseApproved = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            TransitionedAtUtc = new DateTimeOffset(2026, 6, 17, 16, 0, 0, TimeSpan.Zero),
            WorkflowTransitionRecordHash = "sha256:pending",
            EvidenceReferences = ["workflow-transition-evidence", "continuation-gate-evidence"],
            BoundaryMaxims = ["WorkflowTransitionRecord is evidence shape only."],
            Boundary = WorkflowTransitionRecordBoundaryText.Boundary
        };

        return Rehash(record);
    }

    private static WorkflowTransitionRecord Rehash(WorkflowTransitionRecord record) =>
        record with { WorkflowTransitionRecordHash = WorkflowTransitionRecordHashing.ComputeRecordHash(record) };

    private static void AssertNoExternalAuthority(WorkflowTransitionRecord record)
    {
        Assert.IsFalse(record.ReleaseReadinessInferred);
        Assert.IsFalse(record.ReleaseApproved);
        Assert.IsFalse(record.SourceApplyExecuted);
        Assert.IsFalse(record.RollbackExecuted);
    }

    private static void AssertIssue(WorkflowTransitionRecordValidationResult result, string issueCode) =>
        Assert.IsTrue(
            result.Issues.Any(issue => issue.Code == issueCode || issue.Code.EndsWith("." + issueCode, StringComparison.Ordinal)),
            $"Expected issue {issueCode}. Actual: {IssueText(result)}");

    private static string IssueText(WorkflowTransitionRecordValidationResult result) =>
        string.Join("; ", result.Issues.Select(issue => $"{issue.Code}:{issue.Field}"));

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
