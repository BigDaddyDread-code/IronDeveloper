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
