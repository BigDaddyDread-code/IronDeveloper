using System.Text.Json;
using IronDev.Core.Builder;
using IronDev.Core.Governance;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// P2-4 — file-backed mutation leases for the source-apply surface, speaking the
/// E07 governance vocabulary (MutationLeaseSurfaceKind.SourceApply,
/// MutationLeaseState.ObservedHeld). One lease file per active lease under the
/// evidence root; footprints are normalized the same way the batch detector
/// normalizes them, so "src\A.cs" and "/SRC/a.cs" are the same file here too.
///
/// Expiry (MutationLease:TimeoutMinutes, default 30) exists so a crashed apply
/// cannot wedge the batch forever — an expired lease is ignored at acquisition
/// WITH A NOTE naming it, never silently.
///
/// Boundary: a lease is a concurrency guard, not authority — holding one grants
/// nothing, and this service touches no approvals, no runs, no gates. It makes
/// overlapping applies take turns; that is all it can do.
/// </summary>
public sealed class SkeletonMutationLeaseService : ISkeletonMutationLeaseService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // Acquisition is check-then-write; the semaphore makes it atomic within the
    // process (LocalTest runs a single API instance — multi-instance needs the
    // durable E-block store, a later convergence).
    private static readonly SemaphoreSlim AcquisitionGate = new(1, 1);

    private readonly IConfiguration _configuration;

    public SkeletonMutationLeaseService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<SkeletonMutationLeaseOutcome> TryAcquireAsync(
        SkeletonMutationLeaseRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestedPaths = Normalize(request.FootprintPaths);
        if (requestedPaths.Count == 0)
        {
            return new SkeletonMutationLeaseOutcome
            {
                Acquired = false,
                RefusedBecause = "The apply has no footprint paths to lease — an unbounded mutation cannot take a bounded lock."
            };
        }

        await AcquisitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var notes = new List<string>();
            var root = LeasesRoot(request.ProjectId);
            Directory.CreateDirectory(root);

            foreach (var file in Directory.EnumerateFiles(root, "*.json"))
            {
                var existing = JsonSerializer.Deserialize<LeaseRecord>(
                    await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false), JsonOptions);
                if (existing is null)
                    continue;

                if (existing.ExpiresAtUtc <= now)
                {
                    notes.Add(
                        $"Expired lease {existing.LeaseId} held by {existing.HolderRef} (expired {existing.ExpiresAtUtc:O}) was ignored and removed — " +
                        "expiry exists so a crashed apply cannot wedge the batch forever.");
                    File.Delete(file);
                    continue;
                }

                var conflicting = existing.Paths.Intersect(requestedPaths, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (conflicting.Count > 0)
                {
                    return new SkeletonMutationLeaseOutcome
                    {
                        Acquired = false,
                        HolderRunId = existing.RunId,
                        ConflictingPaths = conflicting,
                        Notes = notes,
                        RefusedBecause =
                            $"Lease {existing.LeaseId} is held by {existing.HolderRef} over {conflicting.Count} of the same file(s) " +
                            $"({string.Join(", ", conflicting)}), until {existing.ExpiresAtUtc:O}. Overlapping applies take turns: " +
                            "wait for the holder's apply to finish (the lease releases), then request apply again."
                    };
                }
            }

            var lease = new LeaseRecord
            {
                LeaseId = $"lease-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..40],
                ProjectId = request.ProjectId,
                RunId = request.RunId,
                TicketId = request.TicketId,
                Surface = ISkeletonMutationLeaseService.Surface.ToString(),
                State = MutationLeaseState.ObservedHeld.ToString(),
                Paths = requestedPaths,
                HolderRef = request.HolderRef,
                AcquiredAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(TimeoutMinutes())
            };

            await File.WriteAllTextAsync(
                Path.Combine(root, $"{lease.LeaseId}.json"),
                JsonSerializer.Serialize(lease, JsonOptions),
                cancellationToken).ConfigureAwait(false);

            return new SkeletonMutationLeaseOutcome
            {
                Acquired = true,
                LeaseId = lease.LeaseId,
                Notes = notes
            };
        }
        finally
        {
            AcquisitionGate.Release();
        }
    }

    public Task ReleaseAsync(int projectId, string leaseId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(LeasesRoot(projectId), $"{SafeId(leaseId)}.json");
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private int TimeoutMinutes()
    {
        var value = _configuration["MutationLease:TimeoutMinutes"];
        return int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : 30;
    }

    private string LeasesRoot(int projectId)
    {
        var configured = _configuration["DisposableBuild:EvidenceRoot"] ?? _configuration["LocalTest:LogsRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableEvidence")
            : configured;
        return Path.Combine(root, "mutation-leases", projectId.ToString());
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> paths) =>
        paths
            .Select(path => (path ?? string.Empty).Replace('\\', '/').TrimStart('/').Trim())
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string SafeId(string leaseId) =>
        new(leaseId.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());

    private sealed record LeaseRecord
    {
        public string LeaseId { get; init; } = string.Empty;
        public int ProjectId { get; init; }
        public string RunId { get; init; } = string.Empty;
        public long TicketId { get; init; }
        public string Surface { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public IReadOnlyList<string> Paths { get; init; } = [];
        public string HolderRef { get; init; } = string.Empty;
        public DateTimeOffset AcquiredAtUtc { get; init; }
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }
}
