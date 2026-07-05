using System.Diagnostics;
using System.Text.Json;
using IronDev.Core;
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
using IronDev.Infrastructure.Services.RunReports;
using IronDev.Infrastructure.Services.Runs;
using IronDev.Infrastructure.Services.Workspaces;
using IronDev.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// D-2 — the alpha smoke: one ticket driven through the REAL governed loop, with
/// only the model faked. This is not theatre:
///  - the orchestrator is the real TicketSkeletonRunService;
///  - the workspace is the real DisposableWorkspaceExecutionService — it copies
///    the real Samples/BookSeller, applies the proposed change, and actually runs
///    `dotnet build` and `dotnet test` against it;
///  - the gate is real: continuation consumes a live, hash-matched approval only
///    after an independent critic review is on record.
///
/// The single deterministic substitution is the Builder's output — a fixed,
/// correct implementation of the validate-book ticket stands in for a live model,
/// so the plumbing is proven before any tokens are spent (D-3 swaps in a real
/// model). The run is driven to Completed; the copy-only apply spine is D-2b.
///
/// It shells out to dotnet build/test, so it is LongRunning (registered in
/// Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md) and runs on the SQL integration
/// lane. Scripts/smoke/alpha-smoke.ps1 stands it up clean and surfaces its receipt.
/// </summary>
[TestClass]
[TestCategory("LongRunning")]
public sealed class AlphaLoopSmokeTests
{
    private const int ProjectId = 1;
    private const long TicketId = 101;

