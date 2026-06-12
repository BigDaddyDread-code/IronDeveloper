using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Database;

[TestClass]
[TestCategory("RealDatabaseToolRequestSmoke")]
public sealed class RealDatabaseToolRequestSmokeTests
{
    [TestMethod]
    public void SmokeScript_UsesApprovedStoredProcedureSurfaceOnly()
    {
        var text = File.ReadAllText(SmokeScriptPath());

        StringAssert.Contains(text, "governance.usp_ToolRequest_Create");
        StringAssert.Contains(text, "governance.usp_ToolRequest_GetById");
        StringAssert.Contains(text, "governance.usp_ToolRequest_ListForProject");
        StringAssert.Contains(text, "governance.usp_ToolRequest_ListForCorrelation");
        StringAssert.Contains(text, "governance.GovernanceEvent");
        StringAssert.Contains(text, "tool.request.created");
        StringAssert.Contains(text, "smoke.tool_request");
        StringAssert.Contains(text, "PR74C real DB smoke test");

        AssertNoForbiddenTokens(
            text,
            "INSERT INTO governance.ToolRequest",
            "INSERT INTO governance.GovernanceEvent",
            "UPDATE governance.",
            "DELETE FROM governance.",
            "DROP TABLE",
            "ALTER TABLE",
            "CREATE TABLE",
            "Invoke-WebRequest",
            "HttpClient",
            "Start-Process");
    }

    [TestMethod]
    public void SmokeScript_AssertsNoGateApprovalDogfoodWorkflowA2aSourceApplyOrMemoryPromotion()
    {
        var text = File.ReadAllText(SmokeScriptPath());

        foreach (var expected in new[]
                 {
                     "governance.ApprovalDecision",
                     "governance.PolicyDecision",
                     "governance.PolicyDecisionEvent",
                     "governance.DogfoodReceipt",
                     "governance.WorkflowState",
                     "governance.WorkflowStep",
                     "governance.A2aHandoff",
                     "governance.AgentHandoff",
                     "governance.SourceApply",
                     "governance.MemoryPromotion"
                 })
        {
            StringAssert.Contains(text, expected);
        }

        StringAssert.Contains(text, "gateDecisionCreated = $false");
        StringAssert.Contains(text, "Assert-NoRowsIfObjectExists");
        StringAssert.Contains(text, "approvalDecisionCreated = $false");
        StringAssert.Contains(text, "policyDecisionCreated = $false");
        StringAssert.Contains(text, "dogfoodReceiptCreated = $false");
        StringAssert.Contains(text, "workflowStateCreated = $false");
        StringAssert.Contains(text, "a2aHandoffCreated = $false");
        StringAssert.Contains(text, "sourceApplyCreated = $false");
        StringAssert.Contains(text, "memoryPromotionCreated = $false");
    }

    [TestMethod]
    public void Receipt_DocumentsBothRealDatabaseSmokeCommandsAndSequentialRule()
    {
        var text = File.ReadAllText(ReceiptPath());

        StringAssert.Contains(text, "PR74C Real DB Tool Request Smoke Receipt");
        StringAssert.Contains(text, "IronDeveloper");
        StringAssert.Contains(text, "IronDeveloper_Test");
        StringAssert.Contains(text, @".\Database\apply-migrations.ps1");
        StringAssert.Contains(text, @".\Database\verify-migrations.ps1");
        StringAssert.Contains(text, @".\Database\smoke-tool-request.ps1");
        StringAssert.Contains(text, "Run database-backed smoke commands sequentially.");
        StringAssert.Contains(text, "There is intentionally no API list endpoint in this slice.");
    }

    [TestMethod]
    public void Receipt_PreservesRequestOnlyBoundaryLanguage()
    {
        var text = File.ReadAllText(ReceiptPath());

        foreach (var expected in new[]
                 {
                     "Tool request creation is not a gate decision",
                     "approval decision",
                     "workflow transition",
                     "A2A handoff",
                     "source apply",
                     "memory promotion",
                     "API access is not execution permission",
                     "API status is not governance",
                     "audit evidence is not approval",
                     "gate is not executor"
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
        Path.Combine(RepositoryRoot(), "Database", "smoke-tool-request.ps1");

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR74C_REAL_DB_TOOL_REQUEST_SMOKE_RECEIPT.md");

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
