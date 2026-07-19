using IronDev.Core.Workbench;

namespace IronDev.UnitTests;

[TestClass]
public sealed class WorkbenchBusinessAnalystHostContractsTests
{
    [TestMethod]
    public void ContractKey_FromContextPinsEveryExecutableVersionDimension()
    {
        var context = Context();

        var key = WorkbenchBusinessAnalystContractKey.FromContext(context);

        Assert.AreEqual(WorkbenchBusinessAnalystContract.AgentVersion, key.AgentVersion);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.PromptVersion, key.PromptVersion);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.ToolPolicyVersion, key.ToolPolicyVersion);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.ContextSchemaVersion, key.ContextSchemaVersion);
        Assert.AreEqual(
            WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion,
            key.ContextCanonicalizationVersion);
        Assert.AreEqual(WorkbenchBusinessAnalystContract.OutputSchemaVersion, key.OutputSchemaVersion);
        Assert.AreNotEqual(key, key with { PromptVersion = key.PromptVersion.ToUpperInvariant() });
    }

    [TestMethod]
    public void SnapshotToolNames_AreExactlyTheThreeImmutableProjectReads()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "workbench.project-identity.read",
                "workbench.captured-understanding.read",
                "workbench.bounded-trusted-conversation.read"
            },
            WorkbenchBusinessAnalystSnapshotToolNames.All.ToArray());
        Assert.AreEqual(
            WorkbenchBusinessAnalystSnapshotToolNames.All.Count,
            WorkbenchBusinessAnalystSnapshotToolNames.All.Distinct(StringComparer.Ordinal).Count());
    }

    private static WorkbenchBusinessAnalystContext Context() =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            1,
            7,
            "An idea",
            70,
            1,
            700,
            7000,
            3,
            "{}",
            [new WorkbenchAgentContextMessage(7000, "user", "Shape this", DateTime.UnixEpoch)],
            WorkbenchBusinessAnalystContract.AgentVersion,
            WorkbenchBusinessAnalystContract.PromptVersion,
            WorkbenchBusinessAnalystContract.ToolPolicyVersion,
            WorkbenchBusinessAnalystContract.ContextSchemaVersion,
            WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion,
            WorkbenchBusinessAnalystContract.OutputSchemaVersion,
            new string('a', 64));
}
