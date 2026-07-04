using IronDev.Core.Governance;

namespace IronDev.Core.Builder;

/// <summary>
/// P2-4 — the mutation lease: two applies whose footprints overlap must take
/// turns. This is the E07/E08 library pull the batch loop finally needs — the
/// lease speaks the existing governance vocabulary
/// (MutationLeaseSurfaceKind.SourceApply, MutationLeaseState) so the E-block
/// converges instead of forking.
///
/// Boundary — a lease is a concurrency guard, NOT authority. Holding one grants
/// nothing: no approval, no continuation, no apply permission — every gate is
/// checked exactly as before. Its only power is to make two overlapping applies
/// take turns, and a refusal always names the holder.
/// </summary>
public sealed record SkeletonMutationLeaseRequest
{
    public required int ProjectId { get; init; }
    public required string RunId { get; init; }
    public required long TicketId { get; init; }

    /// <summary>The files this apply will touch — the approved package's change paths.</summary>
    public required IReadOnlyList<string> FootprintPaths { get; init; }

    public required string HolderRef { get; init; }
}

public sealed record SkeletonMutationLeaseOutcome
{
    public required bool Acquired { get; init; }
    public string LeaseId { get; init; } = string.Empty;

    /// <summary>On refusal: who holds the conflicting lease and which paths collide — named, never vague.</summary>
    public string RefusedBecause { get; init; } = string.Empty;
    public string HolderRunId { get; init; } = string.Empty;
    public IReadOnlyList<string> ConflictingPaths { get; init; } = [];

    /// <summary>Anything the acquisition did besides acquire — e.g. an expired lease that was ignored, by name.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A mutation lease is a concurrency guard, not authority: holding one grants nothing, and every " +
        "gate is checked exactly as before. Its only power is to make two overlapping applies take turns, " +
        "and a refusal always names the holder.";
}

/// <summary>
/// Footprint-scoped leases for the source-apply surface. Acquisition refuses on
/// overlap with an unexpired lease (holder named); expired leases are ignored
/// with a note, never silently. Release is idempotent — and the apply path
/// releases in a finally, so a blocked apply can never wedge the batch.
/// </summary>
public interface ISkeletonMutationLeaseService
{
    /// <summary>The one surface this lease store serves today.</summary>
    public const MutationLeaseSurfaceKind Surface = MutationLeaseSurfaceKind.SourceApply;

    Task<SkeletonMutationLeaseOutcome> TryAcquireAsync(SkeletonMutationLeaseRequest request, CancellationToken cancellationToken = default);

    Task ReleaseAsync(int projectId, string leaseId, CancellationToken cancellationToken = default);
}
