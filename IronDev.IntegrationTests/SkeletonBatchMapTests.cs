using IronDev.Core.Builder;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// P2-1 — the batch dependency map as durable evidence. Protected boundaries:
/// - a map is hash-sealed at write and re-verified on read — a rewritten map is
///   reported unverified, never silently served;
/// - a ticket outside the project fails the WHOLE request with the ticket named —
///   a map never quietly describes half a batch;
/// - detection grants nothing: no runs start, no gates move.
/// </summary>
[TestClass]
[TestCategory("SkeletonRun")]
public sealed class SkeletonBatchMapTests
{
    private const int ProjectId = 7;

    [TestMethod]
    public async Task Detect_PersistsAHashSealedMap_ThatReadsBackVerified()
    {
        using var harness = MapHarness.Create(
            Ticket(42, files: "src/Catalog.cs"),
            Ticket(43, blockedBy: "42", files: "src/Paging.cs"));

        var outcome = await harness.Service.DetectAsync(ProjectId, [42, 43], "user-9");

        Assert.IsTrue(outcome!.Succeeded, outcome.FailureReason);
        Assert.AreEqual(1, outcome.Map!.Edges.Count);
        StringAssert.Contains(outcome.Map.Boundary, "schedules nothing");

        var record = await harness.Service.GetAsync(ProjectId, outcome.MapId);
        Assert.IsNotNull(record);
        Assert.IsTrue(record!.Verified, "The map reads back byte-identical to what was sealed.");
        Assert.AreEqual("user-9", record.RequestedByUserId);
        Assert.AreEqual(SkeletonBatchDependencyEdgeKinds.ExplicitBlock, record.Map.Edges.Single().Kind);
    }

    [TestMethod]
    public async Task Detect_ARewrittenMap_ReadsBackUnverified()
    {
        using var harness = MapHarness.Create(
            Ticket(42, files: "src/Catalog.cs"),
            Ticket(43, files: "src/Catalog.cs"));
        var outcome = await harness.Service.DetectAsync(ProjectId, [42, 43], "user-9");

        // History is inconvenient: someone deletes the conflict edge from the record.
        var mapPath = Path.Combine(harness.EvidenceRoot, "batch-maps", ProjectId.ToString(), $"{outcome!.MapId}.json");
        await File.WriteAllTextAsync(mapPath, (await File.ReadAllTextAsync(mapPath)).Replace("footprint-overlap", "no-overlap-here"));

        var record = await harness.Service.GetAsync(ProjectId, outcome.MapId);

        Assert.IsFalse(record!.Verified, "A rewritten map is a broken seal, and the read says so.");
    }

    [TestMethod]
    public async Task Detect_ATicketOutsideTheProject_FailsTheWholeRequest_NamingTheTicket()
    {
        using var harness = MapHarness.Create(
            Ticket(42, files: "src/A.cs"),
            Ticket(43, files: "src/B.cs", projectId: 8));

        var outcome = await harness.Service.DetectAsync(ProjectId, [42, 43], "user-9");

        Assert.IsFalse(outcome!.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "Ticket 43");
        StringAssert.Contains(outcome.FailureReason, "half a batch");
    }

    [TestMethod]
    public async Task Detect_FewerThanTwoTickets_IsRefused()
    {
        using var harness = MapHarness.Create(Ticket(42, files: "src/A.cs"));

        var outcome = await harness.Service.DetectAsync(ProjectId, [42, 42], "user-9");

        Assert.IsFalse(outcome!.Succeeded);
        StringAssert.Contains(outcome.FailureReason, "at least two tickets");
    }

    [TestMethod]
    public async Task Detect_UnknownProject_ReturnsNull()
    {
        using var harness = MapHarness.Create(Ticket(42), Ticket(43));

        Assert.IsNull(await harness.Service.DetectAsync(999, [42, 43], "user-9"));
    }

    [TestMethod]
    public void BatchMapService_GrantsNothing()
    {
        var source = File.ReadAllText(RepositoryFile("IronDev.Infrastructure", "Services", "SkeletonBatchMapService.cs"));

        foreach (var forbidden in new[]
        {
            "StartAsync",
            "ContinueAsync",
            "ApplyAsync",
            "AcceptedApproval",
            "SatisfyPolicy",
            "TransitionAsync",
            "ControlledSourceApply"
        })
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Detection maps the batch; it must never run it or touch a gate: {forbidden}");
        }

        StringAssert.Contains(source, "grants nothing");
        StringAssert.Contains(source, "advisory");
    }

    private static ProjectTicket Ticket(long id, string? blockedBy = null, string? files = null, int projectId = ProjectId) =>
        new()
        {
            Id = id,
            ProjectId = projectId,
            TenantId = 1,
            Title = $"Ticket {id}",
            BlockedByTicketIds = blockedBy,
            LinkedFilePaths = files
        };

    private sealed class MapHarness : IDisposable
    {
        public required SkeletonBatchMapService Service { get; init; }
        public required string EvidenceRoot { get; init; }

        public static MapHarness Create(params ProjectTicket[] tickets)
        {
            var evidenceRoot = Path.Combine(Path.GetTempPath(), $"irondev-batch-map-{Guid.NewGuid():N}");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DisposableBuild:EvidenceRoot"] = evidenceRoot
                })
                .Build();

            return new MapHarness
            {
                Service = new SkeletonBatchMapService(
                    new StubTicketService(tickets),
                    new StubProjectService(new Project { Id = ProjectId, TenantId = 1, Name = "BookSeller" }),
                    configuration),
                EvidenceRoot = evidenceRoot
            };
        }

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

    private sealed class StubTicketService(IReadOnlyList<ProjectTicket> tickets) : ITicketService
    {
        public Task<long> SaveTicketAsync(ProjectTicket toSave, CancellationToken ct = default) => Task.FromResult(toSave.Id);
        public Task<IReadOnlyList<ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken ct = default) =>
            Task.FromResult(tickets);
        public Task<ProjectTicket?> GetTicketByIdAsync(long ticketId, CancellationToken ct = default) =>
            Task.FromResult(tickets.FirstOrDefault(ticket => ticket.Id == ticketId));
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

    private static string RepositoryFile(params string[] parts)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "IronDev.slnx")))
            root = Path.GetDirectoryName(root);
        Assert.IsNotNull(root, "Repository root not found.");
        return Path.Combine(root!, Path.Combine(parts));
    }
}
