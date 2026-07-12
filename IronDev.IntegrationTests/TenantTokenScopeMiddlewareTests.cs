using System.Security.Claims;
using System.Text.Json;
using IronDev.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class TenantTokenScopeMiddlewareTests
{
    [TestMethod]
    public async Task BaseToken_CannotReachProductData()
    {
        var context = Context(HttpMethods.Get, "/api/projects");
        var dispatched = false;

        await Middleware(() => dispatched = true).InvokeAsync(context);

        Assert.IsFalse(dispatched);
        Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        var refusal = await ResponseJsonAsync(context);
        Assert.AreEqual(TenantTokenScopeMiddleware.TenantSelectionRequiredReasonCode, refusal.GetProperty("reasonCode").GetString());
        Assert.IsFalse(refusal.GetProperty("allowed").GetBoolean());
    }

    [TestMethod]
    public async Task BaseToken_CanReachOnlyIdentityEnvironmentAndTenantSelectionEndpoints()
    {
        var allowed = new[]
        {
            (HttpMethods.Get, "/api/auth/me"),
            (HttpMethods.Post, "/api/auth/logout"),
            (HttpMethods.Get, "/api/tenants"),
            (HttpMethods.Post, "/api/tenants/select"),
            (HttpMethods.Get, "/api/environment")
        };

        foreach (var (method, path) in allowed)
        {
            var context = Context(method, path);
            var dispatched = false;
            await Middleware(() => dispatched = true).InvokeAsync(context);
            Assert.IsTrue(dispatched, $"Base token endpoint was blocked: {method} {path}");
        }
    }

    [TestMethod]
    public async Task TenantToken_MustMatchTenantRoute()
    {
        var matching = Context(HttpMethods.Post, "/api/tenants/7/users", tenantId: "7", routeTenantId: "7");
        var matchingDispatched = false;
        await Middleware(() => matchingDispatched = true).InvokeAsync(matching);
        Assert.IsTrue(matchingDispatched);

        var mismatched = Context(HttpMethods.Delete, "/api/tenants/8/users/4", tenantId: "7", routeTenantId: "8");
        var mismatchDispatched = false;
        await Middleware(() => mismatchDispatched = true).InvokeAsync(mismatched);

        Assert.IsFalse(mismatchDispatched);
        Assert.AreEqual(StatusCodes.Status404NotFound, mismatched.Response.StatusCode);
        var refusal = await ResponseJsonAsync(mismatched);
        Assert.AreEqual(TenantTokenScopeMiddleware.TenantRouteMismatchReasonCode, refusal.GetProperty("reasonCode").GetString());
    }

    [TestMethod]
    public async Task AnonymousRequest_IsLeftForAuthorizationMiddleware()
    {
        var context = Context(HttpMethods.Get, "/api/projects", authenticated: false);
        var dispatched = false;

        await Middleware(() => dispatched = true).InvokeAsync(context);

        Assert.IsTrue(dispatched);
    }

    [TestMethod]
    public void Pipeline_AttributesBeforeTenantAndProjectRefusals()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));

        AssertOrder(program, "app.UseAuthorization();", "app.UseMiddleware<UserMutationAttributionMiddleware>();");
        AssertOrder(program, "app.UseMiddleware<UserMutationAttributionMiddleware>();", "app.UseMiddleware<TenantTokenScopeMiddleware>();");
        AssertOrder(program, "app.UseMiddleware<TenantTokenScopeMiddleware>();", "app.UseMiddleware<ProjectMembershipMiddleware>();");
    }

    private static TenantTokenScopeMiddleware Middleware(Action onDispatch) =>
        new(_ =>
        {
            onDispatch();
            return Task.CompletedTask;
        });

    private static DefaultHttpContext Context(
        string method,
        string path,
        string? tenantId = null,
        string? routeTenantId = null,
        bool authenticated = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (routeTenantId is not null)
            context.Request.RouteValues["tenantId"] = routeTenantId;

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "42") };
        if (tenantId is not null)
            claims.Add(new Claim("tenant_id", tenantId));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null));
        return context;
    }

    private static async Task<JsonElement> ResponseJsonAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
    }

    private static void AssertOrder(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, firstIndex);
        Assert.IsGreaterThanOrEqualTo(0, secondIndex);
        Assert.IsTrue(firstIndex < secondIndex, $"{first} must appear before {second}.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
