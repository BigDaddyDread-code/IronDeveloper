namespace IronDev.Core.Builder;

/// <summary>
/// REVISE-1 — finding-driven revision. At the human gate, the human may direct
/// the Builder to revise the proposal under review instead of approving it:
/// cited critic findings plus a written human instruction produce a NEW
/// proposal, a fresh attempt-scoped build/test, and a NEW critic package at the
/// SAME human gate.
///
/// Boundary: a revision is human-directed, proposal-shaped work — never
/// authority. It cannot approve, continue, or apply anything. The revised
/// package needs its OWN critic review, its own finding dispositions, and its
/// own hash-bound accepted approval; a review or approval of the superseded
/// package satisfies nothing. Attempts are bounded by explicit configuration
/// (SkeletonRevision:MaxAttempts, default 0 = off), and every attempt's
/// evidence and events are preserved — the superseded package stays on disk as
/// history, never erased.
/// </summary>
public sealed record SkeletonRunRevisionRequest
{
    /// <summary>
    /// The critic findings the human is answering with this revision. Every id
    /// must exist on a critic review of the CURRENT package and be
    /// undispositioned; every other undispositioned finding must already carry
    /// a disposition — a revision may not leave any finding unanswered behind it.
    /// </summary>
    public required IReadOnlyList<string> FindingIds { get; init; }

    /// <summary>
    /// Required. The human's written revision instruction — the Builder revises
    /// from this instruction and the cited finding ids, not from the critic's
    /// authority. A revision without an instruction is a dismissal, and
    /// dismissals are not decisions.
    /// </summary>
    public required string Reason { get; init; }

    public required string RequestedByUserId { get; init; }
}

/// <summary>The orchestrator's request for one bounded revision proposal.</summary>
public sealed record SkeletonRevisionContext
{
    /// <summary>The revision ordinal for this run (1 = first revision).</summary>
    public required int AttemptNumber { get; init; }

    /// <summary>The finding ids the human cited — identifiers, not critic text.</summary>
    public required IReadOnlyList<string> FindingIds { get; init; }

    /// <summary>The human's written revision instruction.</summary>
    public required string Instruction { get; init; }

    /// <summary>
    /// The proposal under revision, read from the hash-verified critic package
    /// on disk — durable evidence, never the requester's copy.
    /// </summary>
    public required IReadOnlyList<SkeletonCriticPackageChange> PreviousChanges { get; init; }
}
