using System.Runtime.CompilerServices;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Core.Workspaces;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.Workspaces;
using IronDev.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// P0-1 walking-skeleton boundary and behavior tests.
///
/// Protected boundaries:
/// - blocked states are explicit and terminal (named reason + next safe action, no silent fallthrough);
/// - the orchestrator adds no approval surface (no request, consumption, or simulation of approval);
/// - gates stay at their owning steps (readiness inside proposal generation; path containment
///   inside the workspace service);
/// - workspace mutation is workspace-only — file writes can never escape into the source repository.
/// </summary>
[TestClass]
[TestCategory("SkeletonRun")]
public sealed class SkeletonRunTests
{
    private const int ProjectId = 7;
    private const long TicketId = 42;

    // ── Blocked states are explicit ───────────────────────────────────────────

    [TestMethod]
    public async Task StartAsync_TicketNotInProject_ReturnsNull()
    {
        var harness = SkeletonHarness.Create(ticketProjectId: ProjectId + 1);

        var result = await harness.Service.StartAsync(ProjectId, TicketId);

        Assert.IsNull(result);
        Assert.AreEqual(0, harness.Workspaces.Requests.Count);
    }

    [TestMethod]
    public async Task StartAsync_MissingProjectPath_BlocksExplicitly()
    {
        var harness = SkeletonHarness.Create(localPath: null);

        var result = await harness.Service.StartAsync(ProjectId, TicketId);

        Assert.IsNotNull(result);
        Assert.AreEqual(RunLifecycleState.Failed.ToString(), result!.Status);
        StringAssert.StartsWith(result.Message, "Blocked: ProjectPathMissing");
        var blocked = harness.Events.Single("SkeletonRunBlocked");
        Assert.AreEqual("ProjectPathMissing", blocked.Payload["blockedReason"]);
        Assert.IsTrue(blocked.Payload.ContainsKey("nextSafeAction"), "A blocked state must name the next safe action.");
        Assert.AreEqual(0, harness.Workspaces.Requests.Count, "A blocked run must not reach the workspace.");
    }

    [TestMethod]
    public async Task StartAsync_ReadinessBlocked_BlocksExplicitly_GateStaysAtOwningStep()
    {
        var harness = SkeletonHarness.Create(proposalBehavior: () =>
            throw new InvalidOperationException("Build blocked: index is stale."));

        var result = await harness.Service.StartAsync(ProjectId, TicketId);

        Assert.IsNotNull(result);
        StringAssert.StartsWith(result!.Message, "Blocked: ReadinessBlocked");
        StringAssert.Contains(result.Message, "index is stale");
        Assert.AreEqual("ReadinessBlocked", harness.Events.Single("SkeletonRunBlocked").Payload["blockedReason"]);
        Assert.AreEqual(0, harness.Workspaces.Requests.Count);
    }

    [TestMethod]
    public async Task StartAsync_EmptyProposal_BlocksExplicitly()
    {
        var harness = SkeletonHarness.Create(proposalBehavior: () => new BuilderProposal
        {
            TicketId = TicketId,
            ProjectId = ProjectId,
            Changes = [new ProposedFileChange { FilePath = "src/Broken.cs", IsValid = false }]
        });

        var result = await harness.Service.StartAsync(ProjectId, TicketId);

        Assert.IsNotNull(result);
        StringAssert.StartsWith(result!.Message, "Blocked: ProposalEmpty");
        Assert.AreEqual(0, harness.Workspaces.Requests.Count);
    }

    // ── Happy path: composition, evidence, and the non-authority ending ───────

