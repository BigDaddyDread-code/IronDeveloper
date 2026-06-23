namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC13ProductionEnvironmentSafetyRegressionTests
{
    [TestMethod]
    public void BlockC13_Program_ValidatesEnvironmentSafetyBeforeBuild()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var validateIndex = program.IndexOf("ValidateEnvironmentSafety(environmentInfo);", StringComparison.Ordinal);
        var buildIndex = program.IndexOf("var app = builder.Build();", StringComparison.Ordinal);

        Assert.IsTrue(validateIndex >= 0, "Program.cs must call ValidateEnvironmentSafety.");
        Assert.IsTrue(buildIndex >= 0, "Program.cs must still build the API host explicitly.");
        Assert.IsTrue(validateIndex < buildIndex, "Environment safety validation must happen before builder.Build().");
    }

    [TestMethod]
    public void BlockC13_Program_KeepsLocalTestAndProductionLikeDelegates()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var safetyBlock = ExtractBetween(program, "static void ValidateEnvironmentSafety", "static string[] ResolveAllowedCorsOrigins");

        StringAssert.Contains(program, "static void ValidateEnvironmentSafety(EnvironmentInfoDto environmentInfo)");
        StringAssert.Contains(safetyBlock, "ValidateLocalTestEnvironmentSafety(environmentInfo)");
        StringAssert.Contains(safetyBlock, "ValidateProductionLikeEnvironmentSafety(environmentInfo, StartupEnvironmentSafety.Current)");
        StringAssert.Contains(safetyBlock, "static bool IsProductionLikeEnvironment(string environmentName)");
        StringAssert.Contains(safetyBlock, "\"Development\"");
        StringAssert.Contains(safetyBlock, "\"Test\"");
        StringAssert.Contains(safetyBlock, "\"LocalTest\"");
    }

    [TestMethod]
    public void BlockC13_ProductionLikeSafety_ChecksConnectionStringAndDatabasePosture()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var safetyBlock = SafetyBlock();

        StringAssert.Contains(program, "CreateEnvironmentSafetyContext");
        StringAssert.Contains(program, "GetConnectionString(\"IronDeveloperDb\")");
        StringAssert.Contains(safetyBlock, "ContainsPlaceholderDatabaseConfiguration");
        StringAssert.Contains(safetyBlock, "Production-like environment must configure a database connection string.");
        StringAssert.Contains(safetyBlock, "Production-like environment must configure a database name.");
        StringAssert.Contains(safetyBlock, "Production-like environment must not use placeholder database server configuration.");
        StringAssert.Contains(safetyBlock, "Production-like environment must not use test-like database names.");
        StringAssert.Contains(safetyBlock, "HasUnsafeProductionLikeDatabaseMarker");
    }

    [TestMethod]
    public void BlockC13_ProductionLikeSafety_ChecksLocalDatabaseServerMarkers()
    {
        var safetyBlock = SafetyBlock();

        StringAssert.Contains(safetyBlock, "IsLocalDatabaseServer");
        StringAssert.Contains(safetyBlock, "localhost");
        StringAssert.Contains(safetyBlock, "127.0.0.1");
        StringAssert.Contains(safetyBlock, "::1");
        StringAssert.Contains(safetyBlock, "(local)");
        StringAssert.Contains(safetyBlock, "(localdb)");
        StringAssert.Contains(safetyBlock, "DESKTOP-");
        StringAssert.Contains(safetyBlock, "SQLEXPRESS");
        StringAssert.Contains(safetyBlock, "Production-like environment must not use a local database server.");
    }

    [TestMethod]
    public void BlockC13_ProductionLikeSafety_ChecksDangerFlagAndUnsafeRoots()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var safetyBlock = SafetyBlock();

        StringAssert.Contains(safetyBlock, "environmentInfo.DangerRealRepoWritesEnabled");
        StringAssert.Contains(safetyBlock, "Production-like environment must not enable dangerous real repo writes.");
        StringAssert.Contains(safetyBlock, "IsUnsafeProductionLikeRoot");
        StringAssert.Contains(program, "LocalTest:WorkspaceRoot");
        StringAssert.Contains(program, "LocalTest:LogsRoot");
        StringAssert.Contains(program, "DisposableBuild:WorkspaceRoot");
        StringAssert.Contains(program, "DisposableBuild:EvidenceRoot");
        StringAssert.Contains(safetyBlock, "Production-like environment must not use local or test workspace roots.");
        StringAssert.Contains(safetyBlock, "Production-like environment must not use local or test logs roots.");
    }

    [TestMethod]
    public void BlockC13_ProductionLikeSafety_DoesNotEchoConnectionStringsOrSecrets()
    {
        var safetyBlock = SafetyBlock();

        StringAssert.Contains(safetyBlock, "password-bearing database configuration");
        AssertDoesNotContain(safetyBlock, "throw new InvalidOperationException(safetyContext.ConnectionString", "C13 safety block");
        AssertDoesNotContain(safetyBlock, "throw new InvalidOperationException(connectionString", "C13 safety block");
        AssertDoesNotContain(safetyBlock, "throw new InvalidOperationException(root", "C13 safety block");
        AssertDoesNotContain(safetyBlock, "throw new InvalidOperationException(dataSource", "C13 safety block");
    }

    [TestMethod]
    public void BlockC13_EnvironmentEndpointRemainsProtectedAndHealthRemainsAnonymous()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var healthEndpoint = ExtractBetween(program, "app.MapGet(\"/health\"", "app.MapGet(\"/api/environment\"");
        var environmentEndpoint = ExtractBetween(program, "app.MapGet(\"/api/environment\"", "app.MapControllers();");

        StringAssert.Contains(healthEndpoint, ".AllowAnonymous();");
        StringAssert.Contains(environmentEndpoint, ".RequireAuthorization();");
        AssertDoesNotContain(environmentEndpoint, ".AllowAnonymous()", "/api/environment endpoint");
    }

    [TestMethod]
    public void BlockC13_C06ThroughC12SecurityProofsRemainPresent()
    {
        var c06 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC06JwtSecretConfigurationTests.cs");
        var c07 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC07JwtStartupValidationTests.cs");
        var c08 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC08EnvironmentEndpointBoundaryTests.cs");
        var c09 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC09ExplicitCorsPolicyTests.cs");
        var c10 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC10WeaviateProductionAuthConfigTests.cs");
        var c11 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC11SecretScanningRegressionTests.cs");
        var c12 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC12LocalTestSafetyRegressionTests.cs");

        StringAssert.Contains(c06, "BlockC06_CommittedApiAppsettings_DoesNotContainJwtSigningKey");
        StringAssert.Contains(c07, "BlockC07_Program_UsesStartupValidationPathBeforeBuild");
        StringAssert.Contains(c08, "BlockC08_EnvironmentEndpoint_RequiresAuthorization");
        StringAssert.Contains(c09, "BlockC09_Program_AddsOneNamedCorsPolicy");
        StringAssert.Contains(c10, "BlockC10_ProductionEnabledWeaviate_MissingApiKeyFailsClosed");
        StringAssert.Contains(c11, "BlockC11_RepositoryTextFiles_DoNotContainHighConfidenceProviderTokens");
        StringAssert.Contains(c12, "BlockC12_Program_ValidatesLocalTestSafetyBeforeBuild");
    }

    [TestMethod]
    public void BlockC13_GovernanceBoundaryCiRunsC11C12AndC13SecurityProofs()
    {
        var script = ReadRepositoryFile("Scripts", "ci", "run-governance-boundary-ci.ps1");

        StringAssert.Contains(script, "$securityBoundaryFilter");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC11SecretScanningRegressionTests");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC12LocalTestSafetyRegressionTests");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC13ProductionEnvironmentSafetyRegressionTests");
        StringAssert.Contains(script, "-Name \"Security boundary tests\"");
        AssertDoesNotContain(script, "upload-artifact", "governance-boundary CI script");
    }

    [TestMethod]
    public void BlockC13_ReceiptRecordsBoundaryAndReviewTraps()
    {
        var receipt = ReadRepositoryFile("Docs", "receipts", "C13_PRODUCTION_ENVIRONMENT_SAFETY_VALIDATION.md");

        StringAssert.Contains(receipt, "Production environment safety validation prevents production-like API startup from using obvious local, test, placeholder, or dangerous resources.");
        StringAssert.Contains(receipt, "It does not grant authority, approval, policy satisfaction, execution permission, release readiness, deployment readiness, or workflow continuation.");
        StringAssert.Contains(receipt, "Passing production safety validation is not release readiness. It is only startup configuration evidence.");
        StringAssert.Contains(receipt, "production-like API can start with `YOUR_SERVER`");
        StringAssert.Contains(receipt, "production-like API can enable dangerous real repo writes");
    }

    [TestMethod]
    public void BlockC13_ProductionSafetyDoesNotAddMutationOrAuthoritySurface()
    {
        var safetyBlock = SafetyBlock();

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
            AssertDoesNotContain(safetyBlock, forbidden, "C13 production safety block");
        }
    }

    private static string SafetyBlock()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        return ExtractBetween(program, "static void ValidateEnvironmentSafety", "static string[] ResolveAllowedCorsOrigins");
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
}
