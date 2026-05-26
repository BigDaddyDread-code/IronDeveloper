using IronDev.Core.RunReports;
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
    private readonly IRunEventStore _events;

    public RunsController(IRunReportService reports, IRunEventStore events)
    {
        _reports = reports;
        _events = events;
    }

    [HttpGet("{runId}")]
    public async Task<ActionResult<RunStatusDto>> GetRun(string runId, CancellationToken ct)
    {
        var events = await _events.GetEventsAsync(runId, ct);
        if (events.Count > 0)
            return Ok(ToStatus(runId, events));

        var report = await _reports.GetRunAsync(runId, ct);
        if (report is not null)
            return Ok(ToStatus(report));

        return NotFound();
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

    [HttpGet("{runId}/events")]
    [Produces("text/event-stream")]
    public async Task GetRunEvents(string runId, CancellationToken ct)
    {
        var events = await _events.GetEventsAsync(runId, ct);
        var report = events.Count == 0 ? await _reports.GetRunAsync(runId, ct) : null;
        if (events.Count == 0 && report is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        if (events.Count > 0)
        {
            await foreach (var runEvent in _events.StreamEventsAsync(runId, ct))
            {
                await WriteSseEventAsync(runEvent, ct);
            }

            return;
        }

        foreach (var runEvent in ToEvents(report!))
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

    private static IEnumerable<RunEventDto> ToEvents(RunReportDetail report)
    {
        yield return CreateEvent(report.RunId, "RunStarted", $"Run started: {report.Title}");

        foreach (var stage in report.Stages)
        {
            yield return CreateEvent(
                report.RunId,
                "StepStarted",
                $"Stage started: {stage.StageName}",
                new Dictionary<string, string>
                {
                    ["stageName"] = stage.StageName,
                    ["agentName"] = stage.AgentName
                });

            yield return CreateEvent(
                report.RunId,
                IsFailureStatus(stage.Status) ? "Error" : "StepCompleted",
                $"Stage {stage.Status}: {stage.StageName}",
                new Dictionary<string, string>
                {
                    ["stageName"] = stage.StageName,
                    ["agentName"] = stage.AgentName,
                    ["status"] = stage.Status,
                    ["summary"] = stage.Summary
                });
        }

        foreach (var warning in report.Warnings)
        {
            yield return CreateEvent(report.RunId, "Warning", warning);
        }

        yield return CreateEvent(
            report.RunId,
            IsFailureStatus(report.Status) ? "RunFailed" : "RunCompleted",
            string.IsNullOrWhiteSpace(report.Summary) ? $"Run {report.Status}: {report.Title}" : report.Summary,
            new Dictionary<string, string>
            {
                ["status"] = report.Status,
                ["recommendation"] = report.Recommendation
            });
    }

    private static RunEventDto CreateEvent(
        string runId,
        string eventType,
        string message,
        IReadOnlyDictionary<string, string>? payload = null) => new()
        {
            RunId = runId,
            EventType = eventType,
            Message = message,
            Payload = payload ?? new Dictionary<string, string>()
        };

    private static bool IsFailureStatus(string status) =>
        status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("error", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("block", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminalEvent(string eventType) =>
        string.Equals(eventType, "RunCompleted", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "RunFailed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "ApprovalRequired", StringComparison.OrdinalIgnoreCase);
}
