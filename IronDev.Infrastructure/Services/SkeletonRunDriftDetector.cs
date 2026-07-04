using System.Text.Json;
using IronDev.Core.Builder;
using IronDev.Core.RunReports;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// AG-3 (orchestrator diet) — P2-5 drift detection, lifted out of the
/// walking-skeleton orchestrator into its own collaborator. A halted run's
/// evidence describes the source as it was when the package was prepared; if
/// another run APPLIED overlapping changes to the project after that moment, the
/// evidence describes a source that no longer exists and the run is stale.
///
/// Pure over durable evidence: the upstream's SkeletonApplied event timestamp
/// versus this run's CriticReviewPackageReady timestamp, and the footprint
/// intersection of both critic packages on disk. An upstream whose footprint
/// cannot be read is treated as overlapping — conservatively, and by name.
/// Returns the named staleness reason, or null.
/// </summary>
public sealed class SkeletonRunDriftDetector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IRunEventStore _events;

    public SkeletonRunDriftDetector(IRunEventStore events)
    {
        _events = events;
    }

    public async Task<string?> DetectAsync(int projectId, string runId, string evidenceRoot, CancellationToken cancellationToken = default)
    {
        var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        var packageReady = events.FirstOrDefault(runEvent => runEvent.EventType == "CriticReviewPackageReady");
        if (packageReady is null)
            return null;

        var preparedAtUtc = packageReady.TimestampUtc;
        var footprint = await ReadFootprintAsync(evidenceRoot, runId, cancellationToken).ConfigureAwait(false);

        var recentRunIds = await _events.GetRecentRunIdsAsync(200, cancellationToken).ConfigureAwait(false);
        foreach (var otherRunId in recentRunIds.Where(candidate => !string.Equals(candidate, runId, StringComparison.Ordinal)))
        {
            var otherEvents = await _events.GetEventsAsync(otherRunId, cancellationToken).ConfigureAwait(false);
            var applied = otherEvents.FirstOrDefault(runEvent =>
                runEvent.EventType == "SkeletonApplied" &&
                Payload(runEvent, "projectId") == projectId.ToString() &&
                runEvent.TimestampUtc > preparedAtUtc);
            if (applied is null)
                continue;

            var otherFootprint = await ReadFootprintAsync(evidenceRoot, otherRunId, cancellationToken).ConfigureAwait(false);
            if (footprint is null || otherFootprint is null)
            {
                return
                    $"Run {otherRunId} applied changes to this project after this run's package was prepared, and a " +
                    "footprint could not be read — treated as overlapping, conservatively and by name. The halt evidence " +
                    "describes a source that no longer exists: start a fresh skeleton run. The gate never consumes a stale package.";
            }

            var shared = footprint.Intersect(otherFootprint, StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (shared.Count > 0)
            {
                return
                    $"Run {otherRunId} applied changes overlapping this run's footprint ({string.Join(", ", shared)}) after " +
                    "this package was prepared. The halt evidence describes a source that no longer exists: start a fresh " +
                    "skeleton run. The gate never consumes a stale package.";
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<string>?> ReadFootprintAsync(string evidenceRoot, string runId, CancellationToken cancellationToken)
    {
        var packagePath = Path.Combine(evidenceRoot, runId, "evidence", "critic-package.json");
        if (!File.Exists(packagePath))
            return null;

        try
        {
            var package = JsonSerializer.Deserialize<SkeletonCriticPackage>(
                await File.ReadAllTextAsync(packagePath, cancellationToken).ConfigureAwait(false), JsonOptions);
            return package?.Changes
                .Select(change => (change.FilePath ?? string.Empty).Replace('\\', '/').TrimStart('/').Trim())
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Payload(RunEventDto runEvent, string key) =>
        runEvent.Payload.TryGetValue(key, out var value) ? value : string.Empty;
}
