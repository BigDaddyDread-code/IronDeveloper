using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.ApiCliContract;

[TestClass]
[TestCategory("ApiCliReleaseGate")]
public sealed class ApiCliReleaseGateReportTests
{
    private static readonly string ReportPath = Path.Combine(LocateRepoRoot(), "Docs", "API_CLI_RELEASE_GATE_REPORT.md");

    [TestMethod]
    public void ApiCliReleaseGateReport_Exists()
    {
        Assert.IsTrue(File.Exists(ReportPath), ReportPath);
        var report = ReadReport();

        StringAssert.Contains(report, "# API/CLI Release Gate Report");
        StringAssert.Contains(report, "This report is a receipt, not a trophy.");
    }

    [TestMethod]
    public void ApiCliReleaseGateReport_StatesNotReleaseApproval()
    {
        var report = ReadReport();

        StringAssert.Contains(report, "Block F API/CLI exposure is ready as a controlled internal operating surface.");
        StringAssert.Contains(report, "It is not a product release gate for IronDev as a whole.");
        StringAssert.Contains(report, "This is release-readiness evidence, not release approval.");
        StringAssert.Contains(report, "Block F API/CLI exposure: ready for internal completion.");
        StringAssert.Contains(report, "IronDev full release: not ready.");
    }

    [TestMethod]
    public void ApiCliReleaseGateReport_ListsCompletedApiSurfaces()
    {
        var report = ReadReport();
        var expected = new[]
        {
            "PR 58 exposed the read-only agent run API v1.",
            "GET /api/v1/agent-runs",
            "GET /api/v1/agent-runs/{agentRunId}",
            "GET /api/v1/agent-runs/{agentRunId}/audit",
            "PR 59 exposed the manual critic API v1.",
            "POST /api/v1/manual-critic/reviews",
            "GET /api/v1/manual-critic/reviews/{agentRunId}",
            "PR 60 exposed the manual memory improvement API v1.",
            "POST /api/v1/manual-memory-improvements",
            "GET /api/v1/manual-memory-improvements/{agentRunId}",
            "PR 61 exposed the tool request API v1.",
            "POST /api/v1/tool-requests",
            "GET /api/v1/tool-requests/{toolRequestId}",
            "PR 62 exposed the tool gate API v1.",
            "POST /api/v1/tool-gates/evaluations",
            "API only / no CLI yet",
            "PR 63 exposed the dogfood loop API v1.",
            "POST /api/v1/dogfood-loops",
            "GET /api/v1/dogfood-loops/{dogfoodLoopId}"
        };

        AssertContainsAll(report, expected);
    }

    [TestMethod]
    public void ApiCliReleaseGateReport_ListsCompletedCliSurfaces()
    {
        var report = ReadReport();
        var expected = new[]
        {
            "PR 64 added the CLI foundation.",
            "api ping",
            "PR 65 added CLI agent run inspection commands.",
            "agent-runs list",
            "agent-runs get",
            "agent-runs audit",
            "PR 66 added CLI manual critic commands.",
            "critic review create",
            "critic review get",
            "PR 67 added CLI memory improvement commands.",
            "memory-improvements create",
            "memory-improvements get",
            "PR 68 added CLI tool request commands.",
            "tool-requests create",
            "tool-requests get",
            "PR 69 added CLI dogfood loop commands.",
            "dogfood-loops create",
            "dogfood-loops get",
            "No CLI tool gate command exists in Block F."
        };

        AssertContainsAll(report, expected);
    }

