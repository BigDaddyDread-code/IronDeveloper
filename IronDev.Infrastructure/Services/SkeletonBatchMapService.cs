using System.Text.Json;
using IronDev.Core.Builder;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// P2-1 — detects the batch dependency map and persists it as hash-sealed
/// durable evidence (map.json + sha256 sidecar, re-verified on every read —
/// same seal discipline as the catch-rate measurements).
///
/// Boundary: detection is advisory evidence. This service loads tickets, runs
/// the deterministic detector, and writes the record — it grants nothing, starts
/// no runs, moves no gates. A ticket outside the project fails the whole request
/// with the ticket named: a map must never quietly describe half a batch.
/// </summary>
public sealed class SkeletonBatchMapService : ISkeletonBatchMapService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ITicketService _tickets;
    private readonly IProjectService _projects;
    private readonly IConfiguration _configuration;

    public SkeletonBatchMapService(ITicketService tickets, IProjectService projects, IConfiguration configuration)
    {
        _tickets = tickets;
        _projects = projects;
        _configuration = configuration;
    }

    public async Task<SkeletonBatchMapOutcome?> DetectAsync(
        int projectId,
        IReadOnlyList<long> ticketIds,
        string requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var distinctIds = ticketIds.Distinct().ToList();
        if (distinctIds.Count < 2)
        {
            return new SkeletonBatchMapOutcome
            {
                Succeeded = false,
                FailureReason = "A batch needs at least two tickets — one ticket has no dependencies to map."
            };
        }

        var tickets = new List<ProjectTicket>();
        foreach (var ticketId in distinctIds)
        {
            var ticket = await _tickets.GetTicketByIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
            if (ticket is null || ticket.ProjectId != projectId)
            {
                return new SkeletonBatchMapOutcome
                {
                    Succeeded = false,
                    FailureReason =
                        $"Ticket {ticketId} does not belong to project {projectId}. " +
                        "A map must never quietly describe half a batch — fix the ticket list and detect again."
                };
            }
            tickets.Add(ticket);
        }

        var map = SkeletonBatchDependencyDetector.Detect(projectId, tickets);
        var detectedAtUtc = DateTimeOffset.UtcNow;
        var mapId = $"batch-map-{detectedAtUtc:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..44];

        var record = new PersistedMap
        {
            MapId = mapId,
            DetectedAtUtc = detectedAtUtc,
            RequestedByUserId = requestedByUserId,
            Map = map
        };

        var mapPath = MapPath(projectId, mapId);
        Directory.CreateDirectory(Path.GetDirectoryName(mapPath)!);
        await File.WriteAllTextAsync(mapPath, JsonSerializer.Serialize(record, JsonOptions), cancellationToken).ConfigureAwait(false);
        var hash = ComputeSha256(await File.ReadAllBytesAsync(mapPath, cancellationToken).ConfigureAwait(false));
        await File.WriteAllTextAsync(HashPath(projectId, mapId), hash, cancellationToken).ConfigureAwait(false);

        return new SkeletonBatchMapOutcome
        {
            Succeeded = true,
            MapId = mapId,
            DetectedAtUtc = detectedAtUtc,
            Map = map
        };
    }

    public async Task<SkeletonBatchMapRecord?> GetAsync(
        int projectId,
        string mapId,
        CancellationToken cancellationToken = default)
    {
        var mapPath = MapPath(projectId, mapId);
        var hashPath = HashPath(projectId, mapId);
        if (!File.Exists(mapPath) || !File.Exists(hashPath))
            return null;

        var bytes = await File.ReadAllBytesAsync(mapPath, cancellationToken).ConfigureAwait(false);
        var persisted = JsonSerializer.Deserialize<PersistedMap>(System.Text.Encoding.UTF8.GetString(bytes), JsonOptions);
        if (persisted is null || persisted.Map is null || persisted.Map.ProjectId != projectId)
            return null;

        return new SkeletonBatchMapRecord
        {
            MapId = persisted.MapId,
            DetectedAtUtc = persisted.DetectedAtUtc,
            RequestedByUserId = persisted.RequestedByUserId,
            Map = persisted.Map,
            RecordedSha256 = (await File.ReadAllTextAsync(hashPath, cancellationToken).ConfigureAwait(false)).Trim(),
            Sha256OnDisk = ComputeSha256(bytes)
        };
    }

    private string MapsRoot(int projectId)
    {
        var configured = _configuration["DisposableBuild:EvidenceRoot"] ?? _configuration["LocalTest:LogsRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableEvidence")
            : configured;
        return Path.Combine(root, "batch-maps", projectId.ToString());
    }

    private string MapPath(int projectId, string mapId) => Path.Combine(MapsRoot(projectId), $"{SafeId(mapId)}.json");

    private string HashPath(int projectId, string mapId) => Path.Combine(MapsRoot(projectId), $"{SafeId(mapId)}.sha256");

    private static string SafeId(string mapId) =>
        new(mapId.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record PersistedMap
    {
        public string MapId { get; init; } = string.Empty;
        public DateTimeOffset DetectedAtUtc { get; init; }
        public string RequestedByUserId { get; init; } = string.Empty;
        public SkeletonBatchMap? Map { get; init; }
    }
}
