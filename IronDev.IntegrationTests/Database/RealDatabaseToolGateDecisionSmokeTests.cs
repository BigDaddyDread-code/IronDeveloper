using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Database;

[TestClass]
[TestCategory("RealDatabaseToolGateDecisionSmoke")]
public sealed class RealDatabaseToolGateDecisionSmokeTests
{
    [TestMethod]
    public void SmokeScript_UsesApprovedStoredProcedureSurfaceOnly()
    {
        var text = File.ReadAllText(SmokeScriptPath());

        StringAssert.Contains(text, "governance.usp_ToolRequest_Create");
        StringAssert.Contains(text, "governance.usp_ToolGateDecision_Record");
        StringAssert.Contains(text, "governance.ToolGateDecision");
        StringAssert.Contains(text, "tool.gate.decision.recorded");
        StringAssert.Contains(text, "smoke.tool_gate");
        StringAssert.Contains(text, "PR75 real DB tool gate decision smoke test");

        AssertNoForbiddenTokens(
            text,
            "INSERT INTO governance.ToolGateDecision",
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
    public void SmokeScript_PreservesGateNotApprovalOrExecutionBoundary()
    {
        var text = File.ReadAllText(SmokeScriptPath());

        StringAssert.Contains(text, "durableGateDecisionRecorded = $true");
        StringAssert.Contains(text, "gateDecisionIsApproval = $false");
        StringAssert.Contains(text, "gatePassIsHumanApproval = $false");
        StringAssert.Contains(text, "policyDecisionCreated = $false");
        StringAssert.Contains(text, "executionPermissionGranted = $false");
        StringAssert.Contains(text, "toolExecuted = $false");
        StringAssert.Contains(text, "sourceApplied = $false");
        StringAssert.Contains(text, "memoryPromoted = $false");
        StringAssert.Contains(text, "governance.PolicyDecisionEvent");
        StringAssert.Contains(text, "CK_ToolGateDecision_NoApprovalGrant");
        StringAssert.Contains(text, "CK_ToolGateDecision_NoExecutionGrant");
    }

    [TestMethod]
    public void Receipt_DocumentsRealDatabaseSmokeCommandAndBoundaries()
    {
        var text = File.ReadAllText(ReceiptPath());

        StringAssert.Contains(text, "PR75 Real DB Tool Gate Decision Smoke Receipt");
        StringAssert.Contains(text, @".\Database\smoke-tool-gate-decision.ps1");
        StringAssert.Contains(text, "Run database-backed smoke commands sequentially.");
        StringAssert.Contains(text, "Gate decision is not approval.");
        StringAssert.Contains(text, "Gate decision is not execution permission.");
        StringAssert.Contains(text, "Gate decision is not source apply.");
        StringAssert.Contains(text, "Gate decision is not memory promotion.");
        StringAssert.Contains(text, "Human review remains required for source apply and memory promotion.");
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

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static string SmokeScriptPath() =>
        Path.Combine(RepositoryRoot(), "Database", "smoke-tool-gate-decision.ps1");

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR75_REAL_DB_TOOL_GATE_DECISION_SMOKE_RECEIPT.md");

    private static void AssertNoForbiddenTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected token: {token}");
        }
    }

    private static void AssertAsciiNoBomNoHiddenUnicode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"{path} has a UTF-8 BOM.");
        foreach (var value in bytes)
        {
            Assert.IsTrue(value is 9 or 10 or 13 or >= 32 and <= 126, $"{path} contains non-ASCII or hidden control byte 0x{value:X2}.");
        }
    }
}
