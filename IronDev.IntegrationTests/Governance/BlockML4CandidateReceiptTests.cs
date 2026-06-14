using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class BlockML4CandidateReceiptTests
{
    [TestMethod]
    public void ReceiptFile_Exists()
    {
        Assert.IsTrue(File.Exists(ReceiptPath()), "PR136 receipt must exist.");
    }

    [TestMethod]
    public void Receipt_StatesBlockMAddsBoundedL4CandidateLayer() => AssertReceiptContains("Block M adds a bounded L4 candidate workflow layer.");

    [TestMethod]
    public void Receipt_StatesCandidateWorkflowsPackageSuppliedEvidenceOnly() => AssertReceiptContains("L4 candidate workflows package supplied safe evidence into review material.");

    [TestMethod]
    public void Receipt_StatesCandidateWorkflowsDoNotExecuteWork() => AssertReceiptContains("L4 candidate workflows do not execute work.");

    [TestMethod]
    public void Receipt_StatesCandidateWorkflowsDoNotGrantAuthority() => AssertReceiptContains("L4 candidate workflows do not grant authority.");

    [TestMethod]
    public void Receipt_ListsPr127TestFailureReviewCandidate() => AssertReceiptContains("PR127 - Test Failure Review Candidate Workflow");

    [TestMethod]
    public void Receipt_ListsPr128CriticReviewRequestCandidate() => AssertReceiptContains("PR128 - Critic Review Request Candidate Workflow");

    [TestMethod]
    public void Receipt_ListsPr129ImplementationProposalPackage() => AssertReceiptContains("PR129 - Implementation Proposal Package Workflow");

    [TestMethod]
    public void Receipt_ListsPr130ToolRequestGatePreview() => AssertReceiptContains("PR130 - Tool Request and Gate Preview Workflow");

    [TestMethod]
    public void Receipt_ListsPr131MemoryImprovementPackage() => AssertReceiptContains("PR131 - Memory Improvement Package Workflow");

    [TestMethod]
    public void Receipt_ListsPr132HumanApprovalPackage() => AssertReceiptContains("PR132 - Human Approval Package Workflow");

    [TestMethod]
    public void Receipt_ListsPr133DogfoodEvidenceBundle() => AssertReceiptContains("PR133 - Dogfood Evidence Bundle Workflow");

    [TestMethod]
    public void Receipt_ListsPr134RepeatedFailurePatternReview() => AssertReceiptContains("PR134 - Repeated Failure Pattern Review Workflow");

    [TestMethod]
    public void Receipt_ListsPr135L4CandidateCannotMutateSourceMemoryTests() => AssertReceiptContains("PR135 - L4 Candidate Cannot Mutate Source/Memory Tests");

    [TestMethod]
    public void Receipt_StatesEvidenceIsNotApproval() => AssertReceiptContains("Evidence is not approval.");

    [TestMethod]
    public void Receipt_StatesProposalIsNotImplementation() => AssertReceiptContains("Proposal is not implementation.");

    [TestMethod]
    public void Receipt_StatesPreviewIsNotExecution() => AssertReceiptContains("Preview is not execution.");

    [TestMethod]
    public void Receipt_StatesPackageIsNotPromotion() => AssertReceiptContains("Package is not promotion.");

    [TestMethod]
    public void Receipt_StatesApprovalPackageIsNotApproval() => AssertReceiptContains("Approval package is not approval.");

    [TestMethod]
    public void Receipt_StatesBundleIsNotProof() => AssertReceiptContains("Bundle is not proof.");

    [TestMethod]
    public void Receipt_StatesPatternReviewIsNotDiagnosis() => AssertReceiptContains("Pattern review is not diagnosis.");

    [TestMethod]
    public void Receipt_StatesReceiptIsNotCapability() => AssertReceiptContains("Receipt is not capability.");

    [TestMethod]
    public void Receipt_StatesTraceabilityIsNotAuthority() => AssertReceiptContains("Traceability is not authority.");

    [TestMethod]
    public void Receipt_StatesCandidateOutputCannotGrantAuthority() => AssertReceiptContains("Candidate output cannot grant authority.");

    [TestMethod]
    public void Receipt_StatesNoSourceApply() => AssertReceiptContains("Block M does not add source apply.");

    [TestMethod]
    public void Receipt_StatesNoPatchApply() => AssertReceiptContains("Block M does not add patch apply.");

    [TestMethod]
    public void Receipt_StatesNoMemoryPromotion() => AssertReceiptContains("Block M does not add memory promotion.");

    [TestMethod]
    public void Receipt_StatesNoAcceptedMemoryMutation() => AssertReceiptContains("Block M does not add accepted-memory mutation.");

    [TestMethod]
    public void Receipt_StatesNoRetrievalActivation() => AssertReceiptContains("Block M does not add retrieval activation.");

    [TestMethod]
    public void Receipt_StatesNoApprovalSatisfaction() => AssertReceiptContains("Block M does not add approval satisfaction.");

    [TestMethod]
    public void Receipt_StatesNoPolicySatisfaction() => AssertReceiptContains("Block M does not add policy satisfaction.");

    [TestMethod]
    public void Receipt_StatesNoWorkflowContinuation() => AssertReceiptContains("Block M does not add workflow continuation.");

    [TestMethod]
    public void Receipt_StatesNoToolExecution() => AssertReceiptContains("Block M does not add tool execution.");

    [TestMethod]
    public void Receipt_StatesNoCommandExecution() => AssertReceiptContains("Block M does not add command execution.");

    [TestMethod]
    public void Receipt_StatesNoAgentDispatch() => AssertReceiptContains("Block M does not add agent dispatch.");

    [TestMethod]
    public void Receipt_StatesNoModelExecution() => AssertReceiptContains("Block M does not add model execution.");

    [TestMethod]
    public void Receipt_StatesNoPromptConstruction() => AssertReceiptContains("Block M does not add prompt construction.");

    [TestMethod]
    public void Receipt_StatesNoTicketCreation() => AssertReceiptContains("Block M does not add ticket creation.");

    [TestMethod]
    public void Receipt_StatesNoIncidentCreation() => AssertReceiptContains("Block M does not add incident creation.");

    [TestMethod]
    public void Receipt_StatesNoDogfoodExecution() => AssertReceiptContains("Block M does not add dogfood execution.");

    [TestMethod]
    public void Receipt_StatesNoTestExecution() => AssertReceiptContains("Block M does not add test execution.");

    [TestMethod]
    public void Receipt_StatesNoValidationProof() => AssertReceiptContains("Block M does not add validation proof.");

    [TestMethod]
    public void Receipt_StatesNoReleaseReadiness() => AssertReceiptContains("Block M does not add release readiness.");

    [TestMethod]
    public void Receipt_StatesNoRootCauseProof() => AssertReceiptContains("Block M does not add root-cause proof.");

    [TestMethod]
    public void Receipt_StatesNoPatternProof() => AssertReceiptContains("Block M does not add pattern proof.");

    [TestMethod]
    public void Receipt_StatesNoSqlApiCliUiRuntimeHosting() => AssertReceiptContains("Block M does not add SQL/API/CLI/UI/runtime hosting.");

    [TestMethod]
    public void Receipt_StatesFutureStagesNeedExplicitAuthority()
    {
        AssertReceiptContains("Block M prepares review material for future governed stages. Future stages still need explicit authority, storage, policy, approval, and execution boundaries.");
        AssertReceiptContains("Later source apply, memory promotion, tool execution, approval recording, and workflow continuation remain unimplemented unless added by a later governed block.");
    }

    [TestMethod]
    public void Receipt_UsesCorrectReviewLine() => AssertReceiptContains("PR136 closes the Block M receipt. It does not turn candidates into execution.");

    private static void AssertReceiptContains(string expected) => StringAssert.Contains(ReadReceipt(), expected);

    private static string ReadReceipt() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() => Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR136_BLOCK_M_L4_CANDIDATE_RECEIPT.md");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory!.FullName;
    }
}
