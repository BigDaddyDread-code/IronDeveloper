using System.Net;
using IronDev.Core.Agents.Concrete;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
[TestCategory("ConfigBoundary")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class TestEnvironmentIsolationSafetyTests
{
    private const string TestJwtKey = "test-environment-isolation-jwt-key-32chars";
    private const string DefaultTestSqlServer = "(localdb)\\MSSQLLocalDB";

    [TestMethod]
    public async Task Test_WithIsolatedDatabase_Starts()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(
            environmentName: "Test",
            databaseName: "IronDeveloper_Test",
            localTestWorkspaceRoot: Path.Combine(Path.GetTempPath(), "IronDev", "Test", "workspaces"),
            localTestLogsRoot: Path.Combine(Path.GetTempPath(), "IronDev", "Test", "logs"),
            disposableWorkspaceRoot: Path.Combine(Path.GetTempPath(), "IronDev", "CI", "workspaces"),
            disposableEvidenceRoot: Path.Combine(Path.GetTempPath(), "IronDev", "CI", "evidence"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public void Test_WithMissingDatabaseName_FailsStartup()
    {
        using var factory = BuildFactory(environmentName: "Test", databaseName: string.Empty);

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "Non-LocalTest test environment");
        StringAssert.Contains(exception.ToString(), "isolated test database");
    }

    [DataTestMethod]
    [DataRow("IronDeveloper")]
    [DataRow("IronDeveloper_Main")]
    [DataRow("IronDeveloper_Local")]
    [DataRow("IronDeveloper_Dev")]
    [DataRow("IronDeveloper_Prod")]
    [DataRow("IronDeveloper_Live")]
    [DataRow("IronDeveloper_UAT")]
    [DataRow("IronDeveloper_Demo")]
    [DataRow("IronDeveloper_Accept")]
    [DataRow("IronDeveloper_Staging")]
    [DataRow("ProductionTestBackup")]
    [DataRow("Contest")]
    [DataRow("Latest")]
    [DataRow("Testament")]
    [DataRow("ProdTest")]
    public void Test_WithUnsafeDatabaseName_FailsStartup(string databaseName)
    {
        using var factory = BuildFactory(environmentName: "Test", databaseName: databaseName);

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "Non-LocalTest test environment");
        StringAssert.Contains(exception.ToString(), "isolated test database name");
    }

    [DataTestMethod]
    [DataRow("CI", "IronDeveloper_CI")]
    [DataRow("IntegrationTest", "IronDeveloper_IntegrationTest")]
    [DataRow("E2E", "IronDeveloper_E2E")]
    [DataRow("AutomationTest", "IronDeveloper_AutomationTest")]
    [DataRow("SmokeTest", "IronDeveloper_SmokeTest")]
    public async Task TestShapedAliases_WithIsolatedDatabase_Start(string environmentName, string databaseName)
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(environmentName: environmentName, databaseName: databaseName);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public void Test_WithDangerousRealRepoWritesEnabled_FailsStartup()
    {
        using var factory = BuildFactory(
            environmentName: "Test",
            databaseName: "IronDeveloper_Test",
            dangerRealRepoWritesEnabled: true);

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "Non-LocalTest test environment");
        StringAssert.Contains(exception.ToString(), "dangerous real repo writes");
    }

    [DataTestMethod]
    [DataRow("LocalTest:WorkspaceRoot", "IronDevWorkspaces", "test workspace root")]
    [DataRow("LocalTest:LogsRoot", "IronDevLogs", "test logs root")]
    [DataRow("DisposableBuild:WorkspaceRoot", "IronDevLocalWorkspaces", "disposable workspace root")]
    [DataRow("DisposableBuild:EvidenceRoot", "ProductionEvidence", "disposable evidence root")]
    public void Test_WithUnsafeConfiguredRoot_FailsStartup(
        string key,
        string unsafeSegment,
        string expectedMessage)
    {
        using var factory = BuildFactory(
            environmentName: "Test",
            databaseName: "IronDeveloper_Test",
            extraSettings: new Dictionary<string, string?>
            {
                [key] = Path.Combine(Path.GetTempPath(), unsafeSegment)
            });

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "Non-LocalTest test environment");
        StringAssert.Contains(exception.ToString(), expectedMessage);
        AssertDoesNotContain(exception.ToString(), Path.GetTempPath(), "startup exception");
    }

    [TestMethod]
    public async Task Development_DoesNotInheritJ09Rules()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(
            environmentName: "Development",
            databaseName: "IronDeveloper",
            localTestWorkspaceRoot: Path.Combine(Path.GetTempPath(), "IronDevWorkspaces"),
            localTestLogsRoot: Path.Combine(Path.GetTempPath(), "IronDevLogs"),
            dangerRealRepoWritesEnabled: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public void LocalTest_StillUsesC12Rules()
    {
        using var factory = BuildFactory(
            environmentName: "LocalTest",
            databaseName: "IronDeveloper_Prod",
            localTestWorkspaceRoot: Path.Combine(Path.GetTempPath(), "IronDevTestWorkspaces"),
            localTestLogsRoot: Path.Combine(Path.GetTempPath(), "IronDevTestLogs"));

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "LocalTest");
        StringAssert.Contains(exception.ToString(), "isolated test database");
    }

    [TestMethod]
    public void ProductionLike_StillUsesC13Rules()
    {
        using var factory = BuildFactory(
            environmentName: "Production",
            connectionString: "Server=YOUR_SERVER;Database=IronDeveloper_Main;Integrated Security=True;");

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "Production-like environment");
        StringAssert.Contains(exception.ToString(), "placeholder database server");
    }

    [TestMethod]
    public void UnknownEnvironment_RemainsProductionLike()
    {
        using var factory = BuildFactory(
            environmentName: "Preview",
            connectionString: "Server=YOUR_SERVER;Database=IronDeveloper_Main;Integrated Security=True;");

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "Production-like environment");
        StringAssert.Contains(exception.ToString(), "placeholder database server");
    }

    [TestMethod]
    public void StartupErrors_DoNotEchoConnectionStringSecretsOrFullPaths()
    {
        var hiddenPassword = "hidden-j09-password-value";
        var rawUserPath = string.Join(Path.DirectorySeparatorChar, "C:", "Users", "Robert", ".irondev", "logs");
        var connectionString = string.Join(
            ';',
            "Server=" + DefaultTestSqlServer,
            "Database=IronDeveloper_Test",
            "User Id=irondev",
            "Pwd" + "=" + hiddenPassword,
            "Encrypt=True");
        using var factory = BuildFactory(
            environmentName: "Test",
            connectionString: connectionString,
            localTestLogsRoot: rawUserPath);

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "Non-LocalTest test environment");
        AssertDoesNotContain(exception.ToString(), connectionString, "startup exception");
        AssertDoesNotContain(exception.ToString(), hiddenPassword, "startup exception");
        AssertDoesNotContain(exception.ToString(), rawUserPath, "startup exception");
        AssertDoesNotContain(exception.ToString(), "Robert", "startup exception");
        AssertDoesNotContain(exception.ToString(), TestJwtKey, "startup exception");
    }

    private static WebApplicationFactory<Program> BuildFactory(
        string environmentName = "Test",
        string? databaseName = "IronDeveloper_Test",
        string? connectionString = null,
        string? localTestWorkspaceRoot = null,
        string? localTestLogsRoot = null,
        string? disposableWorkspaceRoot = null,
        string? disposableEvidenceRoot = null,
        bool dangerRealRepoWritesEnabled = false,
        IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        connectionString ??= BuildConnectionString(databaseName);

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environmentName);
                builder.UseSetting("Jwt:Issuer", "irondev-api");
                builder.UseSetting("Jwt:Audience", "irondev-client");
                builder.UseSetting("ConnectionStrings:IronDeveloperDb", connectionString);
                builder.UseSetting("LocalTest:DangerRealRepoWritesEnabled", dangerRealRepoWritesEnabled.ToString());

                if (localTestWorkspaceRoot is not null)
                    builder.UseSetting("LocalTest:WorkspaceRoot", localTestWorkspaceRoot);

                if (localTestLogsRoot is not null)
                    builder.UseSetting("LocalTest:LogsRoot", localTestLogsRoot);

                if (disposableWorkspaceRoot is not null)
                    builder.UseSetting("DisposableBuild:WorkspaceRoot", disposableWorkspaceRoot);

                if (disposableEvidenceRoot is not null)
                    builder.UseSetting("DisposableBuild:EvidenceRoot", disposableEvidenceRoot);

                if (extraSettings is not null)
                {
                    foreach (var setting in extraSettings)
                        builder.UseSetting(setting.Key, setting.Value);
                }

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IStoredManualIndependentCriticAgentService>();
                    services.AddScoped<IStoredManualIndependentCriticAgentService, StartupOnlyStoredCriticService>();
                });

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:Key"] = TestJwtKey,
                        ["Weaviate:Enabled"] = "false"
                    });
                });
            });
    }

    private static string BuildConnectionString(string? databaseName)
    {
        var parts = new List<string>
        {
            "Server=" + DefaultTestSqlServer,
            "Integrated Security=True",
            "Encrypt=True",
            "TrustServerCertificate=True"
        };

        if (!string.IsNullOrWhiteSpace(databaseName))
            parts.Insert(1, $"Database={databaseName}");

        return string.Join(';', parts);
    }

    private static void AssertDoesNotContain(string source, string unexpected, string sourceName)
    {
        Assert.IsFalse(
            source.Contains(unexpected, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain '{unexpected}'.");
    }

    private sealed class StartupOnlyStoredCriticService : IStoredManualIndependentCriticAgentService
    {
        public StoredManualAgentExecutionResult<CriticReviewResult> ExecuteAndStore(
            ManualCriticReviewRequest request,
            ManualAgentExecutionSpecialisationSelection specialisation,
            DateTimeOffset executedAtUtc) =>
            new()
            {
                Status = StoredManualAgentExecutionStatus.Rejected,
                AgentRunId = "j09-startup-only-stub",
                AgentId = "independent-critic",
                SpecialisationId = specialisation.SpecialisationId,
                Issues =
                [
                    new StoredManualAgentExecutionIssue
                    {
                        Code = "J09_STARTUP_ONLY",
                        Severity = "Info",
                        Message = "J09 startup tests do not execute stored critic reviews."
                    }
                ]
            };
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

        public static TemporaryEnvironmentVariable Set(string name, string value) =>
            new(name, value);

        public void Dispose() =>
            Environment.SetEnvironmentVariable(_name, _originalValue);
    }
}