    [TestMethod]
    public async Task StartAsync_ValidProposal_AppliesFileWritesInWorkspaceAndPackagesEvidence()
    {
        var harness = SkeletonHarness.Create(proposalBehavior: () => new BuilderProposal
        {
            TicketId = TicketId,
            ProjectId = ProjectId,
            Summary = "Add book sorting.",
            Changes =
            [
                new ProposedFileChange { FilePath = "src/Catalog/SortOptions.cs", IsValid = true, IsNewFile = true, FullContentAfter = "public enum SortOptions { Title }" },
                new ProposedFileChange { FilePath = "src/Old.cs", IsValid = true, IsDeletion = true },
                new ProposedFileChange { FilePath = "src/Skipped.cs", IsValid = false, FullContentAfter = "ignored" }
            ]
        });

        var result = await harness.Service.StartAsync(ProjectId, TicketId);

        Assert.IsNotNull(result);
        Assert.IsFalse(result!.RequiresHumanApproval, "The skeleton must not request or simulate approval.");

        var request = harness.Workspaces.Requests.Single();
        Assert.AreEqual(2, request.FileWrites.Count, "Only valid changes become workspace file writes.");
        Assert.AreEqual("src/Catalog/SortOptions.cs", request.FileWrites[0].RelativePath);
        Assert.IsTrue(request.FileWrites[1].IsDeletion);
        Assert.IsTrue(request.Commands.Count >= 2, "Build and test commands must run in the workspace.");

        var generated = harness.Events.Single("ProposalGenerated");
        StringAssert.StartsWith(generated.Payload["proposalId"], "prop-");

        var packaged = harness.Events.Single("SkeletonEvidencePackaged");
        StringAssert.Contains(packaged.Message, "This run grants nothing");
        Assert.AreEqual(generated.Payload["proposalId"], packaged.Payload["proposalId"]);

        var proposalEvidence = Path.Combine(harness.EvidenceRoot, "runs", result.RunId, "evidence", "proposal.json");
        Assert.IsTrue(File.Exists(proposalEvidence), "The proposal must persist as run evidence with an id.");
        StringAssert.Contains(await File.ReadAllTextAsync(proposalEvidence), "It is not approval");
    }

    // ── No approval surface, statically ───────────────────────────────────────

    [TestMethod]
    public void SkeletonRunService_SourceHasNoApprovalOrExecutorSurface()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Infrastructure", "Services", "TicketSkeletonRunService.cs"));

        foreach (var forbidden in new[]
        {
            "IAcceptedApprovalStore",
            "ControlledSourceApply",
            "ControlledCommitExecutor",
            "ControlledPushExecutor",
            "SatisfyPolicy",
            "PausedForApproval",
            "ApprovalGranted"
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"The skeleton orchestrator must not touch the approval or executor tier: {forbidden}");
        }

