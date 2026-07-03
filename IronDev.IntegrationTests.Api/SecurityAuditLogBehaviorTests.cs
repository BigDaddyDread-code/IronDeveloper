using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
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
public sealed class SecurityAuditLogBehaviorTests
{
    private const string AdminEmail = "admin@irondev.local";
    private const string AdminPassword = "password123";
    private const int AssignedTenantId = 1;
    private const int UnassignedTenantId = 2;
    private const string TestJwtKey = "irondev-c15-audit-test-key-not-from-config-32chars";

    [TestMethod]
    public async Task SuccessfulLogin_AppendsAuthLoginSucceeded()
    {
        var auditLog = new SecurityAuditTestLog();
        using var factory = BuildFactory(auditLog);
        using var client = factory.CreateClient();
        var before = auditLog.Snapshot().Count;

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();
        var auditEvent = EventsAfter(auditLog, before).Last(item => item.EventType == SecurityAuditEventType.AuthLoginSucceeded);

        Assert.AreEqual(SecurityAuditOutcome.Succeeded, auditEvent.Outcome);
        Assert.AreEqual("1", auditEvent.ActorUserId);
        Assert.AreEqual("1", auditEvent.TargetUserId);
        Assert.AreEqual("CredentialsAccepted", auditEvent.ReasonCode);
        StringAssert.StartsWith(auditEvent.ActorEmailHash, "sha256:");
        AssertDoesNotContain(Serialize(auditEvent), token!, "login success audit record");
        AssertDoesNotContain(Serialize(auditEvent), AdminPassword, "login success audit record");
    }

