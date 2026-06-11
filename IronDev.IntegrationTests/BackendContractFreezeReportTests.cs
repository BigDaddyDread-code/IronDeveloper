using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BackendContractFreezeReportTests
{
    private static readonly string[] RequiredSections =
    [
        "## Freeze Verdict",
        "## Assessment Identity",
        "## Validation Summary",
        "## Contract Inventory Summary",
        "## PR 42-55 Evidence Summary",
        "## Backend Boundary Freeze Matrix",
        "## Known Red Lanes and Freeze Exceptions",
        "## Freeze Decision Rules",
        "## Post-Freeze Rules",
        "## Allowed Next Work After Freeze",
        "## Blocked Work Until Separate Contract Change",
        "## Reviewer Checklist"
    ];

    private static readonly string[] RequiredInvariants =
    [
        "SQL source of truth",
        "Vector/index retrieval only",
        "Retrieval match vs memory candidate",
        "Candidate vs memory",
        "Candidate/proposal/promotion",
        "Proposal/review/apply",
        "Audit vs approval",
        "Gate vs executor",
        "Critic vs governance",
        "Tool request vs execution permission",
        "Model output advisory only",
        "Human review for source apply",
        "Human review for memory promotion"
    ];

    private static readonly string[] RequiredInventories =
    [
        "Docs/BACKEND_NAMING_INVENTORY.md",
        "Docs/BACKEND_TEST_FIXTURE_INVENTORY.md",
        "Docs/BACKEND_SQL_INVENTORY.md",
        "Docs/BACKEND_INLINE_SQL_INVENTORY.md",
        "Docs/BACKEND_ENTITY_TABLE_INVENTORY.md",
        "Docs/BACKEND_CONFIGURATION_DEPENDENCY_INVENTORY.md",
        "Docs/BACKEND_ARCHITECTURE.md",
        "Docs/ADR/README.md",
        "Docs/L4_L5_OPERATIONAL_DEBUGGING.md"
    ];

    private static readonly string[] RequiredRedLanes =
    [
        "ProjectsTicketsMemoryAndChat_ShouldRoundTripThroughApiBoundary",
        "EndpointContractTests.cs:189",
        "governance/agent runner approval assertions",
        "WPF/source boundary scan failures",
        "local-clock usage scan failures",
        "chat context effective-work-text expectations",
        "CollectiveMemoryRetrievalCandidate",
        "L4 release gate failures",
        "static boundary scans",
        "Legacy runtime DDL/bootstrap ownership exceptions",
        "Uncertain package references",
        "Uncertain config keys",
        "Ugly names"
    ];

    private static readonly string[] ForbiddenBoundaryInversions =
    [
        "audit is approval",
        "audit record is approval",
        "critic is governance",
        "gate is executor",
        "proposal is apply",
        "retrieval match is memory candidate",
        "memory safe is approval",
        "model output is permission",
        "model output is authority",
        "vector/index is truth",
        "automatic source apply is allowed",
        "automatic memory promotion is allowed"
    ];

    [TestMethod]
    public void BackendContractFreezeReport_FileExistsAndContainsRequiredSections()
    {
        var report = ReadReport();

        StringAssert.Contains(report, "Freeze approved with exceptions");
        StringAssert.Contains(report, "PR 42 through PR 55");

        foreach (var section in RequiredSections)
        {
            StringAssert.Contains(report, section);
        }
    }

    [TestMethod]
    public void BackendContractFreezeReport_ContainsCoreInvariantsAndBoundaryMatrix()
    {
        var report = ReadReport();

        StringAssert.Contains(report, "| Boundary | Frozen meaning | Evidence | Freeze status | Exceptions |");

        foreach (var invariant in RequiredInvariants)
        {
            StringAssert.Contains(report, invariant);
        }
    }

    [TestMethod]
    public void BackendContractFreezeReport_ReferencesRequiredInventories()
    {
        var report = ReadReport();

        foreach (var inventory in RequiredInventories)
        {
            StringAssert.Contains(report, inventory);
        }
    }

    [TestMethod]
    public void BackendContractFreezeReport_ListsKnownRedLanesAndExceptions()
    {
        var report = ReadReport();

        StringAssert.Contains(report, "Known Red Lanes and Freeze Exceptions");
        StringAssert.Contains(report, "Accepted as freeze exception");

        foreach (var lane in RequiredRedLanes)
        {
            StringAssert.Contains(report, lane);
        }
    }

    [TestMethod]
    public void BackendContractFreezeReport_DefinesDecisionRulesAndPostFreezeRules()
    {
        var report = ReadReport();

        StringAssert.Contains(report, "Freeze can proceed only if:");
        StringAssert.Contains(report, "Freeze is blocked if:");
        StringAssert.Contains(report, "API/CLI work may consume frozen contracts.");
        StringAssert.Contains(report, "API/CLI work must not redefine backend authority.");
        StringAssert.Contains(report, "Human review remains required for source apply and memory promotion.");
    }

    [TestMethod]
    public void BackendContractFreezeReport_DoesNotInvertCoreBoundaries()
    {
        var report = ReadReport().ToLowerInvariant();

        foreach (var forbidden in ForbiddenBoundaryInversions)
        {
            Assert.IsFalse(report.Contains(forbidden, StringComparison.Ordinal), $"Report must not contain boundary inversion: {forbidden}");
        }
    }

    [TestMethod]
    public void BackendContractFreezeReport_IsAsciiNoBomAndNoHiddenUnicode()
    {
        var path = ReportPath();
        var bytes = File.ReadAllBytes(path);

        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "Report must not contain UTF-8 BOM.");

        for (var index = 0; index < bytes.Length; index++)
        {
            Assert.IsTrue(bytes[index] <= 0x7F, $"Report must be ASCII-only. Non-ASCII byte 0x{bytes[index]:X2} at offset {index}.");
        }

        var report = Encoding.ASCII.GetString(bytes);
        foreach (var ch in report)
        {
            var category = char.GetUnicodeCategory(ch);
            Assert.IsFalse(category == System.Globalization.UnicodeCategory.Format, $"Report contains hidden format character U+{(int)ch:X4}.");
            Assert.IsFalse(char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t', $"Report contains unexpected control character U+{(int)ch:X4}.");
        }
    }

    [TestMethod]
    public void BackendContractFreezeReport_StatesReportOnlyAndNoCapabilityChange()
    {
        var report = ReadReport();

        StringAssert.Contains(report, "Report-only; no behavior change intended.");
        StringAssert.Contains(report, "No SQL/schema/proc/runtime/API/CLI/UI/persistence/capability changes.");
        StringAssert.Contains(report, "No new capability introduced.");
    }

    private static string ReadReport()
    {
        return File.ReadAllText(ReportPath());
    }

    private static string ReportPath()
    {
        return Path.Combine(RepositoryRoot(), "Docs", "BACKEND_CONTRACT_FREEZE_REPORT.md");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root containing IronDev.slnx.");
    }
}
