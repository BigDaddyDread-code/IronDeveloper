namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC08EnvironmentEndpointBoundaryTests
{
    [TestMethod]
    public void BlockC08_EnvironmentEndpoint_RequiresAuthorization()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var endpoint = ExtractEndpointChain(program, "app.MapGet(\"/api/environment\"");

        StringAssert.Contains(endpoint, "Results.Ok(environmentInfo)");
        StringAssert.Contains(endpoint, ".RequireAuthorization()");
        AssertDoesNotContain(endpoint, ".AllowAnonymous()", "/api/environment endpoint");
    }

    [TestMethod]
    public void BlockC08_HealthEndpoint_RemainsAnonymous()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var endpoint = ExtractEndpointChain(program, "app.MapGet(\"/health\"");

        StringAssert.Contains(endpoint, ".AllowAnonymous()");
        AssertDoesNotContain(endpoint, ".RequireAuthorization()", "/health endpoint");
    }

    [TestMethod]
    public void BlockC08_EnvironmentEndpoint_RemainsReadOnlyDiagnosticEvidence()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var endpoint = ExtractEndpointChain(program, "app.MapGet(\"/api/environment\"");

        foreach (var marker in new[]
        {
            "MapPost(",
            "MapPut(",
            "MapPatch(",
            "MapDelete(",
            "RunProcessAsync",
            "ProcessStartInfo",
            "SqlConnection",
            "CreateCommand",
            "File.Write",
            "File.Append",
            "SourceApply",
            "CommitExecutor",
            "PushExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "PromoteMemory",
            "ContinueWorkflow"
        })
        {
            AssertDoesNotContain(endpoint, marker, "/api/environment endpoint");
        }
    }

    [TestMethod]
    public void BlockC08_JwtStartupTokenAndTenantPathsRemainBackendOwned()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var tokenService = ReadRepositoryFile("IronDev.Api", "Auth", "JwtTokenService.cs");
        var tenantContext = ReadRepositoryFile("IronDev.Api", "Auth", "JwtTenantContext.cs");

        StringAssert.Contains(program, "app.UseAuthentication();");
        StringAssert.Contains(program, "app.UseAuthorization();");
        StringAssert.Contains(program, "JwtSigningKeyResolver.Resolve(builder.Configuration)");
        StringAssert.Contains(tokenService, "JwtSigningKeyResolver.Resolve(configuration)");
        StringAssert.Contains(tokenService, "tenant_id");
        StringAssert.Contains(tenantContext, "tenant_id");
    }

    [TestMethod]
    public void BlockC08_DoesNotAddFrontendGeneratedSqlGovernanceReleaseOrDeploySurfaces()
    {
        foreach (var relativePath in new[]
        {
            "IronDev.TauriShell/src",
            "IronDev.TauriShell/openapi",
            "IronDev.TauriShell/src/api/generated",
            "Database",
            "IronDev.Core/Governance"
        })
        {
            Assert.IsTrue(Directory.Exists(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar))));
        }

        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var endpoint = ExtractEndpointChain(program, "app.MapGet(\"/api/environment\"");

        foreach (var marker in new[]
        {
            "OpenApi",
            "Generated",
            "DbContext",
            "Migration",
            "PolicySatisfaction",
            "Approval",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "MemoryPromotion",
            "WorkflowContinuation"
        })
        {
            AssertDoesNotContain(endpoint, marker, "/api/environment endpoint");
        }
    }

    private static string ExtractEndpointChain(string source, string endpointStart)
    {
        var start = source.IndexOf(endpointStart, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Could not find endpoint starting with {endpointStart}.");

        var terminator = source.IndexOf(";\r\n", start, StringComparison.Ordinal);
        if (terminator < 0)
            terminator = source.IndexOf(";\n", start, StringComparison.Ordinal);

        Assert.IsTrue(terminator > start, $"Could not find endpoint terminator for {endpointStart}.");
        return source[start..terminator];
    }

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static void AssertDoesNotContain(string text, string marker, string sourceName)
    {
        Assert.IsFalse(
            text.Contains(marker, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain '{marker}'.");
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
