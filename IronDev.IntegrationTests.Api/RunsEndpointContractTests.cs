using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using IronDev.Api.Controllers;
using IronDev.Core.RunReports;
using IronDev.Infrastructure.Services.RunReports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class RunsEndpointContractTests
{
    [TestMethod]
    public async Task RunsController_ReturnsStatusAndReport()
    {
        var events = new InMemoryRunEventStore();
        await events.PublishAsync(new RunEventDto
        {
            RunId = "run-123",
            EventType = "RunStarted",
            Message = "Boundary hardening"
        });
        await events.PublishAsync(new RunEventDto
        {
            RunId = "run-123",
            EventType = "RunCompleted",
            Message = "Run completed.",
            Payload = new Dictionary<string, string>
            {
                ["status"] = "Completed"
            }
        });

        var controller = new RunsController(new StubRunReportService(), events);

        var statusResult = await controller.GetRun("run-123", CancellationToken.None);
        var status = ((OkObjectResult)statusResult.Result!).Value as RunStatusDto;
        Assert.IsNotNull(status);
        Assert.AreEqual("run-123", status.RunId);
        Assert.AreEqual("Completed", status.Status);

        var reportResult = await controller.GetRunReport("run-123", CancellationToken.None);
        var report = ((OkObjectResult)reportResult.Result!).Value as RunReportDto;
        Assert.IsNotNull(report);
        Assert.AreEqual("run-123", report.Status.RunId);
        Assert.AreEqual("Run completed.", report.Report?.Summary);
    }

    [TestMethod]
    public async Task RunsController_ReturnsNotFoundForMissingRun()
    {
        var controller = new RunsController(new StubRunReportService(), new InMemoryRunEventStore());

        var status = await controller.GetRun("missing", CancellationToken.None);
        var report = await controller.GetRunReport("missing", CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(status.Result);
        Assert.IsInstanceOfType<NotFoundResult>(report.Result);
    }

    [TestMethod]
    public async Task RunsController_ReturnsNotFoundWhenRunHasOnlyFileBackedReport()
    {
        var controller = new RunsController(new StubRunReportService(), new InMemoryRunEventStore());
        await using var body = new MemoryStream();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Response =
                {
                    Body = body
                }
            }
        };

        await controller.GetRunEvents("run-123", CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status404NotFound, controller.Response.StatusCode);
        var text = Encoding.UTF8.GetString(body.ToArray());
        Assert.AreEqual(string.Empty, text);
    }

    [TestMethod]
    public async Task RunsController_StreamsStoredEventsInsteadOfFileReportSnapshots()
    {
        var events = new InMemoryRunEventStore();
        await events.PublishAsync(new RunEventDto
        {
            RunId = "live-run",
            EventType = "RunStarted",
            Message = "Live run started."
        });
        await events.PublishAsync(new RunEventDto
        {
            RunId = "live-run",
            EventType = "ApprovalRequired",
            Message = "Review generated code proposal.",
            Payload = new Dictionary<string, string>
            {
                ["status"] = "AwaitingCodeApproval"
            }
        });

        var controller = new RunsController(new StubRunReportService(), events);
        await using var body = new MemoryStream();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Response =
                {
                    Body = body
                }
            }
        };

        var statusResult = await controller.GetRun("live-run", CancellationToken.None);
        var status = ((OkObjectResult)statusResult.Result!).Value as RunStatusDto;
        Assert.IsNotNull(status);
        Assert.AreEqual("AwaitingCodeApproval", status.Status);

        await controller.GetRunEvents("live-run", CancellationToken.None);

        var text = Encoding.UTF8.GetString(body.ToArray());
        StringAssert.Contains(text, "event: RunStarted");
        StringAssert.Contains(text, "event: ApprovalRequired");
        StringAssert.Contains(text, "Live run started.");
        Assert.IsFalse(text.Contains("Run completed.", StringComparison.Ordinal), "SSE must not synthesize events from file-backed reports.");
    }

    private sealed class StubRunReportService : IRunReportService
    {
        public Task<IReadOnlyList<RunReportSummary>> GetRecentRunsAsync(string? project = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RunReportSummary>>([]);

        public Task<RunReportDetail?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
        {
            if (runId == "missing")
                return Task.FromResult<RunReportDetail?>(null);

            return Task.FromResult<RunReportDetail?>(new RunReportDetail
            {
                RunId = runId,
                TraceId = "trace-123",
                Project = "IronDev",
                Title = "Boundary hardening",
                Status = "Completed",
                Summary = "Run completed.",
                Recommendation = "Review",
                RealRepoMutationCount = 0,
                DisposableFilesChanged = 2,
                Stages =
                [
                    new RunStageStatus
                    {
                        StageName = "Build",
                        AgentName = "Builder",
                        Status = "Completed",
                        Summary = "Build passed."
                    }
                ],
                Warnings = ["Review manually."]
            });
        }
    }
}
