using IronDev.Core.RunReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/run-reports")]
public sealed class RunReportsController : ControllerBase
{
    private readonly IRunReportService _reports;
    private readonly IRunEvidenceService _evidence;

    public RunReportsController(IRunReportService reports, IRunEvidenceService evidence)
    {
        _reports = reports;
        _evidence = evidence;
    }

    [HttpGet]
    public Task<IReadOnlyList<RunReportSummary>> GetRecent([FromQuery] string? project, CancellationToken ct) =>
        _reports.GetRecentRunsAsync(string.IsNullOrWhiteSpace(project) ? null : project, ct);

    [HttpGet("{runId}")]
    public Task<RunReportDetail?> GetRun(string runId, CancellationToken ct) =>
        _reports.GetRunAsync(runId, ct);

    [HttpGet("{runId}/evidence")]
    public Task<IReadOnlyList<RunEvidenceItem>> GetEvidence(string runId, CancellationToken ct) =>
        _evidence.GetEvidenceAsync(runId, ct);

    [HttpGet("{runId}/evidence/text")]
    public Task<string?> GetEvidenceText(string runId, [FromQuery] string path, CancellationToken ct) =>
        _evidence.ReadEvidenceTextAsync(runId, path, ct);
}
