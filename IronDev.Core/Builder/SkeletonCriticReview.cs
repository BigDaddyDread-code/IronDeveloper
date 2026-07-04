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
    /// <summary>The durable finding id — what a disposition answers (P1-3).</summary>
    public required string FindingId { get; init; }

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

    /// <summary>P1-2: the ground truth the auditor established independently — every check, passed or failed.</summary>
    public SkeletonGroundTruthVerification? GroundTruth { get; init; }

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

// ── P1-2: trust but verify — the auditor pulls ground truth ─────────────────

/// <summary>The canonical ground-truth check names — shared by the verifier that runs them and the canary corpus that expects them.</summary>
public static class SkeletonGroundTruthCheckNames
{
    public const string PackageHash = "package-hash-matches-halt-announcement";
    public const string InternalConsistency = "package-internally-consistent";
    public const string CommandEvidence = "claimed-command-evidence-on-disk";
    public const string CriterionCoverage = "criterion-coverage-record-honest";
    public const string ReExecution = "claims-reproduce-on-independent-re-execution";
}

/// <summary>One ground-truth check: what the package claims versus what the evidence shows.</summary>
public sealed record SkeletonGroundTruthCheck
{
    public required string CheckName { get; init; }
    public required bool Passed { get; init; }
    public string Expected { get; init; } = string.Empty;
    public string Actual { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    /// <summary>A failed check that undermines the package's core claim (tamper, non-reproduction) blocks; degraded verifiability does not.</summary>
    public bool BlocksMerge { get; init; }
}

/// <summary>
/// The auditor's independently established ground truth for a work package.
/// Every check names expected vs actual — a mismatch is review material by
/// construction, never a judgment call.
/// </summary>
public sealed record SkeletonGroundTruthVerification
{
    public IReadOnlyList<SkeletonGroundTruthCheck> Checks { get; init; } = [];
    public IReadOnlyList<SkeletonGroundTruthCheck> Mismatches => Checks.Where(check => !check.Passed).ToList();
    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "Ground-truth verification is evidence, not judgment: it compares the package's claims against " +
        "durable evidence and independent re-execution. It grants nothing and blocks nothing by itself — " +
        "mismatches enter the critic review as findings, and the human gate stays separate.";
}

/// <summary>
/// Establishes ground truth for a work package independently of the package's own
/// claims: re-hashes the package against the hash announced at halt, checks the
/// claimed command evidence exists, checks the package for internal contradictions,
/// and re-executes the proposed changes plus authored tests in a fresh disposable
/// workspace. The verifier is the deterministic harness AROUND the critic — the
/// boxed critic agent itself remains review-only with RunTool and MutateSource
/// forbidden; it consumes this verification as evidence.
/// </summary>
public interface ISkeletonCriticGroundTruthVerifier
{
    Task<SkeletonGroundTruthVerification> VerifyAsync(
        string runId,
        SkeletonCriticPackage package,
        string packagePath,
        string packageSha256,
        CancellationToken cancellationToken = default);
}
