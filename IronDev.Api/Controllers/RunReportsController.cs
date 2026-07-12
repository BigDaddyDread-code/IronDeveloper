using IronDev.Core.RunReports;
using IronDev.Api.Middleware;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/run-reports")]
public sealed class RunReportsController : ControllerBase
{
    private readonly IRunReportService _reports;
    private readonly IRunEvidenceService _evidence;
    private readonly IProjectArtifactAccessService _artifacts;
    private readonly ICurrentTenantContext _tenant;

    public RunReportsController(
        IRunReportService reports,
        IRunEvidenceService evidence,
        IProjectArtifactAccessService artifacts,
        ICurrentTenantContext tenant)
    {
        _reports = reports;
        _evidence = evidence;
        _artifacts = artifacts;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<IReadOnlyList<RunReportSummary>> GetRecent([FromQuery] string? project, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return [];

        var reports = await _reports.GetRecentRunsAsync(string.IsNullOrWhiteSpace(project) ? null : project, ct);
        var visible = new List<RunReportSummary>();
        foreach (var report in reports)
        {
            if (await _artifacts.HasAccessAsync(_tenant.TenantId, userId, ProjectArtifactKind.RunReport, report.RunId, ct))
                visible.Add(report);
        }

        return visible;
    }

    [HttpGet("{runId}")]
    [RequireProjectArtifactAccess(ProjectArtifactKind.RunReport, "runId")]
    public Task<RunReportDetail?> GetRun(string runId, CancellationToken ct) =>
        _reports.GetRunAsync(runId, ct);

    [HttpGet("{runId}/evidence")]
    [RequireProjectArtifactAccess(ProjectArtifactKind.RunReport, "runId")]
    public Task<IReadOnlyList<RunEvidenceItem>> GetEvidence(string runId, CancellationToken ct) =>
        _evidence.GetEvidenceAsync(runId, ct);

    [HttpGet("{runId}/evidence/text")]
    [RequireProjectArtifactAccess(ProjectArtifactKind.RunReport, "runId")]
    public Task<string?> GetEvidenceText(string runId, [FromQuery] string path, CancellationToken ct) =>
        _evidence.ReadEvidenceTextAsync(runId, path, ct);

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out userId);
}
