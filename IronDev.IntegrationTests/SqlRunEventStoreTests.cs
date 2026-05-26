using IronDev.Core.RunReports;
using IronDev.Data;
using IronDev.Infrastructure.Services.RunReports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class SqlRunEventStoreTests : IntegrationTestBase
{
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
