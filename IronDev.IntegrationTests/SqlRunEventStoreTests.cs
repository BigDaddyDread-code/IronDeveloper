using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Data;
using IronDev.Infrastructure.Services.Runs;
using IronDev.Infrastructure.Services.RunReports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class SqlRunEventStoreTests : IntegrationTestBase
{
    [TestMethod]
    public async Task SqlRunStore_PersistsRunLifecycleAcrossStoreInstances()
    {
        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var store = new SqlRunStore(connectionFactory);

        var created = await store.CreateAsync(new CreateRunRequest
        {
            RunId = "durable-run-state-1",
            ProjectId = 7,
            TicketId = 42,
            IsDisposable = true,
            Summary = "Run created."
        });

        Assert.AreEqual(RunLifecycleState.Created, created.State);

        await store.TransitionAsync(new RunStateTransition
        {
            RunId = created.RunId,
            State = RunLifecycleState.Running,
            Summary = "Run started."
        });

        await store.TransitionAsync(new RunStateTransition
        {
            RunId = created.RunId,
            State = RunLifecycleState.Failed,
            Summary = "Run failed.",
            FailureReason = "Build failed."
        });

        var restartedStore = new SqlRunStore(connectionFactory);
        var run = await restartedStore.GetAsync(created.RunId);

        Assert.IsNotNull(run);
        Assert.AreEqual(7, run.ProjectId);
        Assert.AreEqual(42, run.TicketId);
        Assert.AreEqual(RunLifecycleState.Failed, run.State);
        Assert.IsTrue(run.IsDisposable);
        Assert.AreEqual("Build failed.", run.FailureReason);
        Assert.IsNotNull(run.StartedUtc);
        Assert.IsNotNull(run.CompletedUtc);
    }

    [TestMethod]
    public async Task PublishAsync_PersistsEventsAcrossStoreInstances()
    {
        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var store = new SqlRunEventStore(connectionFactory);

        await store.PublishAsync(new RunEventDto
        {
            RunId = "durable-run-1",
            EventType = "RunStarted",
            Message = "Run started.",
            Payload = new Dictionary<string, string>
            {
                ["status"] = "Running"
            }
        });

        await store.PublishAsync(new RunEventDto
        {
            RunId = "durable-run-1",
            EventType = "ApprovalRequired",
            Message = "Approval required.",
            Payload = new Dictionary<string, string>
            {
                ["status"] = "AwaitingCodeApproval"
            }
        });

        var restartedStore = new SqlRunEventStore(connectionFactory);
        var events = await restartedStore.GetEventsAsync("durable-run-1");

        Assert.AreEqual(2, events.Count);
        Assert.IsTrue(events.All(e => e.EventId != Guid.Empty));
        Assert.AreEqual("RunStarted", events[0].EventType);
        Assert.AreEqual("ApprovalRequired", events[1].EventType);
        Assert.AreEqual("AwaitingCodeApproval", events[1].Payload["status"]);
    }

    [TestMethod]
    public async Task StreamEventsAsync_ReplaysPersistedTerminalEventsAndCompletes()
    {
        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        var store = new SqlRunEventStore(connectionFactory);
        var runId = $"durable-stream-{Guid.NewGuid():N}";

        await store.PublishAsync(new RunEventDto
        {
            RunId = runId,
            EventType = "RunStarted",
            Message = "Run started."
        });

        await store.PublishAsync(new RunEventDto
        {
            RunId = runId,
            EventType = "RunCompleted",
            Message = "Run completed.",
            Payload = new Dictionary<string, string>
            {
                ["status"] = "Completed"
            }
        });

        var restartedStore = new SqlRunEventStore(connectionFactory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var streamed = new List<RunEventDto>();

        await foreach (var runEvent in restartedStore.StreamEventsAsync(runId, timeout.Token))
            streamed.Add(runEvent);

        Assert.AreEqual(2, streamed.Count);
        Assert.AreEqual("RunStarted", streamed[0].EventType);
        Assert.AreEqual("RunCompleted", streamed[1].EventType);
    }
}
