using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Database;

[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestClass]
[TestCategory("RealDatabaseThoughtLedgerGovernanceReferenceSmoke")]
[TestCategory("ThoughtLedgerGovernanceReference")]
public sealed class RealDatabaseThoughtLedgerGovernanceReferenceSmokeTests
{
    [TestMethod]
    public void SmokeScript_UsesApprovedStoredProcedureSurfaceOnly()
    {
        var text = File.ReadAllText(SmokeScriptPath());

        StringAssert.Contains(text, "governance.AppendGovernanceEvent");
        StringAssert.Contains(text, "governance.usp_ThoughtLedgerGovernanceEventReference_Record");
        StringAssert.Contains(text, "governance.ThoughtLedgerGovernanceEventReference");
        StringAssert.Contains(text, "thoughtledger.reference.smoke.event");

        AssertNoForbiddenTokens(
            text,
            "INSERT INTO governance.ThoughtLedgerGovernanceEventReference",
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
    public void SmokeScript_PreservesReferenceNotAuthorityBoundary()
    {
        var text = File.ReadAllText(SmokeScriptPath());

        StringAssert.Contains(text, "grantsApproval\":false");
        StringAssert.Contains(text, "grantsExecution\":false");
        StringAssert.Contains(text, "mutatesSource\":false");
        StringAssert.Contains(text, "promotesMemory\":false");
        StringAssert.Contains(text, "startsWorkflow\":false");
        StringAssert.Contains(text, "satisfiesPolicy\":false");
    }

    [TestMethod]
    public void Receipt_DocumentsRealDatabaseSmokeCommandAndBoundaries()
    {
        var text = File.ReadAllText(ReceiptPath());

        StringAssert.Contains(text, "PR79 Real DB ThoughtLedger Governance Reference Smoke Receipt");
        StringAssert.Contains(text, @".\Database\smoke-thoughtledger-governance-reference.ps1");
        StringAssert.Contains(text, "ThoughtLedger governance reference is evidence only.");
        StringAssert.Contains(text, "ThoughtLedger governance reference is not approval.");
        StringAssert.Contains(text, "ThoughtLedger governance reference is not execution permission.");
        StringAssert.Contains(text, "ThoughtLedger governance reference is not policy satisfaction.");
        StringAssert.Contains(text, "ThoughtLedger governance reference is not source apply.");
        StringAssert.Contains(text, "ThoughtLedger governance reference is not memory promotion.");
        StringAssert.Contains(text, "ThoughtLedger governance reference is not workflow continuation.");
        StringAssert.Contains(text, "ThoughtLedger governance reference is not release approval.");
        StringAssert.Contains(text, "ThoughtLedger governance reference is not dogfood receipt creation.");
        StringAssert.Contains(text, "ThoughtLedger governance reference is not A2A handoff creation.");
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
        Path.Combine(RepositoryRoot(), "Database", "smoke-thoughtledger-governance-reference.ps1");

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR79_REAL_DB_THOUGHTLEDGER_GOVERNANCE_REFERENCE_SMOKE_RECEIPT.md");

    private static void AssertNoForbiddenTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected token: {token}");
    }

    private static void AssertAsciiNoBomNoHiddenUnicode(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"{path} has a UTF-8 BOM.");
        foreach (var value in bytes)
            Assert.IsTrue(value is 9 or 10 or 13 or >= 32 and <= 126, $"{path} contains non-ASCII or hidden control byte 0x{value:X2}.");
    }
}