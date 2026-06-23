namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC14SensitiveApiRateLimitAuthBoundaryTests
{
    private static readonly string[] SensitiveControllerPaths =
    [
        Path.Combine("IronDev.Api", "Controllers", "ToolRequestsV1Controller.cs"),
        Path.Combine("IronDev.Api", "Controllers", "ToolGatesV1Controller.cs"),
        Path.Combine("IronDev.Api", "Controllers", "AgentRunsV1Controller.cs"),
        Path.Combine("IronDev.Api", "Controllers", "PatchArtifactsV1Controller.cs"),
        Path.Combine("IronDev.Api", "Controllers", "ApplyPreviewController.cs"),
        Path.Combine("IronDev.Api", "Controllers", "ManualMemoryImprovementsV1Controller.cs"),
        Path.Combine("IronDev.Api", "Controllers", "ReleaseReadinessDecisionRecordsController.cs")
    ];

    [TestMethod]
    public void BlockC14_Program_RegistersNamedRateLimitPolicies()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");

        StringAssert.Contains(program, "builder.Services.AddRateLimiter");
        StringAssert.Contains(program, "const string AuthLoginRateLimitPolicyName = \"AuthLoginPolicy\";");
        StringAssert.Contains(program, "const string SensitiveApiRateLimitPolicyName = \"SensitiveApiPolicy\";");
        StringAssert.Contains(program, "options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;");
        StringAssert.Contains(program, "options.AddPolicy(AuthLoginRateLimitPolicyName");
        StringAssert.Contains(program, "options.AddPolicy(SensitiveApiRateLimitPolicyName");
        StringAssert.Contains(program, "\"RateLimiting:AuthLogin:PermitLimit\"");
        StringAssert.Contains(program, "\"RateLimiting:SensitiveApi:PermitLimit\"");
    }

    [TestMethod]
    public void BlockC14_MiddlewareOrder_PreservesCorsAuthRateLimitAndAuthorizationBoundary()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");

        AssertBefore(program, "app.UseRouting();", "app.UseCors(CorsPolicyName);");
        AssertBefore(program, "app.UseCors(CorsPolicyName);", "app.UseAuthentication();");
        AssertBefore(program, "app.UseAuthentication();", "app.UseRateLimiter();");
        AssertBefore(program, "app.UseRateLimiter();", "app.UseAuthorization();");
        AssertBefore(program, "app.UseAuthorization();", "app.MapControllers();");
    }

    [TestMethod]
    public void BlockC14_Login_IsAnonymousButRateLimited()
    {
        var authController = ReadRepositoryFile("IronDev.Api", "Controllers", "AuthController.cs");
        var loginBlock = ExtractBetween(authController, "[HttpPost(\"login\")]", "public async Task<IActionResult> Login");

        StringAssert.Contains(loginBlock, "[AllowAnonymous]");
        StringAssert.Contains(loginBlock, "[EnableRateLimiting(\"AuthLoginPolicy\")]");
        AssertDoesNotContain(loginBlock, "[Authorize]", "login metadata");
    }

    [TestMethod]
    public void BlockC14_AuthMeAndLogout_RemainAuthorizedAndSensitiveRateLimited()
    {
        var authController = ReadRepositoryFile("IronDev.Api", "Controllers", "AuthController.cs");
        var meBlock = ExtractBetween(authController, "[HttpGet(\"me\")]", "public IActionResult Me()");
        var logoutBlock = ExtractBetween(authController, "[HttpPost(\"logout\")]", "public IActionResult Logout()");

        StringAssert.Contains(meBlock, "[Authorize]");
        StringAssert.Contains(meBlock, "[EnableRateLimiting(\"SensitiveApiPolicy\")]");
        StringAssert.Contains(logoutBlock, "[Authorize]");
        StringAssert.Contains(logoutBlock, "[EnableRateLimiting(\"SensitiveApiPolicy\")]");
        AssertDoesNotContain(meBlock, "[AllowAnonymous]", "auth/me metadata");
        AssertDoesNotContain(logoutBlock, "[AllowAnonymous]", "auth/logout metadata");
    }

    [TestMethod]
    public void BlockC14_SensitiveControllers_RemainAuthorizedAndRateLimited()
    {
        foreach (var controllerPath in SensitiveControllerPaths)
        {
            var source = ReadRepositoryFile(controllerPath);
            var classMetadata = ExtractBetween(source, "[ApiController]", "public sealed class");

            StringAssert.Contains(classMetadata, "[Authorize]");
            StringAssert.Contains(classMetadata, "[EnableRateLimiting(\"SensitiveApiPolicy\")]");
            AssertDoesNotContain(source, "[AllowAnonymous]", controllerPath);
        }
    }

    [TestMethod]
    public void BlockC14_EnvironmentEndpoint_IsAuthorizedAndSensitiveRateLimited()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var healthEndpoint = ExtractBetween(program, "app.MapGet(\"/health\"", "app.MapGet(\"/api/environment\"");
        var environmentEndpoint = ExtractBetween(program, "app.MapGet(\"/api/environment\"", "app.MapControllers();");

        StringAssert.Contains(healthEndpoint, ".AllowAnonymous();");
        AssertDoesNotContain(healthEndpoint, "RequireRateLimiting", "health endpoint");
        StringAssert.Contains(environmentEndpoint, ".RequireAuthorization()");
        StringAssert.Contains(environmentEndpoint, ".RequireRateLimiting(SensitiveApiRateLimitPolicyName)");
        AssertDoesNotContain(environmentEndpoint, ".AllowAnonymous()", "/api/environment endpoint");
    }

    [TestMethod]
    public void BlockC14_RateLimitPartitionKeys_DoNotUseRawCredentialsOrRequestBody()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var rateLimitRegistration = ExtractBetween(program, "builder.Services.AddRateLimiter", "// Infrastructure");
        var rateLimitHelpers = ExtractBetween(program, "static string ResolveLoginRateLimitPartitionKey", "static IRunReportService CreateRunReportService");
        var rateLimitSource = rateLimitRegistration + Environment.NewLine + rateLimitHelpers;

        StringAssert.Contains(rateLimitHelpers, "context.Connection.RemoteIpAddress");
        StringAssert.Contains(rateLimitHelpers, "context.User.FindFirst");
        StringAssert.Contains(rateLimitHelpers, "\"tenant_id\"");
        AssertDoesNotContain(rateLimitSource, "Authorization", "rate-limit partition source");
        AssertDoesNotContain(rateLimitSource, "Bearer", "rate-limit partition source");
        AssertDoesNotContain(rateLimitSource, "Password", "rate-limit partition source");
        AssertDoesNotContain(rateLimitSource, "ApiKey", "rate-limit partition source");
        AssertDoesNotContain(rateLimitSource, "api_key", "rate-limit partition source");
        AssertDoesNotContain(rateLimitSource, "Request.Body", "rate-limit partition source");
        AssertDoesNotContain(rateLimitSource, "ReadFromJsonAsync", "rate-limit partition source");
        AssertDoesNotContain(rateLimitSource, "ReadToEnd", "rate-limit partition source");
    }

    [TestMethod]
    public void BlockC14_RateLimitingDoesNotGrantAuthorityOrExecution()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var registration = ExtractBetween(program, "builder.Services.AddRateLimiter", "// Infrastructure");
        var helpers = ExtractBetween(program, "static string ResolveLoginRateLimitPartitionKey", "static IRunReportService CreateRunReportService");
        var source = registration + Environment.NewLine + helpers;

        foreach (var forbidden in new[]
        {
            "Approve",
            "PolicySatisfaction",
            "SourceApply",
            "Commit",
            "Push",
            "PullRequest",
            "Merge",
            "Release",
            "Deploy",
            "WorkflowContinuation",
            "MemoryPromotion"
        })
        {
            AssertDoesNotContain(source, forbidden, "C14 rate-limit implementation");
        }
    }

    [TestMethod]
    public void BlockC14_C06ThroughC13SecurityProofsRemainPresent()
    {
        var c06 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC06JwtSecretConfigurationTests.cs");
        var c07 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC07JwtStartupValidationTests.cs");
        var c08 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC08EnvironmentEndpointBoundaryTests.cs");
        var c09 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC09ExplicitCorsPolicyTests.cs");
        var c10 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC10WeaviateProductionAuthConfigTests.cs");
        var c11 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC11SecretScanningRegressionTests.cs");
        var c12 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC12LocalTestSafetyRegressionTests.cs");
        var c13 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC13ProductionEnvironmentSafetyRegressionTests.cs");

        StringAssert.Contains(c06, "BlockC06_CommittedApiAppsettings_DoesNotContainJwtSigningKey");
        StringAssert.Contains(c07, "BlockC07_Program_UsesStartupValidationPathBeforeBuild");
        StringAssert.Contains(c08, "BlockC08_EnvironmentEndpoint_RequiresAuthorization");
        StringAssert.Contains(c09, "BlockC09_Program_AddsOneNamedCorsPolicy");
        StringAssert.Contains(c10, "BlockC10_ProductionEnabledWeaviate_MissingApiKeyFailsClosed");
        StringAssert.Contains(c11, "BlockC11_RepositoryTextFiles_DoNotContainHighConfidenceProviderTokens");
        StringAssert.Contains(c12, "BlockC12_Program_ValidatesLocalTestSafetyBeforeBuild");
        StringAssert.Contains(c13, "BlockC13_Program_ValidatesEnvironmentSafetyBeforeBuild");
    }

    [TestMethod]
    public void BlockC14_GovernanceBoundaryCiRunsC11ThroughC14SecurityProofs()
    {
        var script = ReadRepositoryFile("Scripts", "ci", "run-governance-boundary-ci.ps1");

        StringAssert.Contains(script, "$securityBoundaryFilter");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC11SecretScanningRegressionTests");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC12LocalTestSafetyRegressionTests");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC13ProductionEnvironmentSafetyRegressionTests");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC14SensitiveApiRateLimitAuthBoundaryTests");
        StringAssert.Contains(script, "-Name \"Security boundary tests\"");
        AssertDoesNotContain(script, "upload-artifact", "governance-boundary CI script");
    }

    [TestMethod]
    public void BlockC14_ReceiptRecordsBoundaryAndReviewTraps()
    {
        var receipt = ReadRepositoryFile("Docs", "receipts", "C14_SENSITIVE_API_RATE_LIMIT_AUTH_HARDENING.md");

        StringAssert.Contains(receipt, "Rate limiting slows request abuse. It does not authenticate users, authorize tenants, grant authority, approve execution, satisfy policy, create release readiness, create deployment readiness, or continue workflow.");
        StringAssert.Contains(receipt, "A rate-limited sensitive endpoint is still not safe unless backend authorization and authority gates approve the requested action.");
        StringAssert.Contains(receipt, "login is anonymous but not unlimited");
        StringAssert.Contains(receipt, "sensitive endpoints lose `[Authorize]`");
        StringAssert.Contains(receipt, "rate-limit keys use raw bearer tokens, passwords, API keys, or request bodies");
    }

    private static void AssertBefore(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);

        Assert.IsTrue(firstIndex >= 0, $"Missing marker: {first}");
        Assert.IsTrue(secondIndex >= 0, $"Missing marker: {second}");
        Assert.IsTrue(firstIndex < secondIndex, $"{first} must appear before {second}.");
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
