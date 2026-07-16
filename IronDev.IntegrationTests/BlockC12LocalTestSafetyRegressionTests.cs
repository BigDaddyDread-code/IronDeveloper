using System.Text.Json;
using System.Text.RegularExpressions;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC12LocalTestSafetyRegressionTests
{
    [TestMethod]
    public void BlockC12_Program_ValidatesLocalTestSafetyBeforeBuild()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var validateIndex = program.IndexOf("ValidateEnvironmentSafety(environmentInfo);", StringComparison.Ordinal);
        var buildIndex = program.IndexOf("var app = builder.Build();", StringComparison.Ordinal);

        Assert.IsTrue(validateIndex >= 0, "Program.cs must call ValidateEnvironmentSafety.");
        Assert.IsTrue(buildIndex >= 0, "Program.cs must still build the API host explicitly.");
        Assert.IsTrue(validateIndex < buildIndex, "LocalTest environment safety must be validated before builder.Build().");
    }

    [TestMethod]
    public void BlockC12_Program_KeepsExplicitLocalTestSafetyHelpers()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");

        StringAssert.Contains(program, "static void ValidateEnvironmentSafety(EnvironmentInfoDto environmentInfo)");
        StringAssert.Contains(program, "static bool IsSafeLocalTestDatabaseName(string database)");
        StringAssert.Contains(program, "static bool IsSafeLocalTestPath(string path, string expectedLabel)");
        StringAssert.Contains(program, "static string[] SplitLocalTestSafetySegments(string value)");
        AssertDoesNotContain(program, "Contains(\"Test\", StringComparison.OrdinalIgnoreCase)", "Program.cs");
    }

    [TestMethod]
    public void BlockC12_LocalTestSafety_ChecksDatabaseWorkspaceLogsAndDangerFlag()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var safetyBlock = ExtractBetween(program, "static void ValidateEnvironmentSafety", "static string[] ResolveAllowedCorsOrigins");

        StringAssert.Contains(safetyBlock, "environmentInfo.Environment");
        StringAssert.Contains(safetyBlock, "IsSafeLocalTestDatabaseName(environmentInfo.Database)");
        StringAssert.Contains(safetyBlock, "IsSafeLocalTestPath(environmentInfo.WorkspaceRoot, \"workspace\")");
        StringAssert.Contains(safetyBlock, "IsSafeLocalTestPath(environmentInfo.LogsRoot, \"logs\")");
        StringAssert.Contains(safetyBlock, "environmentInfo.DangerRealRepoWritesEnabled");
        StringAssert.Contains(safetyBlock, "dangerous real repo writes");
    }

    [TestMethod]
    public void BlockC12_LocalTestSafety_RejectsAmbiguousAndProductionLikeMarkers()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");

        StringAssert.Contains(program, "HasExplicitTestSegment");
        StringAssert.Contains(program, "HasProductionLikeSegment");
        StringAssert.Contains(program, "segment.Equals(\"Test\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(program, "segment.Contains(\"Prod\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(program, "segment.Contains(\"Live\", StringComparison.OrdinalIgnoreCase)");
        StringAssert.Contains(program, "segment.Contains(\"Accept\", StringComparison.OrdinalIgnoreCase)");
    }

    [TestMethod]
    public void BlockC12_EnvironmentEndpoint_RemainsProtectedAndHealthRemainsAnonymous()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var healthEndpoint = ExtractBetween(program, "app.MapGet(\"/health\"", "app.MapGet(\"/api/environment\"");
        var environmentEndpoint = ExtractBetween(program, "app.MapGet(\"/api/environment\"", "app.MapControllers();");

        StringAssert.Contains(healthEndpoint, ".AllowAnonymous();");
        StringAssert.Contains(environmentEndpoint, ".RequireAuthorization();");
        AssertDoesNotContain(environmentEndpoint, ".AllowAnonymous()", "environment endpoint");
    }

    [TestMethod]
    public void BlockC12_C06AndC07JwtHardening_RemainsIntact()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var c06 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC06JwtSecretConfigurationTests.cs");
        var c07 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC07JwtStartupValidationTests.cs");

        StringAssert.Contains(program, "JwtStartupConfigurationValidator.Validate(builder.Configuration)");
        StringAssert.Contains(program, "JwtSigningKeyResolver.Resolve(builder.Configuration)");
        StringAssert.Contains(c06, "BlockC06_CommittedApiAppsettings_DoesNotContainJwtSigningKey");
        StringAssert.Contains(c07, "BlockC07_Program_UsesStartupValidationPathBeforeBuild");
    }

    [TestMethod]
    public void BlockC12_C08C09C10C11SecurityProofs_RemainPresent()
    {
        var c08 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC08EnvironmentEndpointBoundaryTests.cs");
        var c09 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC09ExplicitCorsPolicyTests.cs");
        var c10 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC10WeaviateProductionAuthConfigTests.cs");
        var c11 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC11SecretScanningRegressionTests.cs");

        StringAssert.Contains(c08, "BlockC08_EnvironmentEndpoint_RequiresAuthorization");
        StringAssert.Contains(c09, "BlockC09_Program_AddsOneNamedCorsPolicy");
        StringAssert.Contains(c10, "BlockC10_ProductionEnabledWeaviate_MissingApiKeyFailsClosed");
        StringAssert.Contains(c11, "BlockC11_RepositoryTextFiles_DoNotContainHighConfidenceProviderTokens");
    }

    [TestMethod]
    public void BlockC12_GovernanceBoundaryCiRunsC11AndC12SecurityProofs()
    {
        var script = ReadRepositoryFile("Scripts", "ci", "run-governance-boundary-ci.ps1");

        StringAssert.Contains(script, "$securityBoundaryFilter");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC11SecretScanningRegressionTests");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC12LocalTestSafetyRegressionTests");
        StringAssert.Contains(script, "-Name \"Security boundary tests\"");
        AssertDoesNotContain(script, "upload-artifact", "governance-boundary CI script");
    }

    [TestMethod]
    public void BlockC12_LocalTestSafety_DoesNotAddMutationOrAuthoritySurface()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var safetyBlock = ExtractBetween(program, "static void ValidateEnvironmentSafety", "static string[] ResolveAllowedCorsOrigins");

        foreach (var forbidden in new[]
        {
            "SourceApply",
            "Commit",
            "Push",
            "PullRequest",
            "Merge",
            "Release",
            "Deploy",
            "Memory",
            "Governed",
            "PolicySatisfaction",
            "WorkflowContinuation"
        })
        {
            AssertDoesNotContain(safetyBlock, forbidden, "LocalTest safety block");
        }
    }

    [TestMethod]
    public void BlockC12_LocalTestSeedContract_IsCompleteResettableAndProductionDisabled()
    {
        using var document = JsonDocument.Parse(ReadRepositoryFile("tools", "localtest", "localtest-seed-contract.json"));
        var root = document.RootElement;

        Assert.AreEqual(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual("LocalTest", root.GetProperty("environment").GetString());
        Assert.IsFalse(root.GetProperty("productionEnabled").GetBoolean());
        Assert.IsTrue(root.GetProperty("resetAllowed").GetBoolean());
        Assert.AreEqual("IronDeveloper_Test", root.GetProperty("database").GetProperty("name").GetString());
        Assert.AreEqual(@"C:\IronDevTestWorkspaces", root.GetProperty("paths").GetProperty("workspaceRoot").GetString());
        Assert.AreEqual(@"C:\IronDevTestLogs", root.GetProperty("paths").GetProperty("logsRoot").GetString());
        Assert.AreEqual("LocalTest only", root.GetProperty("credentials").GetProperty("scope").GetString());
        Assert.IsTrue(root.GetProperty("users").GetArrayLength() >= 1);
        Assert.IsTrue(root.GetProperty("projects").GetArrayLength() >= 2);
        Assert.IsTrue(root.GetProperty("seededTickets").GetArrayLength() >= 1);
        Assert.IsTrue(root.GetProperty("seededRuns").GetArrayLength() >= 1);
        Assert.IsTrue(root.GetProperty("knownArtifacts").GetArrayLength() >= 1);

        var projectIds = root.GetProperty("projects").EnumerateArray()
            .Select(project => project.GetProperty("id").GetInt32())
            .ToHashSet();
        foreach (var ticket in root.GetProperty("seededTickets").EnumerateArray())
            Assert.IsTrue(projectIds.Contains(ticket.GetProperty("projectId").GetInt32()), "Every seeded ticket must reference a contracted project.");
        foreach (var run in root.GetProperty("seededRuns").EnumerateArray())
            Assert.IsTrue(projectIds.Contains(run.GetProperty("projectId").GetInt32()), "Every seeded run must reference a contracted project.");
    }

    [TestMethod]
    public void BlockC12_LocalTestScripts_ConsumeOneSeedContractAndValidateSeededSqlTruth()
    {
        var reset = ReadRepositoryFile("tools", "localtest", "reset-localtest-data.ps1");
        var launcher = ReadRepositoryFile("tools", "localtest", "start-alpha-localtest.ps1");
        var smoke = ReadRepositoryFile("tools", "localtest", "Invoke-LocalTestSmoke.ps1");
        var helper = ReadRepositoryFile("tools", "localtest", "localtest-seed-contract.ps1");

        foreach (var script in new[] { reset, launcher, smoke })
        {
            StringAssert.Contains(script, "Get-LocalTestSeedContract");
            AssertDoesNotContain(script, "bob@irondev.local", "LocalTest contract consumer");
            AssertDoesNotContain(script, "change-me-local-only", "LocalTest contract consumer");
        }

        StringAssert.Contains(reset, "Assert-LocalTestSeedTarget");
        StringAssert.Contains(reset, "New-LocalTestSeedValidationSql");
        StringAssert.Contains(smoke, "ToolCallCompleted");
        StringAssert.Contains(smoke, "StepCompleted");
        AssertDoesNotContain(smoke, "DisposableCommandCompleted", "LocalTest smoke current run-event vocabulary");
        AssertDoesNotContain(smoke, "DisposableWorkspaceCreated", "LocalTest smoke current run-event vocabulary");
        StringAssert.Contains(helper, "productionEnabled");
        StringAssert.Contains(helper, "requiredNamePattern");
        StringAssert.Contains(helper, "PASS LocalTest seed contract.");
    }

    [TestMethod]
    public void Dux1_LocalTestLauncher_KeepsStableDatabaseAliasAndChecksFrontDoorBeforeUi()
    {
        var launcher = ReadRepositoryFile("tools", "localtest", "start-alpha-localtest.ps1");
        var smoke = ReadRepositoryFile("tools", "localtest", "Invoke-LocalTestSmoke.ps1");
        var preflightIndex = launcher.IndexOf("$preflight = Get-LocalTestPreflight", StringComparison.Ordinal);
        var loginIndex = launcher.IndexOf("$login = Test-LocalTestAuthenticationContract", StringComparison.Ordinal);
        var browserStartIndex = launcher.IndexOf("$uiProcess = Start-BrowserShell", StringComparison.Ordinal);

        Assert.IsTrue(preflightIndex >= 0, "The launcher must call the LocalTest preflight.");
        Assert.IsTrue(loginIndex > preflightIndex, "The real seeded login must run after preflight.");
        Assert.IsTrue(browserStartIndex > loginIndex, "The UI must start only after preflight and seeded login pass.");
        AssertDoesNotContain(launcher, "Resolve-LocalDbDataSource", "LocalTest launcher");
        AssertDoesNotContain(smoke, "Resolve-LocalDbDataSource", "LocalTest smoke");
        AssertDoesNotContain(launcher, "return \"np:$pipe\"", "LocalTest launcher");
        AssertDoesNotContain(smoke, "return \"np:$pipe\"", "LocalTest smoke");
    }

    [TestMethod]
    public void Dux1_LocalTestLauncher_WritesSessionIdentityManifestAndUniqueLogs()
    {
        var launcher = ReadRepositoryFile("tools", "localtest", "start-alpha-localtest.ps1");

        StringAssert.Contains(launcher, "$sessionId = [Guid]::NewGuid()");
        StringAssert.Contains(launcher, "irondev-localtest-sessions");
        StringAssert.Contains(launcher, "session-manifest.json");
        foreach (var requiredField in new[]
        {
            "repositoryCommit",
            "apiPid",
            "uiPid",
            "apiBaseUrl",
            "uiUrl",
            "databaseName",
            "environment",
            "sessionMode",
            "sandboxApplyRequested",
            "sandboxApplyEnabled",
            "sandboxApplyRoot",
            "capabilities",
            "seedContractVersion",
            "seededLoginCheckResult",
            "startupTimestampUtc"
        })
        {
            StringAssert.Contains(launcher, requiredField);
        }

        StringAssert.Contains(launcher, "IRONDEV_LOCALTEST_SESSION_ID");
        StringAssert.Contains(launcher, "VITE_IRONDEV_LOCALTEST_SESSION_ID");
        StringAssert.Contains(launcher, "IRONDEV_LOCALTEST_API_LOG_PATH");
    }

    [TestMethod]
    public void Dux1_ProjectWorkLauncher_EnablesControlledApplyOnlyForSafeExplicitSession()
    {
        var wrapper = ReadRepositoryFile("tools", "localtest", "start-pr-manual-test.ps1");
        var launcher = ReadRepositoryFile("tools", "localtest", "start-alpha-localtest.ps1");
        const string restart = @".\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset -EnableSandboxApply";

        StringAssert.Contains(wrapper, "[switch]$EnableSandboxApply");
        StringAssert.Contains(wrapper, "$arguments += \"-EnableSandboxApply\"");
        StringAssert.Contains(launcher, "[switch]$EnableSandboxApply");
        StringAssert.Contains(launcher, restart);
        StringAssert.Contains(launcher, "Assert-SafeSandboxApplyRoot");
        StringAssert.Contains(launcher, "SkeletonApply__Enabled");
        StringAssert.Contains(launcher, "SkeletonApply__LauncherSessionId");
        StringAssert.Contains(launcher, "IRONDEV_LOCALTEST_QUALIFICATION_KEY = New-LocalTestJwtKey");
        StringAssert.Contains(ReadRepositoryFile("IronDev.Infrastructure", "Services", "ProjectApplyQualificationStore.cs"),
            ".irondev-disposable-sandbox");
        StringAssert.Contains(launcher, "ProjectFeatureWork");
        StringAssert.Contains(launcher, "ControlledSandboxApply");
        AssertDoesNotContain(launcher, "git commit", "LocalTest project-work launcher");
        AssertDoesNotContain(launcher, "git push", "LocalTest project-work launcher");
    }

    [TestMethod]
    public void Dux1_ProjectConnect_UsesTheCapabilityOwnerForDisposableQualification()
    {
        var controller = ReadRepositoryFile("IronDev.Api", "Controllers", "ProjectsController.cs");

        Assert.AreEqual(
            1,
            Regex.Matches(controller, @"_applyCapability\s*\.QualifyDisposableProjectAsync\(").Count,
            "The controller must have one qualification authority call, behind its post-mutation retry-safe wrapper.");
        Assert.AreEqual(
            3,
            Regex.Matches(controller, @"await TryQualifyDisposableProjectAsync\(").Count,
            "Initial connection, a new-session selection, and a later path change must use the same retry-safe qualification wrapper.");
        StringAssert.Contains(controller, "await TryQualifyDisposableProjectAsync(id, user.UserId, ct);");
        StringAssert.Contains(controller, "await TryQualifyDisposableProjectAsync(projectId, user.UserId, ct);");
        StringAssert.Contains(controller, "instead of creating a duplicate project");
    }

    [TestMethod]
    public void Dux1_LocalTestLauncher_FailureStopsBothSurfacesAndPrintsOneSafeReset()
    {
        var launcher = ReadRepositoryFile("tools", "localtest", "start-alpha-localtest.ps1");
        var failureIndex = launcher.IndexOf("$failure = $_.Exception.Message", StringComparison.Ordinal);
        const string resetCommand = @".\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset";

        Assert.IsTrue(failureIndex >= 0, "The launcher must have a unified failure handler.");
        var failureBlock = launcher[failureIndex..];

        StringAssert.Contains(failureBlock, "Stop-Listener -Port $UiPort");
        StringAssert.Contains(failureBlock, "Stop-Listener -Port $apiPort");
        StringAssert.Contains(failureBlock, "-Status \"Failed\"");
        StringAssert.Contains(failureBlock, "Safe reset: $resetCommand");
        StringAssert.Contains(launcher, resetCommand);
        AssertDoesNotContain(launcher, "silently resets", "LocalTest launcher");
    }

    [TestMethod]
    public void BlockC12_LocalTestSeedSql_ContainsEveryContractedIdentity()
    {
        using var document = JsonDocument.Parse(ReadRepositoryFile("tools", "localtest", "localtest-seed-contract.json"));
        var root = document.RootElement;
        var seedSql = ReadRepositoryFile("tools", "localtest", "localtest-seed.sql");

        StringAssert.Contains(seedSql, root.GetProperty("credentials").GetProperty("email").GetString()!);
        StringAssert.Contains(seedSql, root.GetProperty("credentials").GetProperty("password").GetString()!);
        StringAssert.Contains(seedSql, root.GetProperty("tenant").GetProperty("name").GetString()!);

        foreach (var project in root.GetProperty("projects").EnumerateArray())
        {
            StringAssert.Contains(seedSql, project.GetProperty("name").GetString()!);
            AssertIdentityAppears(seedSql, project.GetProperty("id").GetInt32(), "project");
        }
        foreach (var ticket in root.GetProperty("seededTickets").EnumerateArray())
        {
            StringAssert.Contains(seedSql, ticket.GetProperty("title").GetString()!);
            AssertIdentityAppears(seedSql, ticket.GetProperty("id").GetInt32(), "ticket");
        }
        foreach (var run in root.GetProperty("seededRuns").EnumerateArray())
            StringAssert.Contains(seedSql, run.GetProperty("runId").GetString()!);
        foreach (var artifact in root.GetProperty("knownArtifacts").EnumerateArray())
            AssertIdentityAppears(seedSql, artifact.GetProperty("id").GetInt32(), artifact.GetProperty("kind").GetString()!);
    }

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static string ExtractBetween(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.IsTrue(startIndex >= 0, $"Missing start marker: {start}");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.IsTrue(endIndex > startIndex, $"Missing end marker after {start}: {end}");
        return source[startIndex..endIndex];
    }

    private static void AssertDoesNotContain(string source, string unexpected, string sourceName)
    {
        Assert.IsFalse(
            source.Contains(unexpected, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain '{unexpected}'.");
    }

    private static void AssertIdentityAppears(string source, int id, string kind) =>
        Assert.IsTrue(Regex.IsMatch(source, $@"\b{Regex.Escape(id.ToString())}\b"), $"LocalTest seed SQL is missing contracted {kind} ID {id}.");

    private static string RepositoryRoot()
    {
        foreach (var start in new[]
                 {
                     Environment.GetEnvironmentVariable("IRONDEV_REPOSITORY_ROOT"),
                     Environment.CurrentDirectory,
                     AppContext.BaseDirectory
                 }.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var directory = new DirectoryInfo(start!);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
