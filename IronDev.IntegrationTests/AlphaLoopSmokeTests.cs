using System.Diagnostics;
using System.Text.Json;
using IronDev.Core;
using IronDev.Core.Builder;
using IronDev.Core.Governance;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Core.RunReadiness;
using IronDev.Core.Workflow;
using IronDev.Core.Workspaces;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.RunReports;
using IronDev.Infrastructure.Services.Runs;
using IronDev.Infrastructure.Services.Workspaces;
using IronDev.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// D-series deterministic alpha smoke: one BookSeller ticket driven through the real
/// governed loop, with only model words faked.
///
/// Gate mode proves the real orchestrator, disposable workspace, build/test
/// execution, critic-package creation, report reconstruction, and approval halt.
/// REL-2 applied mode then records an explicit deterministic human approval,
/// requests continuation, and uses the copy-only apply spine. Neither mode grants
/// release/deployment authority or claims alpha readiness.
/// </summary>
[TestClass]
[TestCategory("LongRunning")]
[TestCategory("AlphaSmoke")]
[TestCategory("ReleaseReadiness")]
public sealed class AlphaLoopSmokeTests
{
    private const int ProjectId = 1;
    private const long TicketId = 101;

    private const string ValidatedBook =
        """
        namespace BookSeller.Domain;

        public sealed class Book
        {
            public Book(string isbn, string title, string author, decimal price)
            {
                if (string.IsNullOrWhiteSpace(isbn))
                    throw new System.ArgumentException("ISBN is required.", nameof(isbn));
                if (string.IsNullOrWhiteSpace(title))
                    throw new System.ArgumentException("Title is required.", nameof(title));
                if (price < 0)
                    throw new System.ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");

                Isbn = isbn;
                Title = title;
                Author = author;
                Price = price;
            }

            public string Isbn { get; }
            public string Title { get; }
            public string Author { get; }
            public decimal Price { get; }
        }
        """;

    [TestMethod]
    public async Task AlphaSmoke_OneTicket_ReachesHumanGate_WithADeterministicBuilder()
    {
        var workspaceParent = TempDir("irondev-alpha-ws");
        var evidenceRoot = TempDir("irondev-alpha-ev");
        var sampleCopy = TempDir("irondev-alpha-src");
        try
        {
            using var noNodeReuse = DisableMsBuildNodeReuse();
            CopySample(SampleRoot(), sampleCopy);
            PrepareRestoredBookSellerSource(sampleCopy);
            GitInit(sampleCopy);

            var runs = new InMemoryRunStore();
            var events = new InMemoryRunEventStore();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DisposableBuild:EvidenceRoot"] = evidenceRoot,
                ["DisposableBuild:WorkspaceRoot"] = workspaceParent,
                ["DisposableBuild:BuildTimeoutSeconds"] = "300",
                ["DisposableBuild:TestTimeoutSeconds"] = "300",
                ["AgentProfiles:Root"] = Path.Combine(evidenceRoot, "agent-profiles"),
                ["Ai:Provider"] = "fake",
                ["Ai:Model"] = "deterministic-alpha",
                ["Ai:TimeoutSeconds"] = "60",
                ["AgentProfiles:AllowFakeProvider"] = "true"
            }).Build();

            var service = new TicketSkeletonRunService(
                new StubTicketService(new ProjectTicket
                {
                    Id = TicketId,
                    ProjectId = ProjectId,
                    Title = "Reject invalid books at the door",
                    Summary = "Validate a Book at construction.",
                    AcceptanceCriteria = "Empty ISBN, empty title, or negative price must be rejected; zero price is valid."
                }),
                new StubProjectService(new Project { Id = ProjectId, TenantId = 1, Name = "BookSeller", LocalPath = sampleCopy }),
                new DeterministicBuilder(),
                new DisposableWorkspaceExecutionService(runs, events),
                runs,
                events,
                new InMemoryApprovals(),
                new ApprovalSatisfactionEvaluator(),
                new WorkflowApprovalHaltEvaluator(),
                new EmptyTestAuthoring(),
                new SkeletonMutationLeaseService(configuration),
                new StubProjectMembershipService(),
                new SkeletonAgentProfileService(configuration),
                configuration,
                new ReadyRunReadinessService(),
                new ReadyApplyCapabilityService());

