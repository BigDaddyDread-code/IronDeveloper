using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("Receipt")]
[TestClass]
public sealed class BlockOOperationalReadinessReceiptTests
{
    [TestMethod] public void ReceiptFile_Exists() => Assert.IsTrue(File.Exists(ReceiptPath()));
    [TestMethod] public void Receipt_StatesBlockOAddsOperationalObservability() => AssertReceiptContains("Block O adds operational observability and traceability.");
    [TestMethod] public void Receipt_StatesBlockODoesNotAddOperationalAuthority() => AssertReceiptContains("Block O does not add operational authority.");
    [TestMethod] public void Receipt_StatesOperationalVisibilityIsNotAuthority() => AssertReceiptContains("Operational visibility is not operational authority.");
    [TestMethod] public void Receipt_StatesObservationIsNotApproval() => AssertReceiptContains("Observation is not approval.");
    [TestMethod] public void Receipt_StatesTraceabilityIsNotMutationPermission() => AssertReceiptContains("Traceability is not mutation permission.");
    [TestMethod] public void Receipt_StatesDiagnosisIsNotRepair() => AssertReceiptContains("Diagnosis is not repair.");
    [TestMethod] public void Receipt_StatesHealthIsNotReleaseReadiness() => AssertReceiptContains("Health is not release readiness.");
    [TestMethod] public void Receipt_StatesCorrelationIsNotApproval() => AssertReceiptContains("Correlation is not approval.");
    [TestMethod] public void Receipt_StatesRecommendationIsNotExecution() => AssertReceiptContains("Recommendation is not execution.");
    [TestMethod] public void Receipt_StatesRetentionRuleIsNotCleanupExecution() => AssertReceiptContains("Retention rule evaluation is not cleanup execution.");
    [TestMethod] public void Receipt_StatesDebuggingContractIsNotRuntimeControl() => AssertReceiptContains("Debugging contract is not runtime control.");
    [TestMethod] public void Receipt_ListsPr145GovernanceTraceExplorer() => AssertReceiptContains("PR145 - Governance Trace Explorer API");
    [TestMethod] public void Receipt_ListsPr146FailedWorkflowDiagnosis() => AssertReceiptContains("PR146 - Failed Workflow Diagnosis Report");
    [TestMethod] public void Receipt_ListsPr147ApprovalGateDogfoodCorrelation() => AssertReceiptContains("PR147 - Approval/Gate/Dogfood Correlation Report");
    [TestMethod] public void Receipt_ListsPr148AgentRunHealthSummary() => AssertReceiptContains("PR148 - Agent Run Health Summary");
    [TestMethod] public void Receipt_ListsPr149BackendOperationalHealth() => AssertReceiptContains("PR149 - Backend Operational Health Checks");
    [TestMethod] public void Receipt_ListsPr150GovernanceDataRetentionRules() => AssertReceiptContains("PR150 - Governance Data Retention and Cleanup Rules");
    [TestMethod] public void Receipt_ListsPr151OperationalDebuggingContractTests() => AssertReceiptContains("PR151 - Operational Debugging Contract Tests");
    [TestMethod] public void Receipt_StatesNoApprovalAuthority() => AssertReceiptContains("Block O does not add approval authority.");
    [TestMethod] public void Receipt_StatesNoPolicySatisfaction() => AssertReceiptContains("Block O does not add policy satisfaction.");
    [TestMethod] public void Receipt_StatesNoWorkflowTransition() => AssertReceiptContains("Block O does not add workflow transition.");
    [TestMethod] public void Receipt_StatesNoWorkflowContinuation() => AssertReceiptContains("Block O does not add workflow continuation.");
    [TestMethod] public void Receipt_StatesNoWorkflowExecution() => AssertReceiptContains("Block O does not add workflow execution.");
    [TestMethod] public void Receipt_StatesNoToolInvocation() => AssertReceiptContains("Block O does not add tool invocation.");
    [TestMethod] public void Receipt_StatesNoAgentDispatch() => AssertReceiptContains("Block O does not add agent dispatch.");
    [TestMethod] public void Receipt_StatesNoModelCalls() => AssertReceiptContains("Block O does not add model calls.");
    [TestMethod] public void Receipt_StatesNoPromptConstruction() => AssertReceiptContains("Block O does not add prompt construction.");
    [TestMethod] public void Receipt_StatesNoBackendRepair() => AssertReceiptContains("Block O does not add backend repair.");
    [TestMethod] public void Receipt_StatesNoBackendRestart() => AssertReceiptContains("Block O does not add backend restart.");
    [TestMethod] public void Receipt_StatesNoMigrationExecution() => AssertReceiptContains("Block O does not add migration execution.");
    [TestMethod] public void Receipt_StatesNoCleanupExecution() => AssertReceiptContains("Block O does not add cleanup execution.");
    [TestMethod] public void Receipt_StatesNoCleanupScheduling() => AssertReceiptContains("Block O does not add cleanup scheduling.");
    [TestMethod] public void Receipt_StatesNoSourceApply() => AssertReceiptContains("Block O does not add source apply.");
    [TestMethod] public void Receipt_StatesNoPatchApply() => AssertReceiptContains("Block O does not add patch apply.");
    [TestMethod] public void Receipt_StatesNoReleaseReadinessDecision() => AssertReceiptContains("Block O does not add release readiness decision.");
    [TestMethod] public void Receipt_StatesNoRawPrivatePayloadExposure() => AssertReceiptContains("Block O does not add raw/private payload exposure.");
    [TestMethod] public void Receipt_UsesCorrectReviewLine() => AssertReceiptContains("PR152 closes Block O as the observation deck. It does not install the control panel.");

    private static void AssertReceiptContains(string expected) => StringAssert.Contains(File.ReadAllText(ReceiptPath()), expected);

    private static string ReceiptPath() => Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR152_BLOCK_O_OPERATIONAL_READINESS_RECEIPT.md");

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
