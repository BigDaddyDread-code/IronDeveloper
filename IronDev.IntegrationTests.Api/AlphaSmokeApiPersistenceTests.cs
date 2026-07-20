using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IronDev.Api.Controllers;
using Dapper;
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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
[TestCategory("AlphaSmoke")]
[TestCategory("ReleaseReadiness")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
public sealed class AlphaSmokeApiPersistenceTests : ApiTestBase
{
    private const string TicketKey = "validate-book";
    private const int ReviewerUserId = 2;
    private const string ReviewerEmail = "rel3.reviewer@irondev.local";
    private const string ReviewerPassword = "reviewer-password123";
    private const string ReviewerDisplayName = "REL-3 Reviewer";

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
    public async Task Rel3_OneTicket_ReachesApplied_ThroughSqlBackedApi()
    {
        var workspaceParent = TempDir("irondev-rel3-ws");
        var evidenceRoot = TempDir("irondev-rel3-ev");
        var sampleCopy = TempDir("irondev-rel3-src");
        var unavailableSample = sampleCopy + "-temporarily-unavailable";

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
                    services.AddScoped<IBuilderProposalService, DeterministicRel3Builder>();
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
                    "rel3-single-project-apply-capability-v1"),
                sampleCopy,
                Path.GetDirectoryName(sampleCopy)!);
            expectedRunReadiness.ExpectProject(project.Id);
            using var reviewerClient = await CreateReviewerClientAsync(project.Id);
            var ticket = await CreateTicketAsync(client, project.Id);

            var started = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs",
                content: null);
            Assert.AreEqual("PausedForApproval", started.Status);
            Assert.IsTrue(started.RequiresHumanApproval);

            var haltedReport = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            Assert.AreEqual("PausedForApproval", haltedReport.Status);
            Assert.IsNotNull(haltedReport.CriticPackage);
            Assert.IsTrue(haltedReport.CriticPackage!.HashVerified);
            Assert.IsNotNull(haltedReport.Approval);
            Assert.IsTrue(haltedReport.Approval!.HaltObserved);
            Assert.IsFalse(haltedReport.Approval.ContinuationUnblocked);

            var packageHash = haltedReport.Approval.TargetHash;
            Assert.IsFalse(string.IsNullOrWhiteSpace(packageHash));
            Assert.AreEqual(packageHash, haltedReport.CriticPackage.Sha256OnDisk);

            var criticReview = await PostJsonAsync<SkeletonCriticReviewOutcome>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/critic-review",
                content: null);
            Assert.IsTrue(criticReview.Succeeded);
            Assert.AreEqual("NoFindings", criticReview.Verdict);

            var approvalProjectId = TicketSkeletonRunService.ApprovalProjectGuid(project.Id);
            var approval = await CreateAcceptedApprovalAsync(
                reviewerClient,
                approvalProjectId,
                started.RunId,
                packageHash);
            Assert.AreEqual(started.RunId, approval.ApprovalTargetId);
            Assert.AreEqual(packageHash, approval.ApprovalTargetHash);

            var approvalReadback = await GetJsonAsync<AcceptedApprovalApiEnvelope<IReadOnlyList<AcceptedApprovalReadModel>>>(
                client,
                $"/api/v1/projects/{approvalProjectId:D}/accepted-approvals/by-target/{TicketSkeletonRunService.ApprovalTargetKind}/{started.RunId}");
            Assert.AreEqual("found", approvalReadback.Status);
            Assert.IsTrue(approvalReadback.Data!.Any(record => record.AcceptedApprovalId == approval.AcceptedApprovalId));

            var continued = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/continue",
                content: null);
            Assert.AreEqual("Completed", continued.Status);
            Assert.IsFalse(continued.RequiresHumanApproval);

            Directory.Move(sampleCopy, unavailableSample);
            TicketBuildRunDto failedApply;
            try
            {
                failedApply = await PostJsonAsync<TicketBuildRunDto>(
                    client,
                    $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/apply",
                    content: null);
            }
            finally
            {
                Directory.Move(unavailableSample, sampleCopy);
            }
            Assert.AreEqual("Completed", failedApply.Status);

            var failedApplyReport = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            Assert.AreEqual(1, failedApplyReport.Apply!.Attempts.Count);
            Assert.AreEqual(SkeletonApplyAttemptStatuses.Failed, failedApplyReport.Apply.Attempts[0].Status);
            Assert.AreEqual(SkeletonApplyMutationStates.NotObserved, failedApplyReport.Apply.Attempts[0].MutationState);
            CollectionAssert.Contains(failedApplyReport.Apply.Attempts[0].AvailableActions.ToList(), SkeletonApplyRecoveryActions.Retry);

            var applied = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/apply-recovery",
                new { action = SkeletonApplyRecoveryActions.Retry, reason = "Source fixture is available again; retry in a fresh preserved attempt." });
            Assert.AreEqual("Applied", applied.Status);

            var finalStatus = await GetJsonAsync<RunStatusDto>(client, $"/api/runs/{started.RunId}");
            Assert.AreEqual("Applied", finalStatus.Status);

            var finalReport = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            Assert.AreEqual("Applied", finalReport.Status);
            Assert.IsTrue(finalReport.LoopComplete, $"Final report gaps: {string.Join(" | ", finalReport.Gaps)}");
            Assert.AreEqual(approval.AcceptedApprovalId.ToString("D"), finalReport.Approval!.AcceptedApprovalId);
            Assert.AreEqual(ReviewerUserId.ToString(), finalReport.Approval.ApprovedByActorId);
            Assert.AreEqual(ReviewerDisplayName, finalReport.Approval.ApprovedByActorDisplayName);
            Assert.AreEqual("1", finalReport.Approval.ContinuationRequestedByUserId);
            Assert.IsTrue(finalReport.Apply!.Applied);
            Assert.IsTrue(finalReport.Apply.Receipts.All(receipt => receipt.ExistsOnDisk));
            Assert.AreEqual(2, finalReport.Apply.Attempts.Count);
            Assert.AreEqual($"{started.RunId}-apply-001", finalReport.Apply.Attempts[0].AttemptId);
            Assert.AreEqual(SkeletonApplyAttemptStatuses.Failed, finalReport.Apply.Attempts[0].Status);
            Assert.AreNotEqual("unknown-user", finalReport.Apply.Attempts[0].RequestedByUserId);
            Assert.AreEqual($"{started.RunId}-apply-002", finalReport.Apply.Attempts[1].AttemptId);
            Assert.AreEqual(SkeletonApplyAttemptStatuses.Applied, finalReport.Apply.Attempts[1].Status);
            Assert.AreEqual(SkeletonApplyRecoveryActions.Retry, finalReport.Apply.Attempts[1].RequestedAction);
            Assert.AreNotEqual("unknown-user", finalReport.Apply.Attempts[1].RequestedByUserId);
            StringAssert.Contains(finalReport.Apply.WorkspacePath, finalReport.Apply.Attempts[1].AttemptId);

            var unsafeRetry = await client.PostAsJsonAsync(
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/apply-recovery",
                new { action = SkeletonApplyRecoveryActions.Retry, reason = "Attempting to retry an already applied run must fail." });
            Assert.AreEqual(HttpStatusCode.Conflict, unsafeRetry.StatusCode);
            var unsafeRetryBody = await unsafeRetry.Content.ReadFromJsonAsync<JsonElement>();
            Assert.AreEqual("ApplyRecoveryRefused", unsafeRetryBody.GetProperty("code").GetString());

            await AssertSqlPersistenceAsync(started.RunId, project.Id, ticket.Id, approval.AcceptedApprovalId, packageHash);

            var applyReceipt = finalReport.Apply.Receipts.Single(receipt => receipt.Name == "apply-copy.json");
            var persistedApplyReceiptPath = CopySmokeArtifact(applyReceipt.Path, "rel3-apply-copy.json");
            var applyReceiptHash = ComputeSha256(await File.ReadAllBytesAsync(persistedApplyReceiptPath));

            var landedPath = Path.Combine(sampleCopy, "src", "BookSeller.Domain", "Book.cs");
            Assert.AreEqual(
                ValidatedBook.Replace("\r\n", "\n", StringComparison.Ordinal),
                (await File.ReadAllTextAsync(landedPath)).Replace("\r\n", "\n", StringComparison.Ordinal),
                "The API-persisted applied path must land the approved Book change in the disposable source copy.");
            Assert.IsFalse(Directory.Exists(Path.Combine(sampleCopy, ".irondev")),
                "Apply evidence must stay in the workspace, not the source repo.");

            WriteReceipt(new Rel3AlphaSmokeReceipt(
                Ticket: TicketKey,
                Project: "BookSeller",
                ModelMode: "Deterministic",
                RunUntil: "Applied",
                RunId: started.RunId,
                ApiPersisted: true,
                SqlPersisted: true,
                ProjectId: project.Id,
                TicketId: ticket.Id,
                GateState: "PausedForApproval",
                BuildAndTestSucceeded: true,
                CriticPackageSha256: packageHash,
                ApprovalTargetHash: packageHash,
                BuilderModel: "deterministic-fake",
                AcceptedApprovalCreated: true,
                AcceptedApprovalRecorded: true,
                ContinuationRequested: true,
                ApplyRequested: true,
                CriticReviewRecorded: true,
                AcceptedApprovalId: approval.AcceptedApprovalId.ToString("D"),
                ApplyReceiptPath: persistedApplyReceiptPath,
                ApplyReceiptSha256: applyReceiptHash,
                FinalState: "Applied",
                LoopComplete: finalReport.LoopComplete,
                ReportReconstructable: finalReport.Gaps.Count == 0,
                Proves:
                [
                    "authenticated API creates project and ticket",
                    "authenticated API starts the skeleton run and reconstructs the halt report",
                    "authenticated API records critic review evidence",
                    "accepted approval is created through the accepted-approval API and read back from SQL",
                    "continuation consumes live SQL-backed accepted approval evidence",
                    "controlled apply reaches Applied through the API",
                    "SQL contains the run, event trail, and accepted approval rows"
                ],
                DoesNotProveYet:
                [
                    "a live model",
                    "product UI approval recording",
                    "fresh-machine dogfood from clone",
                    "commit, push, release, or deployment"
                ]));
        }
        finally
        {
            TryDelete(sampleCopy);
            TryDelete(unavailableSample);
            TryDelete(workspaceParent);
            TryDelete(evidenceRoot);
        }
    }

    [TestMethod]
    public async Task Rel5_ChatConfirmedTicket_StartsGovernedRun_ThroughSqlBackedApi()
    {
        var workspaceParent = TempDir("irondev-rel5-ws");
        var evidenceRoot = TempDir("irondev-rel5-ev");
        var sampleCopy = TempDir("irondev-rel5-src");
        const string userMessage = "Turn this into a ticket: validate the Book constructor so empty ISBN, empty title, and negative price are rejected.";

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
                builder.UseSetting("SkeletonApply:Enabled", "true");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IBuilderProposalService>();
                    services.AddScoped<IBuilderProposalService, DeterministicRel3Builder>();
                    services.RemoveAll<ISkeletonTestAuthoringService>();
                    services.AddScoped<ISkeletonTestAuthoringService, EmptyTestAuthoring>();
                    services.RemoveAll<ISkeletonCriticReviewService>();
                    services.AddScoped<ISkeletonCriticReviewService, DeterministicCleanCriticReviewService>();
                    services.RemoveAll<IProjectChatResponseService>();
                    services.AddScoped<IProjectChatResponseService, DeterministicFormalizationChatResponseService>();
                    services.RemoveAll<IDraftTicketService>();
                    services.AddScoped<IDraftTicketService, DeterministicRel5DraftTicketService>();
                    services.RemoveAll<IProjectRunReadinessService>();
                    services.AddSingleton<IProjectRunReadinessService>(expectedRunReadiness);
                });
            });

            using var client = factory.CreateClient();
            await AuthenticateAsync(client);

            var project = await CreateProjectAsync(
                client,
                sampleCopy,
                "BookSeller REL-5",
                "REL-5 chat to confirmed ticket to governed run smoke fixture.");
            expectedRunReadiness.ExpectProject(project.Id);

            var sessionId = await PostJsonAsync<long>(
                client,
                $"/api/projects/{project.Id}/chat/sessions",
                new ProjectChatSession
                {
                    ProjectId = project.Id,
                    Title = "REL-5 chat ticket smoke"
                });
            Assert.IsTrue(sessionId > 0);

            var messageId = await PostJsonAsync<long>(
                client,
                $"/api/projects/{project.Id}/chat/sessions/{sessionId}/messages",
                new ChatMessage
                {
                    ProjectId = project.Id,
                    ChatSessionId = sessionId,
                    Role = "user",
                    Message = userMessage,
                    LinkedFilePaths = "src/BookSeller.Domain/Book.cs",
                    LinkedSymbols = "Book"
                });
            Assert.IsTrue(messageId > 0);

            var completion = await PostJsonAsync<ChatController.ChatCompletionResponse>(
                client,
                $"/api/projects/{project.Id}/chat/complete",
                new ChatController.ChatCompletionRequest(project.Id, sessionId, userMessage, null, "projectQuestion"));
            Assert.AreEqual("Formalization", completion.Mode);
            Assert.IsNotNull(completion.Gate);
            Assert.IsTrue(completion.Gate!.CanCreateTicket);

            var draft = await PostJsonAsync<DraftTicket>(
                client,
                $"/api/projects/{project.Id}/tickets/draft",
                new TicketsController.DraftTicketRequest(
                    "BookSeller",
                    "Reject invalid books at the door",
                    userMessage,
                    "src/BookSeller.Domain/Book.cs",
                    "Book",
                    sessionId,
                    messageId));
            Assert.AreEqual(sessionId, draft.SourceChatSessionId);
            Assert.AreEqual(messageId, draft.SourceMessageId);
            Assert.AreEqual(userMessage, draft.SourceMessageText);

            var ticket = await PostJsonAsync<ProjectTicket>(
                client,
                $"/api/projects/{project.Id}/tickets/draft/confirm",
                draft);
            Assert.AreEqual(project.Id, ticket.ProjectId);
            Assert.AreEqual(sessionId, ticket.SourceChatSessionId);
            Assert.AreEqual(messageId, ticket.SourceChatMessageId);
            Assert.IsTrue(ticket.IsGenerated);
            StringAssert.Contains(ticket.Content, "Source chat excerpt");

            var started = await PostJsonAsync<TicketBuildRunDto>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs",
                content: null);
            Assert.AreEqual("PausedForApproval", started.Status);
            Assert.IsTrue(started.RequiresHumanApproval);

            var haltedReport = await GetJsonAsync<SkeletonRunReport>(
                client,
                $"/api/projects/{project.Id}/tickets/{ticket.Id}/skeleton-runs/{started.RunId}/report");
            Assert.AreEqual("PausedForApproval", haltedReport.Status);
            Assert.IsFalse(haltedReport.LoopComplete);
            Assert.IsNotNull(haltedReport.CriticPackage);
            Assert.IsTrue(haltedReport.CriticPackage!.HashVerified);
            Assert.IsNotNull(haltedReport.Approval);
            Assert.IsTrue(haltedReport.Approval!.HaltObserved);
            Assert.IsFalse(haltedReport.Approval.ContinuationUnblocked);
            Assert.IsNull(haltedReport.Apply);

            var packageHash = haltedReport.Approval.TargetHash;
            Assert.IsFalse(string.IsNullOrWhiteSpace(packageHash));
            Assert.AreEqual(packageHash, haltedReport.CriticPackage.Sha256OnDisk);

            await AssertRel5SqlPersistenceAsync(started.RunId, project.Id, ticket.Id, sessionId, messageId);

            WriteReceipt(new Rel5AlphaSmokeReceipt(
                Ticket: TicketKey,
                Project: "BookSeller",
                ModelMode: "Deterministic",
                RunUntil: "Gate",
                RunId: started.RunId,
                ApiPersisted: true,
                SqlPersisted: true,
                ProjectId: project.Id,
                TicketId: ticket.Id,
                ChatSessionId: sessionId,
                ChatMessageId: messageId,
                DraftConfirmed: true,
                ChatTurnPersisted: true,
                SourceMessageLinked: true,
                GateState: "PausedForApproval",
                BuildAndTestSucceeded: true,
                CriticPackageSha256: packageHash,
                ApprovalTargetHash: packageHash,
                BuilderModel: "deterministic-fake",
                AcceptedApprovalCreated: false,
                AcceptedApprovalRecorded: false,
                ContinuationRequested: false,
                ApplyRequested: false,
                CriticReviewRecorded: false,
                AcceptedApprovalId: string.Empty,
                ApplyReceiptPath: string.Empty,
                ApplyReceiptSha256: string.Empty,
                FinalState: "PausedForApproval",
                LoopComplete: false,
                ReportReconstructable: haltedReport.Gaps.Count == 0,
                Proves:
                [
                    "authenticated API persists a chat session and user message",
                    "chat complete can classify the turn as Formalization with ticket creation available",
                    "draft ticket confirmation persists a generated ticket with chat provenance",
                    "authenticated API starts the existing governed skeleton run from that confirmed ticket",
                    "run report reconstructs the critic package and approval halt",
                    "SQL contains chat, ticket provenance, run, and event evidence"
                ],
                DoesNotProveYet:
                [
                    "a live model",
                    "accepted approval creation or consumption",
                    "continuation after approval",
                    "controlled apply",
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

    [TestMethod]
    public async Task ConfirmDraft_WithValidChatProvenance_PersistsReferences()
    {
        using var client = Factory.CreateClient();
        await AuthenticateAsync(client);

        var project = await CreateProjectAsync(
            client,
            Path.GetTempPath(),
            "REL-5 valid draft provenance",
            "Focused draft-confirm provenance regression fixture.");
        var provenance = await CreateChatProvenanceAsync(client, project.Id);
        var draft = CreateRel5Draft(provenance.SessionId, provenance.MessageId);

        var ticket = await PostJsonAsync<ProjectTicket>(
            client,
            $"/api/projects/{project.Id}/tickets/draft/confirm",
            draft);

        Assert.AreEqual("Draft", ticket.Status);
        Assert.AreEqual(provenance.SessionId, ticket.SourceChatSessionId);
        Assert.AreEqual(provenance.MessageId, ticket.SourceChatMessageId);
        StringAssert.Contains(ticket.Content, provenance.MessageText);
        await AssertConfirmedTicketReferencesAsync(project.Id, ticket.Id, provenance.SessionId, provenance.MessageId);
    }

    [TestMethod]
    public async Task ConfirmDraft_IgnoresOrRejectsAuthorityShapedClientStatus()
    {
        using var client = Factory.CreateClient();
        await AuthenticateAsync(client);

        var project = await CreateProjectAsync(
            client,
            Path.GetTempPath(),
            "REL-5 status trust boundary",
            "Focused draft-confirm status regression fixture.");
        var provenance = await CreateChatProvenanceAsync(client, project.Id);
        var draft = CreateRel5Draft(provenance.SessionId, provenance.MessageId);
        draft.Status = "Applied";

        var ticket = await PostJsonAsync<ProjectTicket>(
            client,
            $"/api/projects/{project.Id}/tickets/draft/confirm",
            draft);

        Assert.AreEqual("Draft", ticket.Status, "Draft confirmation must use a backend-owned non-authority status.");

        await using var connection = new SqlConnection(ConnectionString);
        var persistedStatus = await connection.ExecuteScalarAsync<string>(
            "SELECT Status FROM dbo.ProjectTickets WHERE Id = @TicketId AND ProjectId = @ProjectId",
            new { TicketId = ticket.Id, ProjectId = project.Id });
        Assert.AreEqual("Draft", persistedStatus);
    }

    [TestMethod]
    public async Task ConfirmDraft_WithMissingChatSession_Fails()
    {
        using var client = Factory.CreateClient();
        await AuthenticateAsync(client);

        var project = await CreateProjectAsync(
            client,
            Path.GetTempPath(),
            "REL-5 missing chat session",
            "Focused draft-confirm missing-session regression fixture.");

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tickets/draft/confirm",
            CreateRel5Draft(987654321, 987654322));

        await AssertDraftConfirmFailedAsync(response, "ChatSessionMissing");
    }

    [TestMethod]
    public async Task ConfirmDraft_WithMissingChatMessage_Fails()
    {
        using var client = Factory.CreateClient();
        await AuthenticateAsync(client);

        var project = await CreateProjectAsync(
            client,
            Path.GetTempPath(),
            "REL-5 missing chat message",
            "Focused draft-confirm missing-message regression fixture.");
        var provenance = await CreateChatProvenanceAsync(client, project.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tickets/draft/confirm",
            CreateRel5Draft(provenance.SessionId, 987654323));

        await AssertDraftConfirmFailedAsync(response, "ChatMessageMissing");
    }

    [TestMethod]
    public async Task ConfirmDraft_WithMessageFromDifferentSession_Fails()
    {
        using var client = Factory.CreateClient();
        await AuthenticateAsync(client);

        var project = await CreateProjectAsync(
            client,
            Path.GetTempPath(),
            "REL-5 chat session mismatch",
            "Focused draft-confirm session mismatch regression fixture.");
        var first = await CreateChatProvenanceAsync(client, project.Id, "First chat turn.");
        var second = await CreateChatProvenanceAsync(client, project.Id, "Second chat turn.");

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tickets/draft/confirm",
            CreateRel5Draft(first.SessionId, second.MessageId));

        await AssertDraftConfirmFailedAsync(response, "ChatMessageSessionMismatch");
    }

    [TestMethod]
    public async Task ConfirmDraft_WithChatFromDifferentProject_IsConcealedAsMissing()
    {
        using var client = Factory.CreateClient();
        await AuthenticateAsync(client);

        var project = await CreateProjectAsync(
            client,
            Path.GetTempPath(),
            "REL-5 owner project",
            "Focused draft-confirm owner project regression fixture.");
        var otherProject = await CreateProjectAsync(
            client,
            Path.GetTempPath(),
            "REL-5 other project",
            "Focused draft-confirm wrong project regression fixture.");
        var otherProvenance = await CreateChatProvenanceAsync(client, otherProject.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tickets/draft/confirm",
            CreateRel5Draft(otherProvenance.SessionId, otherProvenance.MessageId));

        await AssertDraftConfirmFailedAsync(response, "ChatSessionMissing");
    }

    private static async Task<Project> CreateProjectAsync(
        HttpClient client,
        string localPath,
        string name = "BookSeller REL-3",
        string description = "REL-3 SQL/API persisted alpha smoke fixture.")
    {
        var response = await client.PostAsJsonAsync("/api/projects", new Project
        {
            Name = name,
            Description = description,
            LocalPath = localPath
        });

        return await ReadSuccessAsync<Project>(response);
    }

    private static async Task<ProjectTicket> CreateTicketAsync(HttpClient client, int projectId)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/tickets", new CreateProjectTicketRequest
        {
            Title = "Reject invalid books at the door",
            Type = "Task",
            Priority = "High",
            Summary = "Validate a Book at construction.",
            Problem = "Invalid book data can currently enter the domain model.",
            ProposedChange = "Reject empty ISBN, empty title, and negative price in the Book constructor.",
            AcceptanceCriteria =
            [
                "Empty ISBN is rejected.",
                "Empty title is rejected.",
                "Negative price is rejected.",
                "Zero price is valid."
            ],
            Provenance = new TicketProvenanceDto
            {
                Source = "rel3-alpha-smoke",
                Notes = "Fixture-backed release smoke ticket."
            }
        });

        return await ReadSuccessAsync<ProjectTicket>(response);
    }

    private static async Task<HttpClient> CreateReviewerClientAsync(int projectId)
    {
        await SeedReviewerAsync(projectId);
        var baseToken = await LoginAsync(ReviewerEmail, ReviewerPassword);
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task SeedReviewerAsync(int projectId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var hash = BCrypt.Net.BCrypt.HashPassword(ReviewerPassword, workFactor: 4);
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
            ReviewerUserId,
            ReviewerEmail,
            ReviewerDisplayName,
            Hash = hash,
            TenantId = AssignedTenantId,
            ProjectId = projectId,
            AdminUserId = 1
        });
    }

    private static async Task<ChatProvenance> CreateChatProvenanceAsync(
        HttpClient client,
        int projectId,
        string messageText = "Turn this into a ticket: validate the Book constructor inputs.")
    {
        var sessionId = await PostJsonAsync<long>(
            client,
            $"/api/projects/{projectId}/chat/sessions",
            new ProjectChatSession
            {
                ProjectId = projectId,
                Title = "REL-5 draft-confirm provenance regression"
            });

        var messageId = await PostJsonAsync<long>(
            client,
            $"/api/projects/{projectId}/chat/sessions/{sessionId}/messages",
            new ChatMessage
            {
                ProjectId = projectId,
                ChatSessionId = sessionId,
                Role = "user",
                Message = messageText,
                LinkedFilePaths = "src/BookSeller.Domain/Book.cs",
                LinkedSymbols = "Book"
            });

        return new ChatProvenance(sessionId, messageId, messageText);
    }

    private static DraftTicket CreateRel5Draft(long sessionId, long messageId) =>
        new()
        {
            SourceChatSessionId = sessionId,
            SourceMessageId = messageId,
            SourceMessageText = "client-supplied source text must not replace verified chat message text",
            Title = "Reject invalid books at the door",
            TicketType = "Task",
            Priority = "High",
            Status = "Draft",
            Summary = "Validate a Book at construction before it enters the domain model.",
            Background = "The chat turn asks IronDev to turn Book constructor validation into ticket-shaped work.",
            AcceptanceCriteria = "- Empty ISBN is rejected.",
            LinkedFilePaths = "src/BookSeller.Domain/Book.cs",
            LinkedSymbols = "Book",
            ImplementationPlan = "Update the Book constructor guard clauses.",
            UnitTests = "Add Book constructor validation tests.",
            BuildValidation = "dotnet test BookSeller.slnx",
            IsGenerated = true,
            GenerationNote = "Generated from a persisted chat message. Not approval."
        };

    private static async Task AssertDraftConfirmFailedAsync(HttpResponseMessage response, string expectedReasonCode)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        StringAssert.Contains(text, expectedReasonCode);
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
            $"rel3:{runId}",
            $"critic-package:{runId}",
            [$"critic-package:{runId}", $"halt-package:{packageHash}"],
            [
                "Accepted approval record is input evidence only.",
                "Continuation and controlled apply remain separate governed requests."
            ],
            $"rel3-client:{runId}"));

        var envelope = await ReadSuccessAsync<AcceptedApprovalApiEnvelope<AcceptedApprovalReadModel>>(response);
        Assert.AreEqual("created", envelope.Status);
        Assert.IsTrue(envelope.MutationOccurred);
        Assert.IsNotNull(envelope.Data);
        AssertAcceptedApprovalCreateBoundary(envelope.Boundary);
        return envelope.Data!;
    }

    private static void AssertAcceptedApprovalCreateBoundary(object boundary)
    {
        var json = JsonSerializer.SerializeToElement(boundary, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsFalse(json.GetProperty("acceptedApprovalCreateContinuesWorkflow").GetBoolean());
        Assert.IsFalse(json.GetProperty("acceptedApprovalCreateAppliesSource").GetBoolean());
        Assert.IsFalse(json.GetProperty("acceptedApprovalCreateApprovesRelease").GetBoolean());
        Assert.IsFalse(json.GetProperty("acceptedApprovalCreateAuthorizesExecution").GetBoolean());
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

    private static async Task AssertSqlPersistenceAsync(
        string runId,
        int projectId,
        long ticketId,
        Guid acceptedApprovalId,
        string packageHash)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var run = await connection.QuerySingleAsync<PersistedRunRow>(
            "SELECT RunId, ProjectId, TicketId, State FROM dbo.Runs WHERE RunId = @RunId",
            new { RunId = runId });
        Assert.AreEqual(projectId, run.ProjectId);
        Assert.AreEqual(ticketId, run.TicketId);
        Assert.AreEqual("Applied", run.State);

        var eventTypes = (await connection.QueryAsync<string>(
            "SELECT EventType FROM dbo.RunEvents WHERE RunId = @RunId ORDER BY TimestampUtc, Id",
            new { RunId = runId })).ToArray();
        foreach (var required in new[]
                 {
                     "RunStarted",
                     "ProposalGenerated",
                     "SkeletonEvidencePackaged",
                     "CriticReviewPackageReady",
                     "ApprovalRequiredHalt",
                     "SkeletonCriticReviewRecorded",
                     "SkeletonContinuationUnblocked",
                     "SkeletonApplyAttemptStarted",
                     "SkeletonApplyStarted",
                     "SkeletonApplyPromoted",
                     "SkeletonApplied"
                 })
        {
            CollectionAssert.Contains(eventTypes, required, $"Missing persisted event: {required}");
        }

        var approvalCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM governance.AcceptedApproval
            WHERE AcceptedApprovalId = @AcceptedApprovalId
              AND ApprovalTargetId = @RunId
              AND ApprovalTargetHash = @PackageHash;
            """,
            new { AcceptedApprovalId = acceptedApprovalId, RunId = runId, PackageHash = packageHash });
        Assert.AreEqual(1, approvalCount, "Accepted approval must be persisted and bound to the run/package hash.");
    }

    private static async Task AssertConfirmedTicketReferencesAsync(
        int projectId,
        long ticketId,
        long chatSessionId,
        long chatMessageId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var ticket = await connection.QuerySingleAsync<PersistedTicketProvenanceRow>(
            """
            SELECT SourceChatSessionId, SourceChatMessageId, IsGenerated, Status
            FROM dbo.ProjectTickets
            WHERE Id = @TicketId
              AND ProjectId = @ProjectId;
            """,
            new { TicketId = ticketId, ProjectId = projectId });
        Assert.AreEqual(chatSessionId, ticket.SourceChatSessionId);
        Assert.AreEqual(chatMessageId, ticket.SourceChatMessageId);
        Assert.AreEqual("Draft", ticket.Status);

        var references = (await connection.QueryAsync<ArtifactReferenceRow>(
            """
            SELECT SourceType, SourceId
            FROM dbo.ArtifactSourceReferences
            WHERE ProjectId = @ProjectId
              AND ArtifactType = 'Ticket'
              AND ArtifactId = @TicketId;
            """,
            new { ProjectId = projectId, TicketId = ticketId })).ToArray();

        Assert.IsTrue(
            references.Any(reference => reference.SourceType == "ChatSession" && reference.SourceId == chatSessionId),
            "Ticket source references must include the verified chat session.");
        Assert.IsTrue(
            references.Any(reference => reference.SourceType == "ChatMessage" && reference.SourceId == chatMessageId),
            "Ticket source references must include the verified chat message.");
    }

    private static async Task AssertRel5SqlPersistenceAsync(
        string runId,
        int projectId,
        long ticketId,
        long chatSessionId,
        long chatMessageId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var ticket = await connection.QuerySingleAsync<PersistedTicketProvenanceRow>(
            """
            SELECT SourceChatSessionId, SourceChatMessageId, IsGenerated, Status
            FROM dbo.ProjectTickets
            WHERE Id = @TicketId
              AND ProjectId = @ProjectId;
            """,
            new { TicketId = ticketId, ProjectId = projectId });
        Assert.AreEqual(chatSessionId, ticket.SourceChatSessionId);
        Assert.AreEqual(chatMessageId, ticket.SourceChatMessageId);
        Assert.IsTrue(ticket.IsGenerated);
        Assert.AreEqual("Draft", ticket.Status);

        var chatMessageCount = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM dbo.ChatMessages
            WHERE Id = @ChatMessageId
              AND ChatSessionId = @ChatSessionId
              AND ProjectId = @ProjectId;
            """,
            new { ChatMessageId = chatMessageId, ChatSessionId = chatSessionId, ProjectId = projectId });
        Assert.AreEqual(1, chatMessageCount, "The confirmed ticket must link back to a persisted chat message.");

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
        CollectionAssert.Contains(sourceTypes, "ChatSession", "Ticket source references must include the chat session.");
        CollectionAssert.Contains(sourceTypes, "ChatMessage", "Ticket source references must include the user message.");

        var run = await connection.QuerySingleAsync<PersistedRunRow>(
            "SELECT RunId, ProjectId, TicketId, State FROM dbo.Runs WHERE RunId = @RunId",
            new { RunId = runId });
        Assert.AreEqual(projectId, run.ProjectId);
        Assert.AreEqual(ticketId, run.TicketId);
        Assert.AreEqual("PausedForApproval", run.State);

        var eventTypes = (await connection.QueryAsync<string>(
            "SELECT EventType FROM dbo.RunEvents WHERE RunId = @RunId ORDER BY TimestampUtc, Id",
            new { RunId = runId })).ToArray();
        foreach (var required in new[]
                 {
                     "RunStarted",
                     "ProposalGenerated",
                     "SkeletonEvidencePackaged",
                     "CriticReviewPackageReady",
                     "ApprovalRequiredHalt"
                 })
        {
            CollectionAssert.Contains(eventTypes, required, $"Missing persisted REL-5 event: {required}");
        }

        CollectionAssert.DoesNotContain(eventTypes, "SkeletonContinuationUnblocked");
        CollectionAssert.DoesNotContain(eventTypes, "SkeletonApplyStarted");
        CollectionAssert.DoesNotContain(eventTypes, "SkeletonApplied");
    }

    private sealed class DeterministicRel3Builder : IBuilderProposalService
    {
        public Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken ct = default) =>
            Task.FromResult(new BuilderProposal
            {
                TicketId = ticketId,
                ProjectId = 0,
                Summary = "Validate Book at construction.",
                Rationale = "Reject empty ISBN/title and negative price so downstream code can trust a Book.",
                ModelProvider = "deterministic-fake",
                ModelName = "rel3-validate-book-fixed",
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
            GenerateProposalAsync(0, ct);

        public Task<BuilderProposal> GenerateRepairProposalAsync(long ticketId, SkeletonRepairContext repair, CancellationToken ct = default) =>
            GenerateProposalAsync(ticketId, ct);

        public Task<BuilderProposal> GenerateRevisionProposalAsync(long ticketId, SkeletonRevisionContext revision, CancellationToken ct = default) =>
            throw new NotSupportedException("REL-3 smoke does not exercise human-directed revision.");

        public Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default) =>
            throw new NotSupportedException("REL-3 applies through the skeleton-run API, not direct builder writes.");
    }

    private sealed class EmptyTestAuthoring : ISkeletonTestAuthoringService
    {
        public Task<SkeletonTestAuthoringResult> AuthorTestsAsync(SkeletonTestAuthoringRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SkeletonTestAuthoringResult
            {
                Succeeded = true,
                Tests = [],
                ModelProvider = "deterministic-fake",
                ModelName = "rel3-no-authored-tests"
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

            var reviewId = $"review-{request.RunId}";
            await events.PublishAsync(new RunEventDto
            {
                RunId = request.RunId,
                EventType = "SkeletonCriticReviewRecorded",
                Message = "Deterministic REL-3 critic review recorded no findings. A critic review is not approval.",
                Payload = new Dictionary<string, string>
                {
                    ["criticAgentRunId"] = $"critic-{request.RunId}",
                    ["reviewId"] = reviewId,
                    ["verdict"] = "NoFindings",
                    ["findingCount"] = "0",
                    ["blockingFindingCount"] = "0",
                    ["findingIds"] = string.Empty,
                    ["packageSha256"] = packageHash,
                    ["groundTruthCheckCount"] = "1",
                    ["groundTruthMismatchCount"] = "0",
                    ["modelProvider"] = "deterministic-fake",
                    ["modelName"] = "rel3-critic-clean-fixed",
                    ["requestedByUserId"] = request.RequestedByUserId
                }
            }, cancellationToken);

            return new SkeletonCriticReviewOutcome
            {
                Succeeded = true,
                CriticAgentRunId = $"critic-{request.RunId}",
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
                            Detail = "Deterministic REL-3 review accepted the persisted package hash.",
                            BlocksMerge = false
                        }
                    ]
                },
                Findings = []
            };
        }
    }

    private sealed class DeterministicFormalizationChatResponseService : IProjectChatResponseService
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
                "REL-5 deterministic chat response treats the user turn as ticket formalization evidence only.");

            return Task.FromResult<ProjectChatResponseResult?>(new ProjectChatResponseResult(
                Response: "Draft a ticket to validate Book constructor input. This response is not approval.",
                Mode: ChatGovernanceMode.Formalization.ToString(),
                ModeConfidence: decision.Confidence,
                ModeReason: decision.Reason,
                Clarification: ChatClarificationState.None,
                Gate: ChatGovernanceGate.FromDecision(decision),
                ContextSummary: recentConversationSummary,
                LinkedFilePaths: "src/BookSeller.Domain/Book.cs",
                LinkedSymbols: "Book",
                DogfoodTraceId: dogfoodTraceId,
                TraceId: 1));
        }
    }

    private sealed class DeterministicRel5DraftTicketService : IDraftTicketService
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
                Title = string.IsNullOrWhiteSpace(proposedTitle) ? "Reject invalid books at the door" : proposedTitle,
                TicketType = "Task",
                Priority = "High",
                Status = "Draft",
                Summary = "Validate a Book at construction before it enters the domain model.",
                Background = "The chat turn asks IronDev to turn Book constructor validation into ticket-shaped work.",
                AcceptanceCriteria = string.Join(Environment.NewLine, new[]
                {
                    "- Empty ISBN is rejected.",
                    "- Empty title is rejected.",
                    "- Negative price is rejected.",
                    "- Zero price remains valid."
                }),
                LinkedFilePaths = linkedFilePaths,
                LinkedSymbols = linkedSymbols,
                ImplementationPlan = "Update the Book constructor guard clauses and keep the existing sample test suite green.",
                UnitTests = "Add or preserve tests for empty ISBN, empty title, negative price, and zero price.",
                IntegrationTests = "Run the BookSeller sample tests in the disposable workspace.",
                ManualTests = "Review the generated diff before any approval.",
                RegressionTests = "Ensure valid book construction still succeeds.",
                BuildValidation = "dotnet test BookSeller.slnx",
                IsGenerated = true,
                GenerationNote = "Generated by REL-5 deterministic draft service from a persisted chat message. Not approval."
            });
        }

        public Task<DraftTicket> RegenerateTestsAsync(int projectId, DraftTicket current, CancellationToken ct = default)
        {
            current.UnitTests = string.IsNullOrWhiteSpace(current.UnitTests)
                ? "Add Book constructor validation tests."
                : current.UnitTests;
            current.BuildValidation = string.IsNullOrWhiteSpace(current.BuildValidation)
                ? "dotnet test BookSeller.slnx"
                : current.BuildValidation;
            return Task.FromResult(current);
        }

        public Task<DraftTicket> GeneratePlanAsync(int projectId, DraftTicket current, CancellationToken ct = default)
        {
            current.ImplementationPlan = string.IsNullOrWhiteSpace(current.ImplementationPlan)
                ? "Update Book constructor validation and rerun the sample tests."
                : current.ImplementationPlan;
            return Task.FromResult(current);
        }
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
        RunTool(path, "git", "config user.email alpha-smoke@irondev.local");
        RunTool(path, "git", "config user.name AlphaSmoke");
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

    private static void WriteReceipt(object receipt)
    {
        var path = Environment.GetEnvironmentVariable("ALPHA_SMOKE_RECEIPT");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Path.GetTempPath(), "IronDev", "alpha-smoke", "rel3-run-receipt.json");
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

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class EnvironmentVariableScope(string name, string? previousValue) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, previousValue);
    }

    private sealed record PersistedRunRow(string RunId, int ProjectId, long TicketId, string State);
    private sealed record ChatProvenance(long SessionId, long MessageId, string MessageText);
    private sealed record ArtifactReferenceRow(string SourceType, long SourceId);
    private sealed record PersistedTicketProvenanceRow(long? SourceChatSessionId, long? SourceChatMessageId, bool IsGenerated, string Status);

    private sealed record Rel3AlphaSmokeReceipt(
        string Ticket,
        string Project,
        string ModelMode,
        string RunUntil,
        string RunId,
        bool ApiPersisted,
        bool SqlPersisted,
        int ProjectId,
        long TicketId,
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
        string AcceptedApprovalId,
        string ApplyReceiptPath,
        string ApplyReceiptSha256,
        string FinalState,
        bool LoopComplete,
        bool ReportReconstructable,
        string[] Proves,
        string[] DoesNotProveYet);

    private sealed record Rel5AlphaSmokeReceipt(
        string Ticket,
        string Project,
        string ModelMode,
        string RunUntil,
        string RunId,
        bool ApiPersisted,
        bool SqlPersisted,
        int ProjectId,
        long TicketId,
        long ChatSessionId,
        long ChatMessageId,
        bool DraftConfirmed,
        bool ChatTurnPersisted,
        bool SourceMessageLinked,
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
        string AcceptedApprovalId,
        string ApplyReceiptPath,
        string ApplyReceiptSha256,
        string FinalState,
        bool LoopComplete,
        bool ReportReconstructable,
        string[] Proves,
        string[] DoesNotProveYet);
}
