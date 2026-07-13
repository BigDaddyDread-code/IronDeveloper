using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IronDev.Core.Auth;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class SensitiveApiRateLimitTests
{
    private const string TestJwtKey = "irondev-c14-rate-limit-test-key-not-from-config-32chars";
    private const string DefaultTestConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=IronDeveloper_Test;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
    private const string AllowedOrigin = "http://localhost:1420";

    [TestMethod]
    public async Task Login_AllowsNormalRequestBeforeLimit()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(authLoginPermitLimit: 2);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", LoginBody());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_ReturnsTooManyRequestsAfterThreshold()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(authLoginPermitLimit: 2);
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/login", LoginBody());
        await client.PostAsJsonAsync("/api/auth/login", LoginBody());
        var response = await client.PostAsJsonAsync("/api/auth/login", LoginBody());

        Assert.AreEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [TestMethod]
    public async Task ProtectedSensitiveEndpoint_AnonymousRequestReturnsUnauthorized()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(sensitivePermitLimit: 2);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/environment");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedSensitiveEndpoint_ReturnsTooManyRequestsAfterThresholdWithoutEchoingAuthMaterial()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(sensitivePermitLimit: 2);
        using var client = factory.CreateClient();
        var token = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        await client.GetAsync("/api/environment");
        await client.GetAsync("/api/environment");
        var response = await client.GetAsync("/api/environment");
        var body = await response.Content.ReadAsStringAsync();
        var headerValue = client.DefaultRequestHeaders.Authorization.ToString();

        Assert.AreEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        AssertDoesNotContain(body, token, "rate-limit response");
        AssertDoesNotContain(body, headerValue, "rate-limit response");
        AssertDoesNotContain(string.Join(Environment.NewLine, response.Headers.Select(header => $"{header.Key}:{string.Join(",", header.Value)}")), token, "rate-limit response headers");
    }

    [TestMethod]
    public async Task Health_RemainsAnonymousAndNotSensitiveRateLimited()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(sensitivePermitLimit: 2);
        using var client = factory.CreateClient();

        var first = await client.GetAsync("/health");
        var second = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
    }

    [TestMethod]
    public async Task Environment_IsProtectedAndSensitiveRateLimitedForAuthenticatedCallers()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(sensitivePermitLimit: 2);
        using var client = factory.CreateClient();
        var anonymous = await client.GetAsync("/api/environment");
        var token = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var first = await client.GetAsync("/api/environment");
        var second = await client.GetAsync("/api/environment");

        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [TestMethod]
    public async Task CorsPreflight_RemainsAllowedForSensitiveEndpoint()
    {
        using var jwtKey = TemporaryEnvironmentVariable.Set("IRONDEV_JWT_KEY", TestJwtKey);
        using var factory = BuildFactory(sensitivePermitLimit: 1);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/environment");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization, Content-Type");

        var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.IsTrue(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.AreEqual(AllowedOrigin, origins.Single());
    }

    private static WebApplicationFactory<Program> BuildFactory(
        int authLoginPermitLimit = 5,
        int sensitivePermitLimit = 60)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.UseSetting("Ai:Provider", "fake");
                builder.UseSetting("Jwt:Issuer", "irondev-api");
                builder.UseSetting("Jwt:Audience", "irondev-client");
                builder.UseSetting("ConnectionStrings:IronDeveloperDb", TestConnectionString());
                builder.UseSetting("LocalTest:WorkspaceRoot", Path.Combine(Path.GetTempPath(), "IronDevTestWorkspaces"));
                builder.UseSetting("LocalTest:LogsRoot", Path.Combine(Path.GetTempPath(), "IronDevTestLogs"));
                builder.UseSetting("Cors:AllowedOrigins:0", AllowedOrigin);
                builder.UseSetting("RateLimiting:AuthLogin:PermitLimit", authLoginPermitLimit.ToString());
                builder.UseSetting("RateLimiting:SensitiveApi:PermitLimit", sensitivePermitLimit.ToString());

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:Key"] = TestJwtKey
                    });
                });

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IUserService>();
                    services.AddSingleton<IUserService, FakeUserService>();
                });
            });
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", LoginBody());
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private static object LoginBody() =>
        new
        {
            email = "admin@irondev.local",
            password = "rate-limit-test-password"
        };

    private static string TestConnectionString()
    {
        var overrideValue = Environment.GetEnvironmentVariable("ConnectionStrings__IronDeveloperDb");
        return string.IsNullOrWhiteSpace(overrideValue)
            ? DefaultTestConnectionString
            : overrideValue;
    }

    private static void AssertDoesNotContain(string source, string unexpected, string sourceName)
    {
        Assert.IsFalse(
            source.Contains(unexpected, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain sensitive auth material.");
    }

    private sealed class FakeUserService : IUserService
    {
        public Task<User?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default) =>
            Task.FromResult<User?>(new User
            {
                Id = 1,
                Email = email,
                DisplayName = "Rate Limit Test User",
                IsActive = true
            });

        public Task<User?> GetByIdAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult<User?>(new User
            {
                Id = userId,
                Email = "admin@irondev.local",
                DisplayName = "Rate Limit Test User",
                IsActive = true
            });

        public Task<IReadOnlyList<TenantDto>> GetUserTenantsAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantDto>>([new TenantDto(1, "Default Tenant", "default")]);

        public Task<IReadOnlyList<TenantDto>> GetAllActiveTenantsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantDto>>([new TenantDto(1, "Default Tenant", "default")]);

        public Task<bool> IsMemberOfTenantAsync(int userId, int tenantId, CancellationToken ct = default) =>
            Task.FromResult(tenantId == 1);

        public Task<string?> GetTenantRoleAsync(int userId, int tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>(tenantId == 1 ? TenantUserRoles.Owner : null);

        public Task<IReadOnlyList<TenantUserRecord>> GetTenantUsersAsync(int tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantUserRecord>>([]);

        public Task<CreateTenantUserResult> CreateTenantUserAsync(int tenantId, string email, string displayName, string? password, string role, CancellationToken ct = default) =>
            Task.FromResult(new CreateTenantUserResult(TenantUserMutationStatus.Succeeded, new TenantUserRecord
            {
                Id = 99,
                Email = email,
                DisplayName = displayName,
                IsActive = true,
                Role = role
            }));

        public Task<TenantUserMutationResult> SetTenantUserRoleAsync(int tenantId, int userId, string role, CancellationToken ct = default) =>
            Task.FromResult(TenantUserMutationResult.Succeeded);

        public Task<TenantUserMutationResult> RemoveTenantUserAsync(int tenantId, int userId, CancellationToken ct = default) =>
            Task.FromResult(TenantUserMutationResult.Succeeded);
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
