namespace IronDev.Core.Builder;

/// <summary>
/// P2-6 — risk-tiered gating, recommendation-only. Policy classifies a halted
/// run's gate by risk tier and records what it WOULD do — and that is all it
/// does. A recommendation is advice, not approval: policy cannot click, cannot
/// record an accepted approval, cannot continue or apply anything. The human
/// gate remains the only approval authority.
///
/// The P1-6 catch-rate is a HARD input: without a fresh, verified, clean
/// measurement of the critic net — with re-execution available — policy
/// recommends nothing but human judgment. Eval earns autonomy; no eval, no
/// recommendation. Widening what policy may one day DO with a recommendation
/// is a separate governed decision this contract deliberately cannot express.
/// </summary>
public enum SkeletonGateRiskTier
{
    Unknown = 0,

    /// <summary>Every named low-risk condition holds AND the net is measured: policy would approve — advisorily.</summary>
    Low = 1,

    /// <summary>At least one named condition demands a human: coverage holes, findings, footprint, staleness, or an unmeasured net.</summary>
    HumanRequired = 2
}

public static class SkeletonGateRecommendationKinds
{
    /// <summary>Advice only. Rendering, storing, or reading this value grants nothing.</summary>
    public const string PolicyWouldApprove = "policy-would-approve-advisory-only";

    public const string HumanJudgmentRequired = "human-judgment-required";
}

/// <summary>The measurement the recommendation leaned on — shown so the human can weigh the advice by its evidence.</summary>
public sealed record SkeletonGateMeasurementInput
{
    public string MeasurementId { get; init; } = string.Empty;
    public double CatchRate { get; init; }
    public bool ControlClean { get; init; }
    public bool ReExecutionAvailable { get; init; }
    public bool Verified { get; init; }
    public DateTimeOffset MeasuredAtUtc { get; init; }
}

public sealed record SkeletonGateRecommendation
{
    public required string RunId { get; init; }
    public required SkeletonGateRiskTier Tier { get; init; }
    public required string Recommendation { get; init; }

    /// <summary>Every check that produced this recommendation, pass or fail, in plain words — advice with unnamed reasons is a hunch.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>The hard input. Null when no usable measurement exists — which is itself why the tier is HumanRequired.</summary>
    public SkeletonGateMeasurementInput? MeasurementInput { get; init; }

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A gate recommendation is advice, not approval: policy cannot click. It grants nothing, records " +
        "no approval, continues nothing, and applies nothing — the human gate remains the only approval " +
        "authority, and letting policy act on its own advice would be a separate governed decision this " +
        "surface deliberately cannot express.";
}

/// <summary>
/// Computes the recommendation live from durable evidence: the run report
/// (hash-verified, staleness-aware, disposition-aware), the critic package
/// (footprint), and the latest canary measurement (the hard input). Read-only:
/// it publishes nothing, transitions nothing, and touches no approvals.
/// </summary>
public interface ISkeletonGateRecommendationService
{
    /// <summary>Returns null when the run does not belong to the ticket/project.</summary>
    Task<SkeletonGateRecommendation?> RecommendAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default);
}
