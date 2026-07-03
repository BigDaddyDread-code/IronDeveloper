namespace IronDev.Core.Builder;

/// <summary>
/// P1-1 — the critic actually reviews. The request to review a skeleton run's
/// critic package, made through the critic's own governed surface.
///
/// Boundary — critic independence is enforced BY CONTRACT, twice over:
/// (1) The requester names the run; it cannot hand the critic a curated copy of
///     the work. The critic PULLS the package itself from durable evidence —
///     trust but verify starts at the front door.
/// (2) There is no field through which team memory, conversation, or narrative
///     could reach the critic. Outside memory is not outside evidence: the
///     package and its evidence refs are proven facts; memory is a belief the
///     team formed, and it must not enter the critic's input.
/// A critic review is advisory. A finding is not a veto, review is not approval,
/// and the human gate remains a separate governed step.
/// </summary>
public sealed record SkeletonCriticReviewRequest
{
    public required int ProjectId { get; init; }
    public required long TicketId { get; init; }
    public required string RunId { get; init; }

    /// <summary>The human who requested the review — reviews are requested, never self-initiated by the orchestrator.</summary>
    public required string RequestedByUserId { get; init; }
}

/// <summary>One advisory finding, projected for the requester. The durable record lives in the agent-run audit store.</summary>
public sealed record SkeletonCriticReviewFindingDto
{
    public required string Severity { get; init; }
    public required string Title { get; init; }
    public required string Problem { get; init; }
    public required string WhyItMatters { get; init; }
    public required string RequiredFix { get; init; }
    public bool BlocksMerge { get; init; }
}

public sealed record SkeletonCriticReviewOutcome
{
    public required bool Succeeded { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public string CriticAgentRunId { get; init; } = string.Empty;
    public string ReviewId { get; init; } = string.Empty;
    public string Verdict { get; init; } = string.Empty;
    public IReadOnlyList<SkeletonCriticReviewFindingDto> Findings { get; init; } = [];
    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "Critic findings are advisory. A finding is not a veto, review is not approval, and " +
        "nothing here blocks, applies, or continues anything — dispositions and the human gate " +
        "remain separate governed steps.";
}

/// <summary>
/// Reviews a skeleton run's critic package with a live model, through the stored
/// manual-critic execution path (validation, audit envelope, persistence). The
/// service can create critic findings and nothing else: no approval, no halt
/// release, no source mutation, no memory.
/// </summary>
public interface ISkeletonCriticReviewService
{
    /// <summary>Returns null when the run does not belong to the ticket/project; an explicit failure outcome otherwise.</summary>
    Task<SkeletonCriticReviewOutcome?> ReviewAsync(SkeletonCriticReviewRequest request, CancellationToken cancellationToken = default);
}
