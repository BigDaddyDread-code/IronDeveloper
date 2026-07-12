using System.Security.Claims;
using IronDev.Api.Middleware;
using IronDev.Core.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class UserMutationAttributionMiddlewareTests
{
    [TestMethod]
    public async Task AuthenticatedWrite_RecordsAttemptAndCompletionWithScopeAndTrace()
    {
        var context = Context(HttpMethods.Post, statusCode: StatusCodes.Status201Created);
        var store = new RecordingStore();
        var middleware = Middleware(nextCalled: null);

        await middleware.InvokeAsync(context, store);

        Assert.HasCount(2, store.Records);
        Assert.AreEqual("Attempted", store.Records[0].Phase);
        Assert.AreEqual("Completed", store.Records[1].Phase);
        Assert.AreEqual(42, store.Records[0].ActorUserId);
        Assert.AreEqual(7, store.Records[0].TenantId);
        Assert.AreEqual("19", store.Records[0].ProjectId);
        Assert.AreEqual("corr-123", store.Records[0].CorrelationId);
        Assert.AreEqual("cause-9", store.Records[0].CausationId);
        Assert.AreEqual("chat", store.Records[0].SourceSurface);
        Assert.AreEqual("tauri", store.Records[0].SourceClient);
        Assert.AreEqual(StatusCodes.Status201Created, store.Records[1].StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedRefusedWrite_RecordsRefusal()
    {
        var context = Context(HttpMethods.Patch, statusCode: StatusCodes.Status409Conflict);
        var store = new RecordingStore();

        await Middleware(nextCalled: null).InvokeAsync(context, store);

        Assert.AreEqual("Refused", store.Records[1].Phase);
        Assert.AreEqual(StatusCodes.Status409Conflict, store.Records[1].StatusCode);
    }

    [TestMethod]
    public async Task AttributionAttemptFailure_StopsMutationBeforeDispatch()
    {
        var context = Context(HttpMethods.Delete, statusCode: StatusCodes.Status204NoContent);
        var nextCalled = false;
        var store = new RecordingStore { FailOnAppendNumber = 1 };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await Middleware(() => nextCalled = true).InvokeAsync(context, store));

        Assert.IsFalse(nextCalled);
        Assert.IsEmpty(store.Records);
    }

    [TestMethod]
    public async Task ReadAndAnonymousRequests_AreNotAttributed()
    {
        var readContext = Context(HttpMethods.Get, statusCode: StatusCodes.Status200OK);
        var anonymousContext = Context(HttpMethods.Post, statusCode: StatusCodes.Status200OK);
        anonymousContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        var store = new RecordingStore();
        var middleware = Middleware(nextCalled: null);

        await middleware.InvokeAsync(readContext, store);
        await middleware.InvokeAsync(anonymousContext, store);

        Assert.IsEmpty(store.Records);
    }

    [TestMethod]
    public void MigrationAndRegistration_AreDurableAndAppendOnly()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(root, "Database", "migrate_user_mutation_attribution.sql"));
        var manifest = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));
        var verifier = File.ReadAllText(Path.Combine(root, "Database", "verify-migrations.ps1"));
        var program = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));

        StringAssert.Contains(migration, "CREATE TABLE dbo.UserMutationAttribution");
        StringAssert.Contains(migration, "TR_UserMutationAttribution_BlockUpdateDelete");
        StringAssert.Contains(manifest, "migrate_user_mutation_attribution.sql");
        StringAssert.Contains(verifier, "dbo.UserMutationAttribution table");
        StringAssert.Contains(program, "UseMiddleware<UserMutationAttributionMiddleware>");
        StringAssert.Contains(program, "IUserMutationAttributionStore, SqlUserMutationAttributionStore");
    }

    private static DefaultHttpContext Context(string method, int statusCode)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = "/api/projects/19/chat/messages";
        context.Request.RouteValues["projectId"] = "19";
        context.Request.Headers[UserMutationAttributionMiddleware.CausationHeaderName] = "cause-9";
        context.Request.Headers[UserMutationAttributionMiddleware.SourceSurfaceHeaderName] = "chat";
        context.Request.Headers[UserMutationAttributionMiddleware.SourceClientHeaderName] = "tauri";
        context.Items[RequestTracingMiddleware.CorrelationHeaderName] = "corr-123";
        context.Response.StatusCode = statusCode;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim("tenant_id", "7")
        ], "test"));
        return context;
    }

    private static UserMutationAttributionMiddleware Middleware(Action? nextCalled) =>
        new(
            _ =>
            {
                nextCalled?.Invoke();
                return Task.CompletedTask;
            },
            NullLogger<UserMutationAttributionMiddleware>.Instance);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class RecordingStore : IUserMutationAttributionStore
    {
        private int _appendCount;
        public int? FailOnAppendNumber { get; init; }
        public List<UserMutationAttributionRecord> Records { get; } = [];

        public Task AppendAsync(UserMutationAttributionRecord record, CancellationToken cancellationToken = default)
        {
            _appendCount++;
            if (_appendCount == FailOnAppendNumber)
                throw new InvalidOperationException("Attribution store unavailable.");
            Records.Add(record);
            return Task.CompletedTask;
        }
    }
}
