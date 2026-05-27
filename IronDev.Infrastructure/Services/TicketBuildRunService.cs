using IronDev.Core.Interfaces;
using IronDev.Core.Runs;
using IronDev.Core.RunReports;
using IronDev.Core.Workflow;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class TicketBuildRunService : ITicketBuildRunService
{
    private readonly ITicketService _tickets;
    private readonly ITicketBuildWorkflowOrchestrator _workflow;
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;
    private readonly IRunReportService _reports;
    private readonly IRunEvidenceService _evidence;

    public TicketBuildRunService(
        ITicketService tickets,
        ITicketBuildWorkflowOrchestrator workflow,
        IRunStore runs,
        IRunEventStore events,
        IRunReportService reports,
        IRunEvidenceService evidence)
    {
        _tickets = tickets;
        _workflow = workflow;
        _runs = runs;
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

        var runs = new List<TicketBuildRunSummaryDto>();
        var durableRuns = await _runs.GetRecentAsync(take <= 0 ? 50 : take, cancellationToken).ConfigureAwait(false);
        foreach (var run in durableRuns.Where(run =>
            run.ProjectId == projectId &&
            run.TicketId == ticketId))
        {
            var events = await _events.GetEventsAsync(run.RunId, cancellationToken).ConfigureAwait(false);
            runs.Add(ToSummary(run, events));
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

        var run = await _runs.GetAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.ProjectId != projectId || run.TicketId != ticketId)
            return null;

        var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        var summary = ToSummary(run, events);
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
        RunRecord run,
        IReadOnlyList<RunEventDto> events)
    {
        var first = events.FirstOrDefault();
        var last = events.LastOrDefault();
        var status = last is null ? run.State.ToString() : ReadPayload(last, "status") ?? last.EventType;
        var currentNode = last is null ? string.Empty : ReadPayload(last, "currentNode") ?? ReadPayload(last, "node") ?? string.Empty;
        var failure = events.LastOrDefault(IsFailureEvent)?.Message;

        return new TicketBuildRunSummaryDto
        {
            RunId = run.RunId,
            ProjectId = run.ProjectId ?? 0,
            TicketId = run.TicketId ?? 0,
            Status = status,
            CurrentNode = currentNode,
            RequiresHumanApproval = string.Equals(last?.EventType, "ApprovalRequired", StringComparison.OrdinalIgnoreCase) ||
                                    run.State == RunLifecycleState.PausedForApproval,
            IsDisposable = run.IsDisposable || events.Any(IsDisposableRunEvent),
            StartedUtc = run.StartedUtc ?? first?.TimestampUtc,
            CompletedUtc = run.CompletedUtc ?? (last is not null && IsTerminal(last.EventType) ? last.TimestampUtc : null),
            Summary = !string.IsNullOrWhiteSpace(last?.Message)
                ? last.Message
                : string.IsNullOrWhiteSpace(run.Summary) ? $"Run {run.RunId} is {status}." : run.Summary,
            FailureReason = run.FailureReason ?? failure
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
