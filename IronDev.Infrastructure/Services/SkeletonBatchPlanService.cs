using System.Text.Json;
using IronDev.Core.Builder;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// P2-2 — sequences a sealed dependency map into a hash-sealed batch plan
/// (plan.json + sha256 sidecar, re-verified on every read).
///
/// Trust-but-verify at the seam: the plan derives ONLY from a map whose seal
/// verifies at planning time. A map that changed after it was recorded is a
/// broken seal, and building a plan on it would launder the tamper into a
/// clean-looking artifact — so planning refuses, with the reason named.
///
/// Boundary: a plan is advisory — this service sequences and writes the record;
/// it grants nothing, starts no runs, and moves no gates. Cycles are named
/// blockers for the human, never auto-broken.
/// </summary>
public sealed class SkeletonBatchPlanService : ISkeletonBatchPlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ISkeletonBatchMapService _maps;
    private readonly IConfiguration _configuration;

    public SkeletonBatchPlanService(ISkeletonBatchMapService maps, IConfiguration configuration)
    {
        _maps = maps;
        _configuration = configuration;
    }

    public async Task<SkeletonBatchPlanOutcome> PlanAsync(
        int projectId,
        string mapId,
        string requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        var mapRecord = await _maps.GetAsync(projectId, mapId, cancellationToken).ConfigureAwait(false);
        if (mapRecord is null)
        {
            return new SkeletonBatchPlanOutcome
            {
                Succeeded = false,
                FailureReason = $"Batch map '{mapId}' was not found for project {projectId}. Detect a map first."
            };
        }

        if (!mapRecord.Verified)
        {
            return new SkeletonBatchPlanOutcome
            {
                Succeeded = false,
                FailureReason =
                    $"Batch map '{mapId}' failed integrity verification — the map on disk is not the map that was sealed. " +
                    "A plan built on a broken seal would launder the tamper into a clean-looking artifact. Detect a fresh map."
            };
        }

        var plan = SkeletonBatchSequencer.Sequence(mapRecord.Map, mapId);
        var plannedAtUtc = DateTimeOffset.UtcNow;
        var planId = $"batch-plan-{plannedAtUtc:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..45];

        var record = new PersistedPlan
        {
            PlanId = planId,
            PlannedAtUtc = plannedAtUtc,
            RequestedByUserId = requestedByUserId,
            Plan = plan
        };

        var planPath = PlanPath(projectId, planId);
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(record, JsonOptions), cancellationToken).ConfigureAwait(false);
        var hash = ComputeSha256(await File.ReadAllBytesAsync(planPath, cancellationToken).ConfigureAwait(false));
        await File.WriteAllTextAsync(HashPath(projectId, planId), hash, cancellationToken).ConfigureAwait(false);

        return new SkeletonBatchPlanOutcome
        {
            Succeeded = true,
            PlanId = planId,
            PlannedAtUtc = plannedAtUtc,
            Plan = plan
        };
    }

    public async Task<SkeletonBatchPlanRecord?> GetAsync(
        int projectId,
        string planId,
        CancellationToken cancellationToken = default)
    {
        var planPath = PlanPath(projectId, planId);
        var hashPath = HashPath(projectId, planId);
        if (!File.Exists(planPath) || !File.Exists(hashPath))
            return null;

        var bytes = await File.ReadAllBytesAsync(planPath, cancellationToken).ConfigureAwait(false);
        var persisted = JsonSerializer.Deserialize<PersistedPlan>(System.Text.Encoding.UTF8.GetString(bytes), JsonOptions);
        if (persisted is null || persisted.Plan is null || persisted.Plan.ProjectId != projectId)
            return null;

        return new SkeletonBatchPlanRecord
        {
            PlanId = persisted.PlanId,
            PlannedAtUtc = persisted.PlannedAtUtc,
            RequestedByUserId = persisted.RequestedByUserId,
            Plan = persisted.Plan,
            RecordedSha256 = (await File.ReadAllTextAsync(hashPath, cancellationToken).ConfigureAwait(false)).Trim(),
            Sha256OnDisk = ComputeSha256(bytes)
        };
    }

    private string PlansRoot(int projectId)
    {
        var configured = _configuration["DisposableBuild:EvidenceRoot"] ?? _configuration["LocalTest:LogsRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableEvidence")
            : configured;
        return Path.Combine(root, "batch-plans", projectId.ToString());
    }

    private string PlanPath(int projectId, string planId) => Path.Combine(PlansRoot(projectId), $"{SafeId(planId)}.json");

    private string HashPath(int projectId, string planId) => Path.Combine(PlansRoot(projectId), $"{SafeId(planId)}.sha256");

    private static string SafeId(string planId) =>
        new(planId.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record PersistedPlan
    {
        public string PlanId { get; init; } = string.Empty;
        public DateTimeOffset PlannedAtUtc { get; init; }
        public string RequestedByUserId { get; init; } = string.Empty;
        public SkeletonBatchPlan? Plan { get; init; }
    }
}
