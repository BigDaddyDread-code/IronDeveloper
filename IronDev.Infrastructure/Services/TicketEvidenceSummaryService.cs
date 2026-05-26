using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Data.Models;

namespace IronDev.Services;

public sealed class TicketEvidenceSummaryService : ITicketEvidenceSummaryService
{
    private readonly ITicketService _tickets;
    private readonly IBuilderReadinessService _readiness;
    private readonly IRunReportService _runReports;

    public TicketEvidenceSummaryService(
        ITicketService tickets,
        IBuilderReadinessService readiness,
        IRunReportService runReports)
    {
        _tickets = tickets;
        _readiness = readiness;
        _runReports = runReports;
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
        var recentRuns = await _runReports.GetRecentRunsAsync(cancellationToken: cancellationToken);
        var latestRun = FindLatestTrustedTicketRun(recentRuns, ticket);
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

    private static LinkedRunSummaryDto? FindLatestTrustedTicketRun(
        IReadOnlyList<RunReportSummary> runs,
        ProjectTicket ticket)
    {
        // RunReportSummary does not currently expose a ticket source relationship.
        // Until it does, the backend must not infer links from titles or loose text.
        return null;
    }

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