        StringAssert.Contains(source, "grants nothing");
        StringAssert.Contains(source, "no new authority");
    }

    [TestMethod]
    public void SkeletonRunContract_ExposesStartOnly()
    {
        var methods = typeof(ITicketSkeletonRunService)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "StartAsync" }, methods,
            "The skeleton contract is start-only: no approve, apply, promote, or continue surface.");
    }

    // ── Workspace containment: file writes cannot escape ──────────────────────

    [TestMethod]
    public async Task WorkspaceFileWrites_EscapingPath_FailsRunAndWritesNothingOutside()
    {
        using var dirs = TempDirs.Create();
        var service = new DisposableWorkspaceExecutionService(new InMemoryRunStore(), new InMemoryRunEventStore());

        var result = await service.RunAsync(new DisposableWorkspaceRunRequest
        {
            RunId = "skel-escape",
            SourcePath = dirs.Source,
            WorkspaceRoot = dirs.WorkspaceRoot,
            EvidenceRoot = dirs.Evidence,
            FileWrites = [new DisposableWorkspaceFileWrite { RelativePath = "../escape.txt", Content = "nope" }],
            Commands = []
        });

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(File.Exists(Path.Combine(dirs.WorkspaceRoot, "escape.txt")), "Nothing may land outside the workspace.");
        Assert.IsFalse(File.Exists(Path.Combine(dirs.Source, "escape.txt")), "Nothing may land in the source repository.");
    }

    [TestMethod]
    public async Task WorkspaceFileWrites_ValidWrite_LandsInWorkspaceOnly_WithManifestEvidence()
    {
        using var dirs = TempDirs.Create();
        await File.WriteAllTextAsync(Path.Combine(dirs.Source, "existing.txt"), "original");
        var service = new DisposableWorkspaceExecutionService(new InMemoryRunStore(), new InMemoryRunEventStore());

        var result = await service.RunAsync(new DisposableWorkspaceRunRequest
        {
            RunId = "skel-write",
            SourcePath = dirs.Source,
            WorkspaceRoot = dirs.WorkspaceRoot,
            EvidenceRoot = dirs.Evidence,
            CleanWorkspaceOnSuccess = false,
            FileWrites = [new DisposableWorkspaceFileWrite { RelativePath = "src/New.cs", Content = "class New {}" }],
            Commands = []
        });

        Assert.IsTrue(result.Succeeded, result.Summary);
        Assert.IsTrue(File.Exists(Path.Combine(result.WorkspacePath, "src", "New.cs")));
        Assert.IsFalse(File.Exists(Path.Combine(dirs.Source, "src", "New.cs")), "The source repository must remain untouched.");
        Assert.IsTrue(File.Exists(Path.Combine(result.EvidencePath, "file-writes.json")), "Applied writes must leave a manifest.");
    }

    // ── Harness and fakes ─────────────────────────────────────────────────────

    private static string RepositoryFile(params string[] parts)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "IronDev.slnx")))
            root = Path.GetDirectoryName(root);
        Assert.IsNotNull(root, "Repository root not found.");
        return Path.Combine(root!, Path.Combine(parts));
    }

    private sealed class SkeletonHarness
    {
        public required TicketSkeletonRunService Service { get; init; }
        public required RecordingWorkspaceService Workspaces { get; init; }
        public required InMemoryRunEventStore Events { get; init; }
        public required string EvidenceRoot { get; init; }

        public static SkeletonHarness Create(
            int? ticketProjectId = null,
            string? localPath = "__temp__",
            Func<BuilderProposal>? proposalBehavior = null)
        {
            var sourceDir = Path.Combine(Path.GetTempPath(), "irondev-skel-src-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sourceDir);
            var evidenceRoot = Path.Combine(Path.GetTempPath(), "irondev-skel-ev-" + Guid.NewGuid().ToString("N"));

            var resolvedPath = localPath == "__temp__" ? sourceDir : localPath;
            var workspaces = new RecordingWorkspaceService();
            var events = new InMemoryRunEventStore();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DisposableBuild:EvidenceRoot"] = evidenceRoot,
                ["DisposableBuild:WorkspaceRoot"] = Path.Combine(Path.GetTempPath(), "irondev-skel-ws-" + Guid.NewGuid().ToString("N"))
            }).Build();

            var service = new TicketSkeletonRunService(
                new StubTicketService(new ProjectTicket { Id = TicketId, ProjectId = ticketProjectId ?? ProjectId, Title = "Add book sorting" }),
                new StubProjectService(new Project { Id = ProjectId, TenantId = 1, Name = "BookSeller", LocalPath = resolvedPath }),
                new StubProposalService(proposalBehavior ?? (() => new BuilderProposal
                {
                    TicketId = TicketId,
                    ProjectId = ProjectId,
                    Changes = [new ProposedFileChange { FilePath = "src/A.cs", IsValid = true, FullContentAfter = "class A {}" }]
                })),
                workspaces,
                new InMemoryRunStore(),
                events,
                configuration);

            return new SkeletonHarness
            {
                Service = service,
                Workspaces = workspaces,
                Events = events,
                EvidenceRoot = evidenceRoot
            };
        }
    }

    private sealed class StubTicketService(ProjectTicket ticket) : ITicketService
    {
        public Task<long> SaveTicketAsync(ProjectTicket toSave, CancellationToken ct = default) => Task.FromResult(toSave.Id);
        public Task<IReadOnlyList<ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProjectTicket>>([ticket]);
        public Task<ProjectTicket?> GetTicketByIdAsync(long ticketId, CancellationToken ct = default) =>
            Task.FromResult<ProjectTicket?>(ticketId == ticket.Id ? ticket : null);
        public Task<bool> ArchiveTicketAsync(long ticketId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class StubProjectService(Project project) : IProjectService
    {
        public Task<int> CreateProjectAsync(Project toCreate, CancellationToken ct = default) => Task.FromResult(toCreate.Id);
        public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Project>>([project]);
        public Task<Project?> GetByIdAsync(int projectId, CancellationToken ct = default) =>
            Task.FromResult<Project?>(projectId == project.Id ? project : null);
        public Task<Project?> UpdateProjectAsync(int projectId, Project toUpdate, CancellationToken ct = default) =>
            Task.FromResult<Project?>(project);
        public Task UpdateLocalPathAsync(int projectId, string localPath, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkIndexStaleAsync(int projectId, string reason, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubProposalService(Func<BuilderProposal> behavior) : IBuilderProposalService
    {
        public Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken ct = default) =>
            Task.FromResult(behavior());
        public Task<BuilderProposal> GenerateProposalFromRequestAsync(int projectId, string request, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class RecordingWorkspaceService : IDisposableWorkspaceExecutionService
    {
        public List<DisposableWorkspaceRunRequest> Requests { get; } = [];

        public Task<DisposableWorkspaceRunResult> RunAsync(DisposableWorkspaceRunRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new DisposableWorkspaceRunResult
            {
                RunId = request.RunId,
                WorkspacePath = Path.Combine(request.WorkspaceRoot, request.RunId),
                Succeeded = true,
                Summary = "Recorded workspace run.",
                EvidencePath = Path.Combine(request.EvidenceRoot ?? request.WorkspaceRoot, request.RunId),
                Commands = []
            });
        }
    }

    private sealed class InMemoryRunStore : IRunStore
    {
        private readonly Dictionary<string, RunRecord> _runs = new(StringComparer.Ordinal);
        private int _sequence;

        public Task<RunRecord> CreateAsync(CreateRunRequest request, CancellationToken ct = default)
        {
            var runId = request.RunId ?? $"run-{Interlocked.Increment(ref _sequence):D4}";
            var record = new RunRecord
            {
                RunId = runId,
                ProjectId = request.ProjectId,
                TicketId = request.TicketId,
                State = RunLifecycleState.Created,
                IsDisposable = request.IsDisposable,
                Summary = request.Summary ?? string.Empty
            };
            _runs[runId] = record;
            return Task.FromResult(record);
        }

        public Task<RunRecord?> GetAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult(_runs.TryGetValue(runId, out var record) ? record : null);

        public Task<IReadOnlyList<RunRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunRecord>>(_runs.Values.Take(limit).ToList());

        public Task<RunRecord?> TransitionAsync(RunStateTransition transition, CancellationToken ct = default)
        {
            if (!_runs.TryGetValue(transition.RunId, out var record))
                return Task.FromResult<RunRecord?>(null);
            var updated = record with
            {
                State = transition.State,
                Summary = string.IsNullOrWhiteSpace(transition.Summary) ? record.Summary : transition.Summary,
                FailureReason = transition.FailureReason ?? record.FailureReason
            };
            _runs[transition.RunId] = updated;
            return Task.FromResult<RunRecord?>(updated);
        }
    }

    private sealed class InMemoryRunEventStore : IRunEventStore
    {
        private readonly List<RunEventDto> _events = [];

        public Task PublishAsync(RunEventDto runEvent, CancellationToken ct = default)
        {
            _events.Add(runEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RunEventDto>> GetEventsAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunEventDto>>(_events.Where(e => e.RunId == runId).ToList());

        public Task<IReadOnlyList<string>> GetRecentRunIdsAsync(int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(_events.Select(e => e.RunId).Distinct().Take(limit).ToList());

        public async IAsyncEnumerable<RunEventDto> StreamEventsAsync(string runId, [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var runEvent in _events.Where(e => e.RunId == runId))
            {
                ct.ThrowIfCancellationRequested();
                yield return runEvent;
            }
            await Task.CompletedTask;
        }

        public RunEventDto Single(string eventType)
        {
            var matches = _events.Where(e => string.Equals(e.EventType, eventType, StringComparison.Ordinal)).ToList();
            Assert.AreEqual(1, matches.Count, $"Expected exactly one '{eventType}' event, found {matches.Count}.");
            return matches[0];
        }
    }

    private sealed class TempDirs : IDisposable
    {
        public required string Source { get; init; }
        public required string WorkspaceRoot { get; init; }
        public required string Evidence { get; init; }

        public static TempDirs Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "irondev-skel-" + Guid.NewGuid().ToString("N"));
            var dirs = new TempDirs
            {
                Source = Path.Combine(root, "source"),
                WorkspaceRoot = Path.Combine(root, "workspaces"),
                Evidence = Path.Combine(root, "evidence")
            };
            Directory.CreateDirectory(dirs.Source);
            Directory.CreateDirectory(dirs.WorkspaceRoot);
            Directory.CreateDirectory(dirs.Evidence);
            return dirs;
        }

        public void Dispose()
        {
            try
            {
                var root = Path.GetDirectoryName(Source);
                if (root is not null && Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best-effort cleanup of temp directories
            }
        }
    }
}
