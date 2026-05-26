using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Data.Models;

namespace IronDev.Services;

public sealed class TicketEvidenceSummaryService : ITicketEvidenceSummaryService
{
    private readonly ITicketService _tickets;
    private readonly IBuilderReadinessService _readiness;
    private readonly IRunEventStore _events;

    public TicketEvidenceSummaryService(
        ITicketService tickets,
        IBuilderReadinessService readiness,
        IRunEventStore events)
    {
        _tickets = tickets;
        _readiness = readiness;
        _events = events;
    }

    public async Task<TicketEvidenceSummaryDto?> GetEvidenceSummaryAsync(
        int projectId,
        long ticketId,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken);
        if (ticket is null || ticket.ProjectId != projectId)
            return null;

        var readiness = await EvaluateReadinessOrNullAsync(projectId, ticketId, cancellationToken);
        var latestRun = await FindLatestTrustedTicketRunAsync(ticket, cancellationToken).ConfigureAwait(false);
        var blockedActions = BuildBlockedActions(readiness, latestRun);

        return new TicketEvidenceSummaryDto
        {
            TicketId = ticket.Id,
            Status = "loaded",
            Message = latestRun is null
                ? "No linked execution evidence is available yet."
                : "Execution evidence is available for this ticket.",
            LatestRun = latestRun,
            LatestPromotionPackage = null,
            LinkedTraceCount = CountTraceLinks(ticket),
            LinkedDocumentCount = ticket.SourceDocumentVersionId.HasValue ? 1 : 0,
            LinkedDecisionCount = 0,
            LinkedRunCount = latestRun is null ? 0 : 1,
            HasBlockingWarnings = blockedActions.Count > 0,
            BlockedActions = blockedActions,
            NextSafeAction = GetNextSafeAction(readiness, latestRun)
        };
    }

    private async Task<BuildReadinessResult?> EvaluateReadinessOrNullAsync(
        int projectId,
        long ticketId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _readiness.EvaluateReadinessAsync(projectId, ticketId, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<LinkedRunSummaryDto?> FindLatestTrustedTicketRunAsync(
        ProjectTicket ticket,
        CancellationToken cancellationToken)
    {
        var recentRunIds = await _events.GetRecentRunIdsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        LinkedRunSummaryDto? latest = null;
        DateTimeOffset latestTimestamp = DateTimeOffset.MinValue;

        foreach (var runId in recentRunIds)
        {
            var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
            if (!BelongsToTicket(events, ticket.ProjectId, ticket.Id))
                continue;

            var first = events[0];
            var last = events[^1];
            var completedUtc = IsTerminal(last.EventType) ? last.TimestampUtc : (DateTimeOffset?)null;
            var candidate = new LinkedRunSummaryDto
            {
                RunId = runId,
                TraceId = null,
                Title = first.Message,
                Status = MapRunStatus(ReadPayload(last, "status") ?? last.EventType),
                Recommendation = last.EventType == "ApprovalRequired" ? "Human review required." : last.Message,
                StartedUtc = first.TimestampUtc,
                CompletedUtc = completedUtc
            };

            if (last.TimestampUtc > latestTimestamp)
            {
                latestTimestamp = last.TimestampUtc;
                latest = candidate;
            }
        }

        return latest;
    }

    private static bool BelongsToTicket(IReadOnlyList<RunEventDto> events, int projectId, long ticketId) =>
        events.Any(runEvent =>
            string.Equals(ReadPayload(runEvent, "projectId"), projectId.ToString(), StringComparison.Ordinal) &&
            string.Equals(ReadPayload(runEvent, "ticketId"), ticketId.ToString(), StringComparison.Ordinal));

    private static string? ReadPayload(RunEventDto runEvent, string key) =>
        runEvent.Payload.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string MapRunStatus(string status)
    {
        if (status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("error", StringComparison.OrdinalIgnoreCase))
            return "failed";

        if (status.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("review", StringComparison.OrdinalIgnoreCase))
            return "needsHumanReview";

        if (status.Contains("running", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("started", StringComparison.OrdinalIgnoreCase))
            return "running";

        if (status.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("passed", StringComparison.OrdinalIgnoreCase))
            return "passed";

        return "unknown";
    }

    private static bool IsTerminal(string eventType) =>
        string.Equals(eventType, "RunCompleted", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "RunFailed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, "ApprovalRequired", StringComparison.OrdinalIgnoreCase);

    private static List<string> BuildBlockedActions(
        BuildReadinessResult? readiness,
        LinkedRunSummaryDto? latestRun)
    {
        var blockedActions = new List<string>();

        if (readiness is null)
        {
            blockedActions.Add("Build readiness has not been refreshed.");
        }
        else if (!readiness.IsReady)
        {
            if (!string.IsNullOrWhiteSpace(readiness.Message))
                blockedActions.Add(readiness.Message);

            blockedActions.AddRange(readiness.BlockingIssues.Where(issue => !string.IsNullOrWhiteSpace(issue)));
        }

        if (latestRun is null)
            blockedActions.Add("No execution run is linked to this ticket yet.");

        return blockedActions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetNextSafeAction(
        BuildReadinessResult? readiness,
        LinkedRunSummaryDto? latestRun)
    {
        if (readiness is null)
            return "Refresh build readiness";

        if (!readiness.IsReady)
            return "Resolve build readiness blockers";

        return latestRun is null ? "Start disposable run" : "Review latest run";
    }

    private static int CountTraceLinks(ProjectTicket ticket) =>
        (ticket.SourceChatSessionId.HasValue ? 1 : 0) +
        (ticket.SourceChatMessageId.HasValue ? 1 : 0);
}
