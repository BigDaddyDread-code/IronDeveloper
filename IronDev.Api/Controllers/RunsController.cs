using IronDev.Core.Runs;
using IronDev.Core.RunReports;
using IronDev.Api.Middleware;
using IronDev.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/runs")]
public sealed class RunsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IRunReportService _reports;
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;

    public RunsController(IRunReportService reports, IRunStore runs, IRunEventStore events)
    {
        _reports = reports;
        _runs = runs;
        _events = events;
    }

    [HttpGet("{runId}")]
    [RequireProjectArtifactAccess(ProjectArtifactKind.Run, "runId")]
    public async Task<ActionResult<RunStatusDto>> GetRun(string runId, CancellationToken ct)
    {
        var run = await _runs.GetAsync(runId, ct);
        var events = await _events.GetEventsAsync(runId, ct);
        if (run is not null)
            return Ok(ToStatus(run, events));

        if (events.Count > 0)
            return Ok(ToStatus(runId, events));

        return NotFound();
    }

    [HttpGet("{runId}/report")]
    [RequireProjectArtifactAccess(ProjectArtifactKind.Run, "runId")]
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

    [HttpGet("{runId}/events")]
    [RequireProjectArtifactAccess(ProjectArtifactKind.Run, "runId")]
    [Produces("text/event-stream")]
    public async Task GetRunEvents(string runId, CancellationToken ct)
    {
        var events = await _events.GetEventsAsync(runId, ct);
        if (events.Count == 0)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        await foreach (var runEvent in _events.StreamEventsAsync(runId, ct))
        {
            await WriteSseEventAsync(runEvent, ct);
        }
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

    private static RunStatusDto ToStatus(RunRecord run, IReadOnlyList<RunEventDto> events)
    {
        var last = events.LastOrDefault();
        var first = events.FirstOrDefault();
        return new RunStatusDto
        {
            RunId = run.RunId,
            Project = run.ProjectId?.ToString() ?? string.Empty,
            Title = string.IsNullOrWhiteSpace(run.Summary) ? first?.Message ?? string.Empty : run.Summary,
            Status = run.State.ToString(),
            Recommendation = run.State == RunLifecycleState.PausedForApproval ? "Approval required" : string.Empty,
            StartedUtc = run.StartedUtc ?? first?.TimestampUtc,
            CompletedUtc = run.CompletedUtc ?? (last is not null && IsTerminalEvent(last.EventType) ? last.TimestampUtc : null)
        };
    }

    private static RunStatusDto ToStatus(string runId, IReadOnlyList<RunEventDto> events)
    {
        var last = events[^1];
        var first = events[0];
        var payload = last.Payload;
        payload.TryGetValue("status", out var status);

        return new RunStatusDto
        {
            RunId = runId,
            Title = first.Message,
            Status = string.IsNullOrWhiteSpace(status) ? last.EventType : status,
            Recommendation = last.EventType == "ApprovalRequired" ? "Approval required" : string.Empty,
            StartedUtc = first.TimestampUtc,
            CompletedUtc = IsTerminalEvent(last.EventType) ? last.TimestampUtc : null
        };
    }

    private async Task WriteSseEventAsync(RunEventDto runEvent, CancellationToken ct)
    {
        await Response.WriteAsync($"event: {runEvent.EventType}\n", ct);
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(runEvent, JsonOptions)}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private static bool IsTerminalEvent(string eventType) =>
        string.Equals(eventType, "RunCompleted", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "RunFailed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "ApprovalRequired", StringComparison.OrdinalIgnoreCase);
}
