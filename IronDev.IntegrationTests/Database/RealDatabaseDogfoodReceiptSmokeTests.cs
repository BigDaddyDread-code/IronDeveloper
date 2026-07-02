using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Database;

[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestClass]
[TestCategory("RealDatabaseDogfoodReceiptSmoke")]
public sealed class RealDatabaseDogfoodReceiptSmokeTests
{
    [TestMethod]
    public void SmokeScript_UsesApprovedStoredProcedureSurfaceOnly()
    {
        var text = File.ReadAllText(SmokeScriptPath());

        StringAssert.Contains(text, "governance.usp_DogfoodReceipt_Record");
        StringAssert.Contains(text, "governance.DogfoodReceipt");
        StringAssert.Contains(text, "dogfood.receipt.recorded");
        StringAssert.Contains(text, "durableDogfoodReceiptRecorded");
        StringAssert.Contains(text, "dogfoodGovernanceEventRecorded");

        AssertNoForbiddenTokens(
            text,
            "INSERT INTO governance.DogfoodReceipt",
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
    public void SmokeScript_PreservesReceiptNotApprovalOrExecutionBoundary()
    {
        var text = File.ReadAllText(SmokeScriptPath());

        StringAssert.Contains(text, "dogfoodReceiptIsReleaseApproval");
        StringAssert.Contains(text, "dogfoodReceiptIsExecutionPermission");
        StringAssert.Contains(text, "policyDecisionCreated");
        StringAssert.Contains(text, "approvalDecisionCreated");
        StringAssert.Contains(text, "gateDecisionCreated");
        StringAssert.Contains(text, "toolRequestCreated");
        StringAssert.Contains(text, "toolExecuted");
        StringAssert.Contains(text, "sourceApplied");
        StringAssert.Contains(text, "memoryPromoted");
        StringAssert.Contains(text, "workflowStarted");
        StringAssert.Contains(text, "a2aHandoffCreated");
    }

    [TestMethod]
    public void Receipt_DocumentsRealDatabaseSmokeCommandAndBoundaries()
    {
        var text = File.ReadAllText(ReceiptPath());

        StringAssert.Contains(text, "PR78 Real DB Dogfood Receipt Smoke Receipt");
        StringAssert.Contains(text, @".\Database\smoke-dogfood-receipt.ps1");
        StringAssert.Contains(text, "Dogfood receipt is evidence only.");
        StringAssert.Contains(text, "Dogfood receipt is not release approval.");
        StringAssert.Contains(text, "Dogfood receipt is not execution permission.");
        StringAssert.Contains(text, "Dogfood receipt is not policy satisfaction.");
        StringAssert.Contains(text, "Dogfood receipt is not source apply.");
        StringAssert.Contains(text, "Dogfood receipt is not memory promotion.");
        StringAssert.Contains(text, "Dogfood receipt is not workflow continuation.");
        StringAssert.Contains(text, "Dogfood receipt is not A2A handoff.");
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
        Path.Combine(RepositoryRoot(), "Database", "smoke-dogfood-receipt.ps1");

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR78_REAL_DB_DOGFOOD_RECEIPT_SMOKE_RECEIPT.md");

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
