using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BackendEntityTableInventoryTests
{
    [TestMethod]
    public void EntityTableInventory_DocumentsRequiredBoundariesAndOwnership()
    {
        var inventory = ReadRepositoryFile("Docs", "BACKEND_ENTITY_TABLE_INVENTORY.md");

        foreach (var expected in new[]
        {
            "PR 51.5 is entity/table contract cleanup, not domain redesign.",
            "No behavior change intended.",
            "No schema semantics change.",
            "No stored procedure result-shape change.",
            "No SQL/API/CLI/UI/runtime/persistence/capability changes.",
            "SQL remains the source of truth.",
            "Retrieval match is not memory candidate.",
            "Candidate is not memory.",
            "Proposal is not apply.",
            "Audit is not approval.",
            "Gate is not executor.",
            "Critic is not governance.",
            "Memory safe is not promotion.",
            "Human review remains required for source apply and memory promotion.",
            "## `dbo` application tables",
            "## Agent memory, audit, retrieval, and promotion tables",
            "## Workspace apply evidence files",
            "## Obsolete mappings removed",
            "## Uncertain artifacts left in place"
        })
        {
            StringAssert.Contains(inventory, expected);
        }
    }

    [TestMethod]
    public void EntityTableInventory_ListsCoreSqlPersistenceConcepts()
    {
        var inventory = ReadRepositoryFile("Docs", "BACKEND_ENTITY_TABLE_INVENTORY.md");

        foreach (var expected in new[]
        {
            "`dbo.Projects`",
            "`dbo.ProjectTickets`",
            "`dbo.ProjectContextDocuments`",
            "`dbo.ProjectObservableStates`",
            "`dbo.CodeIndexEntries`",
            "`agent.AgentLocalMemoryItem`",
            "`agent.AgentMemoryInfluenceRecord`",
            "`agent.AgentMemoryHandoffSlice`",
            "`agent.AgentMemoryImprovementProposal`",
            "`agent.AgentMemoryIndexQueue`",
            "`agent.AgentMemoryIndexEvent`",
            "`agent.AgentRunAuditEnvelope`",
            "`agent.CollectiveMemoryItem`",
            "`toolaudit.ToolExecutionAuditRecord`",
            "`agent.usp_AgentLocalMemory_Create`",
            "`agent.usp_MemoryImprovementProposal_Create`",
            "`toolaudit.AppendToolExecutionAuditRecord`"
        })
        {
            StringAssert.Contains(inventory, expected);
        }
    }

    [TestMethod]
    public void EntityTableInventory_DocumentsUncertainArtifactsWithoutDeletingThem()
    {
        var inventory = ReadRepositoryFile("Docs", "BACKEND_ENTITY_TABLE_INVENTORY.md");

        foreach (var expected in new[]
        {
            "`dbo.SemanticEmbeddings`",
            "`dbo.SemanticIndexRuns`",
            "`dbo.SemanticArtefacts`",
            "`dbo.SemanticChunks`",
            "Uncertain",
            "No entity, model, mapping, table, stored procedure, DTO, API/CLI/UI contract, or test fixture was removed in PR51.5.",
            "No uncertain artifact was deleted.",
            "Runtime bootstrap DDL services",
            "PR 52 - Runtime Bootstrap DDL Removal / Migration Ownership Cleanup"
        })
        {
            StringAssert.Contains(inventory, expected);
        }
    }

    [TestMethod]
    public void EntityTableInventory_DoesNotClaimSchemaOrRuntimeChanges()
    {
        var inventory = ReadRepositoryFile("Docs", "BACKEND_ENTITY_TABLE_INVENTORY.md");
        var forbidden = new[]
        {
            "NOCHECK CONSTRAINT",
            "DISABLE TRIGGER",
            "MapPost",
            "HttpPost",
            "ControllerBase",
            "IHostedService",
            "BackgroundService",
            "ProcessStartInfo",
            "File.WriteAllText",
            "PromoteCollectiveMemory",
            "CreatePullRequest",
            "SubmitReview"
        };

        Assert.IsFalse(
            inventory.Contains("| Yes | No |", StringComparison.OrdinalIgnoreCase),
            "Entity/table inventory must not claim any artifact was changed in PR51.5.");
        StringAssert.Contains(inventory, "No active table was dropped.");
        StringAssert.Contains(inventory, "No column, FK, index, stored procedure result shape, repository behavior, API/CLI/UI contract, or runtime capability was changed.");

        foreach (var token in forbidden)
        {
            Assert.IsFalse(
                inventory.Contains(token, StringComparison.OrdinalIgnoreCase),
                $"Entity/table inventory should not introduce schema/runtime/capability token: {token}");
        }
    }

    [TestMethod]
    public void EntityTableInventory_DoesNotContainHiddenOrBidirectionalUnicode()
    {
        const string inventoryPath = "Docs/BACKEND_ENTITY_TABLE_INVENTORY.md";
        var absolutePath = Path.Combine(RepositoryRoot(), inventoryPath);

        AssertAsciiBytesAndNoBom(inventoryPath, File.ReadAllBytes(absolutePath));
        AssertAsciiAndNoFormatControls(inventoryPath, File.ReadAllText(absolutePath));
    }

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static void AssertAsciiAndNoFormatControls(string path, string source)
    {
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(current);
            if (current > 127 || category == System.Globalization.UnicodeCategory.Format)
                Assert.Fail($"{path} contains hidden or non-ASCII Unicode at index {index}: U+{(int)current:X4}.");
        }
    }

    private static void AssertAsciiBytesAndNoBom(string path, byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            Assert.Fail($"{path} must not contain a UTF-8 byte-order mark.");

        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] > 127)
                Assert.Fail($"{path} contains non-ASCII byte at offset {index}: 0x{bytes[index]:X2}.");
        }
    }

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
