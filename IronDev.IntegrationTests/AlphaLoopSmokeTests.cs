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
/// D-2a deterministic alpha smoke: one BookSeller ticket driven through the real
/// governed loop until the human gate, with only model words faked.
///
/// The smoke proves the real orchestrator, disposable workspace, build/test
/// execution, critic-package creation, report reconstruction, and approval halt.
/// It deliberately does not create approval, request continuation, apply source,
/// release, deploy, or claim alpha readiness.
/// </summary>
[TestClass]
[TestCategory("LongRunning")]
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
            CopySample(SampleRoot(), sampleCopy);
            GitInit(sampleCopy);

            var runs = new InMemoryRunStore();
            var events = new InMemoryRunEventStore();
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
                new InMemoryApprovals(),
                new ApprovalSatisfactionEvaluator(),
                new WorkflowApprovalHaltEvaluator(),
                new EmptyTestAuthoring(),
                new SkeletonMutationLeaseService(configuration),
                configuration);

            var run = await service.StartAsync(ProjectId, TicketId);
            Assert.IsNotNull(run, "The run must start.");
            Assert.AreEqual(RunLifecycleState.PausedForApproval.ToString(), run!.Status,
                "D-2a must stop at the human gate.");
            Assert.IsTrue(run.RequiresHumanApproval, "The returned DTO must name the human gate.");

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
                GateState: run.Status,
                BuildAndTestSucceeded: true,
                CriticPackageSha256: packageHash,
                ApprovalTargetHash: halt.Payload["approvalTargetHash"],
                BuilderModel: "deterministic-fake",
                AcceptedApprovalCreated: false,
                ContinuationRequested: false,
                ApplyRequested: false,
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
        bool ContinuationRequested,
        bool ApplyRequested,
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
        public Task SaveAsync(AcceptedApprovalRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task<AcceptedApprovalRecord?> GetAsync(Guid projectId, Guid acceptedApprovalId, CancellationToken ct = default) =>
            Task.FromResult<AcceptedApprovalRecord?>(null);
        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByTargetAsync(Guid projectId, string targetKind, string targetId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>([]);
        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByCorrelationAsync(string correlationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>([]);
        public Task<IReadOnlyList<AcceptedApprovalRecord>> ListByProjectAndCorrelationAsync(Guid projectId, string correlationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AcceptedApprovalRecord>>([]);
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
