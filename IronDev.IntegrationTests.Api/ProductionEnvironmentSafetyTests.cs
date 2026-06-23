using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class ProductionEnvironmentSafetyTests
{
    private const string TestJwtKey = "production-safety-jwt-key-for-c13-tests-32chars";
    private const string SafeProductionConnectionString =
        "Server=tcp:prod-sql.irondev.example,1433;Database=IronDeveloper_Main;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;";

    [TestMethod]
    public async Task ProductionLike_WithSafeRemoteDatabaseConfig_Starts()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("Production")]
    [DataRow("Staging")]
    [DataRow("UAT")]
    [DataRow("Demo")]
    [DataRow("Accept")]
    [DataRow("Live")]
    [DataRow("CustomEnvironment")]
    public void ProductionLike_EnvironmentNames_ApplySafetyValidation(string environmentName)
    {
        using var factory = BuildFactory(
            environmentName: environmentName,
            connectionString: "Server=YOUR_SERVER;Database=IronDeveloper_Main;Integrated Security=True;");

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "Production-like environment");
        StringAssert.Contains(exception.ToString(), "placeholder database server");
    }

    [TestMethod]
    public void ProductionLike_WithMissingConnectionString_FailsStartup()
    {
        using var factory = BuildFactory(connectionString: string.Empty);

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "database connection string");
    }

    [TestMethod]
    public void ProductionLike_WithPlaceholderServer_FailsStartup()
    {
        using var factory = BuildFactory(
            connectionString: "Server=YOUR_SERVER;Database=IronDeveloper_Main;Integrated Security=True;");

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "placeholder database server");
    }

    [TestMethod]
    public void ProductionLike_WithMissingDatabaseName_FailsStartup()
    {
        using var factory = BuildFactory(
            connectionString: "Server=tcp:prod-sql.irondev.example,1433;Integrated Security=True;Encrypt=True;");

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "database name");
    }

    [DataTestMethod]
    [DataRow("IronDeveloper_Test")]
    [DataRow("IronDeveloper_LocalTest")]
    [DataRow("IronDeveloper_Dev")]
    [DataRow("IronDeveloper_Development")]
    [DataRow("IronDeveloper_Local")]
    [DataRow("IronDeveloper_Scratch")]
    [DataRow("IronDeveloper_Temp")]
    public void ProductionLike_WithTestLikeDatabaseName_FailsStartup(string databaseName)
    {
        using var factory = BuildFactory(
            connectionString: $"Server=tcp:prod-sql.irondev.example,1433;Database={databaseName};Integrated Security=True;Encrypt=True;");

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "test-like database names");
    }

    [DataTestMethod]
    [DataRow("localhost")]
    [DataRow("127.0.0.1")]
    [DataRow("(localdb)\\MSSQLLocalDB")]
    [DataRow("DESKTOP-KFA0H13")]
    [DataRow(".\\SQLEXPRESS")]
    public void ProductionLike_WithLocalDatabaseServer_FailsStartup(string server)
    {
        using var factory = BuildFactory(
            connectionString: $"Server={server};Database=IronDeveloper_Main;Integrated Security=True;Encrypt=True;");

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "local database server");
    }

    [TestMethod]
    public void ProductionLike_WithPasswordBearingConnectionString_FailsStartupWithoutEchoingSecrets()
    {
        var hiddenValue = "prod-hidden-value-c13";
        var connectionString = string.Join(
            ';',
            "Server=tcp:prod-sql.irondev.example,1433",
            "Database=IronDeveloper_Main",
            "User Id=irondev",
            "Pwd" + "=" + hiddenValue,
            "Encrypt=True");
        using var factory = BuildFactory(connectionString: connectionString);

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "password-bearing database configuration");
        AssertDoesNotContain(exception.ToString(), connectionString, "startup exception");
        AssertDoesNotContain(exception.ToString(), hiddenValue, "startup exception");
        AssertDoesNotContain(exception.ToString(), TestJwtKey, "startup exception");
    }

    [TestMethod]
    public void ProductionLike_WithLocalTestWorkspaceRootSetToUnsafePath_FailsStartup()
    {
        using var factory = BuildFactory(
            localTestWorkspaceRoot: Path.Combine(Path.GetTempPath(), "IronDevLocalTestWorkspaces"));

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "workspace roots");
        AssertDoesNotContain(exception.ToString(), Path.GetTempPath(), "startup exception");
    }

    [TestMethod]
    public void ProductionLike_WithLocalTestLogsRootSetToUnsafePath_FailsStartup()
    {
        using var factory = BuildFactory(
            localTestLogsRoot: Path.Combine(Path.GetTempPath(), "IronDevLocalTestLogs"));

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "logs roots");
        AssertDoesNotContain(exception.ToString(), Path.GetTempPath(), "startup exception");
    }

    [TestMethod]
    public void ProductionLike_WithDisposableBuildRootsSetToUnsafePaths_FailsStartup()
    {
        using var factory = BuildFactory(
            disposableWorkspaceRoot: Path.Combine(Path.GetTempPath(), "IronDevDevWorkspaces"),
            disposableEvidenceRoot: "Z:\\ProductionEvidence");

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "disposable workspace roots");
    }

    [TestMethod]
    public void ProductionLike_WithDangerousRealRepoWritesEnabled_FailsStartup()
    {
        using var factory = BuildFactory(dangerRealRepoWritesEnabled: true);

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "dangerous real repo writes");
    }

    [DataTestMethod]
    [DataRow("Development")]
    [DataRow("Test")]
    public async Task NonProductionLike_DoesNotInheritProductionOnlyRules(string environmentName)
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(
            environmentName: environmentName,
            connectionString: "Server=localhost;Database=IronDeveloper_Test;Integrated Security=True;Encrypt=True;",
            localTestWorkspaceRoot: Path.Combine(Path.GetTempPath(), "IronDevTestWorkspaces"),
            localTestLogsRoot: Path.Combine(Path.GetTempPath(), "IronDevTestLogs"),
            dangerRealRepoWritesEnabled: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public void LocalTest_StillUsesLocalTestSafetyRules()
    {
        using var factory = BuildFactory(
            environmentName: "LocalTest",
            connectionString: "Server=localhost;Database=IronDeveloper_Prod;Integrated Security=True;Encrypt=True;",
            localTestWorkspaceRoot: Path.Combine(Path.GetTempPath(), "IronDevTestWorkspaces"),
            localTestLogsRoot: Path.Combine(Path.GetTempPath(), "IronDevTestLogs"));

        var exception = Assert.ThrowsException<InvalidOperationException>(() => factory.CreateClient());

        StringAssert.Contains(exception.ToString(), "LocalTest");
        StringAssert.Contains(exception.ToString(), "isolated test database");
    }

    private static WebApplicationFactory<Program> BuildFactory(
        string environmentName = "Production",
        string? connectionString = SafeProductionConnectionString,
        string? localTestWorkspaceRoot = null,
        string? localTestLogsRoot = null,
        string? disposableWorkspaceRoot = null,
        string? disposableEvidenceRoot = null,
        bool dangerRealRepoWritesEnabled = false)
    {
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

    private static void AssertDoesNotContain(string source, string unexpected, string sourceName)
    {
        Assert.IsFalse(
            source.Contains(unexpected, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain '{unexpected}'.");
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