            var run = await service.StartAsync(ProjectId, TicketId);
            Assert.IsNotNull(run, "The run must start.");
            var haltedRun = await RequireRunStateAsync(
                runs,
                events,
                run!.RunId,
                RunLifecycleState.PausedForApproval,
                "D-2a must stop at the human gate.");

            var runEvents = await events.GetEventsAsync(run.RunId);
            var packaged = runEvents.Single(e => e.EventType == "SkeletonEvidencePackaged");
            Assert.AreEqual("true", packaged.Payload["succeeded"],
                "The real dotnet build and dotnet test of the sample must pass.");

            var criticPackage = runEvents.Single(e => e.EventType == "CriticReviewPackageReady");
            var packageHash = criticPackage.Payload["packageSha256"];
            Assert.IsFalse(string.IsNullOrWhiteSpace(packageHash), "The critic package must be hash-sealed.");

            var halt = runEvents.Single(e => e.EventType == "ApprovalRequiredHalt");
            Assert.AreEqual(packageHash, halt.Payload["approvalTargetHash"],
                "Any later human approval must bind to the exact critic-package hash.");
            Assert.IsFalse(runEvents.Any(e => e.EventType == "SkeletonCriticReviewRecorded"),
                "D-2a prepares the critic package; it does not simulate the independent critic review.");
            Assert.IsFalse(runEvents.Any(e => e.EventType == "SkeletonContinuationUnblocked"),
                "D-2a must not continue past the human gate.");

            var report = await service.GetRunReportAsync(ProjectId, TicketId, run.RunId);
            Assert.IsNotNull(report, "The run report must reconstruct the halted chain.");
            Assert.AreEqual(RunLifecycleState.PausedForApproval.ToString(), report!.Status);
            Assert.IsNotNull(report.CriticPackage);
            Assert.IsNotNull(report.Approval);
            Assert.IsTrue(report.Approval!.HaltObserved);
            Assert.IsFalse(report.Approval.ContinuationUnblocked);

