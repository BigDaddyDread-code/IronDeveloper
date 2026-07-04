namespace IronDev.Core.Builder;

/// <summary>
/// P2-3 — the batch skeleton run: a sealed plan, executed as single-ticket
/// walking-skeleton loops. Eligibility is edge-based: a ticket's run starts only
/// when every ticket it depends on has APPLIED — a halt at an upstream human
/// gate holds its dependents, and only its dependents: independent branches
/// keep flowing. Halt is not upstream-satisfaction; only Applied is.
///
/// Boundary — composition only, and the composition is one verb: the batch can
/// START a ticket's run. It can never continue, approve, disposition, or apply
/// one — every gate stays human, per ticket, exactly where Phase 0/1 put it.
/// Advancing the batch is REQUESTED, never self-acting: there is no scheduler
/// deciding on its own that the world moved.
/// </summary>
public sealed record SkeletonBatchTicketStatus
{
    public required long TicketId { get; init; }
    public required int Wave { get; init; }

    /// <summary>The skeleton run started for this ticket, once one exists.</summary>
    public string RunId { get; init; } = string.Empty;
    public string RunStatus { get; init; } = string.Empty;

    /// <summary>True when every upstream dependency has Applied and no run has started yet.</summary>
    public bool Eligible { get; init; }

    /// <summary>The upstream tickets this one is still waiting on, each with its current state — the block is named, never vague.</summary>
    public IReadOnlyList<string> WaitingOn { get; init; } = [];
}

public sealed record SkeletonBatchRunStatus
{
    public required string BatchId { get; init; }
    public required string PlanId { get; init; }
    public required int ProjectId { get; init; }
    public required string RequestedByUserId { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public IReadOnlyList<SkeletonBatchTicketStatus> Tickets { get; init; } = [];

    /// <summary>True when every ticket's run reached Applied — the whole batch landed.</summary>
    public bool BatchComplete => Tickets.Count > 0 && Tickets.All(ticket => ticket.RunStatus == "Applied");

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A batch run composes single-ticket loops and adds no new authority: it can start a ticket's run " +
        "when the tickets it depends on have applied, and nothing else. It never continues, approves, " +
        "dispositions, or applies anything — every gate stays human, per ticket. Advance is requested, " +
        "never self-acting.";
}

public sealed record SkeletonBatchRunOutcome
{
    public required bool Succeeded { get; init; }
    public string FailureReason { get; init; } = string.Empty;

    /// <summary>Ticket runs started by this call (ticket id → run id).</summary>
    public IReadOnlyDictionary<long, string> StartedRuns { get; init; } = new Dictionary<long, string>();

    public SkeletonBatchRunStatus? Status { get; init; }
}

/// <summary>
/// Starts and advances batch runs over a sealed, schedulable plan. Both seals in
/// the provenance chain (the plan and the map it derives from) are verified
/// before anything starts — a batch must not execute laundered evidence.
/// </summary>
public interface ISkeletonBatchRunService
{
    /// <summary>Creates the batch and starts every ticket that is eligible immediately (wave 1).</summary>
    Task<SkeletonBatchRunOutcome> StartAsync(int projectId, string planId, string requestedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Starts every ticket that became eligible since the last advance (upstreams applied). Idempotent for already-started tickets.</summary>
    Task<SkeletonBatchRunOutcome> AdvanceAsync(int projectId, string batchId, CancellationToken cancellationToken = default);

    /// <summary>Derived live status: per-ticket run state, eligibility, and named waits. Read-only.</summary>
    Task<SkeletonBatchRunStatus?> GetAsync(int projectId, string batchId, CancellationToken cancellationToken = default);
}
