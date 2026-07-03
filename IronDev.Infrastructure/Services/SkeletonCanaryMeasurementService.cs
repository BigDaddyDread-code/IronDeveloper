using System.Text.Json;
using IronDev.Core.Builder;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// P1-6 — runs the canary corpus on demand and persists the catch-rate as
/// hash-sealed durable evidence: measurement.json plus a sha256 sidecar, and
/// every read recomputes the hash — a rewritten record is reported unverified,
/// never silently served.
///
/// The sandbox for independent re-execution comes from CriticCanary:SandboxRepoPath.
/// Without one, the corpus runs degraded and the record says so
/// (ReExecutionAvailable=false, catch-rate honestly lower) — a weaker
/// measurement is recorded as weaker, never dressed up.
///
/// Boundary: a measurement is evidence, not authority. This service grants
/// nothing, widens no autonomy envelope, and feeds no gate — it measures the
/// net and writes the number down.
/// </summary>
public sealed class SkeletonCanaryMeasurementService : ISkeletonCanaryMeasurementService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ISkeletonCriticCanaryRunner _runner;
    private readonly IConfiguration _configuration;

    public SkeletonCanaryMeasurementService(ISkeletonCriticCanaryRunner runner, IConfiguration configuration)
    {
        _runner = runner;
        _configuration = configuration;
    }

    public async Task<SkeletonCanaryMeasurement> MeasureAsync(
        string requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        var sandboxRepoPath = _configuration["CriticCanary:SandboxRepoPath"];
        var reExecutionAvailable = !string.IsNullOrWhiteSpace(sandboxRepoPath) && Directory.Exists(sandboxRepoPath);

        var corpus = await _runner.RunAsync(new SkeletonCanaryRunOptions
        {
            SandboxRepoPath = reExecutionAvailable ? sandboxRepoPath : null
        }, cancellationToken).ConfigureAwait(false);

        var measuredAtUtc = DateTimeOffset.UtcNow;
        var measurement = new SkeletonCanaryMeasurement
        {
            MeasurementId = $"canary-measure-{measuredAtUtc:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..48],
            MeasuredAtUtc = measuredAtUtc,
            RequestedByUserId = requestedByUserId,
            CanaryCount = corpus.CanaryCount,
            CaughtCount = corpus.CaughtCount,
            CatchRate = corpus.CatchRate,
            ControlClean = corpus.ControlClean,
            ReExecutionAvailable = reExecutionAvailable,
            Results = corpus.Results
        };

        var json = JsonSerializer.Serialize(measurement, JsonOptions);
        var measurementPath = MeasurementPath(measurement.MeasurementId);
        Directory.CreateDirectory(Path.GetDirectoryName(measurementPath)!);
        await File.WriteAllTextAsync(measurementPath, json, cancellationToken).ConfigureAwait(false);

        // The seal: the hash of what was written, recorded beside it. Readers
        // recompute — the sidecar is a claim, and claims get verified here.
        var hash = ComputeSha256(await File.ReadAllBytesAsync(measurementPath, cancellationToken).ConfigureAwait(false));
        await File.WriteAllTextAsync(HashPath(measurement.MeasurementId), hash, cancellationToken).ConfigureAwait(false);

        return measurement;
    }

    public async Task<SkeletonCanaryMeasurementRecord?> GetAsync(
        string measurementId,
        CancellationToken cancellationToken = default)
    {
        var measurementPath = MeasurementPath(measurementId);
        var hashPath = HashPath(measurementId);
        if (!File.Exists(measurementPath) || !File.Exists(hashPath))
            return null;

        var bytes = await File.ReadAllBytesAsync(measurementPath, cancellationToken).ConfigureAwait(false);
        var measurement = JsonSerializer.Deserialize<SkeletonCanaryMeasurement>(
            System.Text.Encoding.UTF8.GetString(bytes), JsonOptions);
        if (measurement is null)
            return null;

        return new SkeletonCanaryMeasurementRecord
        {
            Measurement = measurement,
            RecordedSha256 = (await File.ReadAllTextAsync(hashPath, cancellationToken).ConfigureAwait(false)).Trim(),
            Sha256OnDisk = ComputeSha256(bytes)
        };
    }

    public async Task<IReadOnlyList<SkeletonCanaryMeasurementSummary>> ListAsync(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var root = MeasurementsRoot();
        if (!Directory.Exists(root))
            return [];

        var summaries = new List<SkeletonCanaryMeasurementSummary>();
        foreach (var file in Directory.EnumerateFiles(root, "*.json"))
        {
            var record = await GetAsync(Path.GetFileNameWithoutExtension(file), cancellationToken).ConfigureAwait(false);
            if (record is null)
                continue;

            summaries.Add(new SkeletonCanaryMeasurementSummary
            {
                MeasurementId = record.Measurement.MeasurementId,
                MeasuredAtUtc = record.Measurement.MeasuredAtUtc,
                CatchRate = record.Measurement.CatchRate,
                CanaryCount = record.Measurement.CanaryCount,
                CaughtCount = record.Measurement.CaughtCount,
                ControlClean = record.Measurement.ControlClean,
                ReExecutionAvailable = record.Measurement.ReExecutionAvailable,
                Verified = record.Verified
            });
        }

        return summaries
            .OrderByDescending(summary => summary.MeasuredAtUtc)
            .Take(take)
            .ToList();
    }

    private string MeasurementsRoot()
    {
        var configured = _configuration["DisposableBuild:EvidenceRoot"] ?? _configuration["LocalTest:LogsRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableEvidence")
            : configured;
        return Path.Combine(root, "critic-canary-measurements");
    }

    private string MeasurementPath(string measurementId) => Path.Combine(MeasurementsRoot(), $"{measurementId}.json");

    private string HashPath(string measurementId) => Path.Combine(MeasurementsRoot(), $"{measurementId}.sha256");

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
}
