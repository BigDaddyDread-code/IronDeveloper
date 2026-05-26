using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using IronDev.Api.Controllers;
using IronDev.Core.RunReports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class RunsEndpointContractTests
{
    [TestMethod]
    public async Task RunsController_ReturnsStatusAndReport()
    {
        var controller = new RunsController(new StubRunReportService());

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
        var controller = new RunsController(new StubRunReportService());

        var status = await controller.GetRun("missing", CancellationToken.None);
        var report = await controller.GetRunReport("missing", CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(status.Result);
        Assert.IsInstanceOfType<NotFoundResult>(report.Result);
    }

    [TestMethod]
    public async Task RunsController_StreamsReportBackedSseEvents()
    {
        var controller = new RunsController(new StubRunReportService());
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

        Assert.AreEqual("text/event-stream", controller.Response.ContentType);
        var text = Encoding.UTF8.GetString(body.ToArray());
        StringAssert.Contains(text, "event: RunStarted");
        StringAssert.Contains(text, "event: StepCompleted");
        StringAssert.Contains(text, "event: Warning");
        StringAssert.Contains(text, "event: RunCompleted");
        StringAssert.Contains(text, "\"runId\":\"run-123\"");
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
