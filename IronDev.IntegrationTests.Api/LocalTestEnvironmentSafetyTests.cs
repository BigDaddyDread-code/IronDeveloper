using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class LocalTestEnvironmentSafetyTests
{
    private const string TestJwtKey = "localtest-safety-jwt-key-for-c12-tests-32chars";
    private const string DefaultTestSqlServer = "(localdb)\\MSSQLLocalDB";

    [TestMethod]
    public async Task LocalTest_WithClearlyIsolatedResources_Starts()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("IronDeveloper")]
    [DataRow("IronDeveloper_Prod")]
    [DataRow("IronDeveloper_Live")]
    [DataRow("IronDeveloper_Accept")]
    [DataRow("IronDeveloper_Contest")]
    [DataRow("IronDeveloper_Latest")]
    [DataRow("IronDeveloper_Testament")]
    [DataRow("IronDeveloper_ProdTest")]
    [DataRow("ProductionTestBackup")]
    public void LocalTest_WithUnsafeDatabaseName_FailsStartup(string databaseName)
    {
        using var factory = BuildFactory(databaseName: databaseName);

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "LocalTest");
        StringAssert.Contains(exception.ToString(), "database");
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("IronDevWorkspaces")]
    [DataRow("IronDevContestWorkspaces")]
    [DataRow("IronDevLatestWorkspaces")]
    [DataRow("IronDevTestamentWorkspaces")]
    [DataRow("ProductionTestBackup")]
    public void LocalTest_WithUnsafeWorkspaceRoot_FailsStartup(string workspaceSegment)
    {
        using var factory = BuildFactory(workspaceRoot: Path.Combine(Path.GetTempPath(), workspaceSegment));

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "LocalTest");
        StringAssert.Contains(exception.ToString(), "workspace");
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("IronDevLogs")]
    [DataRow("IronDevContestLogs")]
    [DataRow("IronDevLatestLogs")]
    [DataRow("IronDevTestamentLogs")]
    [DataRow("ProductionTestBackup")]
    public void LocalTest_WithUnsafeLogsRoot_FailsStartup(string logsSegment)
    {
        using var factory = BuildFactory(logsRoot: Path.Combine(Path.GetTempPath(), logsSegment));

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "LocalTest");
        StringAssert.Contains(exception.ToString(), "logs");
    }

    [TestMethod]
    public void LocalTest_WithDangerousRealRepoWritesEnabled_FailsStartup()
    {
        using var factory = BuildFactory(dangerRealRepoWritesEnabled: true);

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "LocalTest");
        StringAssert.Contains(exception.ToString(), "dangerous real repo writes");
    }

    [TestMethod]
    public async Task Development_DoesNotApplyLocalTestOnlySafetyRules()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(
            environmentName: "Development",
            databaseName: "IronDeveloper",
            workspaceRoot: Path.Combine(Path.GetTempPath(), "IronDevWorkspaces"),
            logsRoot: Path.Combine(Path.GetTempPath(), "IronDevLogs"),
            dangerRealRepoWritesEnabled: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static WebApplicationFactory<Program> BuildFactory(
        string environmentName = "LocalTest",
        string databaseName = "IronDeveloper_Test",
        string? workspaceRoot = null,
        string? logsRoot = null,
        bool dangerRealRepoWritesEnabled = false)
    {
        workspaceRoot ??= Path.Combine(Path.GetTempPath(), "IronDevTestWorkspaces");
        logsRoot ??= Path.Combine(Path.GetTempPath(), "IronDevTestLogs");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environmentName);
                builder.UseSetting("Ai:Provider", "fake");
                builder.UseSetting("Jwt:Issuer", "irondev-api");
                builder.UseSetting("Jwt:Audience", "irondev-client");
                builder.UseSetting("ConnectionStrings:IronDeveloperDb", BuildConnectionString(databaseName));
                builder.UseSetting("LocalTest:WorkspaceRoot", workspaceRoot);
                builder.UseSetting("LocalTest:LogsRoot", logsRoot);
                builder.UseSetting("LocalTest:DangerRealRepoWritesEnabled", dangerRealRepoWritesEnabled.ToString());
                builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:1420");
                builder.UseSetting("Cors:AllowedOrigins:1", "http://127.0.0.1:1420");

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Weaviate:Enabled"] = "false"
                    });
                });
            });
    }

    private static string BuildConnectionString(string databaseName)
    {
        const string baseConnectionString = "Server=" + DefaultTestSqlServer + ";Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
        return string.IsNullOrWhiteSpace(databaseName)
            ? baseConnectionString
            : $"Server={DefaultTestSqlServer};Database={databaseName};Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
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
