using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using IronDev.Api.Auth;
using Microsoft.Extensions.Configuration;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC06JwtSecretConfigurationTests
{
    private const string OldPlaceholderKey = "irondev-super-secret-jwt-key-change-in-production-min32chars";
    private const string ValidConfigKey = "configured-jwt-signing-key-for-c06-tests-32chars";
    private const string ValidEnvironmentKey = "environment-jwt-signing-key-for-c06-tests-32chars";

    private static readonly string[] ScannedConfigFiles =
    [
        "IronDev.Api/appsettings.json",
        "IronDev.Api/appsettings.Development.json",
        "IronDev.Api/appsettings.LocalTest.json",
        "IronDev.IntegrationTests/appsettings.Test.json",
        "IronDev.IntegrationTests.Api/appsettings.Test.json"
    ];

    [TestMethod]
    public void BlockC06_CommittedApiAppsettings_DoesNotContainJwtSigningKey()
    {
        var path = Path.Combine(RepositoryRoot(), "IronDev.Api", "appsettings.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.IsTrue(document.RootElement.TryGetProperty("Jwt", out var jwt), "Committed API appsettings should keep non-secret JWT metadata.");
        Assert.IsFalse(jwt.TryGetProperty("Key", out _), "Committed API appsettings must not contain Jwt:Key.");
        Assert.AreEqual("irondev-api", jwt.GetProperty("Issuer").GetString());
        Assert.AreEqual("irondev-client", jwt.GetProperty("Audience").GetString());
        Assert.AreEqual("60", jwt.GetProperty("ExpiryMinutes").GetString());
    }

    [TestMethod]
    public void BlockC06_CommittedConfigFiles_DoNotContainOldPlaceholderOrJwtKey()
    {
        foreach (var relativePath in ScannedConfigFiles)
        {
            var absolutePath = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
                continue;

            var source = File.ReadAllText(absolutePath);
            AssertDoesNotContain(source, OldPlaceholderKey, relativePath);

            using var document = JsonDocument.Parse(source);
            if (document.RootElement.TryGetProperty("Jwt", out var jwt))
                Assert.IsFalse(jwt.TryGetProperty("Key", out _), $"{relativePath} must not contain Jwt:Key.");
        }
    }

    [TestMethod]
    public void BlockC06_CommittedConfigFiles_DoNotContainObviousJwtSigningSecrets()
    {
        foreach (var relativePath in ScannedConfigFiles)
        {
            var absolutePath = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
                continue;

            var source = File.ReadAllText(absolutePath);
            foreach (var marker in new[]
            {
                "super-secret",
                "signing-secret",
                "jwt-secret",
                "change-in-production",
                "min32chars",
                "\"Key\""
            })
            {
                AssertDoesNotContain(source, marker, relativePath);
            }
        }
    }

    [TestMethod]
    public void BlockC06_JwtSigningKeyResolver_FailsClosedWhenMissing()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            JwtSigningKeyResolver.Resolve(EmptyConfiguration(), _ => null));

        Assert.AreEqual(JwtSigningKeyResolver.MissingSigningKeyMessage, exception.Message);
        AssertDoesNotContain(exception.Message, "secret", "missing-key exception", allowBoundaryWord: true);
        AssertDoesNotContain(exception.Message, OldPlaceholderKey, "missing-key exception");
    }

    [TestMethod]
    public void BlockC06_JwtTokenService_FailsClosedWhenMissing()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new JwtTokenService(EmptyConfiguration()));

        Assert.AreEqual(JwtSigningKeyResolver.MissingSigningKeyMessage, exception.Message);
        AssertDoesNotContain(exception.Message, OldPlaceholderKey, "JwtTokenService missing-key exception");
    }

    [TestMethod]
    public void BlockC06_JwtSigningKeyResolver_AcceptsConfigurationProvidedKeyBeforeEnvironmentFallback()
    {
        var configuration = Configuration(("Jwt:Key", ValidConfigKey));

        var resolved = JwtSigningKeyResolver.Resolve(configuration, name =>
            string.Equals(name, "IRONDEV_JWT_KEY", StringComparison.Ordinal) ? ValidEnvironmentKey : null);

        Assert.AreEqual(ValidConfigKey, resolved);
    }

    [TestMethod]
    public void BlockC06_JwtSigningKeyResolver_AcceptsIronDevJwtKeyFallback()
    {
        var resolved = JwtSigningKeyResolver.Resolve(EmptyConfiguration(), name =>
            string.Equals(name, "IRONDEV_JWT_KEY", StringComparison.Ordinal) ? ValidEnvironmentKey : null);

        Assert.AreEqual(ValidEnvironmentKey, resolved);
    }

    [TestMethod]
    public void BlockC06_JwtSigningKeyResolver_RejectsShortKeyWithoutEchoingValue()
    {
        const string shortKey = "short-key";
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            JwtSigningKeyResolver.Resolve(Configuration(("Jwt:Key", shortKey)), _ => null));

        Assert.AreEqual(JwtSigningKeyResolver.ShortSigningKeyMessage, exception.Message);
        AssertDoesNotContain(exception.Message, shortKey, "short-key exception");
    }

    [TestMethod]
    public void BlockC06_JwtTokenService_KeepsIssuerAudienceAndExpiryBehavior()
    {
        var configuration = Configuration(
            ("Jwt:Key", ValidConfigKey),
            ("Jwt:Issuer", "irondev-api"),
            ("Jwt:Audience", "irondev-client"),
            ("Jwt:ExpiryMinutes", "60"));

        var token = new JwtTokenService(configuration)
            .CreateToken(7, "dev@irondev.local", "Iron Dev", tenantId: 11);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.AreEqual("irondev-api", jwt.Issuer);
        CollectionAssert.Contains(jwt.Audiences.ToArray(), "irondev-client");
        Assert.AreEqual("7", jwt.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.AreEqual("dev@irondev.local", jwt.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.AreEqual("Iron Dev", jwt.Claims.Single(claim => claim.Type == "display_name").Value);
        Assert.AreEqual("11", jwt.Claims.Single(claim => claim.Type == "tenant_id").Value);
        Assert.IsTrue(jwt.ValidTo > DateTime.UtcNow.AddMinutes(55));
        Assert.IsTrue(jwt.ValidTo <= DateTime.UtcNow.AddMinutes(61));
    }

    [TestMethod]
    public void BlockC06_ApiAuthSetup_UsesSharedSigningKeyResolver()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var tokenService = ReadRepositoryFile("IronDev.Api", "Auth", "JwtTokenService.cs");

        StringAssert.Contains(program, "JwtSigningKeyResolver.Resolve(builder.Configuration)");
        StringAssert.Contains(tokenService, "JwtSigningKeyResolver.Resolve(configuration)");
        AssertDoesNotContain(program, "Jwt:Key is not configured in appsettings", "Program.cs");
        AssertDoesNotContain(tokenService, "Jwt:Key is not configured.", "JwtTokenService.cs");
        AssertDoesNotContain(program, OldPlaceholderKey, "Program.cs");
        AssertDoesNotContain(tokenService, OldPlaceholderKey, "JwtTokenService.cs");
    }

    [TestMethod]
    public void BlockC06_ApiTestHost_UsesInMemoryTestOnlyKeyOverride()
    {
        var apiTestBase = ReadRepositoryFile("IronDev.IntegrationTests.Api", "ApiTestBase.cs");

        StringAssert.Contains(apiTestBase, "cfg.AddInMemoryCollection");
        StringAssert.Contains(apiTestBase, "\"Jwt:Key\"");
        StringAssert.Contains(apiTestBase, "irondev-test-only-jwt-key-not-from-committed-config-32chars");
        AssertDoesNotContain(apiTestBase, OldPlaceholderKey, "ApiTestBase.cs");
    }

    [TestMethod]
    public void BlockC06_DoesNotChangeEndpointTenantPolicyFrontendOrGeneratedClientSurfaces()
    {
        foreach (var relativePath in new[]
        {
            "IronDev.Api/Controllers",
            "IronDev.TauriShell/src",
            "IronDev.TauriShell/openapi",
            "IronDev.TauriShell/src/api/generated",
            "Database"
        })
        {
            Assert.IsTrue(Directory.Exists(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar))));
        }

        var resolverSource = ReadRepositoryFile("IronDev.Api", "Auth", "JwtTokenService.cs");

        foreach (var marker in new[]
        {
            "MapPost(",
            "[HttpPost",
            "TenantPolicy",
            "AuthorizationPolicy",
            "Generated",
            "SourceApply",
            "CommitExecutor",
            "PushExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor"
        })
        {
            AssertDoesNotContain(resolverSource, marker, "JWT resolver source");
        }
    }

    [TestMethod]
    public void BlockC06_BackendConfigurationInventory_RecordsSecretBoundary()
    {
        var inventory = ReadRepositoryFile("Docs", "BACKEND_CONFIGURATION_DEPENDENCY_INVENTORY.md");

        StringAssert.Contains(inventory, "`Jwt:Key` / `Jwt__Key` / `IRONDEV_JWT_KEY`");
        StringAssert.Contains(inventory, "must be supplied by environment/secret configuration rather than committed appsettings");
        AssertDoesNotContain(inventory, OldPlaceholderKey, "configuration inventory");
    }

    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection([]).Build();

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();

    private static string ReadRepositoryFile(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));

    private static void AssertDoesNotContain(
        string text,
        string marker,
        string sourceName,
        bool allowBoundaryWord = false)
    {
        if (allowBoundaryWord && string.Equals(marker, "secret", StringComparison.OrdinalIgnoreCase))
            return;

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
