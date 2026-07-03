using System.Runtime.CompilerServices;
using IronDev.Core.Builder;
using IronDev.Core.Governance;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Core.Workflow;
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
            throw new BuildReadinessBlockedException("Build blocked: index is stale.", ["index is stale"]));

        var result = await harness.Service.StartAsync(ProjectId, TicketId);

        Assert.IsNotNull(result);
        StringAssert.StartsWith(result!.Message, "Blocked: ReadinessBlocked");
        StringAssert.Contains(result.Message, "index is stale");
        Assert.AreEqual("ReadinessBlocked", harness.Events.Single("SkeletonRunBlocked").Payload["blockedReason"]);
        Assert.AreEqual(0, harness.Workspaces.Requests.Count);
    }

    [TestMethod]
    public async Task StartAsync_ProposalServiceFailure_IsClassifiedForOperators_NotAsTicketProblem()
    {
        var harness = SkeletonHarness.Create(proposalBehavior: () =>
            throw new InvalidOperationException("Model provider timed out."));

        var result = await harness.Service.StartAsync(ProjectId, TicketId);

        Assert.IsNotNull(result);
        StringAssert.StartsWith(result!.Message, "Blocked: ProposalGenerationFailed");
        var blocked = harness.Events.Single("SkeletonRunBlocked");
        Assert.AreEqual("ProposalGenerationFailed", blocked.Payload["blockedReason"]);
        StringAssert.Contains(blocked.Payload["nextSafeAction"], "service failure");
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
        // P0-3 conscious update: a successful run now halts FOR approval — honestly
        // reporting that a human decision is required. It still cannot grant one.
        Assert.IsTrue(result!.RequiresHumanApproval, "A successful skeleton run halts for approval.");
        Assert.AreEqual(RunLifecycleState.PausedForApproval.ToString(), result.Status);

        var halt = harness.Events.Single("ApprovalRequiredHalt");
        StringAssert.Contains(halt.Message, "Halt is not approval");
        Assert.AreEqual(TicketSkeletonRunService.ApprovalTargetKind, halt.Payload["approvalTargetKind"]);
        Assert.IsTrue(halt.Payload["approvalTargetHash"].Length == 64, "The approval requirement binds to the critic-package hash.");

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

    // ── P0-2: the critic package ──────────────────────────────────────────────

    [TestMethod]
    public async Task StartAsync_PreparesCriticPackage_AtFullFidelity()
    {
        var harness = SkeletonHarness.Create(proposalBehavior: () => new BuilderProposal
        {
            TicketId = TicketId,
            ProjectId = ProjectId,
            Summary = "Add book sorting.",
            Rationale = "Sorting is a criterion.",
            Changes =
            [
                new ProposedFileChange
                {
                    FilePath = "src/Catalog/SortOptions.cs",
                    Description = "New sort options enum",
                    IsValid = true,
                    IsNewFile = true,
                    Diff = "+public enum SortOptions { Title }",
                    FullContentAfter = "public enum SortOptions { Title }"
                }
            ]
        });

        var result = await harness.Service.StartAsync(ProjectId, TicketId);
        Assert.IsNotNull(result);

        var readyEvent = harness.Events.Single("CriticReviewPackageReady");
        StringAssert.Contains(readyEvent.Message, "not a review");
        StringAssert.StartsWith(readyEvent.Payload["packageId"], "critic-pkg-");

        var package = await harness.Service.GetCriticPackageAsync(ProjectId, TicketId, result!.RunId);
        Assert.IsNotNull(package, "The prepared package must be readable back.");
        Assert.AreEqual(1, package!.Changes.Count);
        Assert.AreEqual("+public enum SortOptions { Title }", package.Changes[0].Diff, "The critic gets the exact diff.");
        Assert.AreEqual("public enum SortOptions { Title }", package.Changes[0].FullContentAfter, "The critic gets the complete proposed content, never a thin manifest.");
        StringAssert.Contains(package.Boundary, "not a review");
        StringAssert.Contains(package.Boundary, "grants nothing");
    }

    [TestMethod]
    public void CriticPackageContract_CarriesNoVerdictFindingOrAuthoritySurface()
    {
        var propertyNames = typeof(SkeletonCriticPackage)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        foreach (var forbidden in new[] { "Verdict", "Finding", "Approve", "Approval", "Decision", "Authority" })
        {
            Assert.IsFalse(
                propertyNames.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)),
                $"The critic package is review material and must carry no review-output or authority surface: {forbidden}");
        }
    }

    [TestMethod]
    public void SkeletonRunService_DoesNotCreateOrSimulateCriticReviews()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Infrastructure", "Services", "TicketSkeletonRunService.cs"));

        foreach (var forbidden in new[]
        {
            "ExecuteAndStore",
            "ManualCriticReviewRequest",
            "CriticReviewResult",
            "FindingDraft",
            "RequestedVerdict"
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"The orchestrator packages work for the critic; it must never write the critic's review: {forbidden}");
        }
    }

    // ── P0-3: approval consumption ────────────────────────────────────────────

    [TestMethod]
    public async Task ContinueAsync_WithoutApproval_StaysHaltedExplicitly()
    {
        var harness = SkeletonHarness.Create();
        var run = await harness.Service.StartAsync(ProjectId, TicketId);

        var result = await harness.Service.ContinueAsync(ProjectId, TicketId, run!.RunId);

        Assert.IsNotNull(result);
        Assert.AreEqual(RunLifecycleState.PausedForApproval.ToString(), result!.Status);
        Assert.IsTrue(result.RequiresHumanApproval);
        var refusals = harness.Events.All("ApprovalRequiredHalt");
        Assert.IsTrue(refusals.Any(e => e.Payload.TryGetValue("refusedReason", out var reason) && reason == "MissingOrUnsatisfiedApproval"),
            "Refusal must be explicit and name its reason.");
    }

    [TestMethod]
    public async Task ContinueAsync_WithMatchingLiveApproval_Unblocks()
    {
        var harness = SkeletonHarness.Create();
        var run = await harness.Service.StartAsync(ProjectId, TicketId);
        var packageHash = harness.Events.Single("CriticReviewPackageReady").Payload["packageSha256"];
        harness.Approvals.Seed(ApprovalFor(run!.RunId, packageHash));

        var result = await harness.Service.ContinueAsync(ProjectId, TicketId, run.RunId);

        Assert.IsNotNull(result);
        Assert.AreEqual(RunLifecycleState.Completed.ToString(), result!.Status);
        Assert.IsFalse(result.RequiresHumanApproval);
        var unblocked = harness.Events.Single("SkeletonContinuationUnblocked");
        StringAssert.Contains(unblocked.Message, "not apply permission");
        Assert.IsFalse(string.IsNullOrWhiteSpace(unblocked.Payload["acceptedApprovalId"]), "The consumed approval must be named.");
        Assert.AreEqual(0, harness.Approvals.SaveCallCount, "The skeleton can never create an approval.");
    }

    [TestMethod]
    public async Task ContinueAsync_WithExpiredApproval_StaysHalted()
    {
        var harness = SkeletonHarness.Create();
        var run = await harness.Service.StartAsync(ProjectId, TicketId);
        var packageHash = harness.Events.Single("CriticReviewPackageReady").Payload["packageSha256"];
        harness.Approvals.Seed(ApprovalFor(run!.RunId, packageHash, expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5)));

        var result = await harness.Service.ContinueAsync(ProjectId, TicketId, run.RunId);

        Assert.AreEqual(RunLifecycleState.PausedForApproval.ToString(), result!.Status);
        Assert.IsTrue(result.RequiresHumanApproval, "An expired approval is not approval.");
    }

    [TestMethod]
    public async Task ContinueAsync_WithHashMismatch_StaysHalted_ApprovalBindsToWhatWasReviewed()
    {
        var harness = SkeletonHarness.Create();
        var run = await harness.Service.StartAsync(ProjectId, TicketId);
        harness.Approvals.Seed(ApprovalFor(run!.RunId, targetHash: new string('a', 64)));

        var result = await harness.Service.ContinueAsync(ProjectId, TicketId, run.RunId);

        Assert.AreEqual(RunLifecycleState.PausedForApproval.ToString(), result!.Status);
        Assert.IsTrue(result.RequiresHumanApproval,
            "An approval bound to a different package hash approves something else — it must not unblock this run.");
    }

    [TestMethod]
    public async Task ContinueAsync_OnRunNotAwaitingApproval_RefusesExplicitly()
    {
        var harness = SkeletonHarness.Create(proposalBehavior: () =>
            throw new BuildReadinessBlockedException("Build blocked: index is stale.", ["index is stale"]));
        var run = await harness.Service.StartAsync(ProjectId, TicketId);

        var result = await harness.Service.ContinueAsync(ProjectId, TicketId, run!.RunId);

        Assert.IsNotNull(result);
        Assert.AreEqual(RunLifecycleState.Failed.ToString(), result!.Status);
        var refused = harness.Events.Single("ContinuationRefused");
        Assert.AreEqual("NotAwaitingApproval", refused.Payload["refusedReason"]);
    }

    // ── No approval surface, statically ───────────────────────────────────────

    [TestMethod]
    public void SkeletonRunService_ConsumesApprovalsButCannotCreateThemOrTouchExecutors()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Infrastructure", "Services", "TicketSkeletonRunService.cs"));

        // History: P0-1 forbade any approval reference ("no approval surface at all").
        // P0-3 consciously permits CONSUMPTION — querying and verifying live accepted
        // approvals, and halting PausedForApproval. Creation and the executor tier
        // remain forbidden.
        foreach (var forbidden in new[]
        {
            "SaveAsync",
            "AcceptedApprovalCreateService",
            "ControlledSourceApply",
            "ControlledCommitExecutor",
            "ControlledPushExecutor",
            "SatisfyPolicy",
            "ApprovalGranted"
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"The skeleton may consume approval evidence but never create it or touch the executor tier: {forbidden}");
        }

        StringAssert.Contains(source, "grants nothing");
        StringAssert.Contains(source, "no new authority");
        StringAssert.Contains(source, "Halt is not approval");
        StringAssert.Contains(source, "never create");
    }

    [TestMethod]
    public void SkeletonRunContract_ExposesStartOnly()
    {
        var methods = typeof(ITicketSkeletonRunService)
            .GetMethods()
            .Select(method => method.Name)
            .ToArray();

        // History: P0-1 pinned StartAsync only; P0-2 added the package read; P0-3 adds
        // ContinueAsync — continuation gated on live approval evidence. The contract
        // still has no approve, apply, promote, or review-create surface: ContinueAsync
        // consumes approvals recorded elsewhere, it cannot mint them.
        CollectionAssert.AreEquivalent(new[] { "StartAsync", "GetCriticPackageAsync", "ContinueAsync" }, methods,
            "The skeleton contract is start + read-package + approval-gated continue: no approve, apply, promote, or review-create surface.");
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
        public required InMemoryAcceptedApprovalStore Approvals { get; init; }
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

            var approvals = new InMemoryAcceptedApprovalStore();
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
                approvals,
                new ApprovalSatisfactionEvaluator(),
                new WorkflowApprovalHaltEvaluator(),
                configuration);

            return new SkeletonHarness
            {
                Service = service,
                Workspaces = workspaces,
                Events = events,
                Approvals = approvals,
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

    private sealed class InMemoryAcceptedApprovalStore : IAcceptedApprovalStore
    {
        private readonly List<AcceptedApprovalRecord> _records = [];

        public int SaveCallCount { get; private set; }

        public void Seed(AcceptedApprovalRecord record) => _records.Add(record);

        public Task SaveAsync(AcceptedApprovalRecord record, CancellationToken ct = default)
        {
            SaveCallCount++;
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task<AcceptedApprovalRecord?> GetAsync(Guid projectId, Guid acceptedApprovalId, CancellationToken ct = default) =>
            Task.FromResult(_records.FirstOrDefault(r => r.ProjectId == projectId && r.AcceptedApprovalId == acceptedApprovalId));

        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByTargetAsync(Guid projectId, string approvalTargetKind, string approvalTargetId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>(_records
                .Where(r => r.ProjectId == projectId &&
                            string.Equals(r.ApprovalTargetKind, approvalTargetKind, StringComparison.Ordinal) &&
                            string.Equals(r.ApprovalTargetId, approvalTargetId, StringComparison.Ordinal))
                .ToList());

        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByCorrelationAsync(string correlationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>([]);

        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByProjectAndCorrelationAsync(Guid projectId, string correlationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>([]);
    }

    private static AcceptedApprovalRecord ApprovalFor(string runId, string targetHash, DateTimeOffset? expiresAtUtc = null, string? capabilityCode = null) =>
        new()
        {
            AcceptedApprovalId = Guid.NewGuid(),
            ProjectId = TicketSkeletonRunService.ApprovalProjectGuid(ProjectId),
            ApprovalTargetKind = TicketSkeletonRunService.ApprovalTargetKind,
            ApprovalTargetId = runId,
            ApprovalTargetHash = targetHash,
            CapabilityCode = capabilityCode ?? TicketSkeletonRunService.ContinueCapabilityCode,
            ApprovalPurpose = AcceptedApprovalPurposes.WorkflowContinuationInput,
            ApprovedByActorId = "human-gate-user-1",
            AcceptedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = expiresAtUtc,
            CorrelationId = $"corr-{runId}",
            CausationId = $"cause-{runId}",
            EvidenceReferences = [$"critic-pkg-{runId}"],
            BoundaryMaxims = ["Approval evidence is not apply permission."]
        };

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
            var matches = All(eventType);
            Assert.AreEqual(1, matches.Count, $"Expected exactly one '{eventType}' event, found {matches.Count}.");
            return matches[0];
        }

        public IReadOnlyList<RunEventDto> All(string eventType) =>
            _events.Where(e => string.Equals(e.EventType, eventType, StringComparison.Ordinal)).ToList();
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
