namespace IronDev.Core.Builder;

/// <summary>
/// P1-3 — finding → disposition, enforced. A critic finding cannot be ignored:
/// before a halted run may continue, every recorded finding must carry a human
/// disposition. A finding is not a veto — the human may accept the risk, defer
/// the fix, or reject the finding outright — but the decision must be made,
/// named, reasoned, and durable.
/// </summary>
public enum SkeletonFindingDispositionKind
{
    /// <summary>Proceed; the named risk is consciously owned by the decider.</summary>
    AcceptRisk = 1,

    /// <summary>Proceed; the fix is deferred to named follow-up work.</summary>
    FixInFollowUp = 2,

    /// <summary>The finding is judged wrong or inapplicable — the reason must say why.</summary>
    Reject = 3,

    /// <summary>
    /// REVISE-1: the finding was answered by a human-directed revision that
    /// built green and replaced the gate package. Recorded ONLY by the governed
    /// revision path after the revision succeeds — a human cannot claim a
    /// revision that never ran, and the disposition surface refuses this kind.
    /// </summary>
    AddressedByRevision = 4
}

/// <summary>
/// Boundary — a disposition is a human decision ABOUT a finding; it is not
/// approval. Dispositioning every finding removes the finding blockage only:
/// continuation still requires its own live accepted approval, and apply still
/// re-verifies everything. Nothing here grants, satisfies, or continues anything.
/// </summary>
public sealed record SkeletonFindingDispositionRequest
{
    public required int ProjectId { get; init; }
    public required long TicketId { get; init; }
    public required string RunId { get; init; }

    /// <summary>The finding being dispositioned — it must exist on a review recorded against this run.</summary>
    public required string FindingId { get; init; }

    public required SkeletonFindingDispositionKind Disposition { get; init; }

    /// <summary>Required. A disposition without a reason is a dismissal, and dismissals are not decisions.</summary>
    public required string Reason { get; init; }

    public required string DecidedByUserId { get; init; }
}

public sealed record SkeletonFindingDispositionOutcome
{
    public required bool Succeeded { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public string FindingId { get; init; } = string.Empty;
    public string Disposition { get; init; } = string.Empty;
    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A disposition is a human decision about a critic finding; it is not approval. " +
        "Continuation still requires its own live accepted approval through the governed surface.";
}

/// <summary>
/// Records human dispositions for critic findings as durable run events. The
/// service validates that the finding exists on a review recorded against the
/// run and that the decision carries a reason — it grants nothing.
/// </summary>
public interface ISkeletonFindingDispositionService
{
    /// <summary>Returns null when the run does not belong to the ticket/project; an explicit failure outcome otherwise.</summary>
    Task<SkeletonFindingDispositionOutcome?> RecordAsync(SkeletonFindingDispositionRequest request, CancellationToken cancellationToken = default);
}
