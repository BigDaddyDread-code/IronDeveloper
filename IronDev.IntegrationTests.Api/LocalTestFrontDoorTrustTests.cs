using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Data;
using Dapper;
using IronDev.Api.Services;
using IronDev.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
[DoNotParallelize]
[TestCategory("Auth")]
public sealed class LocalTestFrontDoorTrustTests : ApiTestBase
{
    private const string SeedEmail = "bob@irondev.local";
    private const string SeedPassword = "change-me-local-only";

    [TestMethod]
    public async Task FreshSeed_DocumentedCredentials_AreReadyAndLogInThroughRealApi()
    {
        await SeedValidContractUserAsync();

        var preflight = await CheckPreflightAsync();
        Assert.AreEqual(LocalTestPreflightStates.LocalTestReady, preflight.State);
        Assert.AreEqual("IronDeveloper_Test", preflight.Database);
        Assert.AreEqual("Passed", preflight.SeededLoginCheckResult);
        Assert.IsNull(preflight.ResetCommand);
        Assert.AreEqual("SmokeSimulation", preflight.SessionMode);
        Assert.IsFalse(preflight.SandboxApplyEnabled);
        CollectionAssert.DoesNotContain(preflight.Capabilities.ToArray(), "ControlledSandboxApply");

        var response = await Client.PostAsJsonAsync("/api/auth/login", new
        {
            email = SeedEmail,
            password = SeedPassword
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<LoginProbe>();
        Assert.IsFalse(string.IsNullOrWhiteSpace(body?.Token));
        Assert.AreEqual(1, body?.UserId);
    }

    [TestMethod]
    public async Task MissingSeedUser_ReturnsNamedState()
    {
        await ExecuteAsync("DELETE FROM dbo.TenantUsers WHERE UserId = 1; DELETE FROM dbo.Users WHERE Id = 1;");

        var preflight = await CheckPreflightAsync();

        Assert.AreEqual(LocalTestPreflightStates.SeedUserMissing, preflight.State);
        Assert.AreEqual(LocalTestPreflightService.ResetCommand, preflight.ResetCommand);
    }

    [TestMethod]
    public async Task WrongSeedHash_ReturnsNamedState()
    {
        await SeedValidContractUserAsync();
        await ExecuteAsync(
            "UPDATE dbo.Users SET PasswordHash = @Hash WHERE Id = 1;",
            new { Hash = BCrypt.Net.BCrypt.HashPassword("wrong-localtest-password", workFactor: 4) });

        var preflight = await CheckPreflightAsync();

        Assert.AreEqual(LocalTestPreflightStates.SeedCredentialInvalid, preflight.State);
        Assert.AreEqual(LocalTestPreflightService.ResetCommand, preflight.ResetCommand);
    }

    [TestMethod]
    public async Task MissingSeedMembership_ReturnsNamedState()
    {
        await SeedValidContractUserAsync();
        await ExecuteAsync("DELETE FROM dbo.TenantUsers WHERE TenantId = 1 AND UserId = 1;");

        var preflight = await CheckPreflightAsync();

        Assert.AreEqual(LocalTestPreflightStates.SeedMembershipMissing, preflight.State);
        Assert.AreEqual(LocalTestPreflightService.ResetCommand, preflight.ResetCommand);
    }

    [TestMethod]
    public async Task WrongConfiguredDatabase_ReturnsNamedStateWithoutQueryingIt()
    {
        var preflight = await CheckPreflightAsync("IronDeveloper_Unexpected_Test");

        Assert.AreEqual(LocalTestPreflightStates.WrongDatabase, preflight.State);
        Assert.AreEqual("IronDeveloper_Unexpected_Test", preflight.Database);
        Assert.AreEqual(LocalTestPreflightService.ResetCommand, preflight.ResetCommand);
    }

    [TestMethod]
    public async Task PreviewScopedDatabase_ReportsIdentityAndOnlyItsResetCommand()
    {
        var preflight = await CheckPreflightAsync(previewId: "workbench-pr00a");

        Assert.AreEqual(LocalTestPreflightStates.WrongDatabase, preflight.State);
        Assert.AreEqual("workbench-pr00a", preflight.PreviewId);
        Assert.AreEqual("0.1.0-preview.4", preflight.WorkbenchVersion);
        Assert.AreEqual("V2", preflight.WorkbenchMode);
        Assert.AreEqual(
            LocalTestPreflightService.ResetCommand + " -PreviewId workbench-pr00a",
            preflight.ResetCommand);
        StringAssert.Contains(preflight.Detail, "IronDeveloper_Test_workbench_pr00a");
    }

    [TestMethod]
    public async Task LauncherCapabilityDisagreement_ReturnsNamedStateAndSupportedRestart()
    {
        await SeedValidContractUserAsync();

        var preflight = await CheckPreflightAsync(capabilityMismatch: true);

        Assert.AreEqual(LocalTestPreflightStates.SessionCapabilityMismatch, preflight.State);
        Assert.AreEqual(LocalTestPreflightService.SandboxApplyRestartCommand, preflight.ResetCommand);
        Assert.AreEqual(LocalTestPreflightService.SandboxApplyRestartCommand, preflight.SandboxApplyRestartCommand);
        StringAssert.Contains(preflight.NextSafeAction, "Session capability mismatch");
    }

    [TestMethod]
    public async Task ProjectWorkCapabilityAgreement_IsVisibleInReadyPreflight()
    {
        await SeedValidContractUserAsync();

        var preflight = await CheckPreflightAsync(sandboxApplyEnabled: true);

        Assert.AreEqual(LocalTestPreflightStates.LocalTestReady, preflight.State);
        Assert.AreEqual("ProjectFeatureWork", preflight.SessionMode);
        Assert.IsTrue(preflight.SandboxApplyRequested);
        Assert.IsTrue(preflight.SandboxApplyEnabled);
        Assert.AreEqual(@"C:\IronDevTestWorkspaces", preflight.SandboxApplyRoot);
        CollectionAssert.Contains(preflight.Capabilities.ToArray(), "ControlledSandboxApply");
    }

    [TestMethod]
    public async Task NonLocalTestApi_ExposesNoResetGuidance()
    {
        var response = await Client.GetAsync("/api/localtest/preflight");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var preflight = await response.Content.ReadFromJsonAsync<LocalTestPreflightResponse>();
        Assert.IsNotNull(preflight);
        Assert.AreEqual(LocalTestPreflightStates.WrongEnvironment, preflight.State);
        Assert.IsNull(preflight.ResetCommand);
        Assert.AreEqual(0, preflight.ApiPid);
        Assert.IsNull(preflight.SessionId);
        Assert.IsNull(preflight.ApiBaseUrl);
    }

    private static async Task<LocalTestPreflightResponse> CheckPreflightAsync(
        string databaseName = "IronDeveloper_Test",
        bool capabilityMismatch = false,
        bool sandboxApplyEnabled = false,
        string previewId = "default")
    {
        var commit = ResolveApiBuildCommit();
        using var session = TemporaryEnvironmentVariable.Set("IRONDEV_LOCALTEST_SESSION_ID", "integration-test-session");
        using var repositoryCommit = TemporaryEnvironmentVariable.Set("IRONDEV_LOCALTEST_REPOSITORY_COMMIT", commit);
        using var apiUrl = TemporaryEnvironmentVariable.Set("IRONDEV_LOCALTEST_API_BASE_URL", "http://127.0.0.1:5000");
        var sessionModeValue = sandboxApplyEnabled ? "ProjectFeatureWork" : "SmokeSimulation";
        var rootValue = sandboxApplyEnabled ? @"C:\IronDevTestWorkspaces" : string.Empty;
        using var sessionMode = TemporaryEnvironmentVariable.Set("IRONDEV_LOCALTEST_SESSION_MODE", sessionModeValue);
        using var requested = TemporaryEnvironmentVariable.Set("IRONDEV_LOCALTEST_SANDBOX_APPLY_REQUESTED", sandboxApplyEnabled ? "true" : "false");
        using var enabled = TemporaryEnvironmentVariable.Set("IRONDEV_LOCALTEST_SANDBOX_APPLY_ENABLED", capabilityMismatch ? "true" : sandboxApplyEnabled ? "true" : "false");
        using var sandboxRoot = TemporaryEnvironmentVariable.Set("IRONDEV_LOCALTEST_SANDBOX_APPLY_ROOT", rootValue);
        using var capabilities = TemporaryEnvironmentVariable.Set("IRONDEV_LOCALTEST_CAPABILITIES", sandboxApplyEnabled
            ? "ProjectFeatureWork;ControlledSandboxApply"
            : "WorkflowSmokeSimulation");

        var connectionString = new SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = databaseName
        }.ConnectionString;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IronDeveloperDb"] = connectionString,
                ["WorkbenchV2:Version"] = "0.1.0-preview.4",
                ["WorkbenchV2:Enabled"] = "true",
                ["WorkbenchV2:V1FallbackEnabled"] = "true",
                ["WorkbenchV2:PreviewId"] = previewId,
                ["SkeletonApply:Enabled"] = sandboxApplyEnabled ? "true" : "false",
                ["SkeletonApply:LauncherCapabilityDeclared"] = sandboxApplyEnabled ? "true" : "false",
                ["SkeletonApply:LauncherSessionId"] = "integration-test-session",
                ["SkeletonApply:SandboxRoot"] = rootValue
            })
            .Build();
        var service = new LocalTestPreflightService(
            new TestHostEnvironment("LocalTest"),
            configuration,
            new TestDbConnectionFactory(ConnectionString),
            NullLogger<LocalTestPreflightService>.Instance);

        return await service.CheckAsync();
    }

    private static async Task SeedValidContractUserAsync()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(SeedPassword, workFactor: 4);
        await ExecuteAsync(
            """
            UPDATE dbo.Users
            SET Email = @Email,
                DisplayName = N'Bob Developer',
                PasswordHash = @Hash,
                IsActive = 1
            WHERE Id = 1;

            DELETE FROM dbo.TenantUsers WHERE UserId = 1;
            INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (1, 1, N'Owner');
            """,
            new { Email = SeedEmail, Hash = hash });
    }

    private static async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private static string ResolveApiBuildCommit()
    {
        var informationalVersion = typeof(LocalTestPreflightService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        var separator = informationalVersion.IndexOf('+');
        return separator >= 0 ? informationalVersion[(separator + 1)..] : informationalVersion;
    }

    private sealed record LoginProbe(string Token, int UserId, string DisplayName);

    private sealed class TestDbConnectionFactory(string connectionString) : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => new SqlConnection(connectionString);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "IronDev.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(AppContext.BaseDirectory);
    }

    private sealed class TemporaryEnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        private TemporaryEnvironmentVariable(string name, string value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public static TemporaryEnvironmentVariable Set(string name, string value) => new(name, value);

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _originalValue);
    }
}
