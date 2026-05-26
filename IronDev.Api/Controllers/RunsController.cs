using IronDev.Core.RunReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/runs")]
public sealed class RunsController : ControllerBase
{
    private readonly IRunReportService _reports;

    public RunsController(IRunReportService reports)
    {
        _reports = reports;
    }

    [HttpGet("{runId}")]
    public async Task<ActionResult<RunStatusDto>> GetRun(string runId, CancellationToken ct)
    {
        var report = await _reports.GetRunAsync(runId, ct);
        if (report is null)
            return NotFound();

        return Ok(ToStatus(report));
    }

    [HttpGet("{runId}/report")]
    public async Task<ActionResult<RunReportDto>> GetRunReport(string runId, CancellationToken ct)
    {
        var report = await _reports.GetRunAsync(runId, ct);
        if (report is null)
            return NotFound();

        return Ok(new RunReportDto
        {
            Status = ToStatus(report),
            Report = report
        });
    }

    private static RunStatusDto ToStatus(RunReportDetail report) => new()
    {
        RunId = report.RunId,
        TraceId = report.TraceId,
        Project = report.Project,
        Title = report.Title,
        Status = report.Status,
        Recommendation = report.Recommendation,
        StartedUtc = null,
        CompletedUtc = null,
        RealRepoMutationCount = report.RealRepoMutationCount,
        DisposableFilesChanged = report.DisposableFilesChanged
    };
}
