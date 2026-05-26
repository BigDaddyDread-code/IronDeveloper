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
                DisposableFilesChanged = 2
            });
        }
    }
}
