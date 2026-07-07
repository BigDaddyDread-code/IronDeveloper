using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("ConfigBoundary")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
[TestCategory("StaticBoundary")]
public sealed class BlockJ09TestEnvironmentIsolationTests
{
    private const string ReceiptPath = "Docs/receipts/J09_TEST_ENVIRONMENT_ISOLATION_OUTSIDE_LOCALTEST.md";

    [TestMethod]
    public void J09_TestAppsettings_DoNotDefineLocalTestRootsOrDangerSwitches()
    {
        foreach (var relativePath in TestConfigurationFiles())
        {
            var text = ReadRepositoryFile(relativePath);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            Assert.IsFalse(root.TryGetProperty("LocalTest", out _), $"{relativePath} must not borrow LocalTest root configuration.");
            AssertDoesNotContain(text, "DangerRealRepoWritesEnabled", relativePath);
            AssertDoesNotContain(text, "WorkspaceRoot", relativePath);
            AssertDoesNotContain(text, "LogsRoot", relativePath);
        }
    }

    [TestMethod]
    public void J09_TestAppsettings_UseOnlyGenericTestDatabaseShape()
    {
        foreach (var relativePath in TestConfigurationFiles())
        {
            var text = ReadRepositoryFile(relativePath);

            StringAssert.Contains(text, "IronDeveloper_Test");
            AssertDoesNotContain(text, "IronDeveloper_Local", relativePath);
            AssertDoesNotContain(text, "IronDeveloper_Dev", relativePath);
            AssertDoesNotContain(text, "IronDeveloper_Prod", relativePath);
            AssertDoesNotContain(text, "YOUR_SERVER", relativePath);
        }
    }

