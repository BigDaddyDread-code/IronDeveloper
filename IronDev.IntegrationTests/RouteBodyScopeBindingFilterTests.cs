using System.Text.Json;
using IronDev.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class RouteBodyScopeBindingFilterTests
{
    [TestMethod]
    public async Task ProjectMismatch_IsRefusedWithStableReasonCode()
    {
        var executed = false;
        var context = Context(HttpMethods.Post, new { projectId = "7" }, new ScopeRequest(8, 0));

        await ExecuteAsync(context, () => executed = true);

        Assert.IsFalse(executed);
        var result = context.Result as ObjectResult;
        Assert.IsNotNull(result);
        Assert.AreEqual(StatusCodes.Status400BadRequest, result.StatusCode);
        var response = result.Value as RouteBodyScopeMismatchResponse;
        Assert.IsNotNull(response);
        Assert.IsFalse(response.Allowed);
        Assert.AreEqual(RouteBodyScopeBindingFilter.ProjectMismatchReasonCode, response.ReasonCode);
        Assert.AreEqual("7", response.RouteValue);
        Assert.AreEqual("8", response.BodyValue);
    }

    [TestMethod]
    public async Task TenantGuidMismatch_InNestedJson_IsRefused()
    {
        var routeTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var bodyTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var body = JsonSerializer.SerializeToElement(new { request = new { tenantId = bodyTenant } });
        var context = Context(HttpMethods.Patch, new { tenantId = routeTenant }, body);

        await ExecuteAsync(context, () => Assert.Fail("Mismatched tenant scope reached the action."));

        var result = context.Result as ObjectResult;
        var response = result?.Value as RouteBodyScopeMismatchResponse;
        Assert.IsNotNull(response);
        Assert.AreEqual(RouteBodyScopeBindingFilter.TenantMismatchReasonCode, response.ReasonCode);
    }

    [TestMethod]
    public async Task MatchingAndOmittedScopes_ReachTheAction()
    {
        foreach (var request in new[] { new ScopeRequest(7, 3), new ScopeRequest(0, 0) })
        {
            var executed = false;
            var context = Context(HttpMethods.Put, new { projectId = 7, tenantId = 3 }, request);
            await ExecuteAsync(context, () => executed = true);
            Assert.IsTrue(executed);
            Assert.IsNull(context.Result);
        }
    }

    [TestMethod]
    public async Task ReadRequests_AreNotScopeMutationGates()
    {
        var executed = false;
        var context = Context(HttpMethods.Get, new { projectId = 7 }, new ScopeRequest(999, 0));
        await ExecuteAsync(context, () => executed = true);
        Assert.IsTrue(executed);
    }

    [TestMethod]
    public async Task CyclicNonScopeObjects_DoNotBreakModelBoundWrites()
    {
        var executed = false;
        var request = new CyclicRequest();
        request.Self = request;
        var context = Context(HttpMethods.Post, new { projectId = 7 }, request);
        await ExecuteAsync(context, () => executed = true);
        Assert.IsTrue(executed);
    }

    [TestMethod]
    public void Program_RegistersOneGlobalScopeFilter()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Program.cs"));
        StringAssert.Contains(program, "options.Filters.Add<RouteBodyScopeBindingFilter>()");
        Assert.AreEqual(1, Count(program, "RouteBodyScopeBindingFilter"));
    }

    private static ActionExecutingContext Context(string method, object routes, object body)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.TraceIdentifier = "correlation-cln-11";
        var routeData = new RouteData();
        foreach (var property in routes.GetType().GetProperties())
            routeData.Values[property.Name] = property.GetValue(routes);

        return new ActionExecutingContext(
            new ActionContext(http, routeData, new ActionDescriptor()),
            [],
            new Dictionary<string, object?> { ["request"] = body },
            new object());
    }

    private static Task ExecuteAsync(ActionExecutingContext context, Action executed)
    {
        var filter = new RouteBodyScopeBindingFilter();
        return filter.OnActionExecutionAsync(context, () =>
        {
            executed();
            return Task.FromResult(new ActionExecutedContext(context, context.Filters, context.Controller));
        });
    }

    private static int Count(string source, string value) =>
        (source.Length - source.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

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

    private sealed record ScopeRequest(int ProjectId, int TenantId);

    private sealed class CyclicRequest
    {
        public CyclicRequest? Self { get; set; }
    }
}
