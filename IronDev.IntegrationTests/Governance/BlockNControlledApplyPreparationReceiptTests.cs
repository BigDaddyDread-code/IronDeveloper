using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("Receipt")]
[TestClass]
[TestCategory("BlockNControlledApplyPreparation")]
public sealed class BlockNControlledApplyPreparationReceiptTests
{
    [TestMethod] public void ReceiptFile_Exists() => Assert.IsTrue(File.Exists(ReceiptPath()));
    [TestMethod] public void Receipt_StatesBlockNAddsControlledApplyPreparation() => AssertReceiptContains("Block N adds controlled apply preparation surfaces.");
    [TestMethod] public void Receipt_StatesBlockNDoesNotAddControlledApplyExecution() => AssertReceiptContains("Block N does not add controlled apply execution.");
    [TestMethod] public void Receipt_StatesSourceApplyRemainsUnimplemented() => AssertReceiptContains("Source apply remains unimplemented.");
    [TestMethod] public void Receipt_StatesPatchApplyRemainsUnimplemented() => AssertReceiptContains("Patch apply remains unimplemented.");
    [TestMethod] public void Receipt_StatesApplyDryRunExecutionRemainsUnimplemented() => AssertReceiptContains("Apply dry-run execution remains unimplemented.");
    [TestMethod] public void Receipt_StatesAcceptedApprovalRecordsRemainUnimplemented() => AssertReceiptContains("Accepted approval records remain unimplemented unless added by a later governed slice.");
    [TestMethod] public void Receipt_ListsPr137SourceApplyApprovalRequirementContract() => AssertReceiptContains("PR137 - Source Apply Approval Requirement Contract");
    [TestMethod] public void Receipt_ListsPr138PatchProposalEvidencePackage() => AssertReceiptContains("PR138 - Patch Proposal Evidence Package");
    [TestMethod] public void Receipt_ListsPr139ControlledApplyPlanModel() => AssertReceiptContains("PR139 - Controlled Apply Plan Model");
    [TestMethod] public void Receipt_ListsPr140ApplyDryRunStore() => AssertReceiptContains("PR140 - Apply Dry-run Store");
    [TestMethod] public void Receipt_ListsPr141ApplyPreviewApi() => AssertReceiptContains("PR141 - Apply Preview API");
    [TestMethod] public void Receipt_ListsPr142ApplyPreviewCli() => AssertReceiptContains("PR142 - Apply Preview CLI");
    [TestMethod] public void Receipt_ListsPr143HumanApprovedApplyBoundaryTests() => AssertReceiptContains("PR143 - Human-approved Apply Boundary Tests");
    [TestMethod] public void Receipt_StatesSourceApplyApprovalRequirementIsNotApproval() => AssertReceiptContains("Source apply approval requirement is not approval.");
    [TestMethod] public void Receipt_StatesPatchProposalEvidencePackageIsNotPatch() => AssertReceiptContains("Patch proposal evidence package is not a patch.");
    [TestMethod] public void Receipt_StatesControlledApplyPlanIsNotExecution() => AssertReceiptContains("Controlled apply plan is not execution.");
    [TestMethod] public void Receipt_StatesApplyDryRunReceiptIsNotExecution() => AssertReceiptContains("Apply dry-run receipt is not dry-run execution.");
    [TestMethod] public void Receipt_StatesApplyPreviewApiIsPreviewOnly() => AssertReceiptContains("Apply preview API is preview-only.");
    [TestMethod] public void Receipt_StatesApplyPreviewCliIsPreviewOnly() => AssertReceiptContains("Apply preview CLI is preview-only.");
    [TestMethod] public void Receipt_StatesHumanApprovedLookingMaterialIsNotApplyAuthority() => AssertReceiptContains("Human-approved-looking review material is not apply authority.");
    [TestMethod] public void Receipt_StatesEvidenceIsNotApproval() => AssertReceiptContains("Evidence is not approval.");
    [TestMethod] public void Receipt_StatesProposalIsNotImplementation() => AssertReceiptContains("Proposal is not implementation.");
    [TestMethod] public void Receipt_StatesPlanIsNotExecution() => AssertReceiptContains("Plan is not execution.");
    [TestMethod] public void Receipt_StatesPreviewIsNotPermission() => AssertReceiptContains("Preview is not permission.");
    [TestMethod] public void Receipt_StatesReceiptIsNotCapability() => AssertReceiptContains("Receipt is not capability.");
    [TestMethod] public void Receipt_StatesTraceabilityIsNotAuthority() => AssertReceiptContains("Traceability is not authority.");
    [TestMethod] public void Receipt_StatesNoSourceApply() => AssertReceiptContains("does not add source apply");
    [TestMethod] public void Receipt_StatesNoPatchApply() => AssertReceiptContains("patch apply");
    [TestMethod] public void Receipt_StatesNoDryRunExecution() => AssertReceiptContains("dry-run execution");
    [TestMethod] public void Receipt_StatesNoApprovalRecording() => AssertReceiptContains("approval recording");
    [TestMethod] public void Receipt_StatesNoAcceptedApprovalRecords() => AssertReceiptContains("accepted approval records");
    [TestMethod] public void Receipt_StatesNoApprovalSatisfaction() => AssertReceiptContains("approval satisfaction");
    [TestMethod] public void Receipt_StatesNoPolicySatisfaction() => AssertReceiptContains("policy satisfaction");
    [TestMethod] public void Receipt_StatesNoWorkflowContinuation() => AssertReceiptContains("workflow continuation");
    [TestMethod] public void Receipt_StatesNoWorkflowTransitionMutation() => AssertReceiptContains("workflow transition mutation");
    [TestMethod] public void Receipt_StatesNoToolExecution() => AssertReceiptContains("tool execution");
    [TestMethod] public void Receipt_StatesNoCommandExecution() => AssertReceiptContains("command execution");
    [TestMethod] public void Receipt_StatesNoAgentDispatch() => AssertReceiptContains("agent dispatch");
    [TestMethod] public void Receipt_StatesNoModelExecution() => AssertReceiptContains("model execution");
    [TestMethod] public void Receipt_StatesNoPromptConstruction() => AssertReceiptContains("prompt construction");
    [TestMethod] public void Receipt_StatesNoValidationExecution() => AssertReceiptContains("validation execution");
    [TestMethod] public void Receipt_StatesNoRollbackExecution() => AssertReceiptContains("rollback execution");
    [TestMethod] public void Receipt_StatesNoTicketCreation() => AssertReceiptContains("ticket creation");
    [TestMethod] public void Receipt_StatesNoMemoryPromotion() => AssertReceiptContains("memory promotion");
    [TestMethod] public void Receipt_StatesNoRetrievalActivation() => AssertReceiptContains("retrieval activation");
    [TestMethod] public void Receipt_StatesNoSourceFileReadWrite() => AssertReceiptContains("source file read/write");
    [TestMethod] public void Receipt_StatesNoPatchPayloadStorage() => AssertReceiptContains("patch payload storage");
    [TestMethod] public void Receipt_StatesNoSourceContentStorage() => AssertReceiptContains("source content storage");
    [TestMethod] public void Receipt_StatesNoApiWriteEndpoint() => AssertReceiptContains("API write endpoints");
    [TestMethod] public void Receipt_StatesNoCliWriteCommand() => AssertReceiptContains("CLI write commands");
    [TestMethod] public void Receipt_StatesNoUiRuntimeExecutor() => AssertReceiptContains("UI/runtime executors");
    [TestMethod] public void Receipt_StatesLaterBlockMustAddAcceptedApprovalRecords() => AssertReceiptContains("A later governed block must explicitly add accepted approval records before any apply authority can exist.");
    [TestMethod] public void Receipt_StatesLaterBlockMustAddSourceApplyExecution() => AssertReceiptContains("A later governed block must explicitly add source apply execution before source mutation can exist.");
    [TestMethod] public void Receipt_StatesLaterBlockMustAddPatchApplyExecution() => AssertReceiptContains("A later governed block must explicitly add patch apply execution before patch application can exist.");
    [TestMethod] public void Receipt_StatesLaterBlockMustAddWorkflowContinuation() => AssertReceiptContains("A later governed block must explicitly add workflow continuation before apply-related workflow state can advance.");
    [TestMethod] public void Receipt_UsesCorrectReviewLine() => AssertReceiptContains("PR144 closes Block N as preparation. It does not smuggle in apply.");

    private static void AssertReceiptContains(string expected)
    {
        var text = File.ReadAllText(ReceiptPath());
        StringAssert.Contains(text, expected);
    }

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR144_BLOCK_N_CONTROLLED_APPLY_PREPARATION_RECEIPT.md");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
