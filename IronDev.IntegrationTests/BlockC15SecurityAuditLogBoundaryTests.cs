using System.Text.RegularExpressions;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC15SecurityAuditLogBoundaryTests
{
    [TestMethod]
    public void BlockC15_SecurityAuditEventModel_DefinesRequiredEventsAndRedactedFields()
    {
        var model = ReadRepositoryFile("IronDev.Core", "Models", "SecurityAuditEvent.cs");

        foreach (var requiredEvent in new[]
        {
            "AuthLoginSucceeded",
            "AuthLoginFailed",
            "AuthLogoutRequested",
            "TenantSelectionSucceeded",
            "TenantSelectionDenied",
            "AdminSecurityChangeRequested",
            "AdminSecurityChangeSucceeded",
            "AdminSecurityChangeDenied"
        })
        {
            StringAssert.Contains(model, requiredEvent);
        }

        StringAssert.Contains(model, "ActorEmailHash");
        StringAssert.Contains(model, "RemoteIpHash");
        StringAssert.Contains(model, "UserAgentHash");
        StringAssert.Contains(model, "HashRedacted");
        AssertDoesNotContain(model, "Password", "security audit event model");
        AssertDoesNotContain(model, "Token", "security audit event model");
        AssertDoesNotContain(model, "AuthorizationHeader", "security audit event model");
        AssertDoesNotContain(model, "RequestBody", "security audit event model");
        AssertDoesNotContain(model, "ConnectionString", "security audit event model");
    }

    [TestMethod]
    public void BlockC15_SecurityAuditLog_IsAppendOnlyAndRejectsUnsafeMaterial()
    {
        var implementation = ReadRepositoryFile("IronDev.Infrastructure", "Security", "SecurityAuditLog.cs");

        StringAssert.Contains(implementation, "public Task AppendAsync");
        StringAssert.Contains(implementation, "_events.Add(normalized)");
        StringAssert.Contains(implementation, "public IReadOnlyList<SecurityAuditEvent> Snapshot()");
        StringAssert.Contains(implementation, "SensitiveValueMarkers");
        StringAssert.Contains(implementation, "Security audit event contains unsafe material.");
        AssertDoesNotContain(implementation, "_events.Remove", "security audit log");
        AssertDoesNotContain(implementation, "_events.Clear", "security audit log");
        AssertDoesNotContain(implementation, "File.Write", "security audit log");
        AssertDoesNotContain(implementation, "SqlConnection", "security audit log");
        AssertDoesNotContain(implementation, "HttpClient", "security audit log");
        AssertDoesNotContain(implementation, "Process.Start", "security audit log");
    }

    [TestMethod]
    public void BlockC15_Program_RegistersSecurityAuditLogWithoutChangingSecurityPolicyOrder()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");

        StringAssert.Contains(program, "using IronDev.Infrastructure.Security;");
        StringAssert.Contains(program, "builder.Services.AddSingleton<ISecurityAuditLog, SecurityAuditLog>();");
        AssertBefore(program, "app.UseAuthentication();", "app.UseRateLimiter();");
        AssertBefore(program, "app.UseRateLimiter();", "app.UseAuthorization();");
    }

    [TestMethod]
    public void BlockC15_AuthController_AuditsLoginSuccessFailureAndLogout()
    {
        var authController = ReadRepositoryFile("IronDev.Api", "Controllers", "AuthController.cs");

        StringAssert.Contains(authController, "ISecurityAuditLog securityAuditLog");
        StringAssert.Contains(authController, "SecurityAuditEventType.AuthLoginFailed");
        StringAssert.Contains(authController, "SecurityAuditEventType.AuthLoginSucceeded");
        StringAssert.Contains(authController, "SecurityAuditEventType.AuthLogoutRequested");
        StringAssert.Contains(authController, "TryAppendAuditEventAsync");
        StringAssert.Contains(authController, "SecurityAuditEvent.HashRedacted(actorEmail)");
        StringAssert.Contains(authController, "SecurityAuditEvent.HashRedacted(HttpContext.Connection.RemoteIpAddress?.ToString())");
        StringAssert.Contains(authController, "SecurityAuditEvent.HashRedacted(Request.Headers.UserAgent.ToString())");
        AssertBefore(authController, "SecurityAuditEventType.AuthLoginSucceeded", "var token = _jwtTokenService.CreateToken");
        AssertBefore(authController, "SecurityAuditEventType.AuthLoginFailed", "return Unauthorized");
        AssertDoesNotContain(ExtractBetween(authController, "BuildAuditEvent(", "private async Task<bool> TryAppendAuditEventAsync"), "request.Password", "auth audit event construction");
    }

    [TestMethod]
    public void BlockC15_TenantController_AuditsTenantSelectionSuccessAndDenied()
    {
        var tenantController = ReadRepositoryFile("IronDev.Api", "Controllers", "TenantController.cs");

        StringAssert.Contains(tenantController, "ISecurityAuditLog securityAuditLog");
        StringAssert.Contains(tenantController, "SecurityAuditEventType.TenantSelectionDenied");
        StringAssert.Contains(tenantController, "SecurityAuditEventType.TenantSelectionSucceeded");
        StringAssert.Contains(tenantController, "TryAppendAuditEventAsync");
        StringAssert.Contains(tenantController, "SecurityAuditEvent.HashRedacted(actorEmail)");
        AssertBefore(tenantController, "SecurityAuditEventType.TenantSelectionSucceeded", "var token = _jwtTokenService.CreateToken");
        AssertBefore(tenantController, "SecurityAuditEventType.TenantSelectionDenied", "return Forbid();");
    }

    [TestMethod]
    public void BlockC15_AuditAppendFailure_FailsClosedBeforeIssuingTokens()
    {
        var authController = ReadRepositoryFile("IronDev.Api", "Controllers", "AuthController.cs");
        var tenantController = ReadRepositoryFile("IronDev.Api", "Controllers", "TenantController.cs");

        foreach (var source in new[] { authController, tenantController })
        {
            StringAssert.Contains(source, "StatusCodes.Status503ServiceUnavailable");
            StringAssert.Contains(source, "Security audit unavailable.");
        }

        AssertBefore(authController, "if (!loginAuditWritten)", "var token = _jwtTokenService.CreateToken");
        AssertBefore(tenantController, "if (!tenantAuditWritten)", "var token = _jwtTokenService.CreateToken");
    }

    [TestMethod]
    public void BlockC15_AuditRecords_DoNotStoreRawCredentialOrHeaderShapes()
    {
        var model = ReadRepositoryFile("IronDev.Core", "Models", "SecurityAuditEvent.cs");
        var contract = ReadRepositoryFile("IronDev.Core", "Interfaces", "ISecurityAuditLog.cs");
        var authController = ReadRepositoryFile("IronDev.Api", "Controllers", "AuthController.cs");
        var tenantController = ReadRepositoryFile("IronDev.Api", "Controllers", "TenantController.cs");
        var auditRecordSource = model + Environment.NewLine + contract;

        foreach (var forbiddenProperty in new[]
        {
            "Password",
            "BearerToken",
            "JwtToken",
            "AuthorizationHeader",
            "RawAuthorization",
            "RequestBody",
            "RawCredential",
            "JwtSigningKey",
            "ApiKey",
            "ConnectionString",
            "StackTrace"
        })
        {
            AssertDoesNotContain(auditRecordSource, forbiddenProperty, "security audit record contract");
        }

        AssertDoesNotContain(authController, "actorEmail: request.Password", "auth audit construction");
        AssertDoesNotContain(authController, "Metadata = request", "auth audit construction");
        AssertDoesNotContain(tenantController, "Metadata = request", "tenant audit construction");
    }

    [TestMethod]
    public void BlockC15_DoesNotAddAdminMutationEndpoints()
    {
        var controllerDirectory = Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers");
        var controllerFiles = Directory.GetFiles(controllerDirectory, "*.cs");

        foreach (var controllerFile in controllerFiles)
        {
            var name = Path.GetFileName(controllerFile);
            var source = File.ReadAllText(controllerFile);
            AssertDoesNotContain(name, "Admin", "controller filename");
            AssertDoesNotContain(source, "[Route(\"api/admin", name);
            AssertDoesNotContain(source, "[Route(\"/api/admin", name);
            AssertDoesNotContain(source, "AdminSecurityChange", name);
        }
    }

    [TestMethod]
    public void BlockC15_AuditLogDoesNotGrantAuthorityOrExecution()
    {
        var source = string.Join(
            Environment.NewLine,
            ReadRepositoryFile("IronDev.Core", "Models", "SecurityAuditEvent.cs"),
            ReadRepositoryFile("IronDev.Core", "Interfaces", "ISecurityAuditLog.cs"),
            ReadRepositoryFile("IronDev.Infrastructure", "Security", "SecurityAuditLog.cs"));

        foreach (var forbidden in new[]
        {
            "Approve",
            "PolicySatisfaction",
            "SourceApply",
            "RollbackExecution",
            "Commit",
            "Push",
            "PullRequest",
            "Merge",
            "Release",
            "Deploy",
            "WorkflowContinuation",
            "PromoteMemory"
        })
        {
            AssertDoesNotContain(source, forbidden, "C15 security audit source");
        }
    }

    [TestMethod]
    public void BlockC15_C06ThroughC14SecurityProofsRemainPresent()
    {
        var c06 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC06JwtSecretConfigurationTests.cs");
        var c07 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC07JwtStartupValidationTests.cs");
        var c08 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC08EnvironmentEndpointBoundaryTests.cs");
        var c09 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC09ExplicitCorsPolicyTests.cs");
        var c10 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC10WeaviateProductionAuthConfigTests.cs");
        var c11 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC11SecretScanningRegressionTests.cs");
        var c12 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC12LocalTestSafetyRegressionTests.cs");
        var c13 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC13ProductionEnvironmentSafetyRegressionTests.cs");
        var c14 = ReadRepositoryFile("IronDev.IntegrationTests", "BlockC14SensitiveApiRateLimitAuthBoundaryTests.cs");

        StringAssert.Contains(c06, "BlockC06_CommittedApiAppsettings_DoesNotContainJwtSigningKey");
        StringAssert.Contains(c07, "BlockC07_Program_UsesStartupValidationPathBeforeBuild");
        StringAssert.Contains(c08, "BlockC08_EnvironmentEndpoint_RequiresAuthorization");
        StringAssert.Contains(c09, "BlockC09_Program_AddsOneNamedCorsPolicy");
        StringAssert.Contains(c10, "BlockC10_ProductionEnabledWeaviate_MissingApiKeyFailsClosed");
        StringAssert.Contains(c11, "BlockC11_RepositoryTextFiles_DoNotContainHighConfidenceProviderTokens");
        StringAssert.Contains(c12, "BlockC12_Program_ValidatesLocalTestSafetyBeforeBuild");
        StringAssert.Contains(c13, "BlockC13_Program_ValidatesEnvironmentSafetyBeforeBuild");
        StringAssert.Contains(c14, "BlockC14_Login_IsAnonymousButRateLimited");
    }

    [TestMethod]
    public void BlockC15_GovernanceBoundaryCiRunsC11ThroughC15SecurityProofs()
    {
        var script = ReadRepositoryFile("Scripts", "ci", "run-governance-boundary-ci.ps1");

        StringAssert.Contains(script, "$securityBoundaryFilter");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC11SecretScanningRegressionTests");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC12LocalTestSafetyRegressionTests");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC13ProductionEnvironmentSafetyRegressionTests");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC14SensitiveApiRateLimitAuthBoundaryTests");
        StringAssert.Contains(script, "FullyQualifiedName~BlockC15SecurityAuditLogBoundaryTests");
        AssertDoesNotContain(script, "upload-artifact", "governance-boundary CI script");
    }

    [TestMethod]
    public void BlockC15_ReceiptRecordsBoundaryAndReviewTraps()
    {
        var receipt = ReadRepositoryFile("Docs", "receipts", "C15_AUTH_TENANT_ADMIN_AUDIT_LOG.md");

        StringAssert.Contains(receipt, "Audit records explain security-sensitive changes. They do not authenticate users, authorize tenants, grant admin authority, approve execution, satisfy policy, create release readiness, create deployment readiness, or continue workflow.");
        StringAssert.Contains(receipt, "An audit record is evidence that a decision or attempt occurred. It is not authority for future decisions.");
        StringAssert.Contains(receipt, "auth/tenant success paths fail closed when audit append fails");
        StringAssert.Contains(receipt, "raw credentials, bearer tokens, authorization headers, request bodies, connection strings, stack traces, and provider keys enter audit records");
        StringAssert.Contains(receipt, "admin endpoints are added without audited security-change events");
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
