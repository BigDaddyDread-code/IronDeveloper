namespace IronDev.Core.Builder;

/// <summary>
/// P1-6 — the catch-rate as durable evidence. One measurement = one on-demand
/// run of the canary corpus through the real critic path, persisted with its
/// own hash so later readers can verify the record was not rewritten.
///
/// Boundary — eval earns autonomy; a measurement is how it is earned, but the
/// measurement itself is EVIDENCE, NOT AUTHORITY: it grants nothing, widens no
/// envelope, satisfies no policy, and feeds no gate automatically. Turning the
/// autonomy dial on the strength of this number is a separate governed human
/// decision, made with this record in hand.
/// </summary>
public sealed record SkeletonCanaryMeasurement
{
    public required string MeasurementId { get; init; }
    public required DateTimeOffset MeasuredAtUtc { get; init; }
    public required string RequestedByUserId { get; init; }

    public required int CanaryCount { get; init; }
    public required int CaughtCount { get; init; }
    public required double CatchRate { get; init; }
    public required bool ControlClean { get; init; }

    /// <summary>
    /// Whether independent re-execution had a sandbox available. A measurement
    /// without it is honestly weaker — the record says so instead of hiding it.
    /// </summary>
    public required bool ReExecutionAvailable { get; init; }

    public IReadOnlyList<SkeletonCanaryResult> Results { get; init; } = [];
    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A catch-rate measurement is evidence, not authority. It grants nothing, widens no autonomy " +
        "envelope, satisfies no policy, and feeds no gate automatically — the dial is turned by a " +
        "separate governed human decision made with this record in hand.";
}

/// <summary>A stored measurement read back from durable evidence, with its integrity re-verified at read time.</summary>
public sealed record SkeletonCanaryMeasurementRecord
{
    public required SkeletonCanaryMeasurement Measurement { get; init; }

    /// <summary>The hash recorded when the measurement was persisted.</summary>
    public required string RecordedSha256 { get; init; }

    /// <summary>Recomputed from the file at read time — verification, not recitation.</summary>
    public required string Sha256OnDisk { get; init; }

    public bool Verified => string.Equals(RecordedSha256, Sha256OnDisk, StringComparison.Ordinal);
}

public sealed record SkeletonCanaryMeasurementSummary
{
    public required string MeasurementId { get; init; }
    public required DateTimeOffset MeasuredAtUtc { get; init; }
    public required double CatchRate { get; init; }
    public required int CanaryCount { get; init; }
    public required int CaughtCount { get; init; }
    public required bool ControlClean { get; init; }
    public required bool ReExecutionAvailable { get; init; }
    public required bool Verified { get; init; }
}

/// <summary>
/// Runs the canary corpus on demand and persists the result as hash-sealed
/// durable evidence. Read paths re-verify integrity; a tampered record is
/// reported as unverified, never silently served.
/// </summary>
public interface ISkeletonCanaryMeasurementService
{
    Task<SkeletonCanaryMeasurement> MeasureAsync(string requestedByUserId, CancellationToken cancellationToken = default);
    Task<SkeletonCanaryMeasurementRecord?> GetAsync(string measurementId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SkeletonCanaryMeasurementSummary>> ListAsync(int take = 20, CancellationToken cancellationToken = default);
}
