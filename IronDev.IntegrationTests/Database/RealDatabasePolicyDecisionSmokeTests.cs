using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Database;

[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestClass]
public sealed class RealDatabasePolicyDecisionSmokeTests
{
    [TestMethod]
    public void PolicyDecisionSmokeScript_RecordsDurablePolicyDecisionWithoutSideEffects()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "Database", "smoke-policy-decision.ps1"));
        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "PR77_REAL_DB_POLICY_DECISION_SMOKE_RECEIPT.md"));

        StringAssert.Contains(script, "governance.usp_ToolRequest_Create");
        StringAssert.Contains(script, "governance.usp_ToolGateDecision_Record");
        StringAssert.Contains(script, "governance.usp_ApprovalDecision_Record");
        StringAssert.Contains(script, "governance.usp_PolicyDecisionEvent_Record");
        StringAssert.Contains(script, "policy.decision.recorded");
        StringAssert.Contains(script, "durablePolicyDecisionRecorded");
        StringAssert.Contains(script, "policyGovernanceEventRecorded");
        StringAssert.Contains(script, "policyDecisionIsApproval");
        StringAssert.Contains(script, "policyDecisionIsExecutionPermission");
        StringAssert.Contains(script, "toolExecuted");
        StringAssert.Contains(script, "sourceApplied");
        StringAssert.Contains(script, "memoryPromoted");
        StringAssert.Contains(script, "workflowStarted");
        StringAssert.Contains(script, "a2aHandoffCreated");
        StringAssert.Contains(script, "dogfoodReceiptCreated");
        StringAssert.Contains(receipt, "Policy decision remains evidence.");
    }

    [TestMethod]
    public void PolicyDecisionSmokeReceipt_IsAsciiAndHasNoBom()
    {
        var root = FindRepositoryRoot();
        foreach (var relativePath in new[]
        {
            Path.Combine("Database", "smoke-policy-decision.ps1"),
            Path.Combine("Docs", "receipts", "PR77_REAL_DB_POLICY_DECISION_SMOKE_RECEIPT.md")
        })
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, relativePath));
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, relativePath);
            Assert.IsFalse(bytes.Any(value => value > 0x7F), relativePath);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
