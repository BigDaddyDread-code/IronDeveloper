using IronDev.Data.Models;

namespace IronDev.Core.Builder;

/// <summary>
/// P2-1 — the batch dependency map. For a set of tickets, the edges the evidence
/// supports: explicit blocks the human declared, and predicted footprint overlaps
/// where two tickets name the same files. Every edge carries its KIND and a
/// NAMED REASON — a predicted conflict is a claim with a source, never a hunch.
///
/// Boundary: a dependency map is advisory evidence. It schedules nothing, starts
/// nothing, and grants nothing — sequencing is a separate step (P2-2), and every
/// per-ticket gate stays exactly where Phase 0/1 put it.
/// </summary>
public sealed record SkeletonBatchDependencyEdge
{
    /// <summary>The ticket that must be dealt with first.</summary>
    public required long FromTicketId { get; init; }

    /// <summary>The ticket that waits.</summary>
    public required long ToTicketId { get; init; }

    /// <summary>"explicit-block" (declared by the human) or "footprint-overlap" (predicted from linked files).</summary>
    public required string Kind { get; init; }

    /// <summary>The evidence for this edge, in plain words.</summary>
    public required string Reason { get; init; }

    public IReadOnlyList<string> SharedPaths { get; init; } = [];
}

public sealed record SkeletonBatchMap
{
    public required int ProjectId { get; init; }
    public IReadOnlyList<long> TicketIds { get; init; } = [];
    public IReadOnlyList<SkeletonBatchDependencyEdge> Edges { get; init; } = [];

    /// <summary>What detection could NOT see, by name — an undetectable overlap is stated, never silently assumed away.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A dependency map is advisory evidence: every edge names its source, and what could not be " +
        "detected is warned, not assumed. The map schedules nothing, starts nothing, and grants nothing.";
}

public static class SkeletonBatchDependencyEdgeKinds
{
    public const string ExplicitBlock = "explicit-block";
    public const string FootprintOverlap = "footprint-overlap";
}

public sealed record SkeletonBatchMapOutcome
{
    public required bool Succeeded { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public string MapId { get; init; } = string.Empty;
    public DateTimeOffset DetectedAtUtc { get; init; }
    public SkeletonBatchMap? Map { get; init; }
}

/// <summary>A stored map read back from durable evidence, integrity re-verified at read time.</summary>
public sealed record SkeletonBatchMapRecord
{
    public required string MapId { get; init; }
    public required DateTimeOffset DetectedAtUtc { get; init; }
    public required string RequestedByUserId { get; init; }
    public required SkeletonBatchMap Map { get; init; }
    public required string RecordedSha256 { get; init; }
    public required string Sha256OnDisk { get; init; }
    public bool Verified => string.Equals(RecordedSha256, Sha256OnDisk, StringComparison.Ordinal);
}

/// <summary>
/// Detects the dependency map for a batch of tickets and persists it as
/// hash-sealed durable evidence. Detection grants nothing: no runs start, no
/// gates move, and reads re-verify integrity like every other record.
/// </summary>
public interface ISkeletonBatchMapService
{
    /// <summary>Returns null when the project is unknown; an explicit failure outcome otherwise (e.g. a ticket outside the project, named).</summary>
    Task<SkeletonBatchMapOutcome?> DetectAsync(int projectId, IReadOnlyList<long> ticketIds, string requestedByUserId, CancellationToken cancellationToken = default);

