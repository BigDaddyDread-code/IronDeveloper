using System.Text.Json;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Runs;
using IronDev.Core.RunReadiness;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// P2-3 — executes a sealed batch plan as single-ticket walking-skeleton loops.
///
/// The whole provenance chain is verified before anything starts: the plan's
/// seal, and the seal of the map the plan derives from (the map supplies the
/// dependency edges that gate eligibility). A broken seal anywhere refuses the
/// batch — a batch must not execute laundered evidence. An unschedulable plan
/// (cycle blockers) refuses with the blockers named.
///
/// Eligibility is edge-based: a ticket starts only when every upstream it
/// depends on has APPLIED. A halt at an upstream human gate holds its
/// dependents — and only its dependents; independent branches keep flowing.
/// A failed upstream holds them too, with the failure named in the status.
///
/// Boundary — composition only, one verb: this service can call StartAsync for
/// an eligible ticket. It never continues, approves, answers findings, or applies
/// anything; every gate stays human, per ticket. Advance is requested and
/// never self-acting: no scheduler decides on its own that the world moved.
/// </summary>
public sealed class SkeletonBatchRunService : ISkeletonBatchRunService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ISkeletonBatchPlanService _plans;
    private readonly ISkeletonBatchMapService _maps;
    private readonly ITicketSkeletonRunService _skeletonRuns;
    private readonly IRunStore _runs;
    private readonly IConfiguration _configuration;
    private readonly IProjectRunReadinessService? _runReadiness;

    public SkeletonBatchRunService(
        ISkeletonBatchPlanService plans,
        ISkeletonBatchMapService maps,
        ITicketSkeletonRunService skeletonRuns,
        IRunStore runs,
        IConfiguration configuration,
        IProjectRunReadinessService? runReadiness = null)
    {
        _plans = plans;
        _maps = maps;
        _skeletonRuns = skeletonRuns;
        _runs = runs;
        _configuration = configuration;
        _runReadiness = runReadiness;
    }

    public async Task<SkeletonBatchRunOutcome> StartAsync(
        int projectId,
        string planId,
        string requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (_runReadiness is not null)
        {
            var readiness = await _runReadiness.EvaluateAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (!readiness.ReadyToRun)
                return Failure(readiness.State == ProjectRunReadinessStates.RunConfigurationRequired
                    ? $"Run configuration required · {readiness.BlockedCount} agent blockers. No batch state or run was created."
                    : "Project setup is incomplete. No batch state or run was created.");
        }

        var plan = await _plans.GetAsync(projectId, planId, cancellationToken).ConfigureAwait(false);
        if (plan is null)
            return Failure($"Batch plan '{planId}' was not found for project {projectId}. Plan the batch first.");

        if (!plan.Verified)
        {
            return Failure(
                $"Batch plan '{planId}' failed integrity verification — a batch must not execute laundered evidence. Plan again from a fresh map.");
        }

        if (!plan.Plan.Schedulable)
        {
            return Failure(
                "The plan is not schedulable: " +
                string.Join(" ", plan.Plan.CycleBlockers.Select(blocker => blocker.Detail)) +
                " Resolve the cycle, detect and plan again.");
        }

        var map = await _maps.GetAsync(projectId, plan.Plan.MapId, cancellationToken).ConfigureAwait(false);
        if (map is null || !map.Verified)
        {
            return Failure(
                $"The map '{plan.Plan.MapId}' this plan derives from is missing or failed integrity verification. " +
                "The provenance chain must verify end to end — detect and plan again.");
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var state = new BatchState
        {
            BatchId = $"batch-run-{startedAtUtc:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..44],
            PlanId = planId,
            MapId = plan.Plan.MapId,
            ProjectId = projectId,
            RequestedByUserId = requestedByUserId,
            StartedAtUtc = startedAtUtc,
            Assignments = []
        };

        return await AdvanceStateAsync(state, plan.Plan, map.Map, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SkeletonBatchRunOutcome> AdvanceAsync(
        int projectId,
        string batchId,
        CancellationToken cancellationToken = default)
    {
        if (_runReadiness is not null)
        {
            var readiness = await _runReadiness.EvaluateAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (!readiness.ReadyToRun)
                return Failure(readiness.State == ProjectRunReadinessStates.RunConfigurationRequired
                    ? $"Run configuration required · {readiness.BlockedCount} agent blockers. No additional run was created."
                    : "Project setup is incomplete. No additional run was created.");
        }

        var state = await LoadStateAsync(projectId, batchId, cancellationToken).ConfigureAwait(false);
        if (state is null)
            return Failure($"Batch run '{batchId}' was not found for project {projectId}.");

        var plan = await _plans.GetAsync(projectId, state.PlanId, cancellationToken).ConfigureAwait(false);
        var map = await _maps.GetAsync(projectId, state.MapId, cancellationToken).ConfigureAwait(false);
        if (plan is null || !plan.Verified || map is null || !map.Verified)
        {
            return Failure(
                "The batch's plan or map no longer verifies — the provenance chain must hold for the batch's whole life. Nothing was started.");
        }

        return await AdvanceStateAsync(state, plan.Plan, map.Map, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SkeletonBatchRunStatus?> GetAsync(
        int projectId,
        string batchId,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadStateAsync(projectId, batchId, cancellationToken).ConfigureAwait(false);
        if (state is null)
            return null;

        var plan = await _plans.GetAsync(projectId, state.PlanId, cancellationToken).ConfigureAwait(false);
        var map = await _maps.GetAsync(projectId, state.MapId, cancellationToken).ConfigureAwait(false);
        if (plan is null || map is null)
            return null;

        return await BuildStatusAsync(state, plan.Plan, map.Map, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts every eligible, unstarted ticket — and nothing else. Idempotent for started tickets.</summary>
    private async Task<SkeletonBatchRunOutcome> AdvanceStateAsync(
        BatchState state,
        SkeletonBatchPlan plan,
        SkeletonBatchMap map,
        CancellationToken cancellationToken)
    {
        var status = await BuildStatusAsync(state, plan, map, cancellationToken).ConfigureAwait(false);
        var started = new Dictionary<long, string>();

        foreach (var ticket in status.Tickets.Where(candidate => candidate.Eligible))
        {
            var run = await _skeletonRuns.StartAsync(state.ProjectId, ticket.TicketId, cancellationToken).ConfigureAwait(false);
            if (run is null)
                return Failure($"Ticket {ticket.TicketId} could not start: it does not belong to project {state.ProjectId}.");

            state.Assignments.Add(new BatchAssignment { TicketId = ticket.TicketId, RunId = run.RunId, Wave = ticket.Wave });
            started[ticket.TicketId] = run.RunId;
        }

        await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        var refreshed = await BuildStatusAsync(state, plan, map, cancellationToken).ConfigureAwait(false);

        return new SkeletonBatchRunOutcome
        {
            Succeeded = true,
            StartedRuns = started,
            Status = refreshed
        };
    }

    private async Task<SkeletonBatchRunStatus> BuildStatusAsync(
        BatchState state,
        SkeletonBatchPlan plan,
        SkeletonBatchMap map,
        CancellationToken cancellationToken)
    {
        var waveByTicket = plan.Waves
            .SelectMany(wave => wave.TicketIds.Select(ticketId => (ticketId, wave.WaveNumber)))
            .ToDictionary(pair => pair.ticketId, pair => pair.WaveNumber);

        var runStates = new Dictionary<long, (string RunId, string Status)>();
        foreach (var assignment in state.Assignments)
        {
            var run = await _runs.GetAsync(assignment.RunId, cancellationToken).ConfigureAwait(false);
            runStates[assignment.TicketId] = (assignment.RunId, run?.State.ToString() ?? "Unknown");
        }

        var tickets = new List<SkeletonBatchTicketStatus>();
        foreach (var ticketId in map.TicketIds.OrderBy(ticketId => ticketId))
        {
            var upstreamIds = map.Edges
                .Where(edge => edge.ToTicketId == ticketId)
                .Select(edge => edge.FromTicketId)
                .Distinct()
                .OrderBy(upstreamId => upstreamId)
                .ToList();

            var waitingOn = upstreamIds
                .Where(upstreamId => !(runStates.TryGetValue(upstreamId, out var upstream) && upstream.Status == nameof(RunLifecycleState.Applied)))
                .Select(upstreamId => runStates.TryGetValue(upstreamId, out var upstream)
                    ? $"ticket {upstreamId} ({upstream.Status})"
                    : $"ticket {upstreamId} (not started)")
                .ToList();

            var hasRun = runStates.TryGetValue(ticketId, out var own);
            tickets.Add(new SkeletonBatchTicketStatus
            {
                TicketId = ticketId,
                Wave = waveByTicket.TryGetValue(ticketId, out var wave) ? wave : 0,
                RunId = hasRun ? own.RunId : string.Empty,
                RunStatus = hasRun ? own.Status : string.Empty,
                Eligible = !hasRun && waitingOn.Count == 0,
                WaitingOn = waitingOn
            });
        }

        return new SkeletonBatchRunStatus
        {
            BatchId = state.BatchId,
            PlanId = state.PlanId,
            ProjectId = state.ProjectId,
            RequestedByUserId = state.RequestedByUserId,
            StartedAtUtc = state.StartedAtUtc,
            Tickets = tickets
        };
    }

    private static SkeletonBatchRunOutcome Failure(string reason) =>
        new() { Succeeded = false, FailureReason = reason };

    private string BatchesRoot(int projectId)
    {
        var configured = _configuration["DisposableBuild:EvidenceRoot"] ?? _configuration["LocalTest:LogsRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableEvidence")
            : configured;
        return Path.Combine(root, "batch-runs", projectId.ToString());
    }

    private async Task<BatchState?> LoadStateAsync(int projectId, string batchId, CancellationToken cancellationToken)
    {
        var path = Path.Combine(BatchesRoot(projectId), $"{SafeId(batchId)}.json");
        if (!File.Exists(path))
            return null;
        return JsonSerializer.Deserialize<BatchState>(
            await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), JsonOptions);
    }

    private async Task SaveStateAsync(BatchState state, CancellationToken cancellationToken)
    {
        var path = Path.Combine(BatchesRoot(state.ProjectId), $"{SafeId(state.BatchId)}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private static string SafeId(string batchId) =>
        new(batchId.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());

    /// <summary>
    /// Batch linkage only: which run belongs to which ticket. Everything
    /// judgement-bearing (run states, packages, approvals, receipts) stays in the
    /// per-ticket durable evidence — this file is a pointer list, not authority.
    /// </summary>
    private sealed record BatchState
    {
        public string BatchId { get; init; } = string.Empty;
        public string PlanId { get; init; } = string.Empty;
        public string MapId { get; init; } = string.Empty;
        public int ProjectId { get; init; }
        public string RequestedByUserId { get; init; } = string.Empty;
        public DateTimeOffset StartedAtUtc { get; init; }
        public List<BatchAssignment> Assignments { get; init; } = [];
    }

    private sealed record BatchAssignment
    {
        public long TicketId { get; init; }
        public string RunId { get; init; } = string.Empty;
        public int Wave { get; init; }
    }
}
