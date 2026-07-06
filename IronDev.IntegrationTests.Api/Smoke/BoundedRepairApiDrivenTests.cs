using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.Workflow;
using IronDev.Data.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api.Smoke;

/// <summary>
/// REPAIR-1 — bounded repair, proven through the product API against real SQL
/// with real disposable build/test runs. A deterministic two-stage builder makes
/// the failure and the repair reproducible; the builds, workspaces, evidence,
/// events, and gate are all real.
///
/// Boundary: a repair attempt is proposal-shaped work, never authority. These
/// proofs assert the budget is enforced, failure without budget is a TERMINAL
/// NAMED state (never a silently stuck run), attempt history is preserved and
/// reported, and the human gate after a successful repair is exactly the same
/// gate — a repaired run earns nothing.
/// </summary>
[TestClass]
[TestCategory("DemoSeed")]
[TestCategory("AlphaSmoke")]
[TestCategory("ReleaseReadiness")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
public sealed class BoundedRepairApiDrivenTests : ApiTestBase
{
    private const string BrokenBook =
        """
        namespace BookSeller.Domain;

        public sealed class Book
        {
            this line does not compile — the deterministic first attempt is broken on purpose
        }
        """;

    private const string RepairedBook =
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
    public async Task Repair_FirstAttemptFails_RepairReachesGate_HistoryPreserved()
    {
        var builder = new TwoStageBuilder(repairSucceeds: true);
        await using var fixture = await RepairFixture.CreateAsync(builder, maxRepairAttempts: 1);

        var started = await fixture.StartRunAsync();
        Assert.AreEqual("PausedForApproval", started.Status,
            "A successful repair attempt reaches the SAME human gate — repair earns nothing more.");

        // The orchestrator handed the repair real context, not a blank slate.
        Assert.AreEqual(1, builder.RepairCalls);
        Assert.IsNotNull(builder.LastRepairContext);
        Assert.AreEqual(2, builder.LastRepairContext!.AttemptNumber);
        Assert.AreEqual(SkeletonBuildFailureKind.BuildFailed, builder.LastRepairContext.Classification.Kind);
        StringAssert.Contains(builder.LastRepairContext.Classification.Excerpt, "error",
            "The repair context carries the real compiler evidence.");
        Assert.AreEqual(1, builder.LastRepairContext.PreviousProposal.Changes.Count,
            "The repair sees the proposal it is repairing.");

        // Attempt history is preserved and reported: both attempts' evidence, in order.
        var report = await fixture.GetReportAsync(started.RunId);
        var repairTrace = report.RepairAttempts.Single();
        Assert.AreEqual(2, repairTrace.AttemptNumber);
        Assert.AreEqual("BuildFailed", repairTrace.FailureKind);
        Assert.IsTrue(repairTrace.RepairProposalEvidenceExistsOnDisk, "The repair proposal evidence file is on disk.");
        Assert.IsTrue(report.Proposal!.EvidenceExistsOnDisk, "The ORIGINAL attempt's proposal evidence is untouched.");

        var packagedEvents = await fixture.QueryEventsAsync(started.RunId, "SkeletonEvidencePackaged");
        Assert.AreEqual(2, packagedEvents, "Both attempts packaged evidence; nothing was erased.");

        // A repaired run grants nothing: continuation refuses exactly as always.
        var refused = await fixture.ContinueAsync(started.RunId);
        Assert.AreEqual("PausedForApproval", refused.Status,
            "Continuation still requires critic review and a live accepted approval — repair changed nothing at the gate.");
    }

    [TestMethod]
    public async Task Repair_CriticPackageReferencesRepairedProposalEvidence()
    {
        var builder = new TwoStageBuilder(repairSucceeds: true);
        await using var fixture = await RepairFixture.CreateAsync(builder, maxRepairAttempts: 1);

        var started = await fixture.StartRunAsync();
        Assert.AreEqual("PausedForApproval", started.Status);

        var report = await fixture.GetReportAsync(started.RunId);
        var packageJson = await File.ReadAllTextAsync(report.CriticPackage!.PackagePath);
        using var package = JsonDocument.Parse(packageJson);

        // The package binds to the repaired proposal that actually built green.
        StringAssert.Contains(packageJson, "proposal-repair-2.json",
            "The critic package's evidence refs must reference the repaired proposal evidence.");
        StringAssert.Contains(packageJson, $"prop-{started.RunId}-repair-2",
            "The critic package's proposal id must be the repaired proposal's id.");
        Assert.IsFalse(packageJson.Contains($"evidence\\\\proposal.json") || packageJson.Contains("evidence/proposal.json"),
            "The package must not reference the original failed proposal.json as its proposal evidence.");
    }

    [TestMethod]
    public async Task Repair_ReportFinalProposalIsRepairedProposal()
    {
        var builder = new TwoStageBuilder(repairSucceeds: true);
        await using var fixture = await RepairFixture.CreateAsync(builder, maxRepairAttempts: 1);

        var started = await fixture.StartRunAsync();
        Assert.AreEqual("PausedForApproval", started.Status);

        var report = await fixture.GetReportAsync(started.RunId);
        Assert.IsNotNull(report.Proposal);
        StringAssert.EndsWith(report.Proposal!.ProposalId, "-repair-2",
            "The report's primary Proposal is the FINAL repaired proposal the gate binds to.");
        StringAssert.EndsWith(report.Proposal.EvidenceRef, "proposal-repair-2.json");
        Assert.IsTrue(report.Proposal.EvidenceExistsOnDisk);
    }

    [TestMethod]
    public async Task Repair_OriginalProposalStillExistsButIsNotTheGateProposal()
    {
        var builder = new TwoStageBuilder(repairSucceeds: true);
        await using var fixture = await RepairFixture.CreateAsync(builder, maxRepairAttempts: 1);

        var started = await fixture.StartRunAsync();
        var report = await fixture.GetReportAsync(started.RunId);

        Assert.IsNotNull(report.InitialProposal, "The original failed proposal is preserved history.");
        Assert.AreEqual($"prop-{started.RunId}", report.InitialProposal!.ProposalId);
        StringAssert.EndsWith(report.InitialProposal.EvidenceRef, "proposal.json");
        Assert.IsTrue(report.InitialProposal.EvidenceExistsOnDisk, "History is never erased.");
        Assert.AreNotEqual(report.InitialProposal.ProposalId, report.Proposal!.ProposalId,
            "The original exists — and it is not the gate proposal.");
    }

    [TestMethod]
    public async Task Repair_ApprovalHashBindsPackageContainingRepairedProposal()
    {
        var builder = new TwoStageBuilder(repairSucceeds: true);
        await using var fixture = await RepairFixture.CreateAsync(builder, maxRepairAttempts: 1);

        var started = await fixture.StartRunAsync();
        var report = await fixture.GetReportAsync(started.RunId);

        // The approval requirement binds to the package hash, recomputed from disk —
        // and that package references the repaired proposal. Approving this run is
        // approving the repaired work, provably.
        Assert.IsTrue(report.CriticPackage!.HashVerified);
        Assert.AreEqual(report.CriticPackage.Sha256OnDisk, report.Approval!.TargetHash,
            "The approval target hash is the hash of the package that contains the repaired proposal.");
        var packageJson = await File.ReadAllTextAsync(report.CriticPackage.PackagePath);
        StringAssert.Contains(packageJson, "proposal-repair-2",
            "The hash-bound package demonstrably carries the repaired proposal reference.");
    }

    [TestMethod]
    public async Task Repair_BudgetExhausted_RunFailsWithNamedReason()
    {
        var builder = new TwoStageBuilder(repairSucceeds: false);
        await using var fixture = await RepairFixture.CreateAsync(builder, maxRepairAttempts: 1);

        var started = await fixture.StartRunAsync();
        Assert.AreEqual("Failed", started.Status, "An exhausted repair budget is a terminal named state.");
        Assert.AreEqual(1, builder.RepairCalls, "The budget bounds attempts: exactly one repair, never a retry loop.");

        var runState = await fixture.QueryRunStateAsync(started.RunId);
        Assert.AreEqual("Failed", runState);

        var blocked = await fixture.QueryEventPayloadAsync(started.RunId, "SkeletonRunBlocked", "blockedReason");
        Assert.AreEqual("RepairBudgetExhausted", blocked);

        var packagedEvents = await fixture.QueryEventsAsync(started.RunId, "SkeletonEvidencePackaged");
        Assert.AreEqual(2, packagedEvents, "Both failed attempts' evidence is preserved.");
    }

    [TestMethod]
    public async Task Repair_DisabledByDefault_FailureIsTerminalAndNamed()
    {
        var builder = new TwoStageBuilder(repairSucceeds: true);
        await using var fixture = await RepairFixture.CreateAsync(builder, maxRepairAttempts: null);

        var started = await fixture.StartRunAsync();
        Assert.AreEqual("Failed", started.Status,
            "Without an explicit repair budget a failed build is terminal — and never a silently stuck Running run.");
        Assert.AreEqual(0, builder.RepairCalls, "Repair is off by default: no attempt fires without explicit configuration.");

        var blocked = await fixture.QueryEventPayloadAsync(started.RunId, "SkeletonRunBlocked", "blockedReason");
        Assert.AreEqual("BuildFailed", blocked);
    }

    // ── Deterministic two-stage builder: broken first attempt, optional repair ──

    private sealed class TwoStageBuilder(bool repairSucceeds) : IBuilderProposalService
    {
        public int RepairCalls;
        public SkeletonRepairContext? LastRepairContext;

        public Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken ct = default) =>
            Task.FromResult(Proposal(ticketId, BrokenBook, "Deterministically broken first attempt."));

        public Task<BuilderProposal> GenerateRepairProposalAsync(long ticketId, SkeletonRepairContext repair, CancellationToken ct = default)
        {
            RepairCalls++;
            LastRepairContext = repair;
            return Task.FromResult(Proposal(
                ticketId,
                repairSucceeds ? RepairedBook : BrokenBook,
                repairSucceeds ? "Deterministic repair: valid Book implementation." : "Deterministically broken repair."));
        }

        public Task<BuilderProposal> GenerateProposalFromRequestAsync(int projectId, string request, CancellationToken ct = default) =>
            GenerateProposalAsync(0, ct);

        public Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default) =>
            throw new NotSupportedException("REPAIR-1 proofs apply nothing; the governed spine owns apply.");

        private static BuilderProposal Proposal(long ticketId, string content, string summary) => new()
        {
            TicketId = ticketId,
            ProjectId = 0,
            Summary = summary,
            Rationale = "REPAIR-1 deterministic fixture. A proposal is review material, not authority.",
            ModelProvider = "deterministic-fake",
            ModelName = "repair1-two-stage-fixed",
            Changes =
            [
                new ProposedFileChange
                {
                    FilePath = "src/BookSeller.Domain/Book.cs",
                    Description = summary,
                    IsValid = true,
                    FullContentAfter = content
                }
            ]
        };
    }

    // ── Fixture: BookSeller copy + API host with the two-stage builder ─────────

    private sealed class RepairFixture : IAsyncDisposable
    {
        private readonly string _sampleCopy;
        private readonly string _workspaceParent;
        private readonly string _evidenceRoot;
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;
        private readonly IDisposable _noNodeReuse;

        public int ProjectId { get; private set; }
        public long TicketId { get; private set; }

        private RepairFixture(
            string sampleCopy,
            string workspaceParent,
            string evidenceRoot,
            Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
            HttpClient client,
            IDisposable noNodeReuse)
        {
            _sampleCopy = sampleCopy;
            _workspaceParent = workspaceParent;
            _evidenceRoot = evidenceRoot;
            _factory = factory;
            _client = client;
            _noNodeReuse = noNodeReuse;
        }

        public static async Task<RepairFixture> CreateAsync(TwoStageBuilder builder, int? maxRepairAttempts)
        {
            var workspaceParent = TempDir("irondev-repair1-ws");
            var evidenceRoot = TempDir("irondev-repair1-ev");
            var sampleCopy = TempDir("irondev-repair1-src");
            var noNodeReuse = DisableMsBuildNodeReuse();

            CopySample(Path.Combine(RepositoryRoot(), "Samples", "BookSeller"), sampleCopy);
            PrepareRestoredBookSellerSource(sampleCopy);
            GitInit(sampleCopy);

            var factory = Factory.WithWebHostBuilder(hostBuilder =>
            {
                hostBuilder.UseEnvironment("Test");
                hostBuilder.UseSetting("DisposableBuild:EvidenceRoot", evidenceRoot);
                hostBuilder.UseSetting("DisposableBuild:WorkspaceRoot", workspaceParent);
                hostBuilder.UseSetting("DisposableBuild:BuildTimeoutSeconds", "300");
                hostBuilder.UseSetting("DisposableBuild:TestTimeoutSeconds", "300");
                if (maxRepairAttempts.HasValue)
                    hostBuilder.UseSetting("SkeletonRepair:MaxAttempts", maxRepairAttempts.Value.ToString());
                hostBuilder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IBuilderProposalService>();
                    services.AddScoped<IBuilderProposalService>(_ => builder);
                    services.RemoveAll<ISkeletonTestAuthoringService>();
                    services.AddScoped<ISkeletonTestAuthoringService, EmptyTestAuthoring>();
                });
            });

            var client = factory.CreateClient();
            await AuthenticateAsync(client);

            var fixture = new RepairFixture(sampleCopy, workspaceParent, evidenceRoot, factory, client, noNodeReuse);
            await fixture.CreateProjectAndTicketAsync();
            return fixture;
        }

        public async Task<TicketBuildRunDto> StartRunAsync() =>
            await PostJsonAsync<TicketBuildRunDto>(_client, $"/api/projects/{ProjectId}/tickets/{TicketId}/skeleton-runs", content: null);

        public async Task<TicketBuildRunDto> ContinueAsync(string runId) =>
            await PostJsonAsync<TicketBuildRunDto>(_client, $"/api/projects/{ProjectId}/tickets/{TicketId}/skeleton-runs/{runId}/continue", content: null);

        public async Task<SkeletonRunReport> GetReportAsync(string runId) =>
            await GetJsonAsync<SkeletonRunReport>(_client, $"/api/projects/{ProjectId}/tickets/{TicketId}/skeleton-runs/{runId}/report");

        public async Task<string?> QueryRunStateAsync(string runId)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            return await connection.ExecuteScalarAsync<string>(
                "SELECT State FROM dbo.Runs WHERE RunId = @RunId", new { RunId = runId });
        }

        public async Task<int> QueryEventsAsync(string runId, string eventType)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.RunEvents WHERE RunId = @RunId AND EventType = @EventType",
                new { RunId = runId, EventType = eventType });
        }

        public async Task<string?> QueryEventPayloadAsync(string runId, string eventType, string payloadKey)
        {
            var report = await GetReportAsync(runId);
            _ = report; // report reconstruction stays exercised even for payload queries
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            var payloadJson = await connection.ExecuteScalarAsync<string>(
                "SELECT TOP 1 PayloadJson FROM dbo.RunEvents WHERE RunId = @RunId AND EventType = @EventType ORDER BY TimestampUtc DESC, Id DESC",
                new { RunId = runId, EventType = eventType });
            if (string.IsNullOrWhiteSpace(payloadJson))
                return null;
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.TryGetProperty(payloadKey, out var value) ? value.GetString() : null;
        }

        private async Task CreateProjectAndTicketAsync()
        {
            var project = await PostJsonAsync<Project>(_client, "/api/projects", new Project
            {
                Name = "BookSeller REPAIR-1",
                Description = "Bounded repair proof fixture.",
                LocalPath = _sampleCopy
            });
            ProjectId = project.Id;

            var ticket = await PostJsonAsync<ProjectTicket>(_client, $"/api/projects/{ProjectId}/tickets", new CreateProjectTicketRequest
            {
                Title = "Reject invalid books at the door",
                Type = "Task",
                Priority = "Medium",
                Summary = "REPAIR-1 fixture ticket: the deterministic first attempt is broken on purpose.",
                Problem = "Prove bounded repair through the product path.",
                ProposedChange = "Two-stage deterministic builder exercises the repair loop.",
                AcceptanceCriteria = ["A governed run either repairs within budget and reaches the gate, or fails with a named terminal state."],
                Provenance = new TicketProvenanceDto
                {
                    Source = "repair1:fixture",
                    Notes = "REPAIR-1 proof ticket. It grants no authority."
                }
            });
            TicketId = ticket.Id;
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _factory.DisposeAsync();
            _noNodeReuse.Dispose();
            TryDelete(_sampleCopy);
            TryDelete(_workspaceParent);
            TryDelete(_evidenceRoot);
        }
    }

    private sealed class EmptyTestAuthoring : ISkeletonTestAuthoringService
    {
        public Task<SkeletonTestAuthoringResult> AuthorTestsAsync(SkeletonTestAuthoringRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SkeletonTestAuthoringResult
            {
                Succeeded = true,
                Tests = [],
                ModelProvider = "deterministic-fake",
                ModelName = "repair1-no-authored-tests"
            });
    }

    // ── Shared plumbing (mirrors the other API-driven proofs) ─────────────────

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        var loginJson = await ReadSuccessAsync<JsonElement>(login);
        var baseToken = loginJson.GetProperty("token").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tenants/select");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", baseToken);
        request.Content = JsonContent.Create(new { tenantId = AssignedTenantId });

        var select = await client.SendAsync(request);
        var selectJson = await ReadSuccessAsync<JsonElement>(select);
        var tenantToken = selectJson.GetProperty("token").GetString();

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tenantToken);
    }

    private static async Task<T> PostJsonAsync<T>(HttpClient client, string path, object? content)
    {
        var response = content is null
            ? await client.PostAsync(path, content: null)
            : await client.PostAsJsonAsync(path, content);
        return await ReadSuccessAsync<T>(response);
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        return await ReadSuccessAsync<T>(response);
    }

    private static async Task<T> ReadSuccessAsync<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.StatusCode}: {text}");
        var result = JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(result, $"Response JSON could not deserialize to {typeof(T).Name}: {text}");
        return result!;
    }

    private static void CopySample(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            if (IsIgnored(dir))
                continue;
            Directory.CreateDirectory(dir.Replace(source, destination, StringComparison.Ordinal));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (IsIgnored(file))
                continue;
            var target = file.Replace(source, destination, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool IsIgnored(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(part, "obj", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase));

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
        RunTool(path, "git", "init");
        RunTool(path, "git", "config user.email repair1@irondev.local");
        RunTool(path, "git", "config user.name Repair1Proof");
        RunTool(path, "git", "add .");
        RunTool(path, "git", "commit -m baseline");
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

    private static IDisposable DisableMsBuildNodeReuse()
    {
        var previous = Environment.GetEnvironmentVariable("MSBUILDDISABLENODEREUSE");
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
        return new EnvironmentVariableScope("MSBUILDDISABLENODEREUSE", previous);
    }

    private static string TempDir(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), "IronDev", "Test", $"{prefix}-{Guid.NewGuid():N}");
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
            // Best effort cleanup for local proof temp folders.
        }
    }

    private static string RepositoryRoot()
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

    private sealed class EnvironmentVariableScope(string name, string? previousValue) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previousValue);
    }
}
