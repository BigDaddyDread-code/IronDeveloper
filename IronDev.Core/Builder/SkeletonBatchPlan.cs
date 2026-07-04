namespace IronDev.Core.Builder;

/// <summary>
/// P2-2 — the batch plan: the dependency map turned into execution waves.
/// Tickets in the same wave are independent and may run in parallel; a ticket
/// waits for every wave containing something it depends on. Cycles are NAMED
/// BLOCKERS the human resolves — the sequencer never breaks one by guessing.
///
/// Boundary: a plan is a PROPOSAL. It derives from a verified map, it is shown
/// to the human before anything runs, and it grants nothing — every per-ticket
/// gate stays exactly where Phase 0/1 put it, and running the plan is a
/// separate governed step (P2-3).
/// </summary>
public sealed record SkeletonBatchWave
{
    public required int WaveNumber { get; init; }
    public IReadOnlyList<long> TicketIds { get; init; } = [];
}

/// <summary>Tickets the sequencer could not place, with the residual edges that trap them — in plain words.</summary>
public sealed record SkeletonBatchCycleBlocker
{
    public IReadOnlyList<long> TicketIds { get; init; } = [];
    public required string Detail { get; init; }
}

public sealed record SkeletonBatchPlan
{
    public required int ProjectId { get; init; }

    /// <summary>Provenance: the sealed map this plan derives from.</summary>
    public required string MapId { get; init; }

    public IReadOnlyList<SkeletonBatchWave> Waves { get; init; } = [];
    public IReadOnlyList<SkeletonBatchCycleBlocker> CycleBlockers { get; init; } = [];

    /// <summary>Warnings carried forward from detection — the plan inherits the map's stated blind spots.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True only when every ticket found a wave. A plan with cycle blockers proposes nothing until the human resolves them.</summary>
    public bool Schedulable => CycleBlockers.Count == 0;

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A batch plan is a proposal derived from a verified dependency map. It grants nothing and runs " +
        "nothing: cycles are named blockers for the human, and every per-ticket gate stays where the " +
        "walking skeleton put it.";
}

public sealed record SkeletonBatchPlanOutcome
{
    public required bool Succeeded { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public string PlanId { get; init; } = string.Empty;
    public DateTimeOffset PlannedAtUtc { get; init; }
    public SkeletonBatchPlan? Plan { get; init; }
}

/// <summary>A stored plan read back from durable evidence, integrity re-verified at read time.</summary>
public sealed record SkeletonBatchPlanRecord
{
    public required string PlanId { get; init; }
    public required DateTimeOffset PlannedAtUtc { get; init; }
    public required string RequestedByUserId { get; init; }
    public required SkeletonBatchPlan Plan { get; init; }
    public required string RecordedSha256 { get; init; }
    public required string Sha256OnDisk { get; init; }
    public bool Verified => string.Equals(RecordedSha256, Sha256OnDisk, StringComparison.Ordinal);
}

/// <summary>
/// Sequences a sealed dependency map into a hash-sealed batch plan. The plan
/// derives ONLY from a map whose seal verifies — a plan must never be built on
/// evidence that changed after it was recorded. Planning grants nothing.
/// </summary>
public interface ISkeletonBatchPlanService
{
    Task<SkeletonBatchPlanOutcome> PlanAsync(int projectId, string mapId, string requestedByUserId, CancellationToken cancellationToken = default);

    Task<SkeletonBatchPlanRecord?> GetAsync(int projectId, string planId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns a dependency map into waves, deterministically (Kahn's algorithm;
/// ticket ids ascending within a wave; the same map always yields the same
/// plan). Whatever cannot be placed — cycle members and everything downstream
/// of them — is reported as a named blocker with the residual edges spelled
/// out. The sequencer never auto-breaks a cycle: choosing which declared
/// dependency to ignore is a human decision, not a graph heuristic.
/// </summary>
public static class SkeletonBatchSequencer
{
    public static SkeletonBatchPlan Sequence(SkeletonBatchMap map, string mapId)
    {
        var ticketIds = map.TicketIds.Distinct().OrderBy(ticketId => ticketId).ToList();
        var edges = map.Edges
            .Where(edge => ticketIds.Contains(edge.FromTicketId) && ticketIds.Contains(edge.ToTicketId))
            .ToList();

        var indegree = ticketIds.ToDictionary(ticketId => ticketId, _ => 0);
        foreach (var edge in edges)
            indegree[edge.ToTicketId]++;

        var placed = new HashSet<long>();
        var waves = new List<SkeletonBatchWave>();
        while (placed.Count < ticketIds.Count)
        {
            var ready = ticketIds
                .Where(ticketId => !placed.Contains(ticketId) && indegree[ticketId] == 0)
                .OrderBy(ticketId => ticketId)
                .ToList();
            if (ready.Count == 0)
                break;

            waves.Add(new SkeletonBatchWave { WaveNumber = waves.Count + 1, TicketIds = ready });
            foreach (var ticketId in ready)
            {
                placed.Add(ticketId);
                foreach (var edge in edges.Where(edge => edge.FromTicketId == ticketId))
                    indegree[edge.ToTicketId]--;
            }
        }

        var blockers = new List<SkeletonBatchCycleBlocker>();
        var residual = ticketIds.Where(ticketId => !placed.Contains(ticketId)).ToList();
        if (residual.Count > 0)
        {
            var residualEdges = edges
                .Where(edge => residual.Contains(edge.FromTicketId) && residual.Contains(edge.ToTicketId))
                .Select(edge => $"{edge.FromTicketId} → {edge.ToTicketId} ({edge.Kind}: {edge.Reason})")
                .ToList();

            blockers.Add(new SkeletonBatchCycleBlocker
            {
                TicketIds = residual,
                Detail =
                    $"Tickets {string.Join(", ", residual)} cannot be placed in any wave: their dependencies form a cycle " +
                    $"(or wait on one). Residual edges: {string.Join("; ", residualEdges)}. " +
                    "The sequencer never breaks a cycle by guessing — remove or re-declare one of these dependencies."
            });
        }

        return new SkeletonBatchPlan
        {
            ProjectId = map.ProjectId,
            MapId = mapId,
            Waves = waves,
            CycleBlockers = blockers,
            Warnings = map.Warnings
        };
    }
}
