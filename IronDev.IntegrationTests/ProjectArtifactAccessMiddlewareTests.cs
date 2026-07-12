using System.Security.Claims;
using System.Text.Json;
using IronDev.Api.Middleware;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using Microsoft.AspNetCore.Http;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ProjectArtifactAccessMiddlewareTests
{
    [TestMethod]
    public async Task AccessibleArtifact_ReachesControllerDispatch()
    {
        var context = Context("documentId", "101", ProjectArtifactKind.Document);
        var dispatched = false;

        await Middleware(() => dispatched = true).InvokeAsync(
            context,
            new StubArtifactAccessService(true),
            new StubTenantContext(7));

        Assert.IsTrue(dispatched);
    }

    [TestMethod]
    public async Task MissingOrCrossProjectArtifact_FailsClosedWithoutDispatch()
    {
        var context = Context("ticketId", "202", ProjectArtifactKind.Ticket);
        var dispatched = false;

        await Middleware(() => dispatched = true).InvokeAsync(
            context,
            new StubArtifactAccessService(false),
            new StubTenantContext(7));

        Assert.IsFalse(dispatched);
        Assert.AreEqual(StatusCodes.Status404NotFound, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var refusal = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        Assert.AreEqual(
            ProjectArtifactAccessMiddleware.ArtifactScopeNotFoundReasonCode,
            refusal.GetProperty("reasonCode").GetString());
        Assert.IsFalse(refusal.GetProperty("allowed").GetBoolean());
    }

    [TestMethod]
    public void CompatibilityEndpoints_DeclareArtifactAccessRequirements()
    {
        var root = FindRepositoryRoot();
        var documents = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "DocumentsController.cs"));
        var tickets = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "TicketsController.cs"));
        var memory = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "MemoryController.cs"));
        var runs = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "RunsController.cs"));
        var runReports = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "RunReportsController.cs"));
        var service = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Services", "ProjectCollaborationService.cs"));
        var program = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));

        StringAssert.Contains(documents, "RequireProjectArtifactAccess(ProjectArtifactKind.Document, \"documentId\")");
        StringAssert.Contains(documents, "RequireProjectArtifactAccess(ProjectArtifactKind.DocumentVersion, \"versionId\")");
        StringAssert.Contains(tickets, "RequireProjectArtifactAccess(ProjectArtifactKind.Ticket, \"ticketId\")");
        StringAssert.Contains(memory, "RequireProjectArtifactAccess(ProjectArtifactKind.MemoryDocument, \"documentId\")");
        StringAssert.Contains(memory, "RequireProjectArtifactAccess(ProjectArtifactKind.ImplementationPlan, \"planId\")");
        StringAssert.Contains(runs, "RequireProjectArtifactAccess(ProjectArtifactKind.Run, \"runId\")");
        StringAssert.Contains(runReports, "RequireProjectArtifactAccess(ProjectArtifactKind.RunReport, \"runId\")");
        StringAssert.Contains(runReports, "ProjectArtifactKind.RunReport, report.RunId");
        StringAssert.Contains(service, "INNER JOIN dbo.Projects p ON p.Id=pm.ProjectId AND p.TenantId=pm.TenantId");
        StringAssert.Contains(service, "INNER JOIN dbo.ProjectMembers pm ON pm.ProjectId=p.Id AND pm.TenantId=p.TenantId");
        AssertOrder(program, "app.UseMiddleware<TenantTokenScopeMiddleware>();", "app.UseMiddleware<ProjectArtifactAccessMiddleware>();");
        AssertOrder(program, "app.UseMiddleware<ProjectArtifactAccessMiddleware>();", "app.UseMiddleware<ProjectMembershipMiddleware>();");
    }

    private static ProjectArtifactAccessMiddleware Middleware(Action onDispatch) =>
        new(_ =>
        {
            onDispatch();
            return Task.CompletedTask;
        });

    private static DefaultHttpContext Context(string routeName, string routeValue, ProjectArtifactKind kind)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.RouteValues[routeName] = routeValue;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "42"), new Claim("tenant_id", "7")],
            "test"));
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new RequireProjectArtifactAccessAttribute(kind, routeName)),
            "artifact test"));
        return context;
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

    private sealed class StubArtifactAccessService(bool allowed) : IProjectArtifactAccessService
    {
        public Task<bool> HasAccessAsync(
            int tenantId,
            int userId,
            ProjectArtifactKind artifactKind,
            string artifactId,
            CancellationToken cancellationToken = default) => Task.FromResult(allowed);
    }

    private sealed class StubTenantContext(int tenantId) : ICurrentTenantContext
    {
        public int TenantId { get; } = tenantId;
    }
}
