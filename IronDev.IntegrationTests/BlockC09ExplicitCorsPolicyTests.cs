using System.Text.Json;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC09ExplicitCorsPolicyTests
{
    [TestMethod]
    public void BlockC09_Program_AddsOneNamedCorsPolicy()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");

        StringAssert.Contains(program, "const string CorsPolicyName = \"IronDevCors\";");
        StringAssert.Contains(program, "builder.Services.AddCors");
        StringAssert.Contains(program, "options.AddPolicy(CorsPolicyName");
        StringAssert.Contains(program, ".WithOrigins(origins)");
        StringAssert.Contains(program, ".WithHeaders(\"Authorization\", \"Content-Type\")");
        StringAssert.Contains(program, ".WithMethods(\"GET\", \"POST\", \"PUT\", \"DELETE\")");
    }

    [TestMethod]
    public void BlockC09_Program_UsesCorsBeforeAuthenticationAndAuthorization()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var httpsIndex = program.IndexOf("app.UseHttpsRedirection();", StringComparison.Ordinal);
        var routingIndex = program.IndexOf("app.UseRouting();", StringComparison.Ordinal);
        var corsIndex = program.IndexOf("app.UseCors(CorsPolicyName);", StringComparison.Ordinal);
        var authnIndex = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var authzIndex = program.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);

        Assert.IsTrue(httpsIndex >= 0, "Program.cs must call UseHttpsRedirection.");
        Assert.IsTrue(routingIndex > httpsIndex, "UseRouting must run after HTTPS redirection.");
        Assert.IsTrue(corsIndex > routingIndex, "UseCors must run after routing.");
        Assert.IsTrue(authnIndex > corsIndex, "UseCors must run before authentication.");
        Assert.IsTrue(authzIndex > authnIndex, "Authorization must still run after authentication.");
    }

    [TestMethod]
    public void BlockC09_Program_DoesNotUsePermissiveCorsHelpers()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");

        foreach (var marker in new[]
        {
            ".AllowAnyOrigin(",
            ".AllowAnyHeader(",
            ".AllowAnyMethod(",
            ".AllowCredentials(",
            "SetIsOriginAllowed(_ => true",
            "SetIsOriginAllowed(origin => true",
            "Request.Headers[\"Origin\"]",
            "Request.Headers.Origin"
        })
        {
            AssertDoesNotContain(program, marker, "Program.cs");
        }
    }

    [TestMethod]
    public void BlockC09_Program_RejectsWildcardBlankInvalidDuplicateAndProductionLocalhostOrigins()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");

        StringAssert.Contains(program, "Cors:AllowedOrigins cannot contain blank origins.");
        StringAssert.Contains(program, "Cors:AllowedOrigins cannot contain wildcard origins.");
        StringAssert.Contains(program, "Duplicate CORS origin configured");
        StringAssert.Contains(program, "CORS origin must use http or https");
        StringAssert.Contains(program, "Cors:AllowedOrigins must not include trailing slashes.");
        StringAssert.Contains(program, "Production CORS configuration cannot include localhost origins.");
    }

    [TestMethod]
    public void BlockC09_CommittedCorsConfiguration_IsExplicitAndNotPermissive()
    {
        using var appsettings = JsonDocument.Parse(ReadRepositoryFile("IronDev.Api", "appsettings.json"));
        var cors = appsettings.RootElement.GetProperty("Cors");
        Assert.AreEqual(0, cors.GetProperty("AllowedOrigins").GetArrayLength());

        foreach (var relativePath in new[]
        {
            "IronDev.Api/appsettings.json",
            "IronDev.Api/appsettings.Development.json",
            "IronDev.Api/appsettings.LocalTest.json"
        })
        {
            using var document = JsonDocument.Parse(ReadRepositoryFile(relativePath.Split('/')));
            if (!document.RootElement.TryGetProperty("Cors", out var configuredCors))
                continue;

            var corsSource = configuredCors.GetRawText();
            AssertDoesNotContain(corsSource, "\"*\"", relativePath);
            AssertDoesNotContain(corsSource, "https://*.", relativePath);
            AssertDoesNotContain(corsSource, "AllowAnyOrigin", relativePath);
            AssertDoesNotContain(corsSource, "AllowCredentials", relativePath);
        }
    }

    [TestMethod]
    public void BlockC09_EnvironmentEndpoint_RemainsProtectedAndHealthRemainsAnonymous()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var environmentEndpoint = ExtractEndpointChain(program, "app.MapGet(\"/api/environment\"");
        var healthEndpoint = ExtractEndpointChain(program, "app.MapGet(\"/health\"");

        StringAssert.Contains(environmentEndpoint, ".RequireAuthorization()");
        AssertDoesNotContain(environmentEndpoint, ".AllowAnonymous()", "/api/environment endpoint");
        StringAssert.Contains(healthEndpoint, ".AllowAnonymous()");
    }

    [TestMethod]
    public void BlockC09_JwtStartupTokenAndTenantBehaviorRemainBackendOwned()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var tokenService = ReadRepositoryFile("IronDev.Api", "Auth", "JwtTokenService.cs");
        var tenantContext = ReadRepositoryFile("IronDev.Api", "Auth", "JwtTenantContext.cs");

        StringAssert.Contains(program, "JwtStartupConfigurationValidator.Validate(builder.Configuration)");
        StringAssert.Contains(program, "JwtSigningKeyResolver.Resolve(builder.Configuration)");
        StringAssert.Contains(tokenService, "JwtSigningKeyResolver.Resolve(configuration)");
        StringAssert.Contains(tokenService, "tenant_id");
        StringAssert.Contains(tenantContext, "tenant_id");
    }

    [TestMethod]
    public void BlockC09_DoesNotAddFrontendGeneratedSqlGovernanceReleaseOrDeploySurfaces()
    {
        foreach (var relativePath in new[]
        {
            "IronDev.TauriShell/src",
            "IronDev.TauriShell/openapi",
            "IronDev.TauriShell/src/api/generated",
            "Database",
            "IronDev.Core/Governance",
            "IronDev.Core/SourceApply"
        })
        {
            Assert.IsTrue(Directory.Exists(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar))));
        }

        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var corsBlock = ExtractBetween(program, "builder.Services.AddCors", "// Infrastructure");

        foreach (var marker in new[]
        {
            "OpenApi",
            "Generated",
            "SqlConnection",
            "Migration",
            "PolicySatisfaction",
            "Approval",
            "SourceApply",
            "CommitExecutor",
            "PushExecutor",
            "PullRequest",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "MemoryPromotion",
            "WorkflowContinuation"
        })
        {
            AssertDoesNotContain(corsBlock, marker, "C09 CORS registration block");
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

    private static string ExtractBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Could not find {startMarker}.");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsTrue(end > start, $"Could not find {endMarker} after {startMarker}.");
        return source[start..end];
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