    Task<SkeletonBatchMapRecord?> GetAsync(int projectId, string mapId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Detects dependency edges deterministically from ticket evidence. Pure by
/// design: the same tickets always yield the same map, so a persisted map can be
/// re-derived and checked — the same trust-but-verify treatment as everything else.
///
/// Footprint-overlap edges express a serialization NEED, not a semantic order:
/// the direction is chosen deterministically (lower ticket id first) and the
/// reason says so. The human can override the order by declaring an explicit
/// block the other way — declared evidence outranks predicted evidence.
/// </summary>
public static class SkeletonBatchDependencyDetector
{
    public static SkeletonBatchMap Detect(int projectId, IReadOnlyList<ProjectTicket> tickets)
    {
        var ordered = tickets.OrderBy(ticket => ticket.Id).ToList();
        var ticketIds = ordered.Select(ticket => ticket.Id).ToHashSet();
        var edges = new List<SkeletonBatchDependencyEdge>();
        var warnings = new List<string>();

        // Explicit blocks — the human's declared ordering.
        foreach (var ticket in ordered)
        {
            foreach (var blockedById in ParseTicketIds(ticket.BlockedByTicketIds))
            {
                if (blockedById == ticket.Id)
                {
                    warnings.Add($"Ticket {ticket.Id} declares itself as a blocker — ignored.");
                    continue;
                }

                if (!ticketIds.Contains(blockedById))
                {
                    warnings.Add(
                        $"Ticket {ticket.Id} declares blocked-by {blockedById}, which is not in this batch — " +
                        "the edge is dropped here, but the dependency still exists outside the batch.");
                    continue;
                }

                edges.Add(new SkeletonBatchDependencyEdge
                {
                    FromTicketId = blockedById,
                    ToTicketId = ticket.Id,
                    Kind = SkeletonBatchDependencyEdgeKinds.ExplicitBlock,
                    Reason = $"Ticket {ticket.Id} declares it is blocked by ticket {blockedById} (BlockedByTicketIds)."
                });
            }
        }

        // Predicted footprint overlap — two tickets naming the same files must
        // not run concurrently. Skipped where an explicit edge already orders
        // the pair: declared evidence outranks predicted evidence.
        var footprints = ordered.ToDictionary(ticket => ticket.Id, ticket => ParseFootprint(ticket.LinkedFilePaths));
        foreach (var ticket in ordered.Where(candidate => footprints[candidate.Id].Count == 0))
        {
            warnings.Add(
                $"Ticket {ticket.Id} has no linked file paths — its footprint is unknown, so overlap with it is " +
                "undetectable. It is treated as independent, and that assumption is this warning.");
        }

        for (var first = 0; first < ordered.Count; first++)
        {
            for (var second = first + 1; second < ordered.Count; second++)
            {
                var firstTicket = ordered[first];
                var secondTicket = ordered[second];
                if (edges.Any(edge => Connects(edge, firstTicket.Id, secondTicket.Id)))
                    continue;

                var shared = footprints[firstTicket.Id].Intersect(footprints[secondTicket.Id], StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (shared.Count == 0)
                    continue;

                edges.Add(new SkeletonBatchDependencyEdge
                {
                    FromTicketId = firstTicket.Id,
                    ToTicketId = secondTicket.Id,
                    Kind = SkeletonBatchDependencyEdgeKinds.FootprintOverlap,
                    Reason =
                        $"Tickets {firstTicket.Id} and {secondTicket.Id} both predict changes to {shared.Count} shared file(s). " +
                        "They must be serialized; the order (lower ticket id first) is deterministic, not semantic — " +
                        "declare an explicit block to order them deliberately.",
                    SharedPaths = shared
                });
            }
        }

        return new SkeletonBatchMap
        {
            ProjectId = projectId,
            TicketIds = ordered.Select(ticket => ticket.Id).ToList(),
            Edges = edges,
            Warnings = warnings
        };
    }

    private static bool Connects(SkeletonBatchDependencyEdge edge, long ticketA, long ticketB) =>
        (edge.FromTicketId == ticketA && edge.ToTicketId == ticketB) ||
        (edge.FromTicketId == ticketB && edge.ToTicketId == ticketA);

    public static IReadOnlyList<long> ParseTicketIds(string? blockedByTicketIds) =>
        (blockedByTicketIds ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => long.TryParse(token.TrimStart('#'), out var ticketId) ? ticketId : (long?)null)
            .Where(ticketId => ticketId.HasValue)
            .Select(ticketId => ticketId!.Value)
            .Distinct()
            .ToList();

    public static IReadOnlyList<string> ParseFootprint(string? linkedFilePaths) =>
        (linkedFilePaths ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Replace('\\', '/').TrimStart('/'))
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
