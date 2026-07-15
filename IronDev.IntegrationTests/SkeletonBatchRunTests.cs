using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Runs;
using IronDev.Core.RunReadiness;
using IronDev.Core.Workflow;
using IronDev.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// P2-3 — the batch skeleton run. Protected boundaries:
/// - the batch has ONE verb: it may start a ticket's run. The harness stub
///   throws on every other skeleton-run method, so any attempt to continue,
///   apply, or read gates through the batch fails structurally;
/// - halt is not upstream-satisfaction: only Applied releases dependents;
/// - one blocked ticket pauses its dependents, not the batch;
/// - the provenance chain (plan seal + map seal) must verify at start AND at
///   every advance.
/// </summary>
[TestClass]
[TestCategory("SkeletonRun")]
public sealed class SkeletonBatchRunTests
{
    private const int ProjectId = 7;

    // Map: 42 → 43 (explicit), 44 independent.
    private static SkeletonBatchMap DefaultMap() => new()
    {
        ProjectId = ProjectId,
        TicketIds = [42, 43, 44],
        Edges =
        [
            new SkeletonBatchDependencyEdge
            {
                FromTicketId = 42,
                ToTicketId = 43,
                Kind = SkeletonBatchDependencyEdgeKinds.ExplicitBlock,
                Reason = "Ticket 43 declares it is blocked by ticket 42."
            }
        ]
    };

    [TestMethod]
    public async Task Start_RunsWaveOne_AndNamesWhatEveryoneElseWaitsOn()
    {
        using var harness = BatchHarness.Create(DefaultMap());

        var outcome = await harness.Service.StartAsync(ProjectId, harness.PlanId, "user-9");

        Assert.IsTrue(outcome.Succeeded, outcome.FailureReason);
        CollectionAssert.AreEquivalent(new[] { 42L, 44L }, outcome.StartedRuns.Keys.ToArray(),
            "Wave 1 = tickets with no upstream: 42 and 44 start; 43 waits.");
        CollectionAssert.AreEquivalent(new[] { 42L, 44L }, harness.SkeletonRuns.StartedTicketIds.ToArray());

        var waiting = outcome.Status!.Tickets.Single(ticket => ticket.TicketId == 43);
        Assert.IsFalse(waiting.Eligible);
        Assert.AreEqual("ticket 42 (PausedForApproval)", waiting.WaitingOn.Single(),
            "The block is named with the upstream's current state — never vague.");
    }

    [TestMethod]
    public async Task Advance_WhileUpstreamIsHalted_StartsNothing_HaltIsNotUpstreamSatisfaction()
    {
        using var harness = BatchHarness.Create(DefaultMap());
        var start = await harness.Service.StartAsync(ProjectId, harness.PlanId, "user-9");

        var advance = await harness.Service.AdvanceAsync(ProjectId, start.Status!.BatchId);

        Assert.IsTrue(advance.Succeeded);
        Assert.AreEqual(0, advance.StartedRuns.Count,
            "The upstream sits at its human gate. A halt releases nothing — only Applied does.");
    }

    [TestMethod]
    public async Task Advance_AfterUpstreamApplies_StartsTheDependent_Once()
    {
        using var harness = BatchHarness.Create(DefaultMap());
        var start = await harness.Service.StartAsync(ProjectId, harness.PlanId, "user-9");

        harness.SkeletonRuns.SetRunState(42, RunLifecycleState.Applied);
        var advance = await harness.Service.AdvanceAsync(ProjectId, start.Status!.BatchId);
        CollectionAssert.AreEqual(new[] { 43L }, advance.StartedRuns.Keys.ToArray(),
            "The dependent starts once its upstream has actually landed.");

        var again = await harness.Service.AdvanceAsync(ProjectId, start.Status.BatchId);
        Assert.AreEqual(0, again.StartedRuns.Count, "Advance is idempotent: started tickets are never restarted.");

        harness.SkeletonRuns.SetRunState(43, RunLifecycleState.Applied);
        harness.SkeletonRuns.SetRunState(44, RunLifecycleState.Applied);
        var status = await harness.Service.GetAsync(ProjectId, start.Status.BatchId);
        Assert.IsTrue(status!.BatchComplete, "Every ticket applied — the whole batch landed.");
    }

