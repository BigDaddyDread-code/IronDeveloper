using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// P2-6 — computes the risk-tier recommendation for a halted run's gate,
/// recommendation-only. Everything derives from evidence that already verifies
/// itself: the run report (package hash recomputed, staleness named,
/// dispositions counted), the critic package (footprint paths), and the latest
/// canary measurement — the HARD input. Every check lands in Reasons, pass or
/// fail: advice with unnamed reasons is a hunch.
///
/// The measurement preconditions are absolute: fresh (RiskGate:MaxMeasurementAgeHours,
/// default 24), integrity-verified, catch-rate at RiskGate:RequiredCatchRate
/// (default 1.0), control clean, and re-execution available. Eval earns
/// autonomy — no eval, no recommendation beyond human judgment.
///
/// Boundary: a recommendation is advice, not approval — policy cannot click.
/// This service reads reports, packages, and measurements; it publishes
/// nothing, transitions nothing, and touches no approvals.
/// </summary>
public sealed class SkeletonGateRecommendationService : ISkeletonGateRecommendationService
{
    private readonly ITicketSkeletonRunService _skeletonRuns;
    private readonly ISkeletonCanaryMeasurementService _measurements;
    private readonly IConfiguration _configuration;

    public SkeletonGateRecommendationService(
        ITicketSkeletonRunService skeletonRuns,
        ISkeletonCanaryMeasurementService measurements,
        IConfiguration configuration)
    {
        _skeletonRuns = skeletonRuns;
        _measurements = measurements;
        _configuration = configuration;
    }

    public async Task<SkeletonGateRecommendation?> RecommendAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var report = await _skeletonRuns.GetRunReportAsync(projectId, ticketId, runId, cancellationToken).ConfigureAwait(false);
        if (report is null)
            return null;

        var reasons = new List<string>();
        var humanRequired = false;

        void Check(bool passed, string passText, string failText)
        {
            reasons.Add(passed ? $"[pass] {passText}" : $"[human] {failText}");
            humanRequired |= !passed;
        }

        // ── The hard input: the measured net ─────────────────────────────────
        var measurementInput = await LatestMeasurementAsync(cancellationToken).ConfigureAwait(false);
        if (measurementInput is null)
        {
            reasons.Add("[human] The critic net has no usable catch-rate measurement. Eval earns autonomy — no eval, no recommendation.");
            humanRequired = true;
        }
        else
        {
            var requiredCatchRate = ReadDouble("RiskGate:RequiredCatchRate", 1.0);
            var maxAgeHours = ReadDouble("RiskGate:MaxMeasurementAgeHours", 24);
            var age = DateTimeOffset.UtcNow - measurementInput.MeasuredAtUtc;

            Check(measurementInput.Verified,
                $"The catch-rate measurement {measurementInput.MeasurementId} reads back with its seal intact.",
                $"The catch-rate measurement {measurementInput.MeasurementId} failed integrity verification — a broken seal advises nothing.");
            Check(measurementInput.CatchRate >= requiredCatchRate,
                $"Catch-rate {measurementInput.CatchRate:0.###} meets the required {requiredCatchRate:0.###}.",
                $"Catch-rate {measurementInput.CatchRate:0.###} is below the required {requiredCatchRate:0.###} — the net has known holes.");
            Check(measurementInput.ControlClean,
                "The honest control came back clean — the net catches defects, not everything.",
                "The honest control was flagged — a net that flags everything catches nothing, and its advice is noise.");
            Check(measurementInput.ReExecutionAvailable,
                "The net was measured with independent re-execution available.",
                "The net was measured WITHOUT re-execution — a degraded measurement cannot underwrite a recommendation.");
            Check(age <= TimeSpan.FromHours(maxAgeHours),
                $"The measurement is fresh ({age.TotalHours:0.#}h old, limit {maxAgeHours:0.#}h).",
                $"The measurement is stale ({age.TotalHours:0.#}h old, limit {maxAgeHours:0.#}h) — the net must be re-measured.");
        }