    // The deterministic Builder output: a correct validate-book implementation that
    // preserves the public surface (so the sample's existing tests keep passing).
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
    public async Task AlphaSmoke_OneTicket_ThroughTheRealLoop_WithADeterministicBuilder()
    {
        var workspaceParent = TempDir("irondev-alpha-ws");
        var evidenceRoot = TempDir("irondev-alpha-ev");
        var sampleCopy = TempDir("irondev-alpha-src");
        try
        {
            CopySample(SampleRoot(), sampleCopy);
            GitInit(sampleCopy);

            var runs = new InMemoryRunStore();
            var events = new InMemoryRunEventStore();
            var approvals = new InMemoryApprovals();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DisposableBuild:EvidenceRoot"] = evidenceRoot,
                ["DisposableBuild:WorkspaceRoot"] = workspaceParent,
                ["BuildTimeoutSeconds"] = "300",
                ["TestTimeoutSeconds"] = "300"
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
                configuration);

            // 1) Start: real proposal → real build+test of the sample → paused at the gate.
            var run = await service.StartAsync(ProjectId, TicketId);
            Assert.IsNotNull(run, "The run must start.");
            Assert.AreEqual(RunLifecycleState.PausedForApproval.ToString(), run!.Status,
                "A clean build/test halts the loop at the human gate — not before, not after.");

            var runEvents = await events.GetEventsAsync(run.RunId);
            var packaged = runEvents.Single(e => e.EventType == "SkeletonEvidencePackaged");
            Assert.AreEqual("true", packaged.Payload["succeeded"],
                "The real dotnet build + dotnet test of the sample must pass — a red workspace is not a demo.");

            var packageHash = runEvents.Single(e => e.EventType == "CriticReviewPackageReady").Payload["packageSha256"];
            Assert.IsFalse(string.IsNullOrWhiteSpace(packageHash), "The critic package must be hash-sealed.");

            // 2) The independent critic review is on record (clean), and a live human
            //    approval bound to exactly this package hash is seeded.
            await events.PublishAsync(new RunEventDto
            {
                RunId = run.RunId,
                EventType = "SkeletonCriticReviewRecorded",
                Message = "Independent critic review recorded (clean).",
                Payload = new Dictionary<string, string>
                {
                    ["reviewId"] = "alpha-critic-review",
                    ["verdict"] = "Approve",
                    ["findingCount"] = "0",
                    ["findingIds"] = ""
                }
            });
            approvals.Seed(ApprovalFor(run.RunId, packageHash));

            // 3) Continue: the gate consumes the live, hash-matched approval.
            var continued = await service.ContinueAsync(ProjectId, TicketId, run.RunId);
            Assert.AreEqual(RunLifecycleState.Completed.ToString(), continued!.Status,
                "With a clean review and a matching live approval, the gate lets the run complete.");

            // 4) Receipt — hash-bearing proof of what actually ran.
            var report = await service.GetRunReportAsync(ProjectId, TicketId, run.RunId);
            WriteReceipt(new AlphaSmokeReceipt(
                Ticket: "validate-book",
                Project: "BookSeller",
                RunId: run.RunId,
                FinalState: continued.Status,
                BuildAndTestSucceeded: true,
                CriticPackageSha256: packageHash,
                BuilderModel: "deterministic-fake",
                Proves: new[]
                {
                    "real orchestrator wiring end to end",
                    "real dotnet build + dotnet test of the real sample against the proposed change",
                    "hash-sealed critic package",
                    "gate consumes a live, hash-matched approval only after an independent critic review"
                },
                DoesNotProveYet: new[]
                {
                    "a live model (D-3)",
                    "SQL/API persistence (in-memory stores here)",
                    "the copy-only apply spine to Applied (D-2b)"
                }));

            Assert.IsNotNull(report, "The run report (the receipt's source) must be reconstructable.");
        }
        finally
        {
            TryDelete(sampleCopy);
            TryDelete(workspaceParent);
            TryDelete(evidenceRoot);
        }
    }

    // ── receipt ────────────────────────────────────────────────────────────

    private sealed record AlphaSmokeReceipt(
        string Ticket, string Project, string RunId, string FinalState, bool BuildAndTestSucceeded,
        string CriticPackageSha256, string BuilderModel, string[] Proves, string[] DoesNotProveYet);

    private static void WriteReceipt(AlphaSmokeReceipt receipt)
    {
        var path = Environment.GetEnvironmentVariable("ALPHA_SMOKE_RECEIPT");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(RepoRoot(), "artifacts", "alpha-smoke", "receipt.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"ALPHA_SMOKE_RECEIPT_PATH::{path}");
    }

    // ── deterministic agents ─────────────────────────────────────────────────

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
        public void Seed(AcceptedApprovalRecord record) => _records.Add(record);

        public Task SaveAsync(AcceptedApprovalRecord record, CancellationToken ct = default)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task<AcceptedApprovalRecord?> GetAsync(Guid projectId, Guid acceptedApprovalId, CancellationToken ct = default) =>
            Task.FromResult(_records.FirstOrDefault(r => r.ProjectId == projectId && r.AcceptedApprovalId == acceptedApprovalId));

        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByTargetAsync(Guid projectId, string targetKind, string targetId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>(_records
                .Where(r => r.ProjectId == projectId && r.ApprovalTargetKind == targetKind && r.ApprovalTargetId == targetId).ToList());

        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByCorrelationAsync(string correlationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>(_records.Where(r => r.CorrelationId == correlationId).ToList());

        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByProjectAndCorrelationAsync(Guid projectId, string correlationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>(_records
                .Where(r => r.ProjectId == projectId && r.CorrelationId == correlationId).ToList());
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
            ApprovedByActorId = "alpha-smoke-human-gate",
            AcceptedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = null,
            CorrelationId = $"corr-{runId}",
            CausationId = $"cause-{runId}",
            EvidenceReferences = [$"critic-pkg-{runId}"],
            BoundaryMaxims = ["Approval evidence is not apply permission."]
        };

    // ── file/git helpers ─────────────────────────────────────────────────────

    private static string SampleRoot() => Path.Combine(RepoRoot(), "Samples", "BookSeller");

    private static void CopySample(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            if (IsIgnored(dir)) continue;
            Directory.CreateDirectory(dir.Replace(source, destination));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (IsIgnored(file)) continue;
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
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        process.WaitForExit();
    }

    private static string TempDir(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best effort */ }
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
