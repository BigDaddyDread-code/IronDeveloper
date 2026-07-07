using IronDev.Core.Builder;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// P1-3 — records human dispositions for critic findings as durable run events.
///
/// Boundary: a disposition is a human decision about a finding; it is not
/// approval. This service validates identity, that the finding exists on a
/// review recorded against the run, and that the decision carries a reason —
/// then it records. It grants nothing: continuation still requires its own live
/// accepted approval, evaluated by its own gate. A finding is not a veto, and a
/// disposition is not a pass — it is the conscious decision the gate demands.
/// </summary>
public sealed class SkeletonFindingDispositionService : ISkeletonFindingDispositionService
{
    private readonly ITicketService _tickets;
    private readonly IRunStore _runs;
    private readonly IRunEventStore _events;

    public SkeletonFindingDispositionService(ITicketService tickets, IRunStore runs, IRunEventStore events)
    {
        _tickets = tickets;
        _runs = runs;
        _events = events;
    }

    public async Task<SkeletonFindingDispositionOutcome?> RecordAsync(
        SkeletonFindingDispositionRequest request,
        CancellationToken cancellationToken = default)
    {
        var ticket = await _tickets.GetTicketByIdAsync(request.TicketId, cancellationToken).ConfigureAwait(false);
        if (ticket is null || ticket.ProjectId != request.ProjectId)
            return null;

        var run = await _runs.GetAsync(request.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null || run.ProjectId != request.ProjectId || run.TicketId != request.TicketId)
            return null;

        if (!Enum.IsDefined(request.Disposition))
        {
            return Failure("The disposition kind is not part of the vocabulary: accept the risk, defer the fix, or reject the finding.");
        }

        // REVISE-1: AddressedByRevision is recorded only by the governed revision
        // path after a revision builds green. A human recording it directly would
        // be claiming a revision that never ran.
        if (request.Disposition == SkeletonFindingDispositionKind.AddressedByRevision)
        {
            return Failure(
                "AddressedByRevision is recorded only by the governed revision path after a revision builds green — " +
                "a human cannot claim a revision that never ran. Direct a revision through the revise surface instead.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Failure("A disposition requires a reason. A disposition without a reason is a dismissal, and dismissals are not decisions.");
        }

        if (string.IsNullOrWhiteSpace(request.DecidedByUserId))
        {
            return Failure("A disposition names its decider — an anonymous decision is not a decision.");
        }

        // The finding must exist on a review recorded against this run: a
        // disposition can only answer a finding the critic actually made.
        var events = await _events.GetEventsAsync(request.RunId, cancellationToken).ConfigureAwait(false);
        var knownFindingIds = events
            .Where(runEvent => runEvent.EventType == "SkeletonCriticReviewRecorded")
            .SelectMany(runEvent => (runEvent.Payload.TryGetValue("findingIds", out var ids) ? ids : string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);

        if (!knownFindingIds.Contains(request.FindingId))
        {
            return Failure(
                $"Finding '{request.FindingId}' is not on any critic review recorded against run '{request.RunId}'. " +
                "A disposition can only answer a finding the critic actually made.");
        }

        await _events.PublishAsync(new RunEventDto
        {
            RunId = request.RunId,
            EventType = "SkeletonFindingDispositionRecorded",
            Message =
                $"Finding {request.FindingId} dispositioned as {request.Disposition} by a human decider. " +
                "A disposition is a human decision about a finding; it is not approval — continuation still requires its own live accepted approval.",
            Payload = new Dictionary<string, string>
            {
                ["findingId"] = request.FindingId,
                ["disposition"] = request.Disposition.ToString(),
                ["reason"] = request.Reason.Trim(),
                ["decidedByUserId"] = request.DecidedByUserId,
                ["projectId"] = request.ProjectId.ToString(),
                ["ticketId"] = request.TicketId.ToString(),
                ["skeletonRun"] = "true",
                ["currentNode"] = "SkeletonFindingDisposition"
            }
        }, cancellationToken).ConfigureAwait(false);

        return new SkeletonFindingDispositionOutcome
        {
            Succeeded = true,
            FindingId = request.FindingId,
            Disposition = request.Disposition.ToString()
        };
    }

    private static SkeletonFindingDispositionOutcome Failure(string reason) =>
        new() { Succeeded = false, FailureReason = reason };
}
