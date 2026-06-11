using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BackendArchitectureDocumentationTests
{
    [TestMethod]
    public void BackendArchitecture_DocumentsCurrentBoundariesAndScope()
    {
        var architecture = ReadRepositoryFile("Docs", "BACKEND_ARCHITECTURE.md");

        foreach (var expected in new[]
        {
            "PR 52 is documentation alignment, not architecture change.",
            "No behavior change intended.",
            "No schema semantics change.",
            "No stored procedure result-shape change.",
            "No SQL/API/CLI/UI/runtime/persistence/capability changes.",
            "No new backend capability is allowed until PR 56",
            "SQL remains the source of truth",
            "Vector, index, and retrieval systems are lookup accelerators only.",
            "Retrieval match is not a memory candidate.",
            "Candidate is not memory.",
            "A memory proposal is not promotion.",
            "Proposal is not apply.",
            "Audit is not approval",
            "Gate is not executor.",
            "Critic is not governance.",
            "Memory safety results",
            "Tool request is a request form, not execution permission.",
            "Model output is advisory only.",
            "Human review remains required for source apply and memory promotion."
        })
        {
            StringAssert.Contains(architecture, expected);
        }
    }

    [TestMethod]
    public void BackendArchitecture_DocumentsBoundaryMap()
    {
        var architecture = ReadRepositoryFile("Docs", "BACKEND_ARCHITECTURE.md");

        StringAssert.Contains(architecture, "## Backend Boundary Map");
        foreach (var expected in new[]
        {
            "| SQL | Stores authoritative state | Does not infer authority |",
            "| Vector/index | Retrieves matches | Is not truth |",
            "| Retrieval match | Lookup result | Is not memory candidate |",
            "| Memory candidate | Possible memory item | Is not persisted memory |",
            "| Memory proposal | Reviewable change | Is not promotion |",
            "| Memory safety result | Advisory classification | Is not approval |",
            "| Promotion | Accepted persistence step | Is not automatic |",
            "| Proposal | Suggested change | Does not apply source |",
            "| Source apply | Mutates source | Requires approval |",
            "| Audit | Records evidence | Does not approve |",
            "| Gate | Blocks/allows path | Does not execute |",
            "| Critic | Reviews/advises | Does not govern |",
            "| Tool request | Request form | Not execution permission |",
            "| Model output | Advisory | Not authority |"
        })
        {
            StringAssert.Contains(architecture, expected);
        }
    }

    [TestMethod]
    public void BackendArchitecture_ReferencesInventoriesAndRecentPrDeltas()
    {
        var architecture = ReadRepositoryFile("Docs", "BACKEND_ARCHITECTURE.md");

        foreach (var expected in new[]
        {
            "BACKEND_SQL_INVENTORY.md",
            "BACKEND_INLINE_SQL_INVENTORY.md",
            "BACKEND_ENTITY_TABLE_INVENTORY.md",
            "BACKEND_TEST_FIXTURE_INVENTORY.md",
            "BACKEND_NAMING_INVENTORY.md",
            "## PR 42-51.5 architecture delta summary",
            "### PR 42 - Tool Execution Audit Store",
            "### PR 43 - Manual Ticket Review to Critic to Fix Proposal Loop",
            "### PR 44 - Test Failure to Critic to Repair Proposal Loop",
            "### PR 45 - Real-run Memory Improvement Detection",
            "### PR 46 - Manual Dogfood Harness",
            "### PR 47 - Backend Dead Code and Redundant Contract Sweep",
            "### PR 48 - Agent/Memory Naming Normalisation",
            "### PR 49 - Test Fixture Consolidation",
            "### PR 50 - SQL Schema and Stored Procedure Cleanup Pass",
            "### PR 51 - Remove Inline SQL and Runtime DDL Leftovers",
            "### PR 51.5 - Entity/Table Contract Inventory and Cleanup"
        })
        {
            StringAssert.Contains(architecture, expected);
        }
    }

    [TestMethod]
    public void BackendArchitecture_DocumentsKnownDebtAndFreezeExceptions()
    {
        var architecture = ReadRepositoryFile("Docs", "BACKEND_ARCHITECTURE.md");

        foreach (var expected in new[]
        {
            "## Known Backend Debt Before Contract Freeze",
            "Stored manual agent DI construction issue in API lane",
            "StoredManualIndependentCriticAgentService",
            "StoredManualMemoryImprovementAgentService",
            "Remaining broad architecture/governance red lanes",
            "Legacy runtime DDL/bootstrap ownership exceptions from PR 51",
            "Intentionally ugly names left from PR 51.5",
            "SQL/entity artifacts marked uncertain in inventories",
            "Full solution broad lanes still failing",
            "freeze exception"
        })
        {
            StringAssert.Contains(architecture, expected);
        }
    }

    [TestMethod]
    public void BackendArchitecture_DoesNotClaimRuntimeOrSchemaChanges()
    {
        var architecture = ReadRepositoryFile("Docs", "BACKEND_ARCHITECTURE.md");
        var forbidden = new[]
        {
            "HttpPost",
            "ControllerBase",
            "WebApplication",
            "AddScoped<",
            "IHostedService",
            "BackgroundService",
            "ProcessStartInfo",
            "File.WriteAllText",
            "NOCHECK CONSTRAINT",
            "DISABLE TRIGGER",
            "CREATE OR ALTER PROCEDURE",
            "ALTER TABLE",
            "DROP TABLE",
            "PromoteCollectiveMemory",
            "SubmitReview"
        };

        foreach (var token in forbidden)
        {
            Assert.IsFalse(
                architecture.Contains(token, StringComparison.OrdinalIgnoreCase),
                $"Backend architecture documentation must not introduce runtime/schema/capability token: {token}");
        }
    }

    [TestMethod]
    public void BackendArchitecture_DoesNotContainHiddenOrBidirectionalUnicode()
    {
        const string architecturePath = "Docs/BACKEND_ARCHITECTURE.md";
        var absolutePath = Path.Combine(RepositoryRoot(), architecturePath);

        AssertAsciiBytesAndNoBom(architecturePath, File.ReadAllBytes(absolutePath));
        AssertAsciiAndNoFormatControls(architecturePath, File.ReadAllText(absolutePath));
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
