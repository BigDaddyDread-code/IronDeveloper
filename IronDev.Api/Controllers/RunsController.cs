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

    [HttpGet("{runId}/events")]
    [Produces("text/event-stream")]
    public async Task GetRunEvents(string runId, CancellationToken ct)
    {
        var report = await _reports.GetRunAsync(runId, ct);
        if (report is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        foreach (var runEvent in ToEvents(report))
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
}
