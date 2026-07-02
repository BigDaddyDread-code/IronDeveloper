using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.ApiCliContract;

[TestCategory("StaticBoundary")]
[TestClass]
[TestCategory("ApiCliContract")]
public sealed class ApiCliStaticBoundaryTests
{
    private static readonly string RepoRoot = LocateRepoRoot();

    [TestMethod]
    public void ApiCliContract_DocsExistAndRecordNonCapabilityBoundary()
    {
        var suite = Read("Docs/API_CLI_CONTRACT_TEST_SUITE.md");
        var matrix = Read("Docs/API_CLI_CONTRACT_MATRIX.md");

        StringAssert.Contains(suite, "contract testing only");
        StringAssert.Contains(suite, "does not add endpoints");
        StringAssert.Contains(suite, "does not add");
        StringAssert.Contains(matrix, "Tool request is request form, not execution permission.");
        StringAssert.Contains(matrix, "Dogfood receipt is evidence, not release approval.");
        StringAssert.Contains(matrix, "Gate evaluation is not execution.");
        StringAssert.Contains(matrix, "API only / no CLI yet");
        StringAssert.Contains(matrix, "POST /api/v1/tool-gates/evaluations");
        StringAssert.Contains(matrix, "Retrieval match is not memory candidate.");
    }

    [TestMethod]
    public void ApiCliContract_BlockFClientFiles_DoNotReferenceExecutionOrAuthorityServices()
    {
        var files = new[]
        {
            "tools/IronDev.Cli/CliFoundation.cs",
            "tools/IronDev.Cli/CliAgentRuns.cs",
            "tools/IronDev.Cli/CliManualCritic.cs",
            "tools/IronDev.Cli/CliMemoryImprovements.cs",
            "tools/IronDev.Cli/CliToolRequests.cs",
            "tools/IronDev.Cli/CliDogfoodLoops.cs",
            "tools/IronDev.Cli/CliApplyPreview.cs"
        };

        var forbidden = new[]
        {
            "IWorkspaceApplyCopyService",
            "IDisposableWorkspaceApplyCopyService",
            "IWorkspacePromotionApprovalService",
            "IAgentToolExecutor",
            "IToolExecutionGateExecutor",
            "IToolExecutionAuditStore",
            "ICollectiveMemoryPromotionService",
            "IMemoryPromotionService",
            "Process.Start",
            "File.Copy(",
            "File.Delete(",
            "Directory.Delete(",
            "SqlConnection",
            "ExecuteNonQuery",
            "CREATE TABLE",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM"
        };

        foreach (var file in files)
        {
            var text = Read(file);
            foreach (var token in forbidden)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{file} must not reference {token}.");
            }
        }
    }

    [TestMethod]
    public void ApiCliContract_ClientProject_RemainsHttpTransportOnly()
    {
        var clientDirectory = Path.Combine(RepoRoot, "IronDev.Client");
        if (!Directory.Exists(clientDirectory))
        {
            Assert.Inconclusive("IronDev.Client project is not present in this checkout.");
            return;
        }

        var forbidden = new[]
        {
            "SqlConnection",
            "DbContext",
            "IWorkspaceApplyCopyService",
            "IAgentToolExecutor",
            "IToolExecutionAuditStore",
            "ICollectiveMemoryPromotionService",
            "Process.Start",
            "File.Copy(",
            "File.Delete(",
            "Directory.Delete(",
            "CREATE TABLE",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM"
        };

        foreach (var file in Directory.EnumerateFiles(clientDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{file} must not reference {token}.");
            }
        }
    }

    [TestMethod]
    public void ApiCliContract_TestSuiteFiles_AreAsciiAndContainNoHiddenUnicode()
    {
        var files = new[]
        {
            "Docs/API_CLI_CONTRACT_TEST_SUITE.md",
            "Docs/API_CLI_CONTRACT_MATRIX.md",
            "IronDev.IntegrationTests/ApiCliContract/ApiCliContractTestSupport.cs",
            "IronDev.IntegrationTests/ApiCliContract/ApiCliCommandMappingTests.cs",
            "IronDev.IntegrationTests/ApiCliContract/ApiCliBoundaryContractTests.cs",
            "IronDev.IntegrationTests/ApiCliContract/ApiCliStaticBoundaryTests.cs"
        };

        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, file));
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"{file} must not contain a UTF-8 BOM.");
            Assert.IsFalse(bytes.Any(b => b > 0x7F), $"{file} must be ASCII only.");
        }
    }

    private static string Read(string relativePath)
    {
        return File.ReadAllText(Path.Combine(RepoRoot, relativePath));
    }

    private static string LocateRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
