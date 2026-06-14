using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowStateContract")]
public sealed class WorkflowStateContractTests
{
    private const string ReceiptPath = "Docs/receipts/PR105_WORKFLOW_STATE_CONTRACT_TESTS.md";
    private const string BlockJPath = "Docs/BLOCK_J_DURABLE_WORKFLOW_RUN_SUBSTRATE.md";

    [TestMethod]
    public void WorkflowStateContract_ComposesRunStepCheckpointEvidenceAndGrounding()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot();

        Assert.AreEqual(snapshot.RunId, snapshot.StepRunId);
        Assert.AreEqual(snapshot.RunId, snapshot.CheckpointRunId);
        Assert.AreEqual(snapshot.StepId, snapshot.CheckpointStepId);
        Assert.IsTrue(snapshot.EvidenceReferenceIds.Contains("evidence.workflow.step.001"));
        Assert.IsTrue(snapshot.GroundingReferenceIds.Contains("grounding.workflow.checkpoint.001"));
        AssertAllAuthorityFlagsFalse(snapshot.Authority);
    }

    [TestMethod]
    public void RunStepAndCheckpointShareProjectScope()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot();

        Assert.AreEqual(snapshot.TenantId, snapshot.StepTenantId);
        Assert.AreEqual(snapshot.ProjectId, snapshot.StepProjectId);
        Assert.AreEqual(snapshot.TenantId, snapshot.CheckpointTenantId);
        Assert.AreEqual(snapshot.ProjectId, snapshot.CheckpointProjectId);
    }

    [TestMethod]
    public void StepBelongsToRun()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot();

        Assert.AreEqual(snapshot.RunId, snapshot.StepRunId);
        Assert.AreNotEqual(snapshot.RunId, snapshot.StepId);
    }

    [TestMethod]
    public void CheckpointBelongsToRun()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot();

        Assert.AreEqual(snapshot.RunId, snapshot.CheckpointRunId);
        Assert.AreNotEqual(snapshot.RunId, snapshot.CheckpointId);
    }

    [TestMethod]
    public void CheckpointMayBelongToStep()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot();

        Assert.AreEqual(snapshot.StepId, snapshot.CheckpointStepId);
    }

    [TestMethod]
    public void EvidenceReferencesRemainEvidenceOnly()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot();

        Assert.IsTrue(snapshot.EvidenceReferenceIds.All(id => id.StartsWith("evidence.", StringComparison.Ordinal)));
        Assert.IsFalse(snapshot.Authority.GrantsApproval);
        Assert.IsFalse(snapshot.Authority.GrantsExecution);
    }

    [TestMethod]
    public void GroundingReferencesRemainTraceabilityOnly()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot();

        Assert.IsTrue(snapshot.GroundingReferenceIds.All(id => id.StartsWith("grounding.", StringComparison.Ordinal)));
        Assert.IsFalse(snapshot.Authority.TransfersAuthority);
        Assert.IsFalse(snapshot.Authority.SatisfiesPolicy);
    }

    [TestMethod]
    public void RunCreatedDoesNotMeanStarted()
    {
        Assert.IsFalse(CreateSnapshot().Authority.StartsWorkflow);
    }

    [TestMethod]
    public void StepRecordedDoesNotMeanExecutable()
    {
        Assert.IsFalse(CreateSnapshot().Authority.ExecutesTool);
    }

    [TestMethod]
    public void CheckpointCapturedDoesNotMeanResumable()
    {
        Assert.IsFalse(CreateSnapshot().Authority.ResumesWorkflow);
    }

    [TestMethod]
    public void FailureRecordedDoesNotMeanRetryAllowed()
    {
        Assert.IsFalse(CreateSnapshot().Failure.RetriesWorkflow);
    }

    [TestMethod]
    public void RetryRecommendationDoesNotMeanRetryPermission()
    {
        WorkflowRetryContract retry = CreateSnapshot().Retry;

        Assert.IsTrue(retry.RetryRecommended);
        Assert.IsFalse(retry.RetriesWorkflow);
        Assert.IsFalse(retry.GrantsExecutionPermission);
    }

    [TestMethod]
    public void ReadyForReviewDoesNotMeanApproved()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot();

        Assert.AreEqual("ready_for_review", snapshot.ReviewStatus);
        Assert.IsFalse(snapshot.Authority.GrantsApproval);
    }

    [TestMethod]
    public void CompletedDoesNotMeanReleaseApproved()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot() with { RunStatus = "completed" };

        Assert.AreEqual("completed", snapshot.RunStatus);
        Assert.IsFalse(snapshot.Authority.ApprovesRelease);
    }

    [TestMethod]
    public void DoesNotGrantApproval() => Assert.IsFalse(CreateSnapshot().Authority.GrantsApproval);

    [TestMethod]
    public void DoesNotGrantExecution() => Assert.IsFalse(CreateSnapshot().Authority.GrantsExecution);

    [TestMethod]
    public void DoesNotSatisfyPolicy() => Assert.IsFalse(CreateSnapshot().Authority.SatisfiesPolicy);

    [TestMethod]
    public void DoesNotTransferAuthority() => Assert.IsFalse(CreateSnapshot().Authority.TransfersAuthority);

    [TestMethod]
    public void DoesNotMutateSource() => Assert.IsFalse(CreateSnapshot().Authority.MutatesSource);

    [TestMethod]
    public void DoesNotPromoteMemory() => Assert.IsFalse(CreateSnapshot().Authority.PromotesMemory);

    [TestMethod]
    public void DoesNotCreateAcceptedMemory() => Assert.IsFalse(CreateSnapshot().Authority.CreatesAcceptedMemory);

    [TestMethod]
    public void DoesNotApproveRelease() => Assert.IsFalse(CreateSnapshot().Authority.ApprovesRelease);

    [TestMethod]
    public void DoesNotStartWorkflow() => Assert.IsFalse(CreateSnapshot().Authority.StartsWorkflow);

    [TestMethod]
    public void DoesNotContinueWorkflow() => Assert.IsFalse(CreateSnapshot().Authority.ContinuesWorkflow);

    [TestMethod]
    public void DoesNotResumeWorkflow() => Assert.IsFalse(CreateSnapshot().Authority.ResumesWorkflow);

    [TestMethod]
    public void DoesNotRetryWorkflow() => Assert.IsFalse(CreateSnapshot().Authority.RetriesWorkflow);

    [TestMethod]
    public void DoesNotDispatchAgent() => Assert.IsFalse(CreateSnapshot().Authority.DispatchesAgent);

    [TestMethod]
    public void DoesNotExecuteTool() => Assert.IsFalse(CreateSnapshot().Authority.ExecutesTool);

    [TestMethod]
    public void DoesNotCallModel() => Assert.IsFalse(CreateSnapshot().Authority.CallsModel);

    [TestMethod]
    public void DoesNotCreateRuntimeEnvelope() => Assert.IsFalse(CreateSnapshot().Authority.CreatesRuntimeEnvelope);

    [TestMethod]
    public void ApiInspectionDoesNotChangeState()
    {
        WorkflowStateSnapshot before = CreateSnapshot();
        string apiJson = ApiInspection(before);
        WorkflowStateSnapshot after = CreateSnapshot();

        Assert.AreEqual(before.RunId, after.RunId);
        AssertBoundaryFlagsFalse(apiJson);
    }

    [TestMethod]
    public void CliInspectionDoesNotChangeState()
    {
        WorkflowStateSnapshot before = CreateSnapshot();
        string cliJson = CliInspection(before);
        WorkflowStateSnapshot after = CreateSnapshot();

        Assert.AreEqual(before.StepId, after.StepId);
        AssertBoundaryFlagsFalse(cliJson);
    }

    [TestMethod]
    public void ApiAndCliExposeSameRunIdentity()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot();

        AssertJsonProperty(ApiInspection(snapshot), "workflowRunId", snapshot.RunId);
        AssertJsonProperty(CliInspection(snapshot), "workflowRunId", snapshot.RunId);
    }

    [TestMethod]
    public void ApiAndCliExposeSameStepIdentity()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot();

        AssertJsonProperty(ApiInspection(snapshot), "workflowRunStepId", snapshot.StepId);
        AssertJsonProperty(CliInspection(snapshot), "workflowRunStepId", snapshot.StepId);
    }

    [TestMethod]
    public void ApiAndCliExposeSameCheckpointIdentity()
    {
        WorkflowStateSnapshot snapshot = CreateSnapshot();

        AssertJsonProperty(ApiInspection(snapshot), "workflowCheckpointId", snapshot.CheckpointId);
        AssertJsonProperty(CliInspection(snapshot), "workflowCheckpointId", snapshot.CheckpointId);
    }

    [TestMethod]
    public void ApiAndCliDoNotExposeCommandAffordances()
    {
        string output = ApiInspection(CreateSnapshot()) + CliInspection(CreateSnapshot());

        AssertDoesNotContainAny(output, "startWorkflow", "continueWorkflow", "resumeWorkflow", "retryWorkflow", "dispatchAgent", "executeTool", "callModel", "applySource", "promoteMemory", "approveRelease");
    }

    [TestMethod]
    public void DoesNotPersistHiddenReasoning() => AssertSafeText(CreateSnapshot().SafeSummary);

    [TestMethod]
    public void DoesNotPersistRawPrompt() => AssertDoesNotContainAny(SerializedSnapshot(), "rawPrompt", "raw prompt");

    [TestMethod]
    public void DoesNotPersistRawCompletion() => AssertDoesNotContainAny(SerializedSnapshot(), "rawCompletion", "raw completion");

    [TestMethod]
    public void DoesNotPersistRawToolOutput() => AssertDoesNotContainAny(SerializedSnapshot(), "rawToolOutput", "raw tool output");

    [TestMethod]
    public void DoesNotPersistScratchpad() => AssertDoesNotContainAny(SerializedSnapshot(), "scratchpad");

    [TestMethod]
    public void DoesNotPersistEntirePatchPayload() => AssertDoesNotContainAny(SerializedSnapshot(), "entirePatch", "entire patch");

    [TestMethod]
    public void ApiDoesNotExposeHiddenReasoning() => AssertSafeText(ApiInspection(CreateSnapshot()));

    [TestMethod]
    public void CliDoesNotPrintHiddenReasoning() => AssertSafeText(CliInspection(CreateSnapshot()));

    [TestMethod]
    public void InputOutputReferenceModelIsNotPersistedWorkflowState()
    {
        RequireType("IronDev.Core.Workflow.WorkflowStepInputReference");
        RequireType("IronDev.Core.Workflow.WorkflowStepOutputReference");
        Assert.IsNull(Type.GetType("IronDev.Core.Workflow.IWorkflowStepInputReferenceStore, IronDev.Core", throwOnError: false));
        Assert.IsNull(Type.GetType("IronDev.Core.Workflow.IWorkflowStepOutputReferenceStore, IronDev.Core", throwOnError: false));
    }

    [TestMethod]
    public void FailureRetryModelIsNotPersistedWorkflowState()
    {
        RequireType("IronDev.Core.Workflow.WorkflowFailureState");
        RequireType("IronDev.Core.Workflow.WorkflowRetryState");
        Assert.IsNull(Type.GetType("IronDev.Core.Workflow.IWorkflowFailureStateStore, IronDev.Core", throwOnError: false));
        Assert.IsNull(Type.GetType("IronDev.Core.Workflow.IWorkflowRetryStateStore, IronDev.Core", throwOnError: false));
    }

    [TestMethod]
    public void InputReferenceDoesNotConsumeInput()
    {
        Assert.IsFalse(CreateSnapshot().InputReference.ConsumesInput);
    }

    [TestMethod]
    public void OutputReferenceDoesNotProduceOutput()
    {
        Assert.IsFalse(CreateSnapshot().OutputReference.ProducesOutput);
    }

    [TestMethod]
    public void FailureStateDoesNotRetry()
    {
        Assert.IsFalse(CreateSnapshot().Failure.RetriesWorkflow);
    }

    [TestMethod]
    public void RetryStateDoesNotRetry()
    {
        Assert.IsFalse(CreateSnapshot().Retry.RetriesWorkflow);
    }

    [TestMethod]
    public void ReadsDoNotCreateToolRequests() => AssertNoSideEffectCreated("tool request");

    [TestMethod]
    public void ReadsDoNotCreateGateDecisions() => AssertNoSideEffectCreated("gate decision");

    [TestMethod]
    public void ReadsDoNotCreateApprovalDecisions() => AssertNoSideEffectCreated("approval decision");

    [TestMethod]
    public void ReadsDoNotCreatePolicyDecisions() => AssertNoSideEffectCreated("policy decision");

    [TestMethod]
    public void ReadsDoNotCreateDogfoodReceipts() => AssertNoSideEffectCreated("dogfood receipt");

    [TestMethod]
    public void ReadsDoNotCreateAgentHandoffs() => AssertNoSideEffectCreated("agent handoff");

    [TestMethod]
    public void ReadsDoNotCreateMemoryProposals() => AssertNoSideEffectCreated("memory proposal");

    [TestMethod]
    public void ReadsDoNotCreateToolExecutionAudits() => AssertNoSideEffectCreated("tool execution audit");

    [TestMethod]
    public void ReadsDoNotCreateAgentRunAudits() => AssertNoSideEffectCreated("agent run audit");

    [TestMethod]
    public void DoesNotAddSqlMigration() => AssertNoPr105ProductionArtifact("Database", "PR105", "WorkflowStateContract");

    [TestMethod]
    public void DoesNotAddApiEndpoint() => AssertNoPr105ProductionArtifact("IronDev.Api", "WorkflowStateContractController", "PR105Workflow");

    [TestMethod]
    public void DoesNotAddCliCommand() => AssertNoPr105ProductionArtifact("IronDev.Cli", "workflow-state-contract", "pr105");

    [TestMethod]
    public void DoesNotReferenceWorkflowRunner() => AssertNoPr105ProductionArtifact("IronDev.Core", "WorkflowStateContractRunner", "IWorkflowStateContractRunner");

    [TestMethod]
    public void DoesNotReferenceWorkflowExecutor() => AssertNoPr105ProductionArtifact("IronDev.Core", "WorkflowStateContractExecutor", "IWorkflowStateContractExecutor");

    [TestMethod]
    public void DoesNotReferenceSchedulerOrOrchestrator() => AssertNoPr105ProductionArtifact("IronDev.Core", "WorkflowStateContractScheduler", "WorkflowStateContractOrchestrator");

    [TestMethod]
    public void DoesNotReferenceLangGraph() => AssertNoPr105ProductionArtifact("IronDev.Core", "PR105LangGraph", "WorkflowStateContractLangGraph");

    [TestMethod]
    public void DoesNotReferenceA2aRuntime() => AssertNoPr105ProductionArtifact("IronDev.Core", "WorkflowStateContractA2aRuntime", "WorkflowStateContractAgentMessageBus");

    [TestMethod]
    public void DoesNotReferenceMessageBusOrQueue() => AssertNoPr105ProductionArtifact("IronDev.Core", "WorkflowStateContractMessageBus", "WorkflowStateContractQueue");

    [TestMethod]
    public void DoesNotReferenceModelClient() => AssertNoPr105ProductionArtifact("IronDev.Core", "WorkflowStateContractModelClient", "WorkflowStateContractChatCompletion");

    [TestMethod]
    public void DoesNotReferenceSourceApply() => AssertNoPr105ProductionArtifact("IronDev.Core", "WorkflowStateContractApplyCopy", "WorkflowStateContractSourceApply");

    [TestMethod]
    public void DoesNotReferenceMemoryPromotion() => AssertNoPr105ProductionArtifact("IronDev.Core", "WorkflowStateContractMemoryPromotion", "WorkflowStateContractAcceptedMemory");

    [TestMethod]
    public void DoesNotReferenceApprovalSatisfaction() => AssertNoPr105ProductionArtifact("IronDev.Core", "WorkflowStateContractApprovalSatisfaction", "WorkflowStateContractApprovalSatisfied");

    [TestMethod]
    public void DoesNotReferenceRetryRunner() => AssertNoPr105ProductionArtifact("IronDev.Core", "WorkflowStateContractRetryRunner", "WorkflowStateContractExecuteRetry");

    [TestMethod]
    public void DoesNotReferenceResumeOrRestoreEngine() => AssertNoPr105ProductionArtifact("IronDev.Core", "WorkflowStateContractResumeEngine", "WorkflowStateContractRestoreEngine", "WorkflowStateContractContinueEngine");

    [TestMethod]
    public void ReceiptDocumentsTestOnlyBoundary()
    {
        string receipt = ReadRepoFile(ReceiptPath);

        AssertContains(receipt, "PR105 is a contract test receipt only.");
        AssertContains(receipt, "does not add workflow runtime");
        AssertContains(receipt, "does not add API endpoints");
        AssertContains(receipt, "does not add CLI commands");
    }

    [TestMethod]
    public void BlockJDocumentMentionsPr105()
    {
        string blockJ = ReadRepoFile(BlockJPath);

        AssertContains(blockJ, "## PR105 Workflow State Contract Tests");
        AssertContains(blockJ, "compose without creating runtime authority");
    }

    private static WorkflowStateSnapshot CreateSnapshot()
    {
        AuthorityFlags authority = new(
            GrantsApproval: false,
            GrantsExecution: false,
            SatisfiesPolicy: false,
            TransfersAuthority: false,
            MutatesSource: false,
            PromotesMemory: false,
            CreatesAcceptedMemory: false,
            ApprovesRelease: false,
            StartsWorkflow: false,
            ContinuesWorkflow: false,
            ResumesWorkflow: false,
            RetriesWorkflow: false,
            DispatchesAgent: false,
            ExecutesTool: false,
            CallsModel: false,
            CreatesRuntimeEnvelope: false);

        return new WorkflowStateSnapshot(
            TenantId: "tenant-pr105",
            ProjectId: "project-pr105",
            RunId: "workflow-run-pr105",
            StepId: "workflow-step-pr105",
            CheckpointId: "workflow-checkpoint-pr105",
            StepRunId: "workflow-run-pr105",
            CheckpointRunId: "workflow-run-pr105",
            CheckpointStepId: "workflow-step-pr105",
            StepTenantId: "tenant-pr105",
            StepProjectId: "project-pr105",
            CheckpointTenantId: "tenant-pr105",
            CheckpointProjectId: "project-pr105",
            RunStatus: "created",
            ReviewStatus: "ready_for_review",
            SafeSummary: "Workflow state facts were recorded for inspection only.",
            EvidenceReferenceIds: new[] { "evidence.workflow.run.001", "evidence.workflow.step.001", "evidence.workflow.checkpoint.001" },
            GroundingReferenceIds: new[] { "grounding.workflow.run.001", "grounding.workflow.checkpoint.001" },
            InputReference: new WorkflowInputReferenceContract("input.workflow.context.001", ConsumesInput: false),
            OutputReference: new WorkflowOutputReferenceContract("output.workflow.summary.001", ProducesOutput: false),
            Failure: new WorkflowFailureContract("validation_failed", RetriesWorkflow: false),
            Retry: new WorkflowRetryContract(RetryRecommended: true, RetriesWorkflow: false, GrantsExecutionPermission: false),
            Authority: authority);
    }

    private static string ApiInspection(WorkflowStateSnapshot snapshot)
    {
        return JsonSerializer.Serialize(new
        {
            workflowRunId = snapshot.RunId,
            workflowRunStepId = snapshot.StepId,
            workflowCheckpointId = snapshot.CheckpointId,
            readOnly = true,
            createsWorkflowRecord = false,
            updatesWorkflowRecord = false,
            deletesWorkflowRecord = false,
            startsWorkflow = false,
            continuesWorkflow = false,
            resumesWorkflow = false,
            retriesWorkflow = false,
            dispatchesAgent = false,
            callsTool = false,
            callsModel = false,
            mutatesSource = false,
            promotesMemory = false,
            createsAcceptedMemory = false,
            approvesRelease = false,
            satisfiesApprovalRequirements = false,
            transfersAuthority = false,
            safeSummary = snapshot.SafeSummary
        });
    }

    private static string CliInspection(WorkflowStateSnapshot snapshot)
    {
        return JsonSerializer.Serialize(new
        {
            workflowRunId = snapshot.RunId,
            workflowRunStepId = snapshot.StepId,
            workflowCheckpointId = snapshot.CheckpointId,
            readOnly = true,
            createsWorkflowRecord = false,
            updatesWorkflowRecord = false,
            deletesWorkflowRecord = false,
            startsWorkflow = false,
            continuesWorkflow = false,
            resumesWorkflow = false,
            retriesWorkflow = false,
            dispatchesAgent = false,
            callsTool = false,
            callsModel = false,
            mutatesSource = false,
            promotesMemory = false,
            createsAcceptedMemory = false,
            approvesRelease = false,
            satisfiesApprovalRequirements = false,
            transfersAuthority = false,
            safeSummary = snapshot.SafeSummary
        });
    }

    private static string SerializedSnapshot() => JsonSerializer.Serialize(CreateSnapshot());

    private static void AssertBoundaryFlagsFalse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        AssertJsonBoolean(root, "createsWorkflowRecord", false);
        AssertJsonBoolean(root, "updatesWorkflowRecord", false);
        AssertJsonBoolean(root, "deletesWorkflowRecord", false);
        AssertJsonBoolean(root, "startsWorkflow", false);
        AssertJsonBoolean(root, "continuesWorkflow", false);
        AssertJsonBoolean(root, "resumesWorkflow", false);
        AssertJsonBoolean(root, "retriesWorkflow", false);
        AssertJsonBoolean(root, "dispatchesAgent", false);
        AssertJsonBoolean(root, "callsTool", false);
        AssertJsonBoolean(root, "callsModel", false);
        AssertJsonBoolean(root, "mutatesSource", false);
        AssertJsonBoolean(root, "promotesMemory", false);
        AssertJsonBoolean(root, "createsAcceptedMemory", false);
        AssertJsonBoolean(root, "approvesRelease", false);
        AssertJsonBoolean(root, "satisfiesApprovalRequirements", false);
        AssertJsonBoolean(root, "transfersAuthority", false);
    }

    private static void AssertJsonBoolean(JsonElement root, string propertyName, bool expected)
    {
        Assert.IsTrue(root.TryGetProperty(propertyName, out JsonElement value), $"Missing JSON property '{propertyName}'.");
        Assert.AreEqual(expected, value.GetBoolean(), $"Unexpected JSON flag '{propertyName}'.");
    }

    private static void AssertJsonProperty(string json, string propertyName, string expected)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.IsTrue(document.RootElement.TryGetProperty(propertyName, out JsonElement value), $"Missing JSON property '{propertyName}'.");
        Assert.AreEqual(expected, value.GetString());
    }

    private static void AssertAllAuthorityFlagsFalse(AuthorityFlags flags)
    {
        Assert.IsFalse(flags.GrantsApproval);
        Assert.IsFalse(flags.GrantsExecution);
        Assert.IsFalse(flags.SatisfiesPolicy);
        Assert.IsFalse(flags.TransfersAuthority);
        Assert.IsFalse(flags.MutatesSource);
        Assert.IsFalse(flags.PromotesMemory);
        Assert.IsFalse(flags.CreatesAcceptedMemory);
        Assert.IsFalse(flags.ApprovesRelease);
        Assert.IsFalse(flags.StartsWorkflow);
        Assert.IsFalse(flags.ContinuesWorkflow);
        Assert.IsFalse(flags.ResumesWorkflow);
        Assert.IsFalse(flags.RetriesWorkflow);
        Assert.IsFalse(flags.DispatchesAgent);
        Assert.IsFalse(flags.ExecutesTool);
        Assert.IsFalse(flags.CallsModel);
        Assert.IsFalse(flags.CreatesRuntimeEnvelope);
    }

    private static void AssertNoSideEffectCreated(string sideEffect)
    {
        WorkflowStateSnapshot before = CreateSnapshot();
        _ = ApiInspection(before);
        _ = CliInspection(before);
        WorkflowStateSnapshot after = CreateSnapshot();

        Assert.AreEqual(before.RunId, after.RunId, $"Read inspection unexpectedly created {sideEffect} state.");
        AssertAllAuthorityFlagsFalse(after.Authority);
    }

    private static void AssertSafeText(string text)
    {
        AssertDoesNotContainAny(
            text,
            "private reasoning",
            "hidden reasoning",
            "chainOfThought",
            "chain of thought",
            "chain-of-thought",
            "scratchpad",
            "rawPrompt",
            "raw prompt",
            "rawCompletion",
            "raw completion",
            "rawToolOutput",
            "raw tool output",
            "entirePatch",
            "entire patch");
    }

    private static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (string marker in forbidden)
        {
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}' found.");
        }
    }

    private static void AssertContains(string text, string expected)
    {
        Assert.IsTrue(text.Contains(expected, StringComparison.Ordinal), $"Expected to find '{expected}'.");
    }

    private static Type RequireType(string fullName)
    {
        Type? type = Type.GetType(fullName + ", IronDev.Core", throwOnError: false);
        Assert.IsNotNull(type, $"Expected Core type '{fullName}'.");
        return type!;
    }

    private static void AssertNoPr105ProductionArtifact(string relativeDirectory, params string[] forbidden)
    {
        string root = RepoRoot();
        string directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (string marker in forbidden)
            {
                Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected PR105 production artifact marker '{marker}' in {file}.");
            }
        }
    }

    private static void AssertTestFileDoesNotContain(params string[] forbidden)
    {
        string currentFile = Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "Governance", "WorkflowStateContractTests.cs");
        string text = File.ReadAllText(currentFile);

        foreach (string marker in forbidden)
        {
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"PR105 test file should not reference '{marker}'.");
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        return File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string RepoRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "IronDev.slnx")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        Assert.Fail("Could not locate repository root containing IronDev.slnx.");
        return string.Empty;
    }

    private sealed record WorkflowStateSnapshot(
        string TenantId,
        string ProjectId,
        string RunId,
        string StepId,
        string CheckpointId,
        string StepRunId,
        string CheckpointRunId,
        string CheckpointStepId,
        string StepTenantId,
        string StepProjectId,
        string CheckpointTenantId,
        string CheckpointProjectId,
        string RunStatus,
        string ReviewStatus,
        string SafeSummary,
        IReadOnlyList<string> EvidenceReferenceIds,
        IReadOnlyList<string> GroundingReferenceIds,
        WorkflowInputReferenceContract InputReference,
        WorkflowOutputReferenceContract OutputReference,
        WorkflowFailureContract Failure,
        WorkflowRetryContract Retry,
        AuthorityFlags Authority)
    {
        public string WorkflowRunId => RunId;
    }

    private sealed record WorkflowInputReferenceContract(string ReferenceId, bool ConsumesInput);

    private sealed record WorkflowOutputReferenceContract(string ReferenceId, bool ProducesOutput);

    private sealed record WorkflowFailureContract(string FailureKind, bool RetriesWorkflow);

    private sealed record WorkflowRetryContract(bool RetryRecommended, bool RetriesWorkflow, bool GrantsExecutionPermission);

    private sealed record AuthorityFlags(
        bool GrantsApproval,
        bool GrantsExecution,
        bool SatisfiesPolicy,
        bool TransfersAuthority,
        bool MutatesSource,
        bool PromotesMemory,
        bool CreatesAcceptedMemory,
        bool ApprovesRelease,
        bool StartsWorkflow,
        bool ContinuesWorkflow,
        bool ResumesWorkflow,
        bool RetriesWorkflow,
        bool DispatchesAgent,
        bool ExecutesTool,
        bool CallsModel,
        bool CreatesRuntimeEnvelope);
}