    [TestMethod]
    public async Task AFailedUpstream_HoldsItsDependentsOnly_TheRestOfTheBatchFlows()
    {
        using var harness = BatchHarness.Create(DefaultMap());
        var start = await harness.Service.StartAsync(ProjectId, harness.PlanId, "user-9");

        harness.SkeletonRuns.SetRunState(42, RunLifecycleState.Failed);
        harness.SkeletonRuns.SetRunState(44, RunLifecycleState.Applied);
        var advance = await harness.Service.AdvanceAsync(ProjectId, start.Status!.BatchId);

        Assert.AreEqual(0, advance.StartedRuns.Count);
        var blocked = advance.Status!.Tickets.Single(ticket => ticket.TicketId == 43);
        Assert.AreEqual("ticket 42 (Failed)", blocked.WaitingOn.Single(),
            "A failed upstream is named as the reason its dependents hold.");
        Assert.AreEqual("Applied", advance.Status.Tickets.Single(ticket => ticket.TicketId == 44).RunStatus,
            "The independent branch was never held: one ticket blocking pauses its dependents, not the batch.");
    }

    [TestMethod]
    public async Task Start_RefusesAnUnverifiedPlan()
    {
        using var harness = BatchHarness.Create(DefaultMap(), planVerifies: false);

        var outcome = await harness.Service.StartAsync(ProjectId, harness.PlanId, "user-9");

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "laundered evidence");
        Assert.AreEqual(0, harness.SkeletonRuns.StartedTicketIds.Count);
    }

    [TestMethod]
    public async Task Start_WhenRunReadinessIsBlocked_CreatesNoBatchStateOrRun()
    {
        using var harness = BatchHarness.Create(DefaultMap(), runReadiness: StubRunReadiness.Blocked(ProjectId));

        var outcome = await harness.Service.StartAsync(ProjectId, harness.PlanId, "user-9");

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "Run configuration required · 4 agent blockers");
        Assert.AreEqual(0, harness.SkeletonRuns.StartedTicketIds.Count);
        Assert.IsFalse(Directory.Exists(harness.EvidenceRoot), "A blocked readiness check must precede batch state persistence.");
    }

    [TestMethod]
    public async Task Start_RefusesAnUnschedulablePlan_NamingTheBlockers()
    {
        var cyclic = DefaultMap() with
        {
            Edges =
            [
                new SkeletonBatchDependencyEdge { FromTicketId = 42, ToTicketId = 43, Kind = "explicit-block", Reason = "42 first" },
                new SkeletonBatchDependencyEdge { FromTicketId = 43, ToTicketId = 42, Kind = "explicit-block", Reason = "43 first" }
            ]
        };
        using var harness = BatchHarness.Create(cyclic);

        var outcome = await harness.Service.StartAsync(ProjectId, harness.PlanId, "user-9");

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "not schedulable");
        StringAssert.Contains(outcome.FailureReason, "never breaks a cycle by guessing");
        Assert.AreEqual(0, harness.SkeletonRuns.StartedTicketIds.Count);
    }

    [TestMethod]
    public async Task Advance_RefusesWhenTheProvenanceChainBreaksMidBatch()
    {
        using var harness = BatchHarness.Create(DefaultMap());
        var start = await harness.Service.StartAsync(ProjectId, harness.PlanId, "user-9");

        harness.BreakMapSeal();
        harness.SkeletonRuns.SetRunState(42, RunLifecycleState.Applied);
        var advance = await harness.Service.AdvanceAsync(ProjectId, start.Status!.BatchId);

        Assert.IsFalse(advance.Succeeded);
        StringAssert.Contains(advance.FailureReason, "provenance chain must hold for the batch's whole life");
        Assert.IsFalse(harness.SkeletonRuns.StartedTicketIds.Contains(43), "Nothing starts on a broken chain.");
    }

    [TestMethod]
    public void BatchRunService_HasOneVerb_AndItIsStart()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Infrastructure", "Services", "SkeletonBatchRunService.cs"));

        foreach (var forbidden in new[]
        {
            "ContinueAsync",
            "ApplyAsync",
            "AcceptedApproval",
            "SatisfyPolicy",
            "TransitionAsync",
            "GetCriticPackageAsync",
            "Disposition"
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"The batch composes single-ticket loops with one verb — start. It must never touch: {forbidden}");
        }

        StringAssert.Contains(source, "composition only");
        StringAssert.Contains(source, "never self-acting");
        StringAssert.Contains(source, "every gate stays human");
    }

    // ── Harness ───────────────────────────────────────────────────────────────

    private sealed class BatchHarness : IDisposable
    {
        public required SkeletonBatchRunService Service { get; init; }
        public required StubSkeletonRunService SkeletonRuns { get; init; }
        public required StubSealStore Seals { get; init; }
        public required string PlanId { get; init; }
        public required string EvidenceRoot { get; init; }

        public static BatchHarness Create(
            SkeletonBatchMap map,
            bool planVerifies = true,
            IProjectRunReadinessService? runReadiness = null)
        {
            var evidenceRoot = Path.Combine(Path.GetTempPath(), $"irondev-batch-run-{Guid.NewGuid():N}");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DisposableBuild:EvidenceRoot"] = evidenceRoot
                })
                .Build();

            var seals = new StubSealStore(map, planVerifies);
            var skeletonRuns = new StubSkeletonRunService();
            return new BatchHarness
            {
                Service = new SkeletonBatchRunService(seals, seals, skeletonRuns, skeletonRuns.Runs, configuration, runReadiness),
                SkeletonRuns = skeletonRuns,
                Seals = seals,
                PlanId = StubSealStore.PlanIdValue,
                EvidenceRoot = evidenceRoot
            };
        }

        public void BreakMapSeal() => Seals.MapVerifies = false;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(EvidenceRoot))
                    Directory.Delete(EvidenceRoot, recursive: true);
            }
            catch (IOException)
            {
                // best-effort temp cleanup
            }
        }
    }

    private sealed class StubRunReadiness(ProjectRunReadiness readiness) : IProjectRunReadinessService
    {
        public static StubRunReadiness Blocked(int projectId) => new(new ProjectRunReadiness
        {
            ProjectId = projectId,
            ProjectSetupReady = true,
            ExecutionReady = false,
            ReadyToRun = false,
            State = ProjectRunReadinessStates.RunConfigurationRequired,
            BlockedCount = 4
        });

        public Task<ProjectRunReadiness> EvaluateAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(readiness);
    }

    /// <summary>Serves the plan (sequenced from the map) and the map, with controllable seal verification.</summary>
    private sealed class StubSealStore(SkeletonBatchMap map, bool planVerifies) : ISkeletonBatchPlanService, ISkeletonBatchMapService
    {
        public const string PlanIdValue = "plan-1";
        public const string MapIdValue = "map-1";

        public bool MapVerifies { get; set; } = true;

        public Task<SkeletonBatchPlanRecord?> GetAsync(int projectId, string planId, CancellationToken ct = default) =>
            Task.FromResult<SkeletonBatchPlanRecord?>(planId != PlanIdValue ? null : new SkeletonBatchPlanRecord
            {
                PlanId = PlanIdValue,
                PlannedAtUtc = DateTimeOffset.UtcNow,
                RequestedByUserId = "user-9",
                Plan = SkeletonBatchSequencer.Sequence(map, MapIdValue),
                RecordedSha256 = "seal",
                Sha256OnDisk = planVerifies ? "seal" : "broken"
            });

        Task<SkeletonBatchMapRecord?> ISkeletonBatchMapService.GetAsync(int projectId, string mapId, CancellationToken ct) =>
            Task.FromResult<SkeletonBatchMapRecord?>(mapId != MapIdValue ? null : new SkeletonBatchMapRecord
            {
                MapId = MapIdValue,
                DetectedAtUtc = DateTimeOffset.UtcNow,
                RequestedByUserId = "user-9",
                Map = map,
                RecordedSha256 = "seal",
                Sha256OnDisk = MapVerifies ? "seal" : "broken"
            });

        public Task<SkeletonBatchPlanOutcome> PlanAsync(int projectId, string mapId, string requestedByUserId, CancellationToken ct = default) =>
            throw new NotSupportedException("The batch harness seals plans directly.");

        public Task<SkeletonBatchMapOutcome?> DetectAsync(int projectId, IReadOnlyList<long> ticketIds, string requestedByUserId, CancellationToken ct = default) =>
            throw new NotSupportedException("The batch harness seals maps directly.");
    }

    /// <summary>
    /// The batch's one verb, instrumented — and every other skeleton-run method
    /// throws: if the batch ever reaches for a gate, the test dies structurally.
    /// </summary>
    private sealed class StubSkeletonRunService : ITicketSkeletonRunService
    {
        private int _sequence;

        public LiteRunStore Runs { get; } = new();
        public List<long> StartedTicketIds { get; } = [];

        public async Task<TicketBuildRunDto?> StartAsync(int projectId, long ticketId, CancellationToken ct = default)
        {
            StartedTicketIds.Add(ticketId);
            var runId = $"run-t{ticketId}-{++_sequence}";
            await Runs.CreateAsync(new CreateRunRequest
            {
                RunId = runId,
                ProjectId = projectId,
                TicketId = ticketId,
                IsDisposable = true,
                Summary = "batch-started skeleton run"
            }, ct);
            Runs.SetState(runId, RunLifecycleState.PausedForApproval);

            return new TicketBuildRunDto
            {
                RunId = runId,
                ProjectId = projectId,
                TicketId = ticketId,
                Status = RunLifecycleState.PausedForApproval.ToString(),
                CurrentNode = "SkeletonRun",
                RequiresHumanApproval = true
            };
        }

        public void SetRunState(long ticketId, RunLifecycleState state) =>
            Runs.SetStateByTicket(ticketId, state);

        public Task<SkeletonCriticPackage?> GetCriticPackageAsync(int projectId, long ticketId, string runId, CancellationToken ct = default) =>
            throw new NotSupportedException("The batch must never read gates through the run service.");

        public Task<TicketBuildRunDto?> ContinueAsync(int projectId, long ticketId, string runId, CancellationToken ct = default) =>
            throw new NotSupportedException("The batch must never continue a run — that gate is human.");

        public Task<TicketBuildRunDto?> ReviseAsync(int projectId, long ticketId, string runId, SkeletonRunRevisionRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException("The batch must never direct a revision — that decision is human.");

        public Task<TicketBuildRunDto?> ApplyAsync(int projectId, long ticketId, string runId, CancellationToken ct = default) =>
            throw new NotSupportedException("The batch must never apply a run — that gate is human.");

        public Task<SkeletonRunReport?> GetRunReportAsync(int projectId, long ticketId, string runId, CancellationToken ct = default) =>
            throw new NotSupportedException("The batch derives status from the run store only.");
    }

    private sealed class LiteRunStore : IRunStore
    {
        private readonly Dictionary<string, RunRecord> _runs = [];

        public Task<RunRecord> CreateAsync(CreateRunRequest request, CancellationToken ct = default)
        {
            var run = new RunRecord
            {
                RunId = request.RunId ?? Guid.NewGuid().ToString("N"),
                ProjectId = request.ProjectId,
                TicketId = request.TicketId,
                State = RunLifecycleState.Created,
                IsDisposable = request.IsDisposable,
                Summary = request.Summary
            };
            _runs[run.RunId] = run;
            return Task.FromResult(run);
        }

        public Task<RunRecord?> GetAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult(_runs.TryGetValue(runId, out var run) ? run : null);

        public Task<IReadOnlyList<RunRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunRecord>>(_runs.Values.Take(limit).ToList());

        public Task<RunRecord?> TransitionAsync(RunStateTransition transition, CancellationToken ct = default) =>
            throw new NotSupportedException("The batch tests mutate state via SetState, not the governed transition path.");

        public void SetState(string runId, RunLifecycleState state) =>
            _runs[runId] = _runs[runId] with { State = state };

        public void SetStateByTicket(long ticketId, RunLifecycleState state)
        {
            var run = _runs.Values.Single(candidate => candidate.TicketId == ticketId);
            _runs[run.RunId] = run with { State = state };
        }
    }

    private static string RepositoryFile(params string[] parts)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "IronDev.slnx")))
            root = Path.GetDirectoryName(root);
        Assert.IsNotNull(root, "Repository root not found.");
        return Path.Combine(root!, Path.Combine(parts));
    }
}
