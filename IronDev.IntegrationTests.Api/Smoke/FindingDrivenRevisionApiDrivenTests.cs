using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.RunReadiness;
using IronDev.Core.Runs;
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
/// REVISE-1 — finding-driven revision, proven through the product API against
/// real SQL with real disposable build/test runs. A deterministic builder and a
/// deterministic finding-emitting critic make the flow reproducible; the builds,
/// workspaces, evidence, events, and gate are all real.
///
/// Boundary: a revision is human-directed, proposal-shaped work, never
/// authority. These proofs assert revision is off by default, bounded, refuses
/// to leave findings unanswered, replaces the gate package ONLY when the
/// revision builds green, and that the revised package needs its OWN critic
/// review — a review or approval of the superseded package satisfies nothing.
/// </summary>
[TestClass]
[TestCategory("DemoSeed")]
[TestCategory("AlphaSmoke")]
[TestCategory("ReleaseReadiness")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
public sealed class FindingDrivenRevisionApiDrivenTests : ApiTestBase
{
    private const string CitedFindingId = "finding-price-rounding";

    private const string InitialBook =
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

    private const string RevisedBook =
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
                Price = decimal.Round(price, 2, System.MidpointRounding.ToEven);
            }

            public string Isbn { get; }
            public string Title { get; }
            public string Author { get; }
            public decimal Price { get; }
        }
        """;

    private const string BrokenRevisedBook =
        """
        namespace BookSeller.Domain;

        public sealed class Book
        {
            this line does not compile — the deterministic revision attempt is broken on purpose
        }
        """;

    [TestMethod]
    public async Task Revise_CitedFinding_RunsRevisionToTheSameGate_AndTheRevisedPackageNeedsItsOwnReview()
    {
        var builder = new RevisionBuilder(revisionContent: RevisedBook);
        await using var fixture = await RevisionFixture.CreateAsync(builder, maxRevisionAttempts: 1, findingIds: [CitedFindingId]);

        var started = await fixture.StartRunAsync();
        Assert.AreEqual("PausedForApproval", started.Status);

        await fixture.RequestCriticReviewAsync(started.RunId);
        var haltedReport = await fixture.GetReportAsync(started.RunId);
        var supersededHash = haltedReport.CriticPackage!.Sha256OnDisk;
        Assert.IsTrue(haltedReport.CriticPackage.HashVerified);

        var revised = await fixture.ReviseAsync(started.RunId, [CitedFindingId], "Round prices to two decimals, banker's rounding.");
        Assert.AreEqual("PausedForApproval", revised.Status,
            "A green revision returns to the SAME human gate — a revision earns nothing more.");

        // The orchestrator handed the Builder the human's directive and the
        // durable proposal under revision, not a blank slate and not critic text.
        Assert.AreEqual(1, builder.RevisionCalls);
        Assert.IsNotNull(builder.LastRevisionContext);
        Assert.AreEqual(1, builder.LastRevisionContext!.AttemptNumber);
        CollectionAssert.Contains(builder.LastRevisionContext.FindingIds.ToList(), CitedFindingId);
        StringAssert.Contains(builder.LastRevisionContext.Instruction, "banker's rounding");
        Assert.AreEqual(1, builder.LastRevisionContext.PreviousChanges.Count,
            "The revision sees the proposal it is revising, read from the sealed package.");

        var report = await fixture.GetReportAsync(started.RunId);

        // The gate re-bound to the REVISED package: new hash, new approval target.
        Assert.IsTrue(report.CriticPackage!.HashVerified, "The revised canonical package verifies from disk.");
        Assert.AreNotEqual(supersededHash, report.CriticPackage.Sha256OnDisk, "A green revision replaced the gate package.");
        Assert.AreEqual(report.CriticPackage.Sha256OnDisk, report.Approval!.TargetHash,
            "The approval requirement re-bound to the revised package hash.");
        StringAssert.EndsWith(report.Proposal!.ProposalId, "-revise-1",
            "The report's primary Proposal is the revised proposal the gate binds to.");
        Assert.IsNotNull(report.InitialProposal, "The original proposal is preserved history.");
        Assert.IsTrue(report.InitialProposal!.EvidenceExistsOnDisk, "History is never erased.");
        Assert.IsTrue(fixture.EvidenceFileExists(started.RunId, "critic-package-superseded-1.json"),
            "The superseded package stays on disk as history.");

        // The revision trace and the AddressedByRevision disposition are durable.
        var revisionTrace = report.RevisionAttempts.Single();
        Assert.AreEqual(1, revisionTrace.AttemptNumber);
        Assert.IsFalse(revisionTrace.Failed);
        CollectionAssert.Contains(revisionTrace.FindingIds.ToList(), CitedFindingId);
        Assert.IsTrue(revisionTrace.RevisionProposalEvidenceExistsOnDisk);
        var disposition = report.FindingDispositions.Single(trace => trace.FindingId == CitedFindingId);
        Assert.AreEqual("AddressedByRevision", disposition.Disposition);

        // The old review satisfies nothing: continuation refuses until the
        // REVISED package gets its own critic review.
        var refused = await fixture.ContinueAsync(started.RunId);
        Assert.AreEqual("PausedForApproval", refused.Status);
        var refusedReason = await fixture.QueryEventPayloadAsync(started.RunId, "ContinuationRefused", "refusedReason");
        Assert.AreEqual("CriticReviewMissing", refusedReason,
            "A review of the superseded package must not satisfy the revised gate.");
    }

    [TestMethod]
    public async Task Revise_OffByDefault_RefusedNamed_AndTheGateIsUnchanged()
    {
        var builder = new RevisionBuilder(revisionContent: RevisedBook);
        await using var fixture = await RevisionFixture.CreateAsync(builder, maxRevisionAttempts: null, findingIds: [CitedFindingId]);

        var started = await fixture.StartRunAsync();
        await fixture.RequestCriticReviewAsync(started.RunId);
        var before = await fixture.GetReportAsync(started.RunId);

        var outcome = await fixture.ReviseAsync(started.RunId, [CitedFindingId], "Round prices to two decimals.");
        Assert.AreEqual("PausedForApproval", outcome.Status);
        Assert.AreEqual(0, builder.RevisionCalls, "Revision is off by default: no attempt fires without explicit configuration.");

        var refusedReason = await fixture.QueryEventPayloadAsync(started.RunId, "SkeletonRevisionRefused", "refusedReason");
        Assert.AreEqual("RevisionDisabled", refusedReason);

        var after = await fixture.GetReportAsync(started.RunId);
        Assert.AreEqual(before.CriticPackage!.Sha256OnDisk, after.CriticPackage!.Sha256OnDisk,
            "A refused revision changes nothing at the gate.");
    }

    [TestMethod]
    public async Task Revise_RefusesToLeaveUncitedFindingsUnanswered()
    {
        var builder = new RevisionBuilder(revisionContent: RevisedBook);
        await using var fixture = await RevisionFixture.CreateAsync(
            builder, maxRevisionAttempts: 1, findingIds: [CitedFindingId, "finding-missing-tests"]);

        var started = await fixture.StartRunAsync();
        await fixture.RequestCriticReviewAsync(started.RunId);

        var outcome = await fixture.ReviseAsync(started.RunId, [CitedFindingId], "Round prices to two decimals.");
        Assert.AreEqual("PausedForApproval", outcome.Status);
        Assert.AreEqual(0, builder.RevisionCalls, "No revision fires while a finding would be left unanswered.");

        var refusedReason = await fixture.QueryEventPayloadAsync(started.RunId, "SkeletonRevisionRefused", "refusedReason");
        Assert.AreEqual("UndispositionedFindingsNotCited", refusedReason,
            "A revision may not leave any finding unanswered behind it.");
    }

    [TestMethod]
    public async Task Revise_FailedRevisionBuild_LeavesThePreviousGateCanonical_AndSpendsTheBudget()
    {
        var builder = new RevisionBuilder(revisionContent: BrokenRevisedBook);
        await using var fixture = await RevisionFixture.CreateAsync(builder, maxRevisionAttempts: 1, findingIds: [CitedFindingId]);

        var started = await fixture.StartRunAsync();
        await fixture.RequestCriticReviewAsync(started.RunId);
        var before = await fixture.GetReportAsync(started.RunId);

        var outcome = await fixture.ReviseAsync(started.RunId, [CitedFindingId], "Round prices to two decimals.");
        Assert.AreEqual("PausedForApproval", outcome.Status,
            "A failed revision returns to the gate — it is never a silently stuck or dead run.");
        Assert.AreEqual(1, builder.RevisionCalls);

        var report = await fixture.GetReportAsync(started.RunId);
        Assert.AreEqual(before.CriticPackage!.Sha256OnDisk, report.CriticPackage!.Sha256OnDisk,
            "Only a GREEN revision replaces the canonical gate package.");
        Assert.IsTrue(report.CriticPackage.HashVerified, "The previous gate package still verifies.");
        var revisionTrace = report.RevisionAttempts.Single();
        Assert.IsTrue(revisionTrace.Failed);
        Assert.AreEqual("BuildFailed", revisionTrace.FailureKind);
        Assert.AreEqual(0, report.FindingDispositions.Count,
            "A failed revision answers nothing: the cited finding keeps blocking the gate.");

        // The budget is spent honestly: the failed attempt counted.
        var second = await fixture.ReviseAsync(started.RunId, [CitedFindingId], "Try again.");
        Assert.AreEqual("PausedForApproval", second.Status);
        Assert.AreEqual(1, builder.RevisionCalls, "The budget bounds attempts: exactly one revision, never a rework loop.");
        var refusedReason = await fixture.QueryEventPayloadAsync(started.RunId, "SkeletonRevisionRefused", "refusedReason");
        Assert.AreEqual("RevisionBudgetExhausted", refusedReason);
    }

    [TestMethod]
    public async Task Revise_AHumanCannotClaimAddressedByRevisionDirectly()
    {
        var builder = new RevisionBuilder(revisionContent: RevisedBook);
        await using var fixture = await RevisionFixture.CreateAsync(builder, maxRevisionAttempts: 1, findingIds: [CitedFindingId]);

        var started = await fixture.StartRunAsync();
        await fixture.RequestCriticReviewAsync(started.RunId);

        var outcome = await fixture.RecordDispositionAsync(started.RunId, CitedFindingId, "AddressedByRevision", "Pretend a revision happened.");
        Assert.IsFalse(outcome.GetProperty("succeeded").GetBoolean(),
            "AddressedByRevision is recorded only by the governed revision path — a human cannot claim a revision that never ran.");
        StringAssert.Contains(outcome.GetProperty("failureReason").GetString(), "revision");
    }

    // ── Deterministic builder: green first attempt, configurable revision ──────

    private sealed class RevisionBuilder(string revisionContent) : IBuilderProposalService
    {
        public int RevisionCalls;
        public SkeletonRevisionContext? LastRevisionContext;

        public Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken ct = default) =>
            Task.FromResult(Proposal(ticketId, InitialBook, "Deterministic first attempt: valid Book implementation."));

        public Task<BuilderProposal> GenerateRepairProposalAsync(long ticketId, SkeletonRepairContext repair, CancellationToken ct = default) =>
            throw new NotSupportedException("REVISE-1 proofs do not exercise bounded repair.");

        public Task<BuilderProposal> GenerateRevisionProposalAsync(long ticketId, SkeletonRevisionContext revision, CancellationToken ct = default)
        {
            RevisionCalls++;
            LastRevisionContext = revision;
            return Task.FromResult(Proposal(ticketId, revisionContent, "Deterministic revision directed by the human at the gate."));
        }

        public Task<BuilderProposal> GenerateProposalFromRequestAsync(int projectId, string request, CancellationToken ct = default) =>
            GenerateProposalAsync(0, ct);

        public Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default) =>
            throw new NotSupportedException("REVISE-1 proofs apply nothing; the governed spine owns apply.");

        private static BuilderProposal Proposal(long ticketId, string content, string summary) => new()
        {
            TicketId = ticketId,
            ProjectId = 0,
            Summary = summary,
            Rationale = "REVISE-1 deterministic fixture. A proposal is review material, not authority.",
            ModelProvider = "deterministic-fake",
            ModelName = "revise1-builder-fixed",
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

    // ── Deterministic critic: emits the configured finding ids, hash-bound ─────

    private sealed class DeterministicFindingCritic(IRunEventStore events, string[] findingIds) : ISkeletonCriticReviewService
    {
        public async Task<SkeletonCriticReviewOutcome?> ReviewAsync(SkeletonCriticReviewRequest request, CancellationToken cancellationToken = default)
        {
            var runEvents = await events.GetEventsAsync(request.RunId, cancellationToken);
            var package = runEvents.LastOrDefault(runEvent => runEvent.EventType == "CriticReviewPackageReady");
            if (package is null || !package.Payload.TryGetValue("packageSha256", out var packageHash))
            {
                return new SkeletonCriticReviewOutcome
                {
                    Succeeded = false,
                    FailureReason = "Critic package evidence is missing."
                };
            }

            var reviewId = $"review-{request.RunId}";
            await events.PublishAsync(new RunEventDto
            {
                RunId = request.RunId,
                EventType = "SkeletonCriticReviewRecorded",
                Message = "Deterministic REVISE-1 critic review recorded findings. A critic review is not approval, and a finding is not a veto.",
                Payload = new Dictionary<string, string>
                {
                    ["criticAgentRunId"] = $"critic-{request.RunId}",
                    ["reviewId"] = reviewId,
                    ["verdict"] = "RequestChanges",
                    ["findingCount"] = findingIds.Length.ToString(),
                    ["blockingFindingCount"] = "0",
                    ["findingIds"] = string.Join(",", findingIds),
                    ["packageSha256"] = packageHash,
                    ["groundTruthCheckCount"] = "1",
                    ["groundTruthMismatchCount"] = "0",
                    ["modelProvider"] = "deterministic-fake",
                    ["modelName"] = "revise1-critic-findings-fixed",
                    ["requestedByUserId"] = request.RequestedByUserId
                }
            }, cancellationToken);

            return new SkeletonCriticReviewOutcome
            {
                Succeeded = true,
                CriticAgentRunId = $"critic-{request.RunId}",
                ReviewId = reviewId,
                Verdict = "RequestChanges"
            };
        }
    }

    // ── Fixture: BookSeller copy + API host with deterministic builder/critic ──

    private sealed class RevisionFixture : IAsyncDisposable
    {
        private readonly string _sampleCopy;
        private readonly string _workspaceParent;
        private readonly string _evidenceRoot;
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;
        private readonly IDisposable _noNodeReuse;

        public int ProjectId { get; private set; }
        public long TicketId { get; private set; }

        private RevisionFixture(
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

        public static async Task<RevisionFixture> CreateAsync(RevisionBuilder builder, int? maxRevisionAttempts, string[] findingIds)
        {
            var workspaceParent = TempDir("irondev-revise1-ws");
            var evidenceRoot = TempDir("irondev-revise1-ev");
            var sampleCopy = TempDir("irondev-revise1-src");
            var noNodeReuse = DisableMsBuildNodeReuse();

            CopySample(Path.Combine(RepositoryRoot(), "Samples", "BookSeller"), sampleCopy);
            PrepareRestoredBookSellerSource(sampleCopy);
            GitInit(sampleCopy);

            var expectedRunReadiness = new ExpectedProjectRunReadinessService();

            var factory = Factory.WithWebHostBuilder(hostBuilder =>
            {
                hostBuilder.UseEnvironment("Test");
                hostBuilder.UseSetting("DisposableBuild:EvidenceRoot", evidenceRoot);
                hostBuilder.UseSetting("DisposableBuild:WorkspaceRoot", workspaceParent);
                hostBuilder.UseSetting("DisposableBuild:BuildTimeoutSeconds", "300");
                hostBuilder.UseSetting("DisposableBuild:TestTimeoutSeconds", "300");
                if (maxRevisionAttempts.HasValue)
                    hostBuilder.UseSetting("SkeletonRevision:MaxAttempts", maxRevisionAttempts.Value.ToString());
                hostBuilder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IBuilderProposalService>();
                    services.AddScoped<IBuilderProposalService>(_ => builder);
                    services.RemoveAll<ISkeletonTestAuthoringService>();
                    services.AddScoped<ISkeletonTestAuthoringService, EmptyTestAuthoring>();
                    services.RemoveAll<ISkeletonCriticReviewService>();
                    services.AddScoped<ISkeletonCriticReviewService>(sp =>
                        new DeterministicFindingCritic(sp.GetRequiredService<IRunEventStore>(), findingIds));
                    services.RemoveAll<IProjectRunReadinessService>();
                    services.AddSingleton<IProjectRunReadinessService>(expectedRunReadiness);
                });
            });

            var client = factory.CreateClient();
            await AuthenticateAsync(client);

            var fixture = new RevisionFixture(sampleCopy, workspaceParent, evidenceRoot, factory, client, noNodeReuse);
            await fixture.CreateProjectAndTicketAsync();
            expectedRunReadiness.ExpectProject(fixture.ProjectId);
            return fixture;
        }

        public async Task<TicketBuildRunDto> StartRunAsync() =>
            await PostJsonAsync<TicketBuildRunDto>(_client, $"/api/projects/{ProjectId}/tickets/{TicketId}/skeleton-runs", content: null);

        public async Task RequestCriticReviewAsync(string runId) =>
            await PostJsonAsync<JsonElement>(_client, $"/api/projects/{ProjectId}/tickets/{TicketId}/skeleton-runs/{runId}/critic-review", content: null);

        public async Task<TicketBuildRunDto> ReviseAsync(string runId, string[] findingIds, string reason) =>
            await PostJsonAsync<TicketBuildRunDto>(_client, $"/api/projects/{ProjectId}/tickets/{TicketId}/skeleton-runs/{runId}/revise",
                new { findingIds, reason });

        public async Task<TicketBuildRunDto> ContinueAsync(string runId) =>
            await PostJsonAsync<TicketBuildRunDto>(_client, $"/api/projects/{ProjectId}/tickets/{TicketId}/skeleton-runs/{runId}/continue", content: null);

        public async Task<JsonElement> RecordDispositionAsync(string runId, string findingId, string disposition, string reason) =>
            await PostJsonAsync<JsonElement>(_client, $"/api/projects/{ProjectId}/tickets/{TicketId}/skeleton-runs/{runId}/findings/{findingId}/disposition",
                new { disposition, reason });

        public async Task<SkeletonRunReport> GetReportAsync(string runId) =>
            await GetJsonAsync<SkeletonRunReport>(_client, $"/api/projects/{ProjectId}/tickets/{TicketId}/skeleton-runs/{runId}/report");

        public bool EvidenceFileExists(string runId, string fileName) =>
            File.Exists(Path.Combine(_evidenceRoot, "runs", runId, "evidence", fileName));

        public async Task<string?> QueryEventPayloadAsync(string runId, string eventType, string payloadKey)
        {
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
                Name = "BookSeller REVISE-1",
                Description = "Finding-driven revision proof fixture.",
                LocalPath = _sampleCopy
            });
            ProjectId = project.Id;

            var ticket = await PostJsonAsync<ProjectTicket>(_client, $"/api/projects/{ProjectId}/tickets", new CreateProjectTicketRequest
            {
                Title = "Round prices honestly",
                Type = "Task",
                Priority = "Medium",
                Summary = "REVISE-1 fixture ticket: the human at the gate directs a bounded revision.",
                Problem = "Prove finding-driven revision through the product path.",
                ProposedChange = "Deterministic builder exercises the revision loop.",
                AcceptanceCriteria = ["A halted run either revises within budget back to the same gate, or the refusal names why."],
                Provenance = new TicketProvenanceDto
                {
                    Source = "revise1:fixture",
                    Notes = "REVISE-1 proof ticket. It grants no authority."
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
                ModelName = "revise1-no-authored-tests"
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
        RunTool(path, "git", "config user.email revise1@irondev.local");
        RunTool(path, "git", "config user.name Revise1Proof");
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
