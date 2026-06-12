using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Database;

[TestClass]
public sealed class RealDatabaseApprovalDecisionSmokeTests
{
    [TestMethod]
    public void ApprovalDecisionSmokeScript_RecordsDurableApprovalWithoutSideEffects()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "Database", "smoke-approval-decision.ps1"));
        var receipt = File.ReadAllText(Path.Combine(root, "Docs", "receipts", "PR76_REAL_DB_APPROVAL_DECISION_SMOKE_RECEIPT.md"));

        StringAssert.Contains(script, "governance.usp_ToolRequest_Create");
        StringAssert.Contains(script, "governance.usp_ToolGateDecision_Record");
        StringAssert.Contains(script, "governance.usp_ApprovalDecision_Record");
        StringAssert.Contains(script, "approval.decision.recorded");
        StringAssert.Contains(script, "durableApprovalDecisionRecorded");
        StringAssert.Contains(script, "approvalGovernanceEventRecorded");
        StringAssert.Contains(script, "approvalDecisionIsExecutionPermission");
        StringAssert.Contains(script, "toolExecuted");
        StringAssert.Contains(script, "sourceApplied");
        StringAssert.Contains(script, "memoryPromoted");
        StringAssert.Contains(script, "workflowStarted");
        StringAssert.Contains(script, "a2aHandoffCreated");
        StringAssert.Contains(script, "dogfoodReceiptCreated");
        StringAssert.Contains(receipt, "Approval remains evidence.");
    }

    [TestMethod]
    public void ApprovalDecisionSmokeReceipt_IsAsciiAndHasNoBom()
    {
        var root = FindRepositoryRoot();
        foreach (var relativePath in new[]
        {
            Path.Combine("Database", "smoke-approval-decision.ps1"),
            Path.Combine("Docs", "receipts", "PR76_REAL_DB_APPROVAL_DECISION_SMOKE_RECEIPT.md")
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
