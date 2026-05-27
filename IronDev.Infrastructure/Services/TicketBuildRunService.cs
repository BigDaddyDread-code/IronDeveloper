using IronDev.Core.Interfaces;
using IronDev.Core.RunReports;
using IronDev.Core.Workflow;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class TicketBuildRunService : ITicketBuildRunService
{
    private readonly ITicketService _tickets;
    private readonly ITicketBuildWorkflowOrchestrator _workflow;
    private readonly IRunEventStore _events;
    private readonly IRunReportService _reports;
    private readonly IRunEvidenceService _evidence;

    public TicketBuildRunService(
        ITicketService tickets,
        ITicketBuildWorkflowOrchestrator workflow,
        IRunEventStore events,
        IRunReportService reports,
        IRunEvidenceService evidence)
    {
        _tickets = tickets;
        _workflow = workflow;
        _events = events;
        _reports = reports;
        _evidence = evidence;
    }

    public async Task<TicketBuildRunDto?> StartDisposableAsync(
        int projectId,
        long ticketId,
        StartTicketBuildRunRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!await TicketBelongsToProjectAsync(projectId, ticketId, cancellationToken).ConfigureAwait(false))
            return null;

        var result = await _workflow.StartAsync(new TicketBuildWorkflowRequest
        {
            WorkflowRunId = request?.WorkflowRunId,
            ProjectId = projectId,
            TicketId = ticketId,
            MaxRetries = request?.MaxRetries ?? 3
        }, cancellationToken).ConfigureAwait(false);

        return new TicketBuildRunDto
        {
            RunId = result.WorkflowRunId.ToString("D"),
            ProjectId = projectId,
            TicketId = ticketId,
            Status = result.Status.ToString(),
            CurrentNode = result.CurrentNode,
            RequiresHumanApproval = result.RequiresHumanApproval,
            Message = result.Message
        };
    }

    public async Task<IReadOnlyList<TicketBuildRunSummaryDto>?> GetRunsAsync(
        int projectId,
        long ticketId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!await TicketBelongsToProjectAsync(projectId, ticketId, cancellationToken).ConfigureAwait(false))
            return null;

        var runIds = await _events.GetRecentRunIdsAsync(take <= 0 ? 50 : take, cancellationToken).ConfigureAwait(false);
        var runs = new List<TicketBuildRunSummaryDto>();

        foreach (var runId in runIds)
        {
            var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
            if (!BelongsToTicket(events, projectId, ticketId))
                continue;

            runs.Add(ToSummary(runId, projectId, ticketId, events));
        }

        return runs;
    }

    public async Task<TicketBuildRunDetailDto?> GetRunAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return null;

        if (!await TicketBelongsToProjectAsync(projectId, ticketId, cancellationToken).ConfigureAwait(false))
            return null;

        var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        if (!BelongsToTicket(events, projectId, ticketId))
            return null;

        var summary = ToSummary(runId, projectId, ticketId, events);
        var report = await _reports.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);
        var evidence = report?.Evidence;
        if (evidence is null || evidence.Count == 0)
            evidence = await _evidence.GetEvidenceAsync(runId, cancellationToken).ConfigureAwait(false);

        return new TicketBuildRunDetailDto
        {
            RunId = summary.RunId,
            ProjectId = summary.ProjectId,
            TicketId = summary.TicketId,
            Status = summary.Status,
            CurrentNode = summary.CurrentNode,
            RequiresHumanApproval = summary.RequiresHumanApproval,
            IsDisposable = summary.IsDisposable,
            StartedUtc = summary.StartedUtc,
            CompletedUtc = summary.CompletedUtc,
            Summary = !string.IsNullOrWhiteSpace(report?.Summary) ? report.Summary : summary.Summary,
            FailureReason = summary.FailureReason,
            ReportPath = report?.ReportPath,
            TracePath = report?.TraceId is null ? null : $"trace:{report.TraceId}",
            LogPath = report?.ReportPath,
            Events = events,
            Evidence = evidence
        };
    }

    private async Task<bool> TicketBelongsToProjectAsync(
        int projectId,
        long ticketId,
        CancellationToken cancellationToken)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        return ticket is not null && ticket.ProjectId == projectId;
    }

    private static TicketBuildRunSummaryDto ToSummary(
        string runId,
        int projectId,
        long ticketId,
        IReadOnlyList<RunEventDto> events)
    {
        var first = events[0];
        var last = events[^1];
        var status = ReadPayload(last, "status") ?? last.EventType;
        var currentNode = ReadPayload(last, "currentNode") ?? ReadPayload(last, "node") ?? string.Empty;
        var failure = events.LastOrDefault(IsFailureEvent)?.Message;

        return new TicketBuildRunSummaryDto
        {
            RunId = runId,
            ProjectId = projectId,
            TicketId = ticketId,
            Status = status,
            CurrentNode = currentNode,
            RequiresHumanApproval = string.Equals(last.EventType, "ApprovalRequired", StringComparison.OrdinalIgnoreCase),
            IsDisposable = events.Any(IsDisposableRunEvent),
            StartedUtc = first.TimestampUtc,
            CompletedUtc = IsTerminal(last.EventType) ? last.TimestampUtc : null,
            Summary = string.IsNullOrWhiteSpace(last.Message)
                ? $"Run {runId} is {status}."
                : last.Message,
            FailureReason = failure
        };
    }

    private static bool BelongsToTicket(IReadOnlyList<RunEventDto> events, int projectId, long ticketId) =>
        events.Count > 0 &&
        events.Any(runEvent =>
            string.Equals(ReadPayload(runEvent, "projectId"), projectId.ToString(), StringComparison.Ordinal) &&
            string.Equals(ReadPayload(runEvent, "ticketId"), ticketId.ToString(), StringComparison.Ordinal));

    private static bool IsDisposableRunEvent(RunEventDto runEvent) =>
        string.Equals(ReadPayload(runEvent, "disposableRun"), "true", StringComparison.OrdinalIgnoreCase);

    private static string? ReadPayload(RunEventDto runEvent, string key) =>
        runEvent.Payload.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool IsFailureEvent(RunEventDto runEvent) =>
        string.Equals(runEvent.EventType, "RunFailed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(runEvent.EventType, "Error", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminal(string eventType) =>
        string.Equals(eventType, "RunCompleted", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "RunFailed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "ApprovalRequired", StringComparison.OrdinalIgnoreCase);
}
