using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core.Builder;
using IronDev.Core.Chat;
using IronDev.Core.Governance;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.RunReadiness;
using IronDev.Core.Workflow;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api.Demo;

[TestClass]
[TestCategory("DemoSeed")]
[TestCategory("AlphaSmoke")]
[TestCategory("ReleaseReadiness")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
public sealed class DemoSeedApiDrivenTests : ApiTestBase
{
    private const string ValidateBookKey = "validate-book";
    private const string SearchByAuthorKey = "search-by-author";
    private const string BulkDiscountKey = "bulk-discount";
    private const int DemoReviewerUserId = 42;
    private const string DemoReviewerEmail = "demo.reviewer@irondev.local";
    private const string DemoReviewerPassword = "demo-reviewer-password123";
    private const string DemoReviewerDisplayName = "DEMO Reviewer";

    private static readonly string[] UsabilityProbeTitles =
    [
        "Demo usability probe — pricing rule 1",
        "Demo usability probe — pricing rule 2"
    ];

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

    private const string CatalogWithAuthorSearch =
        """
        namespace BookSeller.Domain;

        /// <summary>
        /// An in-memory book catalog. Lookup is by ISBN and by author for the
        /// demo ticket path.
        /// </summary>
        public sealed class Catalog
        {
            private readonly Dictionary<string, Book> _booksByIsbn = new(StringComparer.Ordinal);

            public void Add(Book book) => _booksByIsbn[book.Isbn] = book;

            public Book? GetByIsbn(string isbn) =>
                _booksByIsbn.TryGetValue(isbn, out var book) ? book : null;

            public IReadOnlyList<Book> FindByAuthor(string author)
            {
                if (string.IsNullOrWhiteSpace(author))
                    return Array.Empty<Book>();

                return _booksByIsbn.Values
                    .Where(book => string.Equals(book.Author, author, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            public int Count => _booksByIsbn.Count;
        }
        """;

    private const string PricingWithBulkDiscount =
        """
        namespace BookSeller.Domain;

        /// <summary>
        /// Prices an order line. Bulk orders of ten or more copies earn a ten percent
        /// discount; smaller orders keep flat pricing exactly as before.
        /// </summary>
        public sealed class PricingService
        {
            private const int BulkThreshold = 10;
            private const decimal BulkDiscountRate = 0.10m;

            public decimal PriceFor(Book book, int quantity)
            {
                if (quantity <= 0)
                    throw new System.ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");

                var flat = book.Price * quantity;
                if (quantity < BulkThreshold)
                    return flat;

                var discounted = flat * (1m - BulkDiscountRate);
                return System.Math.Round(discounted, 2, System.MidpointRounding.AwayFromZero);
            }
        }
        """;

    /// <summary>
    /// The hero finding is deliberately advisory and deliberately REAL: the golden
    /// bulk-discount diff rounds the discounted branch to 2dp but leaves the flat
    /// branch unrounded — a genuine design observation a human reviewer would raise,
    /// yet correct per acceptance criterion 2 ("priced flat, exactly as before").
    /// Dispositioning it requires understanding the ticket, not rubber-stamping.
    /// </summary>
    private const string HeroFindingId = "finding-bulk-rounding-asymmetry";

    [TestMethod]
    public async Task DemoSeed_BaselineHistory_IsApiDrivenAndSqlPersisted()
    {
        var workspaceParent = TempDir("irondev-demo1-ws");
        var evidenceRoot = TempDir("irondev-demo1-ev");
        var sampleCopy = TempDir("irondev-demo1-src");
        var ticketKinds = new ConcurrentDictionary<long, string>();

        try
        {
            using var noNodeReuse = DisableMsBuildNodeReuse();
            CopySample(SampleRoot(), sampleCopy);
            PrepareRestoredBookSellerSource(sampleCopy);
            GitInit(sampleCopy);

            var expectedApplyCapability = new ExpectedProjectApplyCapabilityService();
            var expectedRunReadiness = new ExpectedProjectRunReadinessService(expectedApplyCapability);

            using var factory = Factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.UseSetting("DisposableBuild:EvidenceRoot", evidenceRoot);
                builder.UseSetting("DisposableBuild:WorkspaceRoot", workspaceParent);
                builder.UseSetting("DisposableBuild:BuildTimeoutSeconds", "300");
                builder.UseSetting("DisposableBuild:TestTimeoutSeconds", "300");
                builder.UseSetting("SkeletonApply:Enabled", "true");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IBuilderProposalService>();
                    services.AddScoped<IBuilderProposalService>(_ => new DemoSeedBuilder(ticketKinds));
                    services.RemoveAll<ISkeletonTestAuthoringService>();
                    services.AddScoped<ISkeletonTestAuthoringService, EmptyTestAuthoring>();
                    services.RemoveAll<ISkeletonCriticReviewService>();
                    services.AddScoped<ISkeletonCriticReviewService, DeterministicCleanCriticReviewService>();
                    services.RemoveAll<IProjectRunReadinessService>();
                    services.AddSingleton<IProjectRunReadinessService>(expectedRunReadiness);
                    services.RemoveAll<IProjectApplyCapabilityService>();
                    services.AddSingleton<IProjectApplyCapabilityService>(expectedApplyCapability);
                });
            });

            using var client = factory.CreateClient();
            await AuthenticateAsync(client);

            var project = await CreateProjectAsync(client, sampleCopy);
            expectedApplyCapability.ExpectProject(
                project.Id,
                ExpectedProjectApplyCapabilityService.CreateReadinessEvidenceHash(
                    project.Id,
                    "demo-baseline-single-project-apply-capability-v1"),
                sampleCopy,
                Path.GetDirectoryName(sampleCopy)!);
            expectedRunReadiness.ExpectProject(project.Id);
            using var reviewerClient = await CreateReviewerClientAsync(project.Id);
            var validateTicket = await CreateFixtureTicketAsync(client, project.Id, ValidateBookKey);
            var searchTicket = await CreateFixtureTicketAsync(client, project.Id, SearchByAuthorKey);
            ticketKinds[validateTicket.Id] = ValidateBookKey;
            ticketKinds[searchTicket.Id] = SearchByAuthorKey;

            var applied = await DriveAppliedAsync(client, reviewerClient, project.Id, validateTicket.Id);
            var paused = await DrivePausedForApprovalAsync(client, project.Id, searchTicket.Id);

            await AssertBaselineSqlPersistenceAsync(
                project.Id,
                validateTicket.Id,
                applied.RunId,
                applied.AcceptedApprovalId,
                applied.CriticPackageSha256,
                searchTicket.Id,
                paused.RunId,
                paused.CriticPackageSha256);

            var visibleTickets = await GetJsonAsync<IReadOnlyList<ProjectTicket>>(client, $"/api/projects/{project.Id}/tickets");
            Assert.IsTrue(visibleTickets.Any(ticket => ticket.Id == validateTicket.Id),
                "The applied baseline ticket must remain visible from the product tickets API.");
            Assert.IsTrue(visibleTickets.Any(ticket => ticket.Id == searchTicket.Id && ticket.Status == "Draft"),
                "The paused baseline ticket remains a ticket record; run state carries PausedForApproval.");

            var liveChatMessageCount = await CountRowsAsync(
                "SELECT COUNT(*) FROM dbo.ChatMessages WHERE ProjectId = @ProjectId",
                new { ProjectId = project.Id });
            Assert.AreEqual(0, liveChatMessageCount, "DEMO-1 must not seed the live chat ticket ahead of the demo.");

            // Seeding baseline history is not enough: the environment must remain USABLE.
            // Prove a viewer can still create a fresh ticket, start a governed run, see real
            // build/test evidence, and reach the human gate — repeatably — after seeding.
            var usabilityProbe = await ProveRemainsUsableAsync(client, project.Id, ticketKinds);

            // The probe must not disturb the seeded baseline it ran alongside.
            await AssertBaselineUnchangedAsync(project.Id, validateTicket.Id, applied.RunId, searchTicket.Id, paused.RunId);

            WriteDemoReceipt(new DemoSeedReceipt(
                Command: "Scripts/demo/demo-seed.ps1 -Seed -Project BookSeller -ModelMode Deterministic",
                CommitSha: CurrentCommitSha(),
                ModelMode: "Deterministic",
                PersistenceMode: "SQL/API",
                ApiBaseUrlClassification: "in-process authenticated API test host",
                RootSafetyStatus: "Passed",
                ProjectId: project.Id,
                AppliedTicket: new DemoTicketReceipt(
                    Key: ValidateBookKey,
                    TicketId: validateTicket.Id,
                    RunId: applied.RunId,
                    State: "Applied",
                    CriticPackageHash: applied.CriticPackageSha256,
                    CriticReviewId: applied.CriticReviewId,
                    AcceptedApprovalId: applied.AcceptedApprovalId.ToString("D"),
                    ApprovalTargetHash: applied.CriticPackageSha256,
                    ContinuationResult: "Completed",
                    ApplyReceiptPath: RedactPath(applied.ApplyReceiptPath),
                    ApplyReceiptSha256: applied.ApplyReceiptSha256,
                    FinalReportReference: $"api/projects/{project.Id}/tickets/{validateTicket.Id}/skeleton-runs/{applied.RunId}/report"),
                PausedTicket: new DemoTicketReceipt(
                    Key: SearchByAuthorKey,
                    TicketId: searchTicket.Id,
                    RunId: paused.RunId,
                    State: "PausedForApproval",
                    CriticPackageHash: paused.CriticPackageSha256,
                    CriticReviewId: string.Empty,
                    AcceptedApprovalId: string.Empty,
                    ApprovalTargetHash: paused.CriticPackageSha256,
                    ContinuationResult: "NotRequested",
                    ApplyReceiptPath: string.Empty,
                    ApplyReceiptSha256: string.Empty,
                    FinalReportReference: $"api/projects/{project.Id}/tickets/{searchTicket.Id}/skeleton-runs/{paused.RunId}/report"),
                LiveChatTicketSeeded: false,
                UsabilityProbe: usabilityProbe,
                IdempotencyResult: "Fixture keys resolve to one intended baseline ticket each in this proof run.",
                RedactionConfirmation: "Secret, token, connection-string, and user-local path values are not emitted raw.",
                KnownGaps:
                [
                    "DEMO-1 proves API/SQL baseline history through the integration host, not a long-lived already-running API process.",
                    "DEMO-2 UI click-path proof is covered by the flow-shell contract and a separate API visible/startable test.",
                    "The usability probe proves the environment stays governable for new work; it stops at the human gate and never approves, continues, or applies."
                ],
                BoundaryStatement: "The seed may replay governed baseline history; it does not create the live chat ticket, invent approval, satisfy policy, continue workflow by itself, or grant release/deployment authority."));
        }
        finally
        {
            TryDelete(sampleCopy);
            TryDelete(workspaceParent);
            TryDelete(evidenceRoot);
        }
    }

    [TestMethod]
    public async Task Demo2_ChatConfirmedTicket_IsVisibleAndStartableThroughApi()
    {
        var workspaceParent = TempDir("irondev-demo2-ws");
        var evidenceRoot = TempDir("irondev-demo2-ev");
        var sampleCopy = TempDir("irondev-demo2-src");
        var ticketKinds = new ConcurrentDictionary<long, string>();
        const string userMessage = "books need a discount validation rule";

        try
        {
            using var noNodeReuse = DisableMsBuildNodeReuse();
            CopySample(SampleRoot(), sampleCopy);
            PrepareRestoredBookSellerSource(sampleCopy);
            GitInit(sampleCopy);

            var expectedRunReadiness = new ExpectedProjectRunReadinessService();

            using var factory = Factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.UseSetting("DisposableBuild:EvidenceRoot", evidenceRoot);
                builder.UseSetting("DisposableBuild:WorkspaceRoot", workspaceParent);
                builder.UseSetting("DisposableBuild:BuildTimeoutSeconds", "300");
                builder.UseSetting("DisposableBuild:TestTimeoutSeconds", "300");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IBuilderProposalService>();
                    services.AddScoped<IBuilderProposalService>(_ => new DemoSeedBuilder(ticketKinds));
                    services.RemoveAll<ISkeletonTestAuthoringService>();
                    services.AddScoped<ISkeletonTestAuthoringService, EmptyTestAuthoring>();
                    services.RemoveAll<ISkeletonCriticReviewService>();
                    services.AddScoped<ISkeletonCriticReviewService, DeterministicCleanCriticReviewService>();
                    services.RemoveAll<IProjectChatResponseService>();
                    services.AddScoped<IProjectChatResponseService, DemoFormalizationChatResponseService>();
                    services.RemoveAll<IDraftTicketService>();
                    services.AddScoped<IDraftTicketService, DemoDraftTicketService>();
                    services.RemoveAll<IProjectRunReadinessService>();
                    services.AddSingleton<IProjectRunReadinessService>(expectedRunReadiness);
                });
            });

            using var client = factory.CreateClient();
            await AuthenticateAsync(client);

            var project = await CreateProjectAsync(
                client,
                sampleCopy,
                "BookSeller DEMO-2",
                "DEMO-2 chat to visible confirmed ticket fixture.");
            expectedRunReadiness.ExpectProject(project.Id);

            var sessionId = await PostJsonAsync<long>(
                client,
                $"/api/projects/{project.Id}/chat/sessions",
                new ProjectChatSession
                {
                    ProjectId = project.Id,
                    Title = "DEMO-2 live ticket shaping"
                });

            var messageId = await PostJsonAsync<long>(
                client,
                $"/api/projects/{project.Id}/chat/sessions/{sessionId}/messages",
                new ChatMessage
                {
                    ProjectId = project.Id,
                    ChatSessionId = sessionId,
                    Role = "user",
                    Message = userMessage,
                    LinkedFilePaths = "src/BookSeller.Domain/PricingService.cs",
                    LinkedSymbols = "PricingService"
                });

            var completion = await PostJsonAsync<ChatController.ChatCompletionResponse>(
                client,
                $"/api/projects/{project.Id}/chat/complete",
                new ChatController.ChatCompletionRequest(project.Id, sessionId, userMessage, null, "projectQuestion"));
            Assert.AreEqual("Formalization", completion.Mode);
            Assert.IsTrue(completion.Gate!.CanCreateTicket);

            var draft = await PostJsonAsync<DraftTicket>(
                client,
                $"/api/projects/{project.Id}/tickets/draft",
                new TicketsController.DraftTicketRequest(
                    "BookSeller",
                    "Bulk orders earn a discount",
                    userMessage,
                    "src/BookSeller.Domain/PricingService.cs",
                    "PricingService",
                    sessionId,
                    messageId));
            Assert.AreEqual(sessionId, draft.SourceChatSessionId);
            Assert.AreEqual(messageId, draft.SourceMessageId);

            var ticket = await PostJsonAsync<ProjectTicket>(
                client,
                $"/api/projects/{project.Id}/tickets/draft/confirm",
                draft);
            ticketKinds[ticket.Id] = BulkDiscountKey;

            Assert.AreEqual("Draft", ticket.Status);
            Assert.AreEqual(sessionId, ticket.SourceChatSessionId);
            Assert.AreEqual(messageId, ticket.SourceChatMessageId);

            var tickets = await GetJsonAsync<IReadOnlyList<ProjectTicket>>(client, $"/api/projects/{project.Id}/tickets");
            Assert.IsTrue(tickets.Any(item => item.Id == ticket.Id), "Confirmed chat ticket must appear in the Tickets API list.");

            var detail = await GetJsonAsync<ProjectTicket>(client, $"/api/projects/{project.Id}/tickets/{ticket.Id}");
            Assert.AreEqual(ticket.Id, detail.Id);
            Assert.AreEqual("Bulk orders earn a discount", detail.Title);

            var started = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs",
                content: null);
            Assert.AreEqual("PausedForApproval", started.Status);
            Assert.IsTrue(started.RequiresHumanApproval);

            var report = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            Assert.AreEqual("PausedForApproval", report.Status);
            Assert.IsNotNull(report.CriticPackage);
            Assert.IsNotNull(report.Approval);
            Assert.IsFalse(report.Approval!.ContinuationUnblocked);
            Assert.IsNull(report.Apply);

            await AssertChatTicketSqlPersistenceAsync(project.Id, ticket.Id, sessionId, messageId, started.RunId);
        }
        finally
        {
            TryDelete(sampleCopy);
            TryDelete(workspaceParent);
            TryDelete(evidenceRoot);
        }
    }

    [TestMethod]
    public async Task Hero_BulkDiscountAdvisoryFinding_RequiresDispositionBeforeApplied()
    {
        var workspaceParent = TempDir("irondev-hero1-ws");
        var evidenceRoot = TempDir("irondev-hero1-ev");
        var sampleCopy = TempDir("irondev-hero1-src");
        var ticketKinds = new ConcurrentDictionary<long, string>();

        try
        {
            using var noNodeReuse = DisableMsBuildNodeReuse();
            CopySample(SampleRoot(), sampleCopy);
            PrepareRestoredBookSellerSource(sampleCopy);
            GitInit(sampleCopy);

            var expectedApplyCapability = new ExpectedProjectApplyCapabilityService();
            var expectedRunReadiness = new ExpectedProjectRunReadinessService(expectedApplyCapability);

            using var factory = Factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");
                builder.UseSetting("DisposableBuild:EvidenceRoot", evidenceRoot);
                builder.UseSetting("DisposableBuild:WorkspaceRoot", workspaceParent);
                builder.UseSetting("DisposableBuild:BuildTimeoutSeconds", "300");
                builder.UseSetting("DisposableBuild:TestTimeoutSeconds", "300");
                builder.UseSetting("SkeletonApply:Enabled", "true");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IBuilderProposalService>();
                    services.AddScoped<IBuilderProposalService>(_ => new DemoSeedBuilder(ticketKinds));
                    services.RemoveAll<ISkeletonTestAuthoringService>();
                    services.AddScoped<ISkeletonTestAuthoringService, EmptyTestAuthoring>();
                    services.RemoveAll<ISkeletonCriticReviewService>();
                    services.AddScoped<ISkeletonCriticReviewService, DemoAdvisoryCriticReviewService>();
                    services.RemoveAll<IProjectRunReadinessService>();
                    services.AddSingleton<IProjectRunReadinessService>(expectedRunReadiness);
                    services.RemoveAll<IProjectApplyCapabilityService>();
                    services.AddSingleton<IProjectApplyCapabilityService>(expectedApplyCapability);
                });
            });

            using var client = factory.CreateClient();
            await AuthenticateAsync(client);

            var project = await CreateProjectAsync(
                client,
                sampleCopy,
                "BookSeller HERO-1",
                "HERO-1 advisory finding disposition gate fixture.");
            expectedApplyCapability.ExpectProject(
                project.Id,
                ExpectedProjectApplyCapabilityService.CreateReadinessEvidenceHash(
                    project.Id,
                    "hero-disposition-single-project-apply-capability-v1"),
                sampleCopy,
                Path.GetDirectoryName(sampleCopy)!);
            expectedRunReadiness.ExpectProject(project.Id);
            var ticket = await CreateFixtureTicketAsync(client, project.Id, BulkDiscountKey);
            ticketKinds[ticket.Id] = BulkDiscountKey;
            using var reviewerClient = await CreateReviewerClientAsync(project.Id);

            var started = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs",
                content: null);
            Assert.AreEqual("PausedForApproval", started.Status);

            var haltedReport = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            var packageHash = haltedReport.Approval!.TargetHash;
            Assert.AreEqual(packageHash, haltedReport.CriticPackage!.Sha256OnDisk);

            // The critic reviews and reports exactly one ADVISORY finding.
            var criticReview = await PostJsonAsync<SkeletonCriticReviewOutcome>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/critic-review",
                content: null);
            Assert.IsTrue(criticReview.Succeeded);
            Assert.AreEqual(1, criticReview.Findings.Count);
            Assert.AreEqual(HeroFindingId, criticReview.Findings[0].FindingId);
            Assert.IsFalse(criticReview.Findings[0].BlocksMerge, "The hero finding is advisory, not a veto.");

            // Approval exists BEFORE disposition — and continuation must still refuse:
            // an accepted approval does not bypass an undispositioned finding.
            var approvalProjectId = TicketSkeletonRunService.ApprovalProjectGuid(project.Id);
            await CreateAcceptedApprovalAsync(reviewerClient, approvalProjectId, started.RunId, packageHash);

            var refusedContinue = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/continue",
                content: null);
            Assert.AreEqual("PausedForApproval", refusedContinue.Status,
                "Continuation must refuse while the finding is undispositioned, even with a live accepted approval.");

            // A disposition without a reason is a dismissal, not a decision.
            var noReason = await PostJsonAsync<SkeletonFindingDispositionOutcome>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/findings/{HeroFindingId}/disposition",
                new TicketsController.FindingDispositionBody("AcceptRisk", ""));
            Assert.IsFalse(noReason.Succeeded, "A disposition must carry a reason.");

            // A disposition can only answer a finding the critic actually made.
            var phantom = await PostJsonAsync<SkeletonFindingDispositionOutcome>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/findings/finding-does-not-exist/disposition",
                new TicketsController.FindingDispositionBody("AcceptRisk", "This finding does not exist."));
            Assert.IsFalse(phantom.Succeeded, "A disposition must answer a real finding.");

            // The golden disposition: accepting the finding requires understanding the ticket.
            var disposition = await PostJsonAsync<SkeletonFindingDispositionOutcome>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/findings/{HeroFindingId}/disposition",
                new TicketsController.FindingDispositionBody(
                    "AcceptRisk",
                    "Flat path intentionally unrounded per acceptance criterion 2: fewer than 10 copies is priced flat, exactly as before."));
            Assert.IsTrue(disposition.Succeeded, disposition.FailureReason);

            var continued = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/continue",
                content: null);
            Assert.AreEqual("Completed", continued.Status);

            var applied = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/apply",
                content: null);
            Assert.AreEqual("Applied", applied.Status);

            var finalReport = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            Assert.AreEqual("Applied", finalReport.Status);
            Assert.IsTrue(finalReport.LoopComplete, $"Final report gaps: {string.Join(" | ", finalReport.Gaps)}");
            Assert.AreEqual(1, finalReport.CriticReviews.Single().FindingCount);
            Assert.AreEqual(0, finalReport.CriticReviews.Single().BlockingFindingCount);
            var dispositionTrace = finalReport.FindingDispositions.Single();
            Assert.AreEqual(HeroFindingId, dispositionTrace.FindingId);
            Assert.AreEqual("AcceptRisk", dispositionTrace.Disposition);
            StringAssert.Contains(dispositionTrace.Reason, "criterion 2");

            await AssertHeroDispositionSqlAsync(project.Id, ticket.Id, started.RunId);
        }
        finally
        {
            TryDelete(sampleCopy);
            TryDelete(workspaceParent);
            TryDelete(evidenceRoot);
        }
    }

    private static async Task AssertHeroDispositionSqlAsync(int projectId, long ticketId, string runId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var state = await connection.ExecuteScalarAsync<string>(
            "SELECT State FROM dbo.Runs WHERE RunId = @RunId AND ProjectId = @ProjectId AND TicketId = @TicketId",
            new { RunId = runId, ProjectId = projectId, TicketId = ticketId });
        Assert.AreEqual("Applied", state);

        var events = (await connection.QueryAsync<string>(
            "SELECT EventType FROM dbo.RunEvents WHERE RunId = @RunId ORDER BY TimestampUtc, Id",
            new { RunId = runId })).ToList();

        // The refusal happened after the review and before the disposition, from durable events.
        var reviewIndex = events.IndexOf("SkeletonCriticReviewRecorded");
        var refusedIndex = events.IndexOf("ContinuationRefused");
        var dispositionIndex = events.IndexOf("SkeletonFindingDispositionRecorded");
        var unblockedIndex = events.IndexOf("SkeletonContinuationUnblocked");
        Assert.IsTrue(reviewIndex >= 0 && refusedIndex > reviewIndex,
            "Continuation must have been refused after the critic review recorded the finding.");
        Assert.IsTrue(dispositionIndex > refusedIndex,
            "The disposition must have been recorded after the refusal.");
        Assert.IsTrue(unblockedIndex > dispositionIndex,
            "Continuation must have been unblocked only after the disposition.");
        CollectionAssert.Contains(events, "SkeletonApplied");
    }

    private static async Task<DemoAppliedRun> DriveAppliedAsync(HttpClient client, HttpClient reviewerClient, int projectId, long ticketId)
    {
        var started = await PostJsonAsync<TicketBuildRunDto>(
            client,
            $"/api/projects/{projectId}/tickets/{ticketId}/skeleton-runs",
            content: null);
        Assert.AreEqual("PausedForApproval", started.Status);

        var haltedReport = await GetJsonAsync<SkeletonRunReport>(
            client,
            $"/api/projects/{projectId}/tickets/{ticketId}/skeleton-runs/{started.RunId}/report");
        Assert.IsNotNull(haltedReport.CriticPackage);
        Assert.IsNotNull(haltedReport.Approval);

        var packageHash = haltedReport.Approval!.TargetHash;
        Assert.AreEqual(packageHash, haltedReport.CriticPackage!.Sha256OnDisk);

        var criticReview = await PostJsonAsync<SkeletonCriticReviewOutcome>(
            client,
            $"/api/projects/{projectId}/tickets/{ticketId}/skeleton-runs/{started.RunId}/critic-review",
            content: null);
        Assert.IsTrue(criticReview.Succeeded);

        var approvalProjectId = TicketSkeletonRunService.ApprovalProjectGuid(projectId);
        var approval = await CreateAcceptedApprovalAsync(reviewerClient, approvalProjectId, started.RunId, packageHash);

        var continued = await PostJsonAsync<TicketBuildRunDto>(
            client,
            $"/api/projects/{projectId}/tickets/{ticketId}/skeleton-runs/{started.RunId}/continue",
            content: null);
        Assert.AreEqual("Completed", continued.Status);

        var applied = await PostJsonAsync<TicketBuildRunDto>(
            client,
            $"/api/projects/{projectId}/tickets/{ticketId}/skeleton-runs/{started.RunId}/apply",
            content: null);
        Assert.AreEqual("Applied", applied.Status);

        var finalReport = await GetJsonAsync<SkeletonRunReport>(
            client,
            $"/api/projects/{projectId}/tickets/{ticketId}/skeleton-runs/{started.RunId}/report");
        Assert.AreEqual("Applied", finalReport.Status);
        Assert.IsTrue(finalReport.LoopComplete, $"Final report gaps: {string.Join(" | ", finalReport.Gaps)}");
        Assert.AreEqual(DemoReviewerUserId.ToString(), finalReport.Approval!.ApprovedByActorId);
        Assert.AreEqual(DemoReviewerDisplayName, finalReport.Approval.ApprovedByActorDisplayName);
        Assert.IsTrue(finalReport.Apply!.Applied);

        var applyReceipt = finalReport.Apply.Receipts.Single(receipt => receipt.Name == "apply-copy.json");
        var persistedApplyReceiptPath = CopyDemoArtifact(applyReceipt.Path, $"demo1-{ticketId}-apply-copy.json");

        return new DemoAppliedRun(
            started.RunId,
            packageHash,
            criticReview.ReviewId ?? string.Empty,
            approval.AcceptedApprovalId,
            persistedApplyReceiptPath,
            ComputeSha256(await File.ReadAllBytesAsync(persistedApplyReceiptPath)));
    }

    private static async Task<DemoPausedRun> DrivePausedForApprovalAsync(HttpClient client, int projectId, long ticketId)
    {
        var started = await PostJsonAsync<TicketBuildRunDto>(
            client,
            $"/api/projects/{projectId}/tickets/{ticketId}/skeleton-runs",
            content: null);
        Assert.AreEqual("PausedForApproval", started.Status);
        Assert.IsTrue(started.RequiresHumanApproval);

        var report = await GetJsonAsync<SkeletonRunReport>(
            client,
            $"/api/projects/{projectId}/tickets/{ticketId}/skeleton-runs/{started.RunId}/report");
        Assert.AreEqual("PausedForApproval", report.Status);
        Assert.IsFalse(report.LoopComplete);
        Assert.IsNotNull(report.CriticPackage);
        Assert.IsNotNull(report.Approval);
        Assert.IsFalse(report.Approval!.ContinuationUnblocked);
        Assert.IsNull(report.Apply);

        return new DemoPausedRun(started.RunId, report.Approval.TargetHash);
    }

    private static async Task<DemoUsabilityProbeReceipt> ProveRemainsUsableAsync(
        HttpClient client,
        int projectId,
        ConcurrentDictionary<long, string> ticketKinds)
    {
        // Repeatability is the claim under test: after baseline history exists, a viewer
        // must still be able to create a NEW ticket and drive it to the human gate on real
        // build/test evidence. We do that twice, through the product API, and stop at the
        // gate every time — the seed proves the environment is usable, it does not spend
        // the human's approval on the viewer's behalf.
        var runs = new List<DemoUsabilityRunReceipt>();
        foreach (var title in UsabilityProbeTitles)
        {
            var ticket = await CreateProbeTicketAsync(client, projectId, title);
            ticketKinds[ticket.Id] = BulkDiscountKey;

            var started = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{projectId}/tickets/{ticket.Id}/skeleton-runs",
                content: null);
            Assert.AreEqual("PausedForApproval", started.Status, "A fresh post-seed ticket must run to the human gate.");
            Assert.IsTrue(started.RequiresHumanApproval);

            var report = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{projectId}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            Assert.AreEqual("PausedForApproval", report.Status);

            // Real build/test evidence — not fabricated: only a green dotnet build+test in
            // the disposable workspace reaches PausedForApproval, the critic package exists
            // on disk, and its hash re-verifies at report time.
            Assert.IsNotNull(report.CriticPackage, "A usable run must produce a critic package from real build/test evidence.");
            Assert.IsTrue(report.CriticPackage!.ExistsOnDisk, "Critic package must exist on disk.");
            Assert.IsTrue(report.CriticPackage.HashVerified, "Critic package hash must re-verify — proving real, unfabricated evidence.");
            Assert.IsTrue(report.Timeline.Any(entry => entry.EventType == "SkeletonEvidencePackaged"),
                "The run timeline must show real disposable build/test evidence was packaged.");

            // The probe reaches the gate and stops. It does not approve, continue, or apply.
            Assert.IsFalse(report.LoopComplete);
            Assert.IsNotNull(report.Approval);
            Assert.IsFalse(report.Approval!.ContinuationUnblocked, "The probe must not continue past the gate.");
            Assert.IsNull(report.Apply, "The probe must not apply.");

            await AssertUsabilityProbeSqlAsync(projectId, ticket.Id, started.RunId);

            runs.Add(new DemoUsabilityRunReceipt(
                TicketId: ticket.Id,
                RunId: started.RunId,
                State: "PausedForApproval",
                CriticPackageHash: report.CriticPackage.Sha256OnDisk,
                BuildTestEvidenceVerified: true));
        }

        Assert.AreEqual(runs.Count, runs.Select(run => run.RunId).Distinct().Count(),
            "Repeated governed runs must be genuinely distinct runs.");
        Assert.AreEqual(runs.Count, runs.Select(run => run.CriticPackageHash).Distinct().Count(),
            "Each governed run must produce its own independent evidence package.");

        return new DemoUsabilityProbeReceipt(
            Proved: true,
            Runs: runs.ToArray(),
            Note: "After baseline seeding, fresh tickets remain creatable and governable to the human gate with real build/test evidence. No probe was approved, continued, or applied.");
    }

    private static async Task<ProjectTicket> CreateProbeTicketAsync(HttpClient client, int projectId, string title)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/tickets", new CreateProjectTicketRequest
        {
            Title = title,
            Type = "Task",
            Priority = "Low",
            Summary = "Post-seed usability probe: prove a fresh ticket still runs to the human gate.",
            Problem = "Confirm the seeded environment remains usable for new governed work.",
            ProposedChange = "Exercise the bulk discount pricing path through a real disposable build/test run.",
            AcceptanceCriteria = ["A governed run reaches the human approval gate with real build and test evidence."],
            Provenance = new TicketProvenanceDto
            {
                Source = "demo-seed:usability-probe",
                Notes = "Usability probe ticket. It proves the environment is usable and grants no authority."
            }
        });

        var ticket = await ReadSuccessAsync<ProjectTicket>(response);
        Assert.AreEqual("Draft", ticket.Status);
        return ticket;
    }

    private static async Task AssertUsabilityProbeSqlAsync(int projectId, long ticketId, string runId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var state = await connection.ExecuteScalarAsync<string>(
            "SELECT State FROM dbo.Runs WHERE RunId = @RunId AND ProjectId = @ProjectId AND TicketId = @TicketId",
            new { RunId = runId, ProjectId = projectId, TicketId = ticketId });
        Assert.AreEqual("PausedForApproval", state);

        var events = (await connection.QueryAsync<string>(
            "SELECT EventType FROM dbo.RunEvents WHERE RunId = @RunId ORDER BY TimestampUtc, Id",
            new { RunId = runId })).ToArray();
        CollectionAssert.Contains(events, "SkeletonEvidencePackaged");
        CollectionAssert.Contains(events, "CriticReviewPackageReady");
        CollectionAssert.Contains(events, "ApprovalRequiredHalt");
        CollectionAssert.DoesNotContain(events, "SkeletonContinuationUnblocked");
        CollectionAssert.DoesNotContain(events, "SkeletonApplied");

        var approvalCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM governance.AcceptedApproval WHERE ApprovalTargetId = @RunId",
            new { RunId = runId });
        Assert.AreEqual(0, approvalCount, "The usability probe must not create accepted approval.");
    }

    private static async Task AssertBaselineUnchangedAsync(
        int projectId,
        long appliedTicketId,
        string appliedRunId,
        long pausedTicketId,
        string pausedRunId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var appliedState = await connection.ExecuteScalarAsync<string>(
            "SELECT State FROM dbo.Runs WHERE RunId = @RunId AND ProjectId = @ProjectId AND TicketId = @TicketId",
            new { RunId = appliedRunId, ProjectId = projectId, TicketId = appliedTicketId });
        Assert.AreEqual("Applied", appliedState, "The usability probe must not disturb applied baseline history.");

        var pausedState = await connection.ExecuteScalarAsync<string>(
            "SELECT State FROM dbo.Runs WHERE RunId = @RunId AND ProjectId = @ProjectId AND TicketId = @TicketId",
            new { RunId = pausedRunId, ProjectId = projectId, TicketId = pausedTicketId });
        Assert.AreEqual("PausedForApproval", pausedState, "The usability probe must not disturb paused baseline history.");
    }

    private static async Task<Project> CreateProjectAsync(
        HttpClient client,
        string localPath,
        string name = "BookSeller DEMO-1",
        string description = "DEMO-1 API-driven demo seed fixture.")
    {
        var response = await client.PostAsJsonAsync("/api/projects", new Project
        {
            Name = name,
            Description = description,
            LocalPath = localPath
        });

        return await ReadSuccessAsync<Project>(response);
    }

    private static async Task<ProjectTicket> CreateFixtureTicketAsync(HttpClient client, int projectId, string key)
    {
        var fixture = LoadFixtureTicket(key);
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/tickets", new CreateProjectTicketRequest
        {
            Title = fixture.Title,
            Type = "Task",
            Priority = key == ValidateBookKey ? "High" : "Medium",
            Summary = fixture.Summary,
            Problem = fixture.Summary,
            ProposedChange = fixture.TechnicalNotes,
            AcceptanceCriteria = SplitCriteria(fixture.AcceptanceCriteria),
            Provenance = new TicketProvenanceDto
            {
                Source = $"demo-seed:{key}",
                Notes = "Fixture-backed demo seed ticket. It grants no authority."
            }
        });

        var ticket = await ReadSuccessAsync<ProjectTicket>(response);
        Assert.AreEqual("Draft", ticket.Status);
        return ticket;
    }

    private static FixtureTicket LoadFixtureTicket(string key)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "TestFixtures", "BookSeller", "tickets.json")));
        var ticket = document.RootElement.GetProperty("tickets")
            .EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == key);
        return new FixtureTicket(
            ticket.GetProperty("title").GetString()!,
            ticket.GetProperty("summary").GetString()!,
            ticket.GetProperty("acceptanceCriteria").GetString()!,
            ticket.GetProperty("technicalNotes").GetString()!);
    }

    private static string[] SplitCriteria(string value) =>
        value.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

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

    private static async Task<AcceptedApprovalReadModel> CreateAcceptedApprovalAsync(
        HttpClient client,
        Guid approvalProjectId,
        string runId,
        string packageHash)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/projects/{approvalProjectId:D}/accepted-approvals", new CreateAcceptedApprovalRequest(
            TicketSkeletonRunService.ApprovalTargetKind,
            runId,
            packageHash,
            TicketSkeletonRunService.ContinueCapabilityCode,
            AcceptedApprovalPurposes.WorkflowContinuationInput,
            DateTimeOffset.UtcNow.AddHours(1),
            $"demo1:{runId}",
            $"critic-package:{runId}",
            [$"critic-package:{runId}", $"halt-package:{packageHash}"],
            [
                "Accepted approval record is input evidence only.",
                "Continuation and controlled apply remain separate governed requests."
            ],
            $"demo-seed-client:{runId}"));

        var envelope = await ReadSuccessAsync<AcceptedApprovalApiEnvelope<AcceptedApprovalReadModel>>(response);
        Assert.AreEqual("created", envelope.Status);
        Assert.IsTrue(envelope.MutationOccurred);
        Assert.IsNotNull(envelope.Data);
        return envelope.Data!;
    }

    private static async Task<HttpClient> CreateReviewerClientAsync(int projectId)
    {
        await SeedReviewerAsync(projectId);
        var baseToken = await LoginAsync(DemoReviewerEmail, DemoReviewerPassword);
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task SeedReviewerAsync(int projectId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var hash = BCrypt.Net.BCrypt.HashPassword(DemoReviewerPassword, workFactor: 4);
        await connection.ExecuteAsync("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = @ReviewerUserId)
            BEGIN
                SET IDENTITY_INSERT dbo.Users ON;
                INSERT INTO dbo.Users (Id, Email, DisplayName, PasswordHash, IsActive)
                VALUES (@ReviewerUserId, @ReviewerEmail, @ReviewerDisplayName, @Hash, 1);
                SET IDENTITY_INSERT dbo.Users OFF;
            END
            ELSE
            BEGIN
                UPDATE dbo.Users
                SET Email = @ReviewerEmail,
                    DisplayName = @ReviewerDisplayName,
                    PasswordHash = @Hash,
                    IsActive = 1
                WHERE Id = @ReviewerUserId;
            END

            IF EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE TenantId = @TenantId AND UserId = @ReviewerUserId)
                UPDATE dbo.TenantUsers SET Role = N'Viewer' WHERE TenantId = @TenantId AND UserId = @ReviewerUserId;
            ELSE
                INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (@TenantId, @ReviewerUserId, N'Viewer');

            IF EXISTS (SELECT 1 FROM dbo.ProjectMembers WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND UserId = @ReviewerUserId)
                UPDATE dbo.ProjectMembers
                SET ProjectRole = N'Contributor',
                    Status = N'Active',
                    AddedByUserId = @AdminUserId,
                    AddedUtc = SYSUTCDATETIME(),
                    RemovedByUserId = NULL,
                    RemovedUtc = NULL
                WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND UserId = @ReviewerUserId;
            ELSE
                INSERT INTO dbo.ProjectMembers (TenantId, ProjectId, UserId, ProjectRole, AddedByUserId)
                VALUES (@TenantId, @ProjectId, @ReviewerUserId, N'Contributor', @AdminUserId);
            """, new
        {
            ReviewerUserId = DemoReviewerUserId,
            ReviewerEmail = DemoReviewerEmail,
            ReviewerDisplayName = DemoReviewerDisplayName,
            Hash = hash,
            TenantId = AssignedTenantId,
            ProjectId = projectId,
            AdminUserId = 1
        });
    }

    private static async Task AuthenticateAsync(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = AdminEmail, password = AdminPassword });
        var loginJson = await ReadSuccessAsync<JsonElement>(login);
        var baseToken = loginJson.GetProperty("token").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(baseToken));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tenants/select");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", baseToken);
        request.Content = JsonContent.Create(new { tenantId = AssignedTenantId });

        var select = await client.SendAsync(request);
        var selectJson = await ReadSuccessAsync<JsonElement>(select);
        var tenantToken = selectJson.GetProperty("token").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(tenantToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
    }

    private static async Task AssertBaselineSqlPersistenceAsync(
        int projectId,
        long appliedTicketId,
        string appliedRunId,
        Guid acceptedApprovalId,
        string appliedPackageHash,
        long pausedTicketId,
        string pausedRunId,
        string pausedPackageHash)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var appliedState = await connection.ExecuteScalarAsync<string>(
            "SELECT State FROM dbo.Runs WHERE RunId = @RunId AND ProjectId = @ProjectId AND TicketId = @TicketId",
            new { RunId = appliedRunId, ProjectId = projectId, TicketId = appliedTicketId });
        Assert.AreEqual("Applied", appliedState);

        var pausedState = await connection.ExecuteScalarAsync<string>(
            "SELECT State FROM dbo.Runs WHERE RunId = @RunId AND ProjectId = @ProjectId AND TicketId = @TicketId",
            new { RunId = pausedRunId, ProjectId = projectId, TicketId = pausedTicketId });
        Assert.AreEqual("PausedForApproval", pausedState);

        var approvalCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM governance.AcceptedApproval
            WHERE AcceptedApprovalId = @AcceptedApprovalId
              AND ApprovalTargetId = @RunId
              AND ApprovalTargetHash = @PackageHash;
            """,
            new { AcceptedApprovalId = acceptedApprovalId, RunId = appliedRunId, PackageHash = appliedPackageHash });
        Assert.AreEqual(1, approvalCount);

        var pausedApprovalCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM governance.AcceptedApproval
            WHERE ApprovalTargetId = @RunId
              OR ApprovalTargetHash = @PackageHash;
            """,
            new { RunId = pausedRunId, PackageHash = pausedPackageHash });
        Assert.AreEqual(0, pausedApprovalCount, "PausedForApproval baseline must not silently create accepted approval.");

        var pausedEvents = (await connection.QueryAsync<string>(
            "SELECT EventType FROM dbo.RunEvents WHERE RunId = @RunId ORDER BY TimestampUtc, Id",
            new { RunId = pausedRunId })).ToArray();
        CollectionAssert.Contains(pausedEvents, "ApprovalRequiredHalt");
        CollectionAssert.DoesNotContain(pausedEvents, "SkeletonContinuationUnblocked");
        CollectionAssert.DoesNotContain(pausedEvents, "SkeletonApplyStarted");
        CollectionAssert.DoesNotContain(pausedEvents, "SkeletonApplied");
    }

    private static async Task AssertChatTicketSqlPersistenceAsync(
        int projectId,
        long ticketId,
        long sessionId,
        long messageId,
        string runId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var chatMessageCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.ChatMessages
            WHERE Id = @MessageId
              AND ChatSessionId = @SessionId
              AND ProjectId = @ProjectId;
            """,
            new { MessageId = messageId, SessionId = sessionId, ProjectId = projectId });
        Assert.AreEqual(1, chatMessageCount);

        var sourceTypes = (await connection.QueryAsync<string>(
            """
            SELECT SourceType
            FROM dbo.ArtifactSourceReferences
            WHERE ProjectId = @ProjectId
              AND ArtifactType = 'Ticket'
              AND ArtifactId = @TicketId
            ORDER BY SourceType;
            """,
            new { ProjectId = projectId, TicketId = ticketId })).ToArray();
        CollectionAssert.Contains(sourceTypes, "ChatSession");
        CollectionAssert.Contains(sourceTypes, "ChatMessage");

        var runState = await connection.ExecuteScalarAsync<string>(
            "SELECT State FROM dbo.Runs WHERE RunId = @RunId AND ProjectId = @ProjectId AND TicketId = @TicketId",
            new { RunId = runId, ProjectId = projectId, TicketId = ticketId });
        Assert.AreEqual("PausedForApproval", runState);
    }

    private static async Task<int> CountRowsAsync(string sql, object args)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, args);
    }

    private static string SampleRoot() => Path.Combine(RepositoryRoot(), "Samples", "BookSeller");

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
        RunTool(path, "git", "config user.email demo-seed@irondev.local");
        RunTool(path, "git", "config user.name DemoSeed");
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
            // Best effort cleanup for local smoke temp folders.
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

    private static string CurrentCommitSha()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = RepositoryRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static void WriteDemoReceipt(DemoSeedReceipt receipt)
    {
        var path = Environment.GetEnvironmentVariable("DEMO_SEED_RECEIPT");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Path.GetTempPath(), "IronDev", "demo-seed", "demo-seed-receipt.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"DEMO_SEED_RECEIPT_PATH::{path}");
    }

    private static string CopyDemoArtifact(string sourcePath, string fileName)
    {
        var receiptPath = Environment.GetEnvironmentVariable("DEMO_SEED_RECEIPT");
        var outputDirectory = string.IsNullOrWhiteSpace(receiptPath)
            ? Path.Combine(Path.GetTempPath(), "IronDev", "demo-seed")
            : Path.GetDirectoryName(receiptPath)!;

        Directory.CreateDirectory(outputDirectory);
        var targetPath = Path.Combine(outputDirectory, fileName);
        File.Copy(sourcePath, targetPath, overwrite: true);
        return targetPath;
    }

    private static string RedactPath(string value)
    {
        var result = value;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            result = result.Replace(home, "<user-home>", StringComparison.OrdinalIgnoreCase);
        var temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(temp))
            result = result.Replace(temp, "<temp>", StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class DemoSeedBuilder(ConcurrentDictionary<long, string> ticketKinds) : IBuilderProposalService
    {
        public Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken ct = default)
        {
            var key = ticketKinds.TryGetValue(ticketId, out var kind) ? kind : ValidateBookKey;
            return Task.FromResult(key switch
            {
                SearchByAuthorKey => SearchProposal(ticketId),
                BulkDiscountKey => BulkDiscountProposal(ticketId),
                _ => ValidateProposal(ticketId)
            });
        }

        public Task<BuilderProposal> GenerateProposalFromRequestAsync(int projectId, string request, CancellationToken ct = default) =>
            GenerateProposalAsync(0, ct);

        public Task<BuilderProposal> GenerateRepairProposalAsync(long ticketId, SkeletonRepairContext repair, CancellationToken ct = default) =>
            GenerateProposalAsync(ticketId, ct);

        public Task<BuilderProposal> GenerateRevisionProposalAsync(long ticketId, SkeletonRevisionContext revision, CancellationToken ct = default) =>
            throw new NotSupportedException("DEMO seed proofs do not exercise human-directed revision.");

        public Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default) =>
            throw new NotSupportedException("DEMO seed applies through skeleton-run API endpoints, not direct builder writes.");

        private static BuilderProposal ValidateProposal(long ticketId) => new()
        {
            TicketId = ticketId,
            ProjectId = 0,
            Summary = "Validate Book at construction.",
            Rationale = "Reject empty ISBN/title and negative price so downstream code can trust a Book.",
            ModelProvider = "deterministic-fake",
            ModelName = "demo-validate-book-fixed",
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
        };

        private static BuilderProposal SearchProposal(long ticketId) => new()
        {
            TicketId = ticketId,
            ProjectId = 0,
            Summary = "Add author search to Catalog.",
            Rationale = "Readers need to browse by author while preserving ISBN lookup behavior.",
            ModelProvider = "deterministic-fake",
            ModelName = "demo-search-by-author-fixed",
            Changes =
            [
                new ProposedFileChange
                {
                    FilePath = "src/BookSeller.Domain/Catalog.cs",
                    Description = "Add case-insensitive author search.",
                    IsValid = true,
                    FullContentAfter = CatalogWithAuthorSearch
                }
            ]
        };

        private static BuilderProposal BulkDiscountProposal(long ticketId) => new()
        {
            TicketId = ticketId,
            ProjectId = 0,
            Summary = "Add bulk discount pricing.",
            Rationale = "Bulk orders should receive a deterministic discount while small orders keep flat pricing.",
            ModelProvider = "deterministic-fake",
            ModelName = "demo-bulk-discount-fixed",
            Changes =
            [
                new ProposedFileChange
                {
                    FilePath = "src/BookSeller.Domain/PricingService.cs",
                    Description = "Apply a ten percent discount for ten or more copies.",
                    IsValid = true,
                    FullContentAfter = PricingWithBulkDiscount
                }
            ]
        };
    }

    private sealed class EmptyTestAuthoring : ISkeletonTestAuthoringService
    {
        public Task<SkeletonTestAuthoringResult> AuthorTestsAsync(SkeletonTestAuthoringRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SkeletonTestAuthoringResult
            {
                Succeeded = true,
                Tests = [],
                ModelProvider = "deterministic-fake",
                ModelName = "demo-no-authored-tests"
            });
    }

    private sealed class DeterministicCleanCriticReviewService(IRunEventStore events) : ISkeletonCriticReviewService
    {
        public async Task<SkeletonCriticReviewOutcome?> ReviewAsync(SkeletonCriticReviewRequest request, CancellationToken cancellationToken = default)
        {
            var runEvents = await events.GetEventsAsync(request.RunId, cancellationToken);
            var package = runEvents.LastOrDefault(e => e.EventType == "CriticReviewPackageReady");
            if (package is null || !package.Payload.TryGetValue("packageSha256", out var packageHash))
            {
                return new SkeletonCriticReviewOutcome
                {
                    Succeeded = false,
                    FailureReason = "Critic package evidence is missing."
                };
            }

            var reviewId = $"demo-review-{request.RunId}";
            await events.PublishAsync(new RunEventDto
            {
                RunId = request.RunId,
                EventType = "SkeletonCriticReviewRecorded",
                Message = "Deterministic DEMO critic review recorded no findings. A critic review is not approval.",
                Payload = new Dictionary<string, string>
                {
                    ["criticAgentRunId"] = $"demo-critic-{request.RunId}",
                    ["reviewId"] = reviewId,
                    ["verdict"] = "NoFindings",
                    ["findingCount"] = "0",
                    ["blockingFindingCount"] = "0",
                    ["findingIds"] = string.Empty,
                    ["packageSha256"] = packageHash,
                    ["groundTruthCheckCount"] = "1",
                    ["groundTruthMismatchCount"] = "0",
                    ["modelProvider"] = "deterministic-fake",
                    ["modelName"] = "demo-critic-clean-fixed",
                    ["requestedByUserId"] = request.RequestedByUserId
                }
            }, cancellationToken);

            return new SkeletonCriticReviewOutcome
            {
                Succeeded = true,
                CriticAgentRunId = $"demo-critic-{request.RunId}",
                ReviewId = reviewId,
                Verdict = "NoFindings",
                GroundTruth = new SkeletonGroundTruthVerification
                {
                    Checks =
                    [
                        new SkeletonGroundTruthCheck
                        {
                            CheckName = SkeletonGroundTruthCheckNames.PackageHash,
                            Passed = true,
                            Expected = packageHash,
                            Actual = packageHash,
                            Detail = "Deterministic DEMO review accepted the persisted package hash.",
                            BlocksMerge = false
                        }
                    ]
                },
                Findings = []
            };
        }
    }

    /// <summary>
    /// The hero critic: deterministic review that records exactly one advisory
    /// (non-blocking) finding — the first fixture critic to give the disposition
    /// gate real work. A finding is not a veto; a review is not approval.
    /// </summary>
    private sealed class DemoAdvisoryCriticReviewService(IRunEventStore events) : ISkeletonCriticReviewService
    {
        public async Task<SkeletonCriticReviewOutcome?> ReviewAsync(SkeletonCriticReviewRequest request, CancellationToken cancellationToken = default)
        {
            var runEvents = await events.GetEventsAsync(request.RunId, cancellationToken);
            var package = runEvents.LastOrDefault(e => e.EventType == "CriticReviewPackageReady");
            if (package is null || !package.Payload.TryGetValue("packageSha256", out var packageHash))
            {
                return new SkeletonCriticReviewOutcome
                {
                    Succeeded = false,
                    FailureReason = "Critic package evidence is missing."
                };
            }

            var reviewId = $"hero-review-{request.RunId}";
            await events.PublishAsync(new RunEventDto
            {
                RunId = request.RunId,
                EventType = "SkeletonCriticReviewRecorded",
                Message = "Deterministic HERO critic review recorded one advisory finding. A finding is not a veto, and a review is not approval — the finding must be dispositioned before continuation.",
                Payload = new Dictionary<string, string>
                {
                    ["criticAgentRunId"] = $"hero-critic-{request.RunId}",
                    ["reviewId"] = reviewId,
                    ["verdict"] = "NoObjection",
                    ["findingCount"] = "1",
                    ["blockingFindingCount"] = "0",
                    ["findingIds"] = HeroFindingId,
                    ["packageSha256"] = packageHash,
                    ["groundTruthCheckCount"] = "1",
                    ["groundTruthMismatchCount"] = "0",
                    ["modelProvider"] = "deterministic-fake",
                    ["modelName"] = "hero-critic-advisory-fixed",
                    ["requestedByUserId"] = request.RequestedByUserId
                }
            }, cancellationToken);

            return new SkeletonCriticReviewOutcome
            {
                Succeeded = true,
                CriticAgentRunId = $"hero-critic-{request.RunId}",
                ReviewId = reviewId,
                Verdict = "NoObjection",
                GroundTruth = new SkeletonGroundTruthVerification
                {
                    Checks =
                    [
                        new SkeletonGroundTruthCheck
                        {
                            CheckName = SkeletonGroundTruthCheckNames.PackageHash,
                            Passed = true,
                            Expected = packageHash,
                            Actual = packageHash,
                            Detail = "Deterministic HERO review accepted the persisted package hash.",
                            BlocksMerge = false
                        }
                    ]
                },
                Findings =
                [
                    new SkeletonCriticReviewFindingDto
                    {
                        FindingId = HeroFindingId,
                        Severity = "Minor",
                        Title = "Flat and discounted branches round inconsistently",
                        Problem = "The discounted branch rounds to 2 decimal places away from zero; the flat branch returns book.Price * quantity unrounded. A price with more than 2 decimal places rounds inconsistently across the bulk threshold.",
                        WhyItMatters = "Money paths that round differently by branch invite reconciliation drift exactly at the discount boundary a buyer will scrutinise.",
                        RequiredFix = "None required if acceptance criterion 2 (fewer than 10 copies priced flat, exactly as before) intends the flat path unrounded; otherwise round both branches identically.",
                        BlocksMerge = false
                    }
                ]
            };
        }
    }

    private sealed class DemoFormalizationChatResponseService : IProjectChatResponseService
    {
        public Task<ProjectChatResponseResult?> RespondAsync(
            int projectId,
            string prompt,
            MemoryRetrievalRequestContext memoryRetrievalContext,
            ChatGovernanceMode? explicitMode = null,
            string? dogfoodTraceId = null,
            string? recentConversationSummary = null,
            long? sessionId = null,
            long? sourceMessageId = null,
            CancellationToken cancellationToken = default)
        {
            var decision = new ChatModeDecision(
                ChatGovernanceMode.Formalization,
                0.99,
                "DEMO-2 deterministic chat response treats the user turn as ticket formalization evidence only.");

            return Task.FromResult<ProjectChatResponseResult?>(new ProjectChatResponseResult(
                Response: "Draft a ticket for bulk discount validation. This response is not approval.",
                Mode: ChatGovernanceMode.Formalization.ToString(),
                ModeConfidence: decision.Confidence,
                ModeReason: decision.Reason,
                Clarification: ChatClarificationState.None,
                Gate: ChatGovernanceGate.FromDecision(decision),
                ContextSummary: recentConversationSummary,
                LinkedFilePaths: "src/BookSeller.Domain/PricingService.cs",
                LinkedSymbols: "PricingService",
                DogfoodTraceId: dogfoodTraceId,
                TraceId: 1));
        }
    }

    private sealed class DemoDraftTicketService : IDraftTicketService
    {
        public Task<DraftTicket> GenerateDraftAsync(
            int projectId,
            string projectName,
            string proposedTitle,
            string messageText,
            string? linkedFilePaths,
            string? linkedSymbols,
            long? sessionId = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(new DraftTicket
            {
                SourceChatSessionId = sessionId.GetValueOrDefault(),
                SourceMessageText = messageText,
                Title = string.IsNullOrWhiteSpace(proposedTitle) ? "Bulk orders earn a discount" : proposedTitle,
                TicketType = "Feature",
                Priority = "Medium",
                Status = "Draft",
                Summary = "Bulk order pricing should validate the discount rule before a governed run starts.",
                Background = "The live demo chat turn asks IronDev to shape a work item from messy user intent.",
                AcceptanceCriteria = string.Join(Environment.NewLine, new[]
                {
                    "- Bulk order discount criteria are explicit.",
                    "- Quantity validation remains backend-owned work.",
                    "- Existing pricing behavior is preserved where the discount does not apply."
                }),
                LinkedFilePaths = linkedFilePaths,
                LinkedSymbols = linkedSymbols,
                ImplementationPlan = "Shape the pricing rule before starting the governed run.",
                UnitTests = "Add pricing tests before any apply request.",
                IntegrationTests = "Run the BookSeller sample tests in the disposable workspace.",
                ManualTests = "Review the generated diff before approval.",
                RegressionTests = "Ensure existing flat-price behavior remains intact.",
                BuildValidation = "dotnet test BookSeller.slnx",
                IsGenerated = true,
                GenerationNote = "Generated by DEMO-2 deterministic draft service from a persisted chat message. Not approval."
            });
        }

        public Task<DraftTicket> RegenerateTestsAsync(int projectId, DraftTicket current, CancellationToken ct = default) =>
            Task.FromResult(current);

        public Task<DraftTicket> GeneratePlanAsync(int projectId, DraftTicket current, CancellationToken ct = default) =>
            Task.FromResult(current);
    }

    private sealed record FixtureTicket(string Title, string Summary, string AcceptanceCriteria, string TechnicalNotes);
    private sealed record DemoAppliedRun(string RunId, string CriticPackageSha256, string CriticReviewId, Guid AcceptedApprovalId, string ApplyReceiptPath, string ApplyReceiptSha256);
    private sealed record DemoPausedRun(string RunId, string CriticPackageSha256);

    private sealed record DemoSeedReceipt(
        string Command,
        string CommitSha,
        string ModelMode,
        string PersistenceMode,
        string ApiBaseUrlClassification,
        string RootSafetyStatus,
        int ProjectId,
        DemoTicketReceipt AppliedTicket,
        DemoTicketReceipt PausedTicket,
        bool LiveChatTicketSeeded,
        DemoUsabilityProbeReceipt UsabilityProbe,
        string IdempotencyResult,
        string RedactionConfirmation,
        string[] KnownGaps,
        string BoundaryStatement);

    private sealed record DemoUsabilityProbeReceipt(
        bool Proved,
        DemoUsabilityRunReceipt[] Runs,
        string Note);

    private sealed record DemoUsabilityRunReceipt(
        long TicketId,
        string RunId,
        string State,
        string CriticPackageHash,
        bool BuildTestEvidenceVerified);

    private sealed record DemoTicketReceipt(
        string Key,
        long TicketId,
        string RunId,
        string State,
        string CriticPackageHash,
        string CriticReviewId,
        string AcceptedApprovalId,
        string ApprovalTargetHash,
        string ContinuationResult,
        string ApplyReceiptPath,
        string ApplyReceiptSha256,
        string FinalReportReference);

    private sealed class EnvironmentVariableScope(string name, string? previousValue) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previousValue);
    }
}
