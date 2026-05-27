using IronDev.Core.Interfaces;
using IronDev.Core.RunReports;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class TicketRunReviewService : ITicketRunReviewService
{
    private readonly ITicketService _tickets;
    private readonly IRunEventStore _events;
    private readonly IRunReportService _reports;
    private readonly IRunEvidenceService _evidence;

    public TicketRunReviewService(
        ITicketService tickets,
        IRunEventStore events,
        IRunReportService reports,
        IRunEvidenceService evidence)
    {
        _tickets = tickets;
        _events = events;
        _reports = reports;
        _evidence = evidence;
    }

    public async Task<TicketRunReviewDto?> GetRunReviewAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
            return null;

        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        if (events.Count == 0 || !BelongsToTicket(events, projectId, ticketId))
            return null;

        var report = await _reports.GetRunAsync(runId, cancellationToken).ConfigureAwait(false);
        var evidence = report?.Evidence;
        if (evidence is null || evidence.Count == 0)
            evidence = await _evidence.GetEvidenceAsync(runId, cancellationToken).ConfigureAwait(false);

        var first = events[0];
        var last = events[^1];
        var status = ReadPayload(last, "status") ?? last.EventType;
        var completed = IsTerminal(last.EventType) ? last.TimestampUtc : (DateTimeOffset?)null;
        var failure = events.LastOrDefault(IsFailureEvent)?.Message;

        return new TicketRunReviewDto
        {
            RunId = runId,
            ProjectId = projectId,
            TicketId = ticketId,
            TicketTitle = ticket.Title ?? $"Ticket {ticketId}",
            Status = status,
            StartedUtc = first.TimestampUtc,
            CompletedUtc = completed,
            IsDisposableRun = events.Any(IsDisposableRunEvent),
            TraceId = report?.TraceId,
            EvidenceSummary = evidence.Count == 0
                ? "No evidence files have been attached to this run yet."
                : $"{evidence.Count} evidence item(s) are attached to this run.",
            OutputSummary = BuildOutputSummary(report, last),
            FailureReason = failure,
            ReportPath = report?.ReportPath,
            TracePath = report?.TraceId is null ? null : $"trace:{report.TraceId}",
            LogPath = report?.ReportPath,
            Evidence = evidence,
            Events = events
        };
    }

    private static bool BelongsToTicket(IReadOnlyList<RunEventDto> events, int projectId, long ticketId) =>
        events.Any(runEvent =>
            string.Equals(ReadPayload(runEvent, "projectId"), projectId.ToString(), StringComparison.Ordinal) &&
            string.Equals(ReadPayload(runEvent, "ticketId"), ticketId.ToString(), StringComparison.Ordinal));

    private static bool IsDisposableRunEvent(RunEventDto runEvent) =>
        string.Equals(ReadPayload(runEvent, "disposableRun"), "true", StringComparison.OrdinalIgnoreCase);

    private static string? ReadPayload(RunEventDto runEvent, string key) =>
        runEvent.Payload.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string BuildOutputSummary(RunReportDetail? report, RunEventDto last)
    {
        if (!string.IsNullOrWhiteSpace(report?.Summary))
            return report.Summary;

        if (!string.IsNullOrWhiteSpace(last.Message))
            return last.Message;

        return "Run output summary is not available yet.";
    }

    private static bool IsFailureEvent(RunEventDto runEvent) =>
        string.Equals(runEvent.EventType, "RunFailed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(runEvent.EventType, "Error", StringComparison.OrdinalIgnoreCase);

    private static bool IsTerminal(string eventType) =>
        string.Equals(eventType, "RunCompleted", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "RunFailed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "ApprovalRequired", StringComparison.OrdinalIgnoreCase);
}
