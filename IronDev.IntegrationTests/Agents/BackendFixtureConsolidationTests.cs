using IronDev.Core.Agents;
using IronDev.Core.Agents.Concrete;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class BackendFixtureConsolidationTests
{
    [TestMethod]
    public void BackendFixtureInventory_DocumentsSharedAndIntentionallyLocalFixtures()
    {
        var inventory = ReadRepositoryFile("Docs", "BACKEND_TEST_FIXTURE_INVENTORY.md");

        StringAssert.Contains(inventory, "PR 49 is test cleanup, not proof weakening.");
        StringAssert.Contains(inventory, "No behavior change intended.");
        StringAssert.Contains(inventory, "No SQL/API/CLI/UI/runtime/persistence/capability changes.");
        StringAssert.Contains(inventory, "BackendAgentToolRequestFixtures.TestingAgentTestRunRequest");
        StringAssert.Contains(inventory, "BackendManualToolExecutionFixtures.PatchProposalRequestThatDoesNotApplySource");
        StringAssert.Contains(inventory, "Intentionally local fixtures");
        StringAssert.Contains(inventory, "Retrieval match remains distinct from memory candidate.");
        StringAssert.Contains(inventory, "Proposal remains distinct from apply.");
        StringAssert.Contains(inventory, "Audit remains distinct from approval.");
        StringAssert.Contains(inventory, "Gate remains distinct from executor.");
    }

    [TestMethod]
    public void SharedTesterFixture_KeepsToolRequestAndGateAuthoritySeparate()
    {
        var request = BackendManualToolExecutionFixtures.TesterExecutionRequestWithGovernanceGateApproval();

        Assert.AreEqual(AgentToolKind.TestRun, request.ToolRequest.ToolKind);
        Assert.AreEqual(AgentToolRequestStatus.PendingGate, request.ToolRequest.Status);
        Assert.IsFalse(request.ToolRequest.ClaimsApproval);
        Assert.IsFalse(request.ToolRequest.ClaimsExecutionPermission);
        Assert.IsFalse(request.ToolRequest.ContainsExecutionResult);
        Assert.IsFalse(request.ToolRequest.IsExecutableWithoutGate);
        Assert.AreEqual(AgentToolExecutionGateDecisionType.Allowed, request.GateDecision.Decision);
        Assert.IsTrue(request.GateDecision.GrantsExecution);
        Assert.IsTrue(request.GateDecision.RequiresExecutor);
        Assert.IsFalse(request.GateDecision.ExecutesTool);
        Assert.IsFalse(request.GateDecision.MutatesSource);
        Assert.IsFalse(request.GateDecision.PersistsResult);
    }

    [TestMethod]
    public void SharedPatchProposalFixture_KeepsProposalSeparateFromApply()
    {
        var request = BackendManualToolExecutionFixtures.PatchProposalRequestThatDoesNotApplySource();
        var output = BackendManualToolExecutionFixtures.PatchProposalOutputThatDoesNotApplySource();

        Assert.AreEqual(AgentToolKind.PatchProposal, request.ToolRequest.ToolKind);
        Assert.AreEqual(AgentToolRequestStatus.PendingGate, request.ToolRequest.Status);
        Assert.IsFalse(request.ToolRequest.ClaimsApproval);
        Assert.IsFalse(request.ToolRequest.ClaimsExecutionPermission);
        Assert.IsFalse(request.ToolRequest.ContainsExecutionResult);
        Assert.IsFalse(request.GateDecision.ExecutesTool);
        Assert.IsFalse(request.GateDecision.MutatesSource);
        Assert.IsFalse(output.MutatesSource);
        Assert.IsFalse(output.AppliesPatch);
        Assert.IsFalse(output.WritesFiles);
        Assert.IsTrue(output.Proposal.IsProposalOnly);
        Assert.IsTrue(output.Proposal.RequiresHumanReview);
        Assert.IsFalse(output.Proposal.MutatesSource);
        Assert.IsFalse(output.Proposal.AppliesPatch);
    }

    [TestMethod]
    public void SharedFixtures_DoNotIntroduceRuntimeSqlOrPersistenceTokens()
    {
        var source = ReadRepositoryFile("IronDev.IntegrationTests", "Agents", "BackendManualToolExecutionFixtures.cs");
        var forbidden = new[]
        {
            "ProcessStartInfo",
            "System.Diagnostics.Process",
            "File.WriteAllText",
            "File.Delete",
            "File.Copy",
            "Directory.Delete",
            "SqlConnection",
            "DbConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE ",
            "HttpClient",
            "WeaviateClient",
            "IAgentRunAuditEnvelopeStore",
            "SqlToolExecutionAuditStore",
            "SqlMemoryImprovementProposalStore",
            "SqlCollectiveMemoryPromotionService"
        };

        foreach (var token in forbidden)
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Shared fixture contains forbidden boundary token: {token}");
    }

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException("Could not locate repository root.");

        return directory.FullName;
    }
}