    [TestMethod]
    public void J09_Program_RoutesTestShapedEnvironmentsThroughJ09()
    {
        var program = ReadRepositoryFile("IronDev.Api/Program.cs");
        var safetyBlock = ExtractBetween(program, "static void ValidateEnvironmentSafety", "static void ValidateLocalTestEnvironmentSafety");

        StringAssert.Contains(program, "builder.Environment.IsEnvironment(\"LocalTest\")");
        StringAssert.Contains(program, "static bool IsNonLocalTestTestEnvironment(string environmentName)");
        StringAssert.Contains(program, "static void ValidateNonLocalTestTestEnvironmentSafety");
        StringAssert.Contains(program, "string.Equals(environmentInfo.Environment, \"LocalTest\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(safetyBlock, "ValidateLocalTestEnvironmentSafety(environmentInfo)");
        StringAssert.Contains(safetyBlock, "IsNonLocalTestTestEnvironment(environmentInfo.Environment)");
        StringAssert.Contains(safetyBlock, "ValidateNonLocalTestTestEnvironmentSafety(environmentInfo, StartupEnvironmentSafety.Current)");
        StringAssert.Contains(program, "!IsNonLocalTestTestEnvironment(environmentName)");
        AssertDoesNotContain(program, "string.Equals(environmentInfo.Environment, \"Test\", StringComparison.OrdinalIgnoreCase)\r\n    {\r\n        ValidateLocalTestEnvironmentSafety", "Program.cs");
    }

    [TestMethod]
    public void J09_Program_KeepsEnvironmentOwnershipBoundaries()
    {
        var program = ReadRepositoryFile("IronDev.Api/Program.cs");
        var testClassifier = ExtractBetween(program, "static bool IsNonLocalTestTestEnvironment", "static (string Database");

        StringAssert.Contains(testClassifier, "\"Test\"");
        StringAssert.Contains(testClassifier, "\"CI\"");
        StringAssert.Contains(testClassifier, "\"IntegrationTest\"");
        StringAssert.Contains(testClassifier, "\"E2E\"");
        StringAssert.Contains(testClassifier, "\"AutomationTest\"");
        StringAssert.Contains(testClassifier, "\"SmokeTest\"");
        AssertDoesNotContain(testClassifier, "\"Development\"", "J09 classifier");
        AssertDoesNotContain(testClassifier, "\"CustomEnvironment\"", "J09 classifier");
        AssertDoesNotContain(testClassifier, "\"Preview\"", "J09 classifier");
        StringAssert.Contains(program, "!string.Equals(environmentName, \"Development\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(program, "!string.Equals(environmentName, \"LocalTest\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(program, "!IsNonLocalTestTestEnvironment(environmentName)");
    }

    [TestMethod]
    public void J09_Program_BlocksDangerousRealRepoWritesAndUnsafeResourceShapes()
    {
        var program = ReadRepositoryFile("IronDev.Api/Program.cs");
        var j09Block = ExtractBetween(program, "static void ValidateNonLocalTestTestEnvironmentSafety", "static void ValidateProductionLikeEnvironmentSafety");

        StringAssert.Contains(j09Block, "IsSafeNonLocalTestDatabaseName");
        StringAssert.Contains(j09Block, "environmentInfo.DangerRealRepoWritesEnabled");
        StringAssert.Contains(j09Block, "IsSafeOptionalOrConfiguredTestRoot");
        StringAssert.Contains(program, "HasExplicitTestEnvironmentSegment");
        StringAssert.Contains(program, "HasUnsafeNonLocalTestResourceSegment");
        StringAssert.Contains(program, "\"Contest\"");
        StringAssert.Contains(program, "\"Latest\"");
        StringAssert.Contains(program, "\"Testament\"");
        StringAssert.Contains(program, "\"ProductionTestBackup\"");
        StringAssert.Contains(program, "\"ProdTest\"");
    }

    [TestMethod]
    public void J09_StartupExceptions_DoNotEchoRawConnectionStringsOrPaths()
    {
        var program = ReadRepositoryFile("IronDev.Api/Program.cs");
        var j09Block = ExtractBetween(program, "static void ValidateNonLocalTestTestEnvironmentSafety", "static void ValidateProductionLikeEnvironmentSafety");

        AssertDoesNotContain(j09Block, "throw new InvalidOperationException(safetyContext.ConnectionString", "J09 safety block");
        AssertDoesNotContain(j09Block, "throw new InvalidOperationException(connectionString", "J09 safety block");
        AssertDoesNotContain(j09Block, "throw new InvalidOperationException(root", "J09 safety block");
        AssertDoesNotContain(j09Block, "throw new InvalidOperationException(safetyContext.LocalTestWorkspaceRoot", "J09 safety block");
        AssertDoesNotContain(j09Block, "throw new InvalidOperationException(safetyContext.LocalTestLogsRoot", "J09 safety block");
        AssertDoesNotContain(j09Block, "throw new InvalidOperationException(safetyContext.DisposableWorkspaceRoot", "J09 safety block");
        AssertDoesNotContain(j09Block, "throw new InvalidOperationException(safetyContext.DisposableEvidenceRoot", "J09 safety block");
    }

    [TestMethod]
    public void J09_TestEnvironmentIsolation_IsDocumentedInInventoryAndReceipt()
    {
        var inventory = ReadRepositoryFile("Docs/testing/INTEGRATION_TEST_CATEGORIES.md");
        var receipt = ReadRepositoryFile(ReceiptPath);

        StringAssert.Contains(inventory, "## J09 Test Environment Isolation Outside LocalTest");
        StringAssert.Contains(receipt, "Before J09, LocalTest had isolated resource checks and production-like environments had local/test-resource rejection, but Test was outside both.");
        StringAssert.Contains(receipt, "J09 is startup validation and regression proof only.");
        StringAssert.Contains(receipt, "It does not provision test infrastructure, create databases, create directories, write evidence, grant approval, satisfy policy, continue workflows, apply source, release, or deploy.");
    }

    [TestMethod]
    public void J09_NoRuntimeMutationAuthoritySurfaceAdded()
    {
        // This test originally diffed the branch against origin/main and asserted zero
        // changed files under the endpoint/authority/workflow folders. That form was
        // retired (PR #741) on evidence:
        //   1. It was not reliably evaluated. On CI checkouts the diff resolved empty
        //      while controller files were in fact changing — #736 modified
        //      TicketsController and #739 added PlannedSurfacesController, and this
        //      lane stayed green both times, then half-fired on #741. A check whose
        //      verdict depends on git fetch depth is not evidence. Selection is not
        //      execution.
        //   2. As a permanent rule it would forbid every future governed endpoint
        //      slice, which was never the J09/J10 claim. The point-in-time claim —
        //      the J09/J10 slice itself added no mutation surface — is recorded in
        //      the receipt and does not need re-asserting against unrelated branches
        //      forever.
        // The durable invariant stays pinned below: the J09 startup-safety block
        // validates and refuses; it never mutates.
        var program = ReadRepositoryFile("IronDev.Api/Program.cs");
        var j09Block = ExtractBetween(program, "static void ValidateNonLocalTestTestEnvironmentSafety", "static void ValidateProductionLikeEnvironmentSafety");
        foreach (var forbidden in new[]
        {
            "CreateDirectory",
            "CreateDatabase",
            "Migrate",
            "WriteAllText",
            "File.Write",
            "Process.Start",
            "Apply",
            "Rollback",
            "WorkflowContinuation"
        })
        {
            AssertDoesNotContain(j09Block, forbidden, "J09 safety block");
        }
    }

    [TestMethod]
    public void J09_GovernanceBoundaryCiRunsStaticAndApiStartupProofs()
    {
        var script = ReadRepositoryFile("Scripts/ci/run-governance-boundary-ci.ps1");

        StringAssert.Contains(script, "FullyQualifiedName~BlockJ09TestEnvironmentIsolationTests");
        StringAssert.Contains(script, "FullyQualifiedName~TestEnvironmentIsolationSafetyTests");
    }

    private static IReadOnlyList<string> TestConfigurationFiles() =>
    [
        "IronDev.IntegrationTests/appsettings.Test.json",
        "IronDev.IntegrationTests.Api/appsettings.Test.json"
    ];

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ExtractBetween(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        if (startIndex < 0)
            return string.Empty;

        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        if (endIndex < 0)
            return source[startIndex..];

        return source[startIndex..endIndex];
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static void AssertDoesNotContain(string text, string marker, string sourceName)
    {
        Assert.IsFalse(
            text.Contains(marker, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain '{marker}'.");
    }
}