        // ── The run's own evidence ────────────────────────────────────────────
        Check(report.CriticPackage is { HashVerified: true },
            "The critic package on disk matches the hash announced at halt.",
            "The critic package does not verify against the halt announcement — nothing about this run should be recommended.");
        Check(report.CriticPackage is { UncoveredCriterionCount: 0 },
            "Every acceptance criterion has a covering test.",
            $"{report.CriticPackage?.UncoveredCriterionCount ?? 0} acceptance criteria have no covering test — owning a coverage hole is a human decision.");

        var findingIds = report.CriticReviews.SelectMany(review => review.FindingIds).Distinct(StringComparer.Ordinal).ToList();
        var dispositioned = report.FindingDispositions.Select(disposition => disposition.FindingId).ToHashSet(StringComparer.Ordinal);
        Check(findingIds.All(dispositioned.Contains),
            "Every critic finding carries a human disposition.",
            $"{findingIds.Count(findingId => !dispositioned.Contains(findingId))} critic finding(s) await a human disposition.");
        Check(report.CriticReviews.All(review => review.BlockingFindingCount == 0),
            "No critic review recorded a blocking finding.",
            "A critic review recorded a blocking finding — a recommendation cannot speak over the critic's strongest objection.");
        Check(report.CriticReviews.All(review => review.GroundTruthMismatchCount == 0),
            "Ground-truth verification found no claim/evidence mismatches.",
            "Ground-truth verification found mismatches — evidence that disagrees with its courier is a human's problem.");
        Check(!report.Gaps.Any(gap => gap.Contains("no longer exists", StringComparison.Ordinal)),
            "No upstream apply has invalidated this run's evidence.",
            "This run is stale after an upstream apply — its evidence describes a source that no longer exists.");

        // ── The footprint ─────────────────────────────────────────────────────
        var package = await _skeletonRuns.GetCriticPackageAsync(projectId, ticketId, runId, cancellationToken).ConfigureAwait(false);
        var footprint = package?.Changes
            .Select(change => (change.FilePath ?? string.Empty).Replace('\\', '/').TrimStart('/'))
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        var maxFootprint = ReadInt("RiskGate:MaxLowRiskFootprint", 5);
        Check(footprint.Count > 0 && footprint.Count <= maxFootprint,
            $"The footprint is bounded: {footprint.Count} file(s), limit {maxFootprint}.",
            footprint.Count == 0
                ? "The footprint could not be read — an unknowable mutation is a human's problem."
                : $"The footprint touches {footprint.Count} file(s), above the low-risk limit of {maxFootprint}.");

        var sensitivePrefixes = (_configuration["RiskGate:SensitivePathPrefixes"] ?? "Database/,.github/")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sensitiveHits = footprint
            .Where(path => sensitivePrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Check(sensitiveHits.Count == 0,
            "The footprint touches no sensitive paths.",
            $"The footprint touches sensitive path(s): {string.Join(", ", sensitiveHits)} — always a human decision.");

        return new SkeletonGateRecommendation
        {
            RunId = runId,
            Tier = humanRequired ? SkeletonGateRiskTier.HumanRequired : SkeletonGateRiskTier.Low,
            Recommendation = humanRequired
                ? SkeletonGateRecommendationKinds.HumanJudgmentRequired
                : SkeletonGateRecommendationKinds.PolicyWouldApprove,
            Reasons = reasons,
            MeasurementInput = measurementInput
        };
    }

    private async Task<SkeletonGateMeasurementInput?> LatestMeasurementAsync(CancellationToken cancellationToken)
    {
        var summaries = await _measurements.ListAsync(1, cancellationToken).ConfigureAwait(false);
        var latest = summaries.FirstOrDefault();
        if (latest is null)
            return null;

        return new SkeletonGateMeasurementInput
        {
            MeasurementId = latest.MeasurementId,
            CatchRate = latest.CatchRate,
            ControlClean = latest.ControlClean,
            ReExecutionAvailable = latest.ReExecutionAvailable,
            Verified = latest.Verified,
            MeasuredAtUtc = latest.MeasuredAtUtc
        };
    }

    private double ReadDouble(string key, double fallback) =>
        double.TryParse(_configuration[key], out var parsed) ? parsed : fallback;

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_configuration[key], out var parsed) && parsed > 0 ? parsed : fallback;
}
