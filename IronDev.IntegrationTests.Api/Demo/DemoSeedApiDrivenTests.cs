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
        /// Prices an order line. Bulk orders receive a deterministic demo discount.
        /// </summary>
        public sealed class PricingService
        {
            public decimal PriceFor(Book book, int quantity)
            {
                var subtotal = book.Price * quantity;
                return quantity >= 10 ? subtotal * 0.9m : subtotal;
            }
        }
        """;

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
                });
            });

            using var client = factory.CreateClient();
            await AuthenticateAsync(client);

            var project = await CreateProjectAsync(client, sampleCopy);
            var validateTicket = await CreateFixtureTicketAsync(client, project.Id, ValidateBookKey);
            var searchTicket = await CreateFixtureTicketAsync(client, project.Id, SearchByAuthorKey);
            ticketKinds[validateTicket.Id] = ValidateBookKey;
            ticketKinds[searchTicket.Id] = SearchByAuthorKey;

            var applied = await DriveAppliedAsync(client, project.Id, validateTicket.Id);
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
                IdempotencyResult: "Fixture keys resolve to one intended baseline ticket each in this proof run.",
                RedactionConfirmation: "Secret, token, connection-string, and user-local path values are not emitted raw.",
                KnownGaps:
                [
                    "DEMO-1 proves API/SQL baseline history through the integration host, not a long-lived already-running API process.",
                    "DEMO-2 UI click-path proof is covered by the flow-shell contract and a separate API visible/startable test."
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
                });
            });

            using var client = factory.CreateClient();
            await AuthenticateAsync(client);

            var project = await CreateProjectAsync(
                client,
                sampleCopy,
                "BookSeller DEMO-2",
                "DEMO-2 chat to visible confirmed ticket fixture.");

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

    private static async Task<DemoAppliedRun> DriveAppliedAsync(HttpClient client, int projectId, long ticketId)
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
        var approval = await CreateAcceptedApprovalAsync(client, approvalProjectId, started.RunId, packageHash);

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

    private sealed class DemoFormalizationChatResponseService : IProjectChatResponseService
    {
        public Task<ProjectChatResponseResult?> RespondAsync(
            int projectId,
            string prompt,
            ChatGovernanceMode? explicitMode = null,
            string? dogfoodTraceId = null,
            string? recentConversationSummary = null,
            long? sessionId = null,
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
        string IdempotencyResult,
        string RedactionConfirmation,
        string[] KnownGaps,
        string BoundaryStatement);

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