    [TestMethod]
    public async Task FailedLogin_AppendsAuthLoginFailedWithoutCredentialMaterial()
    {
        var auditLog = new SecurityAuditTestLog();
        using var factory = BuildFactory(auditLog);
        using var client = factory.CreateClient();
        var before = auditLog.Snapshot().Count;
        var wrongCredential = "audit-denied-value";

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = wrongCredential });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var auditEvent = EventsAfter(auditLog, before).Last(item => item.EventType == SecurityAuditEventType.AuthLoginFailed);

        Assert.AreEqual(SecurityAuditOutcome.Denied, auditEvent.Outcome);
        Assert.AreEqual("InvalidCredentials", auditEvent.ReasonCode);
        Assert.AreEqual(string.Empty, auditEvent.ActorUserId);
        StringAssert.StartsWith(auditEvent.ActorEmailHash, "sha256:");
        AssertDoesNotContain(Serialize(auditEvent), wrongCredential, "login failure audit record");
        AssertDoesNotContain(Serialize(auditEvent), AdminEmail, "login failure audit record");
    }

    [TestMethod]
    public async Task Logout_AppendsAuthLogoutRequested()
    {
        var auditLog = new SecurityAuditTestLog();
        using var factory = BuildFactory(auditLog);
        using var client = factory.CreateClient();
        var baseToken = await LoginAsync(client);
        var tenantToken = await SelectTenantAsync(client, baseToken);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tenantToken);
        var before = auditLog.Snapshot().Count;

        var response = await client.PostAsync("/api/auth/logout", null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var auditEvent = EventsAfter(auditLog, before).Last(item => item.EventType == SecurityAuditEventType.AuthLogoutRequested);

        Assert.AreEqual(SecurityAuditOutcome.Succeeded, auditEvent.Outcome);
        Assert.AreEqual("1", auditEvent.ActorUserId);
        Assert.AreEqual("1", auditEvent.TargetUserId);
        Assert.AreEqual(AssignedTenantId.ToString(), auditEvent.TenantId);
        Assert.AreEqual("StatelessLogoutRequested", auditEvent.ReasonCode);
        Assert.IsTrue(auditEvent.Authenticated);
        AssertDoesNotContain(Serialize(auditEvent), tenantToken, "logout audit record");
    }

    [TestMethod]
    public async Task TenantSelectionSuccess_AppendsTenantSelectionSucceededWithoutIssuedToken()
    {
        var auditLog = new SecurityAuditTestLog();
        using var factory = BuildFactory(auditLog);
        using var client = factory.CreateClient();
        var baseToken = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", baseToken);
        var before = auditLog.Snapshot().Count;

        var response = await client.PostAsJsonAsync("/api/tenants/select", new { tenantId = AssignedTenantId });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var issuedToken = body.GetProperty("token").GetString();
        var auditEvent = EventsAfter(auditLog, before).Last(item => item.EventType == SecurityAuditEventType.TenantSelectionSucceeded);

        Assert.AreEqual(SecurityAuditOutcome.Succeeded, auditEvent.Outcome);
        Assert.AreEqual("1", auditEvent.ActorUserId);
        Assert.AreEqual("1", auditEvent.TargetUserId);
        Assert.AreEqual(AssignedTenantId.ToString(), auditEvent.TenantId);
        Assert.AreEqual(AssignedTenantId.ToString(), auditEvent.TargetTenantId);
        Assert.AreEqual("TenantMembershipAccepted", auditEvent.ReasonCode);
        AssertDoesNotContain(Serialize(auditEvent), issuedToken!, "tenant selection audit record");
        AssertDoesNotContain(Serialize(auditEvent), baseToken, "tenant selection audit record");
    }

    [TestMethod]
    public async Task TenantSelectionDenied_AppendsTenantSelectionDeniedAndDoesNotIssueToken()
    {
        var auditLog = new SecurityAuditTestLog();
        using var factory = BuildFactory(auditLog);
        using var client = factory.CreateClient();
        var baseToken = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", baseToken);
        var before = auditLog.Snapshot().Count;

        var response = await client.PostAsJsonAsync("/api/tenants/select", new { tenantId = UnassignedTenantId });
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        AssertDoesNotContain(body, "token", "tenant selection denial response");
        var auditEvent = EventsAfter(auditLog, before).Last(item => item.EventType == SecurityAuditEventType.TenantSelectionDenied);

        Assert.AreEqual(SecurityAuditOutcome.Denied, auditEvent.Outcome);
        Assert.AreEqual("1", auditEvent.ActorUserId);
        Assert.AreEqual(UnassignedTenantId.ToString(), auditEvent.TargetTenantId);
        Assert.AreEqual("UserNotTenantMember", auditEvent.ReasonCode);
        AssertDoesNotContain(Serialize(auditEvent), baseToken, "tenant selection denial audit record");
    }

    [TestMethod]
    public async Task SecurityAuditRecords_DoNotContainSensitiveMaterial()
    {
        var auditLog = new SecurityAuditTestLog();
        using var factory = BuildFactory(auditLog);
        using var client = factory.CreateClient();
        var baseToken = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", baseToken);
        await client.PostAsJsonAsync("/api/tenants/select", new { tenantId = UnassignedTenantId });
        var auditText = Serialize(auditLog.Snapshot());

        foreach (var forbidden in new[]
        {
            AdminPassword,
            baseToken,
            "Bearer ",
            "Authorization",
            "JwtKey",
            "ApiKey",
            "ConnectionString",
            "RequestBody",
            "raw request body"
        })
        {
            AssertDoesNotContain(auditText, forbidden, "security audit records");
        }
    }

    [TestMethod]
    public async Task AuditAppendFailure_PreventsSuccessfulLoginResponse()
    {
        using var factory = BuildFactory(new FailingSecurityAuditLog(SecurityAuditEventType.AuthLoginSucceeded));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertDoesNotContain(body, "token", "failed login-audit response");
    }

    [TestMethod]
    public async Task AuditAppendFailure_PreventsSuccessfulTenantSelectionResponse()
    {
        using var factory = BuildFactory(new FailingSecurityAuditLog(SecurityAuditEventType.TenantSelectionSucceeded));
        using var client = factory.CreateClient();
        var baseToken = await LoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", baseToken);

        var response = await client.PostAsJsonAsync("/api/tenants/select", new { tenantId = AssignedTenantId });
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertDoesNotContain(body, "token", "failed tenant-audit response");
    }

    [TestMethod]
    public async Task ExistingC14RateLimitBehavior_RemainsIntact()
    {
        using var factory = BuildFactory(new SecurityAuditTestLog(), authLoginPermitLimit: 1, sensitivePermitLimit: 2);
        using var client = factory.CreateClient();

        var firstLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        var secondLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });

        Assert.AreEqual(HttpStatusCode.OK, firstLogin.StatusCode);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, secondLogin.StatusCode);
    }

    [TestMethod]
    public async Task HealthAndEnvironmentBoundaries_RemainIntact()
    {
        using var factory = BuildFactory(new SecurityAuditTestLog());
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        var environment = await client.GetAsync("/api/environment");

        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, environment.StatusCode);
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private static async Task<string> SelectTenantAsync(HttpClient client, string baseToken)
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", baseToken);
        var response = await client.PostAsJsonAsync("/api/tenants/select", new { tenantId = AssignedTenantId });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private static IReadOnlyList<SecurityAuditEvent> EventsAfter(ISecurityAuditLog auditLog, int count) =>
        auditLog.Snapshot().Skip(count).ToArray();

    private static string Serialize(object value) =>
        JsonSerializer.Serialize(value);

    private static WebApplicationFactory<Program> BuildFactory(
        ISecurityAuditLog auditLog,
        int authLoginPermitLimit = 100,
        int sensitivePermitLimit = 100)
    {
        Environment.SetEnvironmentVariable("IRONDEV_JWT_KEY", TestJwtKey);

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.UseSetting("Jwt:Issuer", "irondev-api");
                builder.UseSetting("Jwt:Audience", "irondev-client");
                builder.UseSetting("ConnectionStrings:IronDeveloperDb", "Server=DESKTOP-KFA0H13;Database=IronDeveloper_Test;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;");
                builder.UseSetting("LocalTest:WorkspaceRoot", Path.Combine(Path.GetTempPath(), "IronDevTestWorkspaces"));
                builder.UseSetting("LocalTest:LogsRoot", Path.Combine(Path.GetTempPath(), "IronDevTestLogs"));
                builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:1420");
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
                    services.RemoveAll<ISecurityAuditLog>();
                    services.AddSingleton<IUserService, FakeUserService>();
                    services.AddSingleton(auditLog);
                });
            });
    }

    private static void AssertDoesNotContain(string source, string unexpected, string sourceName)
    {
        Assert.IsFalse(
            source.Contains(unexpected, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain '{unexpected}'.");
    }

    private sealed class SecurityAuditTestLog : ISecurityAuditLog
    {
        private readonly List<SecurityAuditEvent> _events = [];

        public Task AppendAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            _events.Add(auditEvent);
            return Task.CompletedTask;
        }

        public IReadOnlyList<SecurityAuditEvent> Snapshot() => _events.ToArray();
    }

    private sealed class FailingSecurityAuditLog : ISecurityAuditLog
    {
        private readonly SecurityAuditEventType _failingType;
        private readonly List<SecurityAuditEvent> _events = [];

        public FailingSecurityAuditLog(SecurityAuditEventType failingType) =>
            _failingType = failingType;

        public Task AppendAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (auditEvent.EventType == _failingType)
                throw new InvalidOperationException("Injected security audit append failure.");

            _events.Add(auditEvent);
            return Task.CompletedTask;
        }

        public IReadOnlyList<SecurityAuditEvent> Snapshot() => _events.ToArray();
    }

    private sealed class FakeUserService : IUserService
    {
        public Task<User?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default) =>
            Task.FromResult<User?>(
                string.Equals(email, AdminEmail, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(password, AdminPassword, StringComparison.Ordinal)
                    ? CreateUser()
                    : null);

        public Task<User?> GetByIdAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult<User?>(CreateUser(userId));

        public Task<IReadOnlyList<TenantDto>> GetUserTenantsAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantDto>>([new TenantDto(AssignedTenantId, "Default Tenant", "default")]);

        public Task<IReadOnlyList<TenantDto>> GetAllActiveTenantsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantDto>>([new TenantDto(AssignedTenantId, "Default Tenant", "default")]);

        public Task<bool> IsMemberOfTenantAsync(int userId, int tenantId, CancellationToken ct = default) =>
            Task.FromResult(tenantId == AssignedTenantId);

        public Task<string?> GetTenantRoleAsync(int userId, int tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>(tenantId == AssignedTenantId ? TenantUserRoles.Owner : null);

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

        private static User CreateUser(int userId = 1) =>
            new()
            {
                Id = userId,
                Email = AdminEmail,
                DisplayName = "Audit Test User",
                IsActive = true
            };
    }
}