            WriteReceipt(new AlphaSmokeReceipt(
                Ticket: "validate-book",
                Project: "BookSeller",
                ModelMode: "Deterministic",
                RunUntil: "Gate",
                RunId: run.RunId,
                GateState: haltedRun.State.ToString(),
                BuildAndTestSucceeded: true,
                CriticPackageSha256: packageHash,
                ApprovalTargetHash: halt.Payload["approvalTargetHash"],
                BuilderModel: "deterministic-fake",
                AcceptedApprovalCreated: false,
                AcceptedApprovalRecorded: false,
                ContinuationRequested: false,
                ApplyRequested: false,
                CriticReviewRecorded: false,
                FinalState: haltedRun.State.ToString(),
                AcceptedApprovalId: string.Empty,
                ApplyReceiptPath: string.Empty,
                ApplyReceiptSha256: string.Empty,
                LoopComplete: false,
                ReportReconstructable: true,
                Proves:
                [
                    "real orchestrator wiring end to end",
                    "real dotnet build and dotnet test of the sample against the proposed change",
                    "hash-sealed critic package",
                    "halt at the human approval gate without creating approval or continuation"
                ],
                DoesNotProveYet:
                [
                    "a live model",
                    "SQL/API persistence (in-memory stores here)",
                    "independent critic review execution",
                    "human approval recording",
                    "workflow continuation",
                    "controlled source apply"
                ]));
        }
        finally
        {
            TryDelete(sampleCopy);
            TryDelete(workspaceParent);
            TryDelete(evidenceRoot);
        }
    }

    [TestMethod]
    public async Task AlphaSmoke_OneTicket_ReachesApplied_WithDeterministicApproval()
    {
        var workspaceParent = TempDir("irondev-alpha-ws");
        var evidenceRoot = TempDir("irondev-alpha-ev");
        var sampleCopy = TempDir("irondev-alpha-src");
        try
        {
            using var noNodeReuse = DisableMsBuildNodeReuse();
            CopySample(SampleRoot(), sampleCopy);
            PrepareRestoredBookSellerSource(sampleCopy);
            GitInit(sampleCopy);

            var runs = new InMemoryRunStore();
            var events = new InMemoryRunEventStore();
            var approvals = new InMemoryApprovals();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DisposableBuild:EvidenceRoot"] = evidenceRoot,
                ["DisposableBuild:WorkspaceRoot"] = workspaceParent,
                ["DisposableBuild:BuildTimeoutSeconds"] = "300",
                ["DisposableBuild:TestTimeoutSeconds"] = "300",
                ["SkeletonApply:Enabled"] = "true",
                ["AgentProfiles:Root"] = Path.Combine(evidenceRoot, "agent-profiles"),
                ["Ai:Provider"] = "fake",
                ["Ai:Model"] = "deterministic-alpha",
                ["Ai:TimeoutSeconds"] = "60",
                ["AgentProfiles:AllowFakeProvider"] = "true"
            }).Build();

            var service = new TicketSkeletonRunService(
                new StubTicketService(new ProjectTicket
                {
                    Id = TicketId,
                    ProjectId = ProjectId,
                    Title = "Reject invalid books at the door",
                    Summary = "Validate a Book at construction.",
                    AcceptanceCriteria = "Empty ISBN, empty title, or negative price must be rejected; zero price is valid."
                }),
                new StubProjectService(new Project { Id = ProjectId, TenantId = 1, Name = "BookSeller", LocalPath = sampleCopy }),
                new DeterministicBuilder(),
                new DisposableWorkspaceExecutionService(runs, events),
                runs,
                events,
                approvals,
                new ApprovalSatisfactionEvaluator(),
                new WorkflowApprovalHaltEvaluator(),
                new EmptyTestAuthoring(),
                new SkeletonMutationLeaseService(configuration),
                new StubProjectMembershipService(),
                new SkeletonAgentProfileService(configuration),
                configuration,
                new ReadyRunReadinessService(),
                new ReadyApplyCapabilityService());

            var run = await service.StartAsync(ProjectId, TicketId);
            Assert.IsNotNull(run, "The run must start.");
            await RequireRunStateAsync(
                runs,
                events,
                run!.RunId,
                RunLifecycleState.PausedForApproval,
                "REL-2 must still stop at the human gate before approval is recorded.");

            var packageEvent = (await events.GetEventsAsync(run.RunId))
                .Single(e => e.EventType == "CriticReviewPackageReady");
            var packageHash = packageEvent.Payload["packageSha256"];
            var halt = (await events.GetEventsAsync(run.RunId)).Single(e => e.EventType == "ApprovalRequiredHalt");
            Assert.AreEqual(packageHash, halt.Payload["approvalTargetHash"],
                "Approval must bind to the exact critic package hash.");

            await PublishCleanCriticReview(events, run.RunId, packageHash);

            var expectedPhrase = ExpectedApprovalPhrase(run.RunId, packageHash);
            var suppliedPhrase = RenderApprovalPhrase(
                Environment.GetEnvironmentVariable("ALPHA_SMOKE_APPROVAL_PHRASE"),
                run.RunId,
                packageHash);
            Assert.AreEqual(expectedPhrase, suppliedPhrase,
                "REL-2 records approval only when the supplied phrase binds to the run id and package hash.");

            var acceptedApproval = ApprovalFor(run.RunId, packageHash);
            await approvals.SaveAsync(acceptedApproval);

            var continued = await service.ContinueAsAsync(ProjectId, TicketId, run.RunId, "7");
            Assert.IsNotNull(continued);
            await RequireRunStateAsync(
                runs,
                events,
                continued!.RunId,
                RunLifecycleState.Completed,
                "Accepted approval only unblocks continuation; it is still not apply permission.");

            var applied = await service.ApplyAsAsync(ProjectId, TicketId, run.RunId, "7");
            Assert.IsNotNull(applied);
            var appliedRun = await RequireRunStateAsync(
                runs,
                events,
                applied!.RunId,
                RunLifecycleState.Applied,
                "REL-2 must reach Applied only through the governed copy-only apply spine.");

            var report = await service.GetRunReportAsync(ProjectId, TicketId, run.RunId);
            Assert.IsNotNull(report, "The final report must reconstruct the applied run.");
            Assert.IsTrue(report!.LoopComplete, $"Applied loop must be complete. Gaps: {string.Join(" | ", report.Gaps)}");
            Assert.AreEqual(acceptedApproval.AcceptedApprovalId.ToString("D"), report.Approval!.AcceptedApprovalId);
            Assert.IsTrue(report.Apply!.Applied);
            Assert.IsTrue(report.Apply.Receipts.All(receipt => receipt.ExistsOnDisk),
                "Every apply-spine receipt must exist on disk.");

            var landedPath = Path.Combine(sampleCopy, "src", "BookSeller.Domain", "Book.cs");
            Assert.AreEqual(ValidatedBook.Replace("\r\n", "\n"), (await File.ReadAllTextAsync(landedPath)).Replace("\r\n", "\n"),
                "The approved Book change must land in the disposable source copy.");
            Assert.IsFalse(Directory.Exists(Path.Combine(sampleCopy, ".irondev")),
                "Apply evidence must stay in the workspace, not the source repo.");

            var applyReceipt = report.Apply.Receipts.Single(receipt => receipt.Name == "apply-copy.json");
            var persistedApplyReceiptPath = CopySmokeArtifact(applyReceipt.Path, "apply-copy.json");
            var applyReceiptHash = ComputeSha256(await File.ReadAllBytesAsync(persistedApplyReceiptPath));

            WriteReceipt(new AlphaSmokeReceipt(
                Ticket: "validate-book",
                Project: "BookSeller",
                ModelMode: "Deterministic",
                RunUntil: "Applied",
                RunId: run.RunId,
                GateState: RunLifecycleState.PausedForApproval.ToString(),
                BuildAndTestSucceeded: true,
                CriticPackageSha256: packageHash,
                ApprovalTargetHash: halt.Payload["approvalTargetHash"],
                BuilderModel: "deterministic-fake",
                AcceptedApprovalCreated: false,
                AcceptedApprovalRecorded: true,
                ContinuationRequested: true,
                ApplyRequested: true,
                CriticReviewRecorded: true,
                FinalState: appliedRun.State.ToString(),
                AcceptedApprovalId: acceptedApproval.AcceptedApprovalId.ToString("D"),
                ApplyReceiptPath: persistedApplyReceiptPath,
                ApplyReceiptSha256: applyReceiptHash,
                LoopComplete: report.LoopComplete,
                ReportReconstructable: true,
                Proves:
                [
                    "real orchestrator wiring end to end",
                    "real dotnet build and dotnet test of the sample against the proposed change",
                    "hash-sealed critic package",
                    "clean critic review recorded as deterministic smoke evidence",
                    "hash-bound accepted approval consumed by continuation",
                    "copy-only apply spine reached Applied and left an evidence chain"
                ],
                DoesNotProveYet:
                [
                    "a live model",
                    "SQL/API persistence (in-memory stores here)",
                    "external critic service execution",
                    "product UI approval recording",
                    "commit, push, release, or deployment"
                ]));
        }
        finally
        {
            TryDelete(sampleCopy);
            TryDelete(workspaceParent);
            TryDelete(evidenceRoot);
        }
    }

    private sealed record AlphaSmokeReceipt(
        string Ticket,
        string Project,
        string ModelMode,
        string RunUntil,
        string RunId,
        string GateState,
        bool BuildAndTestSucceeded,
        string CriticPackageSha256,
        string ApprovalTargetHash,
        string BuilderModel,
        bool AcceptedApprovalCreated,
        bool AcceptedApprovalRecorded,
        bool ContinuationRequested,
        bool ApplyRequested,
        bool CriticReviewRecorded,
        string FinalState,
        string AcceptedApprovalId,
        string ApplyReceiptPath,
        string ApplyReceiptSha256,
        bool LoopComplete,
        bool ReportReconstructable,
        string[] Proves,
        string[] DoesNotProveYet);

    private static void WriteReceipt(AlphaSmokeReceipt receipt)
    {
        var path = Environment.GetEnvironmentVariable("ALPHA_SMOKE_RECEIPT");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Path.GetTempPath(), "IronDev", "alpha-smoke", "receipt.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"ALPHA_SMOKE_RECEIPT_PATH::{path}");
    }

    private static string CopySmokeArtifact(string sourcePath, string fileName)
    {
        var receiptPath = Environment.GetEnvironmentVariable("ALPHA_SMOKE_RECEIPT");
        var outputDirectory = string.IsNullOrWhiteSpace(receiptPath)
            ? Path.Combine(Path.GetTempPath(), "IronDev", "alpha-smoke")
            : Path.GetDirectoryName(receiptPath)!;

        Directory.CreateDirectory(outputDirectory);
        var targetPath = Path.Combine(outputDirectory, fileName);
        File.Copy(sourcePath, targetPath, overwrite: true);
        return targetPath;
    }

    private sealed class DeterministicBuilder : IBuilderProposalService
    {
        public Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken ct = default) =>
            Task.FromResult(new BuilderProposal
            {
                TicketId = TicketId,
                ProjectId = ProjectId,
                Summary = "Validate Book at construction.",
                Rationale = "Reject empty ISBN/title and negative price so downstream code can trust a Book.",
                ModelProvider = "deterministic-fake",
                ModelName = "validate-book-fixed",
                Changes =
                [
                    new ProposedFileChange
                    {
                        FilePath = "src/BookSeller.Domain/Book.cs",
                        Description = "Add constructor validation.",
                        IsValid = true,
                        FullContentAfter = ValidatedBook
                    }
                ]
            });

        public Task<BuilderProposal> GenerateProposalFromRequestAsync(int projectId, string request, CancellationToken ct = default) =>
            GenerateProposalAsync(TicketId, ct);

        public Task<BuilderProposal> GenerateRepairProposalAsync(long ticketId, SkeletonRepairContext repair, CancellationToken ct = default) =>
            GenerateProposalAsync(ticketId, ct);

        public Task<BuilderProposal> GenerateRevisionProposalAsync(long ticketId, SkeletonRevisionContext revision, CancellationToken ct = default) =>
            throw new NotSupportedException("The alpha smoke does not exercise human-directed revision.");

        public Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default) =>
            throw new NotSupportedException("The skeleton loop applies through its own governed spine, not the builder.");
    }

    private sealed class EmptyTestAuthoring : ISkeletonTestAuthoringService
    {
        public Task<SkeletonTestAuthoringResult> AuthorTestsAsync(SkeletonTestAuthoringRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SkeletonTestAuthoringResult { Succeeded = true, Tests = [] });
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

    private sealed class InMemoryApprovals : IAcceptedApprovalStore
    {
        private readonly List<AcceptedApprovalRecord> _records = [];

        public Task SaveAsync(AcceptedApprovalRecord record, CancellationToken ct = default)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task<AcceptedApprovalRecord?> GetAsync(Guid projectId, Guid acceptedApprovalId, CancellationToken ct = default) =>
            Task.FromResult(_records.FirstOrDefault(record =>
                record.ProjectId == projectId &&
                record.AcceptedApprovalId == acceptedApprovalId));

        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByTargetAsync(Guid projectId, string targetKind, string targetId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>(_records
                .Where(record =>
                    record.ProjectId == projectId &&
                    string.Equals(record.ApprovalTargetKind, targetKind, StringComparison.Ordinal) &&
                    string.Equals(record.ApprovalTargetId, targetId, StringComparison.Ordinal))
                .ToArray());

        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByCorrelationAsync(string correlationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>(_records
                .Where(record => string.Equals(record.CorrelationId, correlationId, StringComparison.Ordinal))
                .ToArray());

        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByProjectAndCorrelationAsync(Guid projectId, string correlationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>(_records
                .Where(record =>
                    record.ProjectId == projectId &&
                    string.Equals(record.CorrelationId, correlationId, StringComparison.Ordinal))
                .ToArray());
    }

    private static AcceptedApprovalRecord ApprovalFor(string runId, string targetHash) =>
        new()
        {
            AcceptedApprovalId = Guid.NewGuid(),
            ProjectId = TicketSkeletonRunService.ApprovalProjectGuid(ProjectId),
            ApprovalTargetKind = TicketSkeletonRunService.ApprovalTargetKind,
            ApprovalTargetId = runId,
            ApprovalTargetHash = targetHash,
            CapabilityCode = TicketSkeletonRunService.ContinueCapabilityCode,
            ApprovalPurpose = AcceptedApprovalPurposes.WorkflowContinuationInput,
            ApprovedByActorId = "8",
            ApprovedByActorDisplayName = "Alice Reviewer",
            AcceptedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            CorrelationId = $"rel2-{runId}",
            CausationId = $"critic-package-{runId}",
            EvidenceReferences = [$"critic-pkg-{runId}"],
            BoundaryMaxims =
            [
                "Accepted approval is continuation input only.",
                "Accepted approval is not apply, commit, push, release, or deployment authority."
            ]
        };

    private static Task PublishCleanCriticReview(InMemoryRunEventStore events, string runId, string packageHash) =>
        events.PublishAsync(new RunEventDto
        {
            RunId = runId,
            EventType = "SkeletonCriticReviewRecorded",
            Message = "Deterministic alpha smoke critic review recorded no findings. A critic review is not approval.",
            Payload = new Dictionary<string, string>
            {
                ["criticAgentRunId"] = $"critic-{runId}",
                ["reviewId"] = $"review-{runId}",
                ["verdict"] = "NoFindings",
                ["findingCount"] = "0",
                ["blockingFindingCount"] = "0",
                ["findingIds"] = string.Empty,
                ["packageSha256"] = packageHash,
                ["groundTruthCheckCount"] = "1",
                ["groundTruthMismatchCount"] = "0",
                ["modelProvider"] = "deterministic-fake",
                ["modelName"] = "critic-clean-fixed"
            }
        });

    private sealed class StubProjectMembershipService : IProjectMembershipService
    {
        private static readonly DateTimeOffset AddedUtc = new(2026, 7, 11, 0, 0, 0, TimeSpan.Zero);

        public Task<bool> HasAccessAsync(int tenantId, int projectId, int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId is 7 or 8);

        public Task<IReadOnlySet<int>> GetAccessibleProjectIdsAsync(int tenantId, int userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<int>>(userId is 7 or 8 ? new HashSet<int> { ProjectId } : new HashSet<int>());

        public Task<IReadOnlyList<ProjectMembershipEntry>> GetMembersAsync(int tenantId, int projectId, int currentUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectMembershipEntry>>(
            [
                new(7, "Bob Developer", "bob@irondev.local", ProjectMemberRoles.Owner, currentUserId == 7, AddedUtc),
                new(8, "Alice Reviewer", "alice@irondev.local", ProjectMemberRoles.Contributor, currentUserId == 8, AddedUtc)
            ]);

        public Task<ProjectMembershipMutationStatus> SetMemberAsync(int tenantId, int projectId, int userId, int actorUserId, string projectRole, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectMembershipMutationStatus.Succeeded);

        public Task<ProjectMembershipMutationStatus> RemoveMemberAsync(int tenantId, int projectId, int userId, int actorUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProjectMembershipMutationStatus.Succeeded);
    }

    private sealed class ReadyRunReadinessService : IProjectRunReadinessService
    {
        public Task<ProjectRunReadiness> EvaluateAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectRunReadiness
            {
                ProjectId = projectId,
                ProjectSetupReady = true,
                ExecutionReady = true,
                CompletionCapabilityReady = true,
                CompletionCapability = ReadyApplyCapabilityService.Result(projectId),
                ReadyToRun = true,
                State = ProjectRunReadinessStates.ReadyToRun,
                BlockedCount = 0
            });
    }

    private sealed class ReadyApplyCapabilityService : IProjectApplyCapabilityService
    {
        public static ProjectApplyCapability Result(int projectId) => new()
        {
            ProjectId = projectId,
            IsReady = true,
            State = "Ready",
            ReasonCode = ProjectApplyCapabilityReasonCodes.Ready,
            LauncherSessionId = "alpha-test-session",
            RepositoryCommit = "alpha-test-commit",
            SandboxRootFingerprint = "alpha-sandbox",
            ProjectPathFingerprint = "alpha-project",
            ReadinessEvidenceHash = "alpha-ready-evidence"
        };

        public Task<ProjectApplyCapability> EvaluateAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(projectId));

        public Task<ProjectApplyCapability> QualifyDisposableProjectAsync(
            int projectId,
            int qualifyingActorUserId,
            CancellationToken cancellationToken = default) =>
            EvaluateAsync(projectId, cancellationToken);
    }

    private static string ExpectedApprovalPhrase(string runId, string packageHash) =>
        $"I approve continuation for run {runId} package {packageHash}";

    private static string RenderApprovalPhrase(string? phrase, string runId, string packageHash)
    {
        var template = string.IsNullOrWhiteSpace(phrase)
            ? "I approve continuation for run <runId> package <hash>"
            : phrase;

        return template.Replace("<runId>", runId, StringComparison.Ordinal)
            .Replace("<hash>", packageHash, StringComparison.Ordinal);
    }

    private static IDisposable DisableMsBuildNodeReuse()
    {
        var previous = Environment.GetEnvironmentVariable("MSBUILDDISABLENODEREUSE");
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
        return new EnvironmentVariableScope("MSBUILDDISABLENODEREUSE", previous);
    }

    private sealed class EnvironmentVariableScope(string name, string? previousValue) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previousValue);
    }

    private static async Task<RunRecord> RequireRunStateAsync(
        InMemoryRunStore runs,
        InMemoryRunEventStore events,
        string runId,
        RunLifecycleState expected,
        string message)
    {
        var run = await runs.GetAsync(runId);
        if (run is not null && run.State == expected)
            return run;

        var eventTrail = string.Join(
            Environment.NewLine,
            (await events.GetEventsAsync(runId))
                .Select(e =>
                    $"{e.EventType}: {e.Message} | {string.Join(", ", e.Payload.Select(pair => $"{pair.Key}={pair.Value}"))}"));

        Assert.Fail($"{message}{Environment.NewLine}Expected: {expected}{Environment.NewLine}Actual: {run?.State.ToString() ?? "<missing>"}{Environment.NewLine}{eventTrail}");
        throw new UnreachableException();
    }

    private static string SampleRoot() => Path.Combine(RepoRoot(), "Samples", "BookSeller");

    private static void CopySample(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            if (IsIgnored(dir))
                continue;
            Directory.CreateDirectory(dir.Replace(source, destination));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (IsIgnored(file))
                continue;
            var target = file.Replace(source, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsIgnored(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(p => string.Equals(p, "bin", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(p, "obj", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(p, ".git", StringComparison.OrdinalIgnoreCase));

    private static void PrepareRestoredBookSellerSource(string path)
    {
        File.WriteAllText(Path.Combine(path, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup>
                <MSBuildProjectExtensionsPath>.assets/$(MSBuildProjectName)/</MSBuildProjectExtensionsPath>
              </PropertyGroup>
            </Project>
            """);
        RunTool(path, "dotnet", "restore BookSeller.slnx --nologo");
    }

    private static void GitInit(string path)
    {
        RunGit(path, "init");
        RunGit(path, "config user.email alpha-smoke@irondev.local");
        RunGit(path, "config user.name AlphaSmoke");
        RunGit(path, "add .");
        RunGit(path, "commit -m baseline");
    }

    private static void RunGit(string workingDirectory, string arguments)
    {
        RunTool(workingDirectory, "git", arguments);
    }

    private static void RunTool(string workingDirectory, string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        Assert.AreEqual(0, process.ExitCode, $"{fileName} {arguments} failed: {stderr.Result}{stdout.Result}");
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static string TempDir(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup for local smoke temp folders.
        }
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
