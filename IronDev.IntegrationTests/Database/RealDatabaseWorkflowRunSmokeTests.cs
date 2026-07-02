using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Database;

[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestClass]
[TestCategory("RealDatabaseWorkflowRunSmoke")]
public sealed class RealDatabaseWorkflowRunSmokeTests
{
    [TestMethod]
    public void SmokeScript_UsesApprovedStoredProcedureSurfaceOnly()
    {
        var text = File.ReadAllText(SmokeScriptPath());

        StringAssert.Contains(text, "workflow.usp_WorkflowRun_Create");
        StringAssert.Contains(text, "workflow.usp_WorkflowRun_Get");
        StringAssert.Contains(text, "workflow.usp_WorkflowRun_ListByCorrelation");
        StringAssert.Contains(text, "workflow.usp_WorkflowRun_ListBySubject");
        StringAssert.Contains(text, "workflow.WorkflowRun");
        StringAssert.Contains(text, "workflow.step.metadata.v1");
        StringAssert.Contains(text, "PR98 real DB workflow run smoke");

        AssertNoForbiddenTokens(
            text,
            "INSERT INTO workflow.WorkflowRun",
            "INSERT INTO workflow.WorkflowRunStep",
            "INSERT INTO workflow.WorkflowRunEvidenceReference",
            "INSERT INTO workflow.WorkflowRunGroundingReference",
            "UPDATE workflow.",
            "DELETE FROM workflow.",
            "DROP TABLE",
            "ALTER TABLE",
            "CREATE TABLE",
            "Invoke-WebRequest",
            "HttpClient",
            "Start-Process");
    }

    [TestMethod]
    public void SmokeScript_AssertsNoApprovalPolicyDogfoodToolA2aSourceApplyOrMemoryPromotion()
    {
        var text = File.ReadAllText(SmokeScriptPath());

        foreach (var expected in new[]
                 {
                     "governance.ApprovalDecision",
                     "governance.PolicyDecisionEvent",
                     "governance.DogfoodReceipt",
                     "governance.ToolGateDecision",
                     "governance.ToolRequest",
                     "a2a.AgentHandoff",
                     "agent.CollectiveMemoryItem",
                     "workflowStarted = $false",
                     "workflowContinued = $false",
                     "toolExecuted = $false",
                     "approvalGranted = $false",
                     "policySatisfied = $false",
                     "sourceApplied = $false",
                     "memoryPromoted = $false",
                     "authorityTransferred = $false"
                 })
        {
            StringAssert.Contains(text, expected);
        }
    }

    [TestMethod]
    public void Receipt_DocumentsRealDatabaseSmokeCommandsAndSequentialRule()
    {
        var text = File.ReadAllText(ReceiptPath());

        StringAssert.Contains(text, "PR98 Real DB Workflow Run Smoke Receipt");
        StringAssert.Contains(text, "IronDeveloper");
        StringAssert.Contains(text, "IronDeveloper_Test");
        StringAssert.Contains(text, @".\Database\apply-migrations.ps1");
        StringAssert.Contains(text, @".\Database\verify-migrations.ps1");
        StringAssert.Contains(text, @".\Database\smoke-workflow-run.ps1");
        StringAssert.Contains(text, "Run database-backed smoke commands sequentially.");
    }

    [TestMethod]
    public void Receipt_PreservesEvidenceOnlyBoundaryLanguage()
    {
        var text = File.ReadAllText(ReceiptPath());

        foreach (var expected in new[]
                 {
                     "Workflow run storage is evidence storage only",
                     "Workflow run is not workflow execution",
                     "Workflow run is not workflow continuation",
                     "Workflow run is not agent dispatch",
                     "Workflow run is not tool execution",
                     "Workflow run is not approval",
                     "Workflow run is not policy satisfaction",
                     "Workflow run is not release approval",
                     "Workflow run is not source apply",
                     "Workflow run is not memory promotion",
                     "Workflow run is not authority transfer"
                 })
        {
            StringAssert.Contains(text, expected);
        }
    }

    [TestMethod]
    public void SmokeReceiptFiles_AreAsciiNoBomAndNoHiddenUnicode()
    {
        AssertAsciiNoBomNoHiddenUnicode(SmokeScriptPath());
        AssertAsciiNoBomNoHiddenUnicode(ReceiptPath());
        AssertAsciiNoBomNoHiddenUnicode(BlockJDocPath());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static string SmokeScriptPath() =>
        Path.Combine(RepositoryRoot(), "Database", "smoke-workflow-run.ps1");

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR98_REAL_DB_WORKFLOW_RUN_SMOKE_RECEIPT.md");

    private static string BlockJDocPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "BLOCK_J_DURABLE_WORKFLOW_RUN_SUBSTRATE.md");

    private static void AssertNoForbiddenTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
    }

    private static void AssertAsciiNoBomNoHiddenUnicode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"UTF-8 BOM found in {path}.");

        var text = File.ReadAllText(path);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            Assert.IsTrue(character is '\r' or '\n' or '\t' || character is >= ' ' and <= '~', $"Unexpected non-ASCII or hidden character U+{(int)character:X4} at index {index} in {path}.");
        }
    }
}