    [TestMethod]
    public void ApiCliReleaseGateReport_ListsBoundaryInvariants()
    {
        var report = ReadReport();
        var expected = new[]
        {
            "Audit is not approval.",
            "Evidence is not permission.",
            "Critic is not governance.",
            "Critic review is not approval.",
            "Memory proposal is not promotion.",
            "Memory safe is not approval.",
            "Candidate is not memory.",
            "Retrieval match is not memory candidate.",
            "Tool request is request form, not execution permission.",
            "Request approval is separate.",
            "Tool execution is separate.",
            "Gate is not executor.",
            "Gate evaluation is not execution.",
            "Gate pass is not human approval.",
            "Dogfood receipt is evidence, not release approval.",
            "Dogfood loop is not autonomous workflow.",
            "API access is not execution permission.",
            "API response status is not governance.",
            "CLI command is not approval.",
            "CLI output is not governance.",
            "Model output is advisory only.",
            "Human review remains required for source apply.",
            "Human review remains required for memory promotion."
        };

        AssertContainsAll(report, expected);
    }

    [TestMethod]
    public void ApiCliReleaseGateReport_ListsNonDurableBoundaries()
    {
        var report = ReadReport();
        var expected = new[]
        {
            "PR 61 Tool Request API is backed by durable SQL tool request records once the durable Tool Request Store has landed.",
            "PR 62/75 Tool Gate API records durable SQL-backed gate decision evidence once the durable Gate Decision Store has landed.",
            "PR 63 Dogfood Loop API is backed by durable SQL dogfood receipt evidence once PR78 lands.",
            "SQL source of truth.",
            "Dogfood receipt records remain evidence only, not approval or release readiness.",
            "Execution evidence.",
            "Approval.",
            "Release evidence."
        };

        AssertContainsAll(report, expected);
    }

    [TestMethod]
    public void ApiCliReleaseGateReport_ListsKnownRedLanes()
    {
        var report = ReadReport();
        var expected = new[]
        {
            "Full API remains red on existing chat wording assertion unless fixed.",
            "Full solution remains red in documented broad governance/static-boundary/memory/context lanes unless fixed.",
            "Block F can be considered internally operable only within its focused validation bands.",
            "It cannot be used to claim full solution release readiness while the broad API/full-solution lanes remain red."
        };

        AssertContainsAll(report, expected);
    }

    [TestMethod]
    public void ApiCliReleaseGateReport_DoesNotClaimFullReleaseReadiness()
    {
        var report = ReadReport();

        StringAssert.Contains(report, "Block F API/CLI exposure: ready for internal completion.");
        StringAssert.Contains(report, "IronDev full release: not ready.");
        Assert.IsFalse(report.Contains("IronDev full release: ready", StringComparison.OrdinalIgnoreCase), report);
        Assert.IsFalse(report.Contains("product-wide readiness: ready", StringComparison.OrdinalIgnoreCase), report);
        Assert.IsFalse(report.Contains("product-wide readiness is granted", StringComparison.OrdinalIgnoreCase), report);
    }

    [TestMethod]
    public void ApiCliReleaseGateReport_DoesNotUseForbiddenReleaseLanguage()
    {
        var report = ReadReport().ToLowerInvariant();
        var forbidden = new[]
        {
            "production ready",
            "approved for release",
            "ship it",
            "fully released",
            "full release approved",
            "all tests green",
            "no known failures",
            "release complete"
        };

        foreach (var phrase in forbidden)
        {
            Assert.IsFalse(report.Contains(phrase, StringComparison.Ordinal), phrase);
        }
    }

    [TestMethod]
    public void ApiCliReleaseGateReport_ReferencesApiCliContractMatrix()
    {
        var report = ReadReport();
        var expected = new[]
        {
            "PR 70 added the API/CLI contract suite and matrix.",
            "Docs/API_CLI_CONTRACT_TEST_SUITE.md",
            "Docs/API_CLI_CONTRACT_MATRIX.md",
            "PR 70 matrix is the canonical Block F command-to-endpoint receipt",
            "POST /api/v1/tool-gates/evaluations"
        };

        AssertContainsAll(report, expected);
    }

    private static string ReadReport()
    {
        return File.ReadAllText(ReportPath);
    }

    private static void AssertContainsAll(string text, IEnumerable<string> expected)
    {
        foreach (var value in expected)
        {
            StringAssert.Contains(text, value, value);
        }
    }

    private static string LocateRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}
