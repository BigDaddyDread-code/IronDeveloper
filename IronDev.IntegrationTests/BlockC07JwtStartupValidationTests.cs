using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using IronDev.Api.Auth;
using Microsoft.Extensions.Configuration;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockC07JwtStartupValidationTests
{
    private const string OldPlaceholderKey = "irondev-super-secret-jwt-key-change-in-production-min32chars";
    private const string ValidConfigKey = "configured-jwt-startup-validation-key-for-c07-tests-32chars";
    private const string ValidEnvironmentKey = "environment-jwt-startup-validation-key-for-c07-tests-32chars";
    private const string OtherEnvironmentKey = "other-environment-jwt-startup-validation-key-for-c07-tests-32chars";

    private static readonly string[] ScannedConfigFiles =
    [
        "IronDev.Api/appsettings.json",
        "IronDev.Api/appsettings.Development.json",
        "IronDev.Api/appsettings.LocalTest.json",
        "IronDev.IntegrationTests/appsettings.Test.json",
        "IronDev.IntegrationTests.Api/appsettings.Test.json"
    ];

    [TestMethod]
    public void BlockC07_StartupValidation_AcceptsConfigurationBackedJwtKey()
    {
        var result = JwtStartupConfigurationValidator.Validate(
            Configuration(("Jwt:Key", ValidConfigKey)),
            _ => OtherEnvironmentKey);

        Assert.AreEqual(ValidConfigKey, result.Key);
        Assert.AreEqual(JwtSigningKeySource.Configuration, result.Source);
        Assert.AreEqual(JwtSigningKeyLengthClassification.Valid, result.LengthClassification);
        AssertMetadataDoesNotExposeKey(result, ValidConfigKey);
    }

    [TestMethod]
    public void BlockC07_StartupValidation_AcceptsIronDevJwtKeyEnvironmentFallback()
    {
        var result = JwtStartupConfigurationValidator.Validate(
            EmptyConfiguration(),
            name => string.Equals(name, "IRONDEV_JWT_KEY", StringComparison.Ordinal) ? ValidEnvironmentKey : null);

        Assert.AreEqual(ValidEnvironmentKey, result.Key);
        Assert.AreEqual(JwtSigningKeySource.IronDevJwtKeyEnvironment, result.Source);
        Assert.AreEqual(JwtSigningKeyLengthClassification.Valid, result.LengthClassification);
        AssertMetadataDoesNotExposeKey(result, ValidEnvironmentKey);
    }

    [TestMethod]
    public void BlockC07_StartupValidation_PrefersConfigurationKeyOverIronDevJwtKeyEnvironmentFallback()
    {
        var result = JwtStartupConfigurationValidator.Validate(
            Configuration(("Jwt:Key", ValidConfigKey)),
            name => string.Equals(name, "IRONDEV_JWT_KEY", StringComparison.Ordinal) ? ValidEnvironmentKey : null);

        Assert.AreEqual(ValidConfigKey, result.Key);
        Assert.AreEqual(JwtSigningKeySource.Configuration, result.Source);
    }

    [TestMethod]
    public void BlockC07_StartupValidation_FailsClosedWhenKeyIsMissing()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            JwtStartupConfigurationValidator.Validate(EmptyConfiguration(), _ => null));

        Assert.AreEqual(JwtStartupConfigurationValidator.StartupValidationFailedMessage, exception.Message);
        Assert.IsNotNull(exception.InnerException);
        Assert.AreEqual(JwtSigningKeyResolver.MissingSigningKeyMessage, exception.InnerException.Message);
        AssertDoesNotContain(exception.ToString(), OldPlaceholderKey, "missing-key exception");
    }

    [TestMethod]
    public void BlockC07_StartupValidation_FailsClosedWhenKeyIsShort()
    {
        const string shortKey = "short-key";
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            JwtStartupConfigurationValidator.Validate(Configuration(("Jwt:Key", shortKey)), _ => null));

        Assert.AreEqual(JwtStartupConfigurationValidator.StartupValidationFailedMessage, exception.Message);
        Assert.IsNotNull(exception.InnerException);
        Assert.AreEqual(JwtSigningKeyResolver.ShortSigningKeyMessage, exception.InnerException.Message);
        AssertDoesNotContain(exception.ToString(), shortKey, "short-key exception");
    }

    [TestMethod]
    public void BlockC07_StartupValidation_FailsClosedWhenKeyIsOldCommittedPlaceholder()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            JwtStartupConfigurationValidator.Validate(Configuration(("Jwt:Key", OldPlaceholderKey)), _ => null));

        Assert.AreEqual(JwtStartupConfigurationValidator.StartupValidationFailedMessage, exception.Message);
        Assert.IsNotNull(exception.InnerException);
        Assert.AreEqual(JwtSigningKeyResolver.PlaceholderSigningKeyMessage, exception.InnerException.Message);
        AssertDoesNotContain(exception.Message, OldPlaceholderKey, "placeholder exception");
        AssertDoesNotContain(exception.InnerException.Message, OldPlaceholderKey, "placeholder inner exception");
    }

    [TestMethod]
    public void BlockC07_StartupValidation_FailsClosedWhenJwtKeyComesFromCommittedAppsettings()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "appsettings.json"),
            $$"""
            {
              "Jwt": {
                "Key": "{{ValidConfigKey}}"
              }
            }
            """);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(temp.Path)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            JwtStartupConfigurationValidator.Validate(configuration, _ => ValidEnvironmentKey));

        Assert.AreEqual(JwtStartupConfigurationValidator.StartupValidationFailedMessage, exception.Message);
        Assert.IsNotNull(exception.InnerException);
        Assert.AreEqual(JwtSigningKeyResolver.ForbiddenCommittedConfigSigningKeyMessage, exception.InnerException.Message);
        AssertDoesNotContain(exception.ToString(), ValidConfigKey, "committed-appsettings exception");
    }

    [TestMethod]
    public void BlockC07_SuccessLogTemplate_DoesNotExposeKeyMaterial()
    {
        var message = JwtStartupConfigurationValidator.StartupValidationPassedLogMessage;

        StringAssert.Contains(message, "{JwtSigningKeySource}");
        AssertDoesNotContain(message, ValidConfigKey, "startup success log template");
        AssertDoesNotContain(message, "prefix", "startup success log template");
        AssertDoesNotContain(message, "suffix", "startup success log template");
        AssertDoesNotContain(message, "hash", "startup success log template");
    }

    [TestMethod]
    public void BlockC07_SourceMetadata_DoesNotContainResolvedKeyValue()
    {
        var result = JwtSigningKeyResolver.ResolveWithMetadata(
            Configuration(("Jwt:Key", ValidConfigKey)),
            _ => null);

        Assert.AreEqual(ValidConfigKey, result.Key);
        AssertMetadataDoesNotExposeKey(result, ValidConfigKey);
    }

    [TestMethod]
    public void BlockC07_Program_UsesStartupValidationPathBeforeBuild()
    {
        var program = ReadRepositoryFile("IronDev.Api", "Program.cs");
        var validateIndex = program.IndexOf(
            "JwtStartupConfigurationValidator.Validate(builder.Configuration)",
            StringComparison.Ordinal);
        var buildIndex = program.IndexOf("var app = builder.Build();", StringComparison.Ordinal);

        Assert.IsTrue(validateIndex >= 0, "Program.cs must call the JWT startup validation path.");
        Assert.IsTrue(buildIndex >= 0, "Program.cs must still build the API host explicitly.");
        Assert.IsTrue(validateIndex < buildIndex, "JWT startup validation must happen before builder.Build().");
        StringAssert.Contains(program, "JwtStartupConfigurationValidator.StartupValidationPassedLogMessage");
        StringAssert.Contains(program, "JwtSigningKeyResolver.Resolve(builder.Configuration)");
        AssertDoesNotContain(program, ValidConfigKey, "Program.cs");
        AssertDoesNotContain(program, ValidEnvironmentKey, "Program.cs");
    }

    [TestMethod]
    public void BlockC07_JwtTokenService_UsesSameResolverPathAsStartupValidation()
    {
        var tokenService = ReadRepositoryFile("IronDev.Api", "Auth", "JwtTokenService.cs");
        var resolver = ReadRepositoryFile("IronDev.Api", "Auth", "JwtSigningKeyResolver.cs");

        StringAssert.Contains(tokenService, "JwtSigningKeyResolver.Resolve(configuration)");
        StringAssert.Contains(resolver, "ResolveWithMetadata(configuration, environmentVariableReader)");
        StringAssert.Contains(resolver, "JwtStartupConfigurationValidator");
        AssertDoesNotContain(tokenService, OldPlaceholderKey, "JwtTokenService.cs");
    }

    [TestMethod]
    public void BlockC07_JwtTokenService_KeepsIssuerAudienceExpiryClaimsAndTenantBehavior()
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
    public void BlockC07_CommittedAppsettingsFiles_DoNotContainJwtKeyOrOldPlaceholder()
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
    public void BlockC07_DoesNotChangeEndpointTenantPolicyFrontendGeneratedClientSqlGovernanceReleaseOrDeploySurfaces()
    {
        var resolver = ReadRepositoryFile("IronDev.Api", "Auth", "JwtSigningKeyResolver.cs");

        foreach (var relativePath in new[]
        {
            "IronDev.Api/Controllers",
            "IronDev.TauriShell/src",
            "IronDev.TauriShell/openapi",
            "IronDev.TauriShell/src/api/generated",
            "Database",
            "IronDev.Core/Governance"
        })
        {
            Assert.IsTrue(Directory.Exists(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar))));
        }

        foreach (var marker in new[]
        {
            "MapPost(",
            "[HttpPost",
            "TenantPolicy",
            "AuthorizationPolicy",
            "OpenApi",
            "Generated",
            "SqlConnection",
            "SourceApply",
            "CommitExecutor",
            "PushExecutor",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "PromoteMemory",
            "ContinueWorkflow"
        })
        {
            AssertDoesNotContain(resolver, marker, "C07 JWT resolver source");
        }
    }

    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection([]).Build();

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(item => item.Key, item => (string?)item.Value))
            .Build();

    private static void AssertMetadataDoesNotExposeKey(JwtSigningKeyResolution result, string key)
    {
        AssertDoesNotContain(result.Source.ToString(), key, "source metadata");
        AssertDoesNotContain(result.LengthClassification.ToString(), key, "length metadata");
        AssertDoesNotContain(result.ToString(), key, "resolution metadata");
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "IronDev-C07-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
