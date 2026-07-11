using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using IronDev.Core.Board;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Workflow;
using IronDev.Core.WorkItems;
using IronDev.Data.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class EndpointContractTests : ApiTestBase
{
    [TestMethod]
    public async Task Swagger_ShouldExposeBoundaryEndpoints()
    {
        var response = await Client.GetAsync("/swagger/v1/swagger.json");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        foreach (var path in new[]
        {
            "/api/auth/login",
            "/api/tenants/select",
            "/api/projects",
            "/api/projects/{projectId}",
            "/api/projects/{projectId}/board",
            "/api/projects/{projectId}/work-items/{workItemId}",
            "/api/projects/{projectId}/tickets",
            "/api/projects/{projectId}/tickets/{ticketId}",
            "/api/projects/{projectId}/tickets/{ticketId}/archive",
            "/api/projects/{projectId}/tickets/import-external",
            "/api/projects/{projectId}/tickets/generate-from-discussion",
            "/api/projects/{projectId}/tickets/draft/confirm",
            "/api/projects/{projectId}/tickets/{ticketId}/build-runs",
            "/api/projects/{projectId}/tickets/{ticketId}/build-runs/disposable",
            "/api/projects/{projectId}/tickets/{ticketId}/build-runs/{runId}",
            "/api/projects/{projectId}/tickets/{ticketId}/build-runs/{runId}/review",
            "/api/projects/{projectId}/discussions",
            "/api/projects/{projectId}/documents/{documentVersionId}/tickets",
            "/api/projects/{projectId}/tickets/{ticketId}/review",
            "/api/projects/{projectId}/tickets/{ticketId}/disposable-code-runs",
            "/api/projects/{projectId}/tickets/{ticketId}/build-runs/{runId}/review-package",
            "/api/projects/{projectId}/tickets/{ticketId}/evidence-summary",
            "/api/projects/{projectId}/documents",
            "/api/projects/{projectId}/documents/upload",
            "/api/projects/{projectId}/documents/{documentId}",
            "/api/projects/{projectId}/documents/{documentId}/archive",
            "/api/projects/{projectId}/documents/{documentId}/process",
            "/api/projects/{projectId}/documents/{documentId}/resolve",
            "/api/projects/{projectId}/documents/{documentId}/versions",
            "/api/projects/{projectId}/documents/{documentId}/versions/current",
            "/api/projects/{projectId}/decisions",
            "/api/projects/{projectId}/decisions/{decisionId}",
            "/api/projects/{projectId}/decisions/{decisionId}/supersede",
            "/api/projects/{projectId}/decisions/{decisionId}/archive",
            "/api/projects/{projectId}/memory/search",
            "/api/projects/{projectId}/memory/status",
            "/api/projects/{projectId}/memory/reindex",
            "/api/projects/{projectId}/services/status",
            "/api/projects/{projectId}/chat/document-sources",
            "/api/projects/{projectId}/chat/complete",
            "/api/projects/{projectId}/channels",
            "/api/projects/{projectId}/channels/{channelReference}",
            "/api/projects/{projectId}/channels/{channelReference}/messages",
            "/api/projects/{projectId}/channels/{channelReference}/assistant-turns/{turnId}/complete",
            "/api/projects/{projectId}/notifications",
            "/api/projects/{projectId}/notifications/{notificationId}/read",
            "/api/projects/{projectId}/tools",
            "/api/projects/{projectId}/tools/{toolId}",
            "/api/projects/{projectId}/members",
            "/api/run-reports",
            "/api/runs/{runId}",
            "/api/runs/{runId}/report",
            "/api/runs/{runId}/events",
            "/api/environment"
        })
        {
            Assert.IsTrue(paths.TryGetProperty(path, out _), $"Swagger is missing {path}.");
        }
    }

    [TestMethod]
    public async Task BoundaryEndpoints_WithoutToken_ShouldReturnUnauthorized()
    {
        foreach (var path in new[]
        {
            "/api/projects",
            "/api/projects/1/board",
            "/api/projects/1/work-items/1",
            "/api/projects/1/tickets",
            "/api/projects/1/documents",
            "/api/projects/1/memory/search?q=architecture",
            "/api/projects/1/chat/sessions",
            "/api/projects/1/tools",
            "/api/projects/1/members"
        })
        {
            var response = await Client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, $"{path} should require a token.");
        }
    }

    [TestMethod]
    public async Task ProjectBoard_ShouldReturnBackendOwnedWorkItemAndReadinessTruth()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var createProject = await client.PostAsJsonAsync("/api/projects", new Project
        {
            Name = "Board Read Contract Test",
            Description = "Exercises the project Board read projection.",
            LocalPath = @"C:\Temp\BoardReadContractTest"
        });
        Assert.AreEqual(HttpStatusCode.Created, createProject.StatusCode);
        var project = await createProject.Content.ReadFromJsonAsync<Project>();
        Assert.IsNotNull(project);

        var createTicket = await client.PostAsJsonAsync($"/api/projects/{project!.Id}/tickets", new CreateProjectTicketRequest
        {
            Title = "Shape the Board projection",
            Summary = "The server owns the stage and next safe action.",
            Priority = "High",
            Type = "Task"
        });
        Assert.AreEqual(HttpStatusCode.OK, createTicket.StatusCode);
        var ticket = await createTicket.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(ticket);

        var response = await client.GetAsync($"/api/projects/{project.Id}/board");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var board = await response.Content.ReadFromJsonAsync<ProjectBoardReadModel>();
        Assert.IsNotNull(board);
        Assert.AreEqual(project.Id, board.ProjectId);
        Assert.AreEqual(project.Name, board.ProjectName);
        Assert.AreEqual(project.Id, board.Readiness.ProjectId);

        var item = board.Items.Single(candidate => candidate.WorkItemId == ticket!.Id);
        Assert.AreEqual(ProjectBoardStages.Shape, item.Stage);
        Assert.AreEqual("Shape the requirement and confirm acceptance criteria.", item.NextSafeAction);
        Assert.AreEqual(ProjectBoardWaitingOnKinds.Human, item.WaitingOn?.Kind);
        Assert.IsNull(item.Assignee);
    }

    [TestMethod]
    public async Task ProjectWorkItem_ShouldReturnContractGateAndHonestCollaborationTruth()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var createProject = await client.PostAsJsonAsync("/api/projects", new Project
        {
            Name = "Work Item Read Contract Test",
            Description = "Exercises the Work Item read projection.",
            LocalPath = @"C:\Temp\WorkItemReadContractTest"
        });
        Assert.AreEqual(HttpStatusCode.Created, createProject.StatusCode);
        var project = await createProject.Content.ReadFromJsonAsync<Project>();
        Assert.IsNotNull(project);

        var createTicket = await client.PostAsJsonAsync($"/api/projects/{project!.Id}/tickets", new CreateProjectTicketRequest
        {
            Title = "Read the governed Work Item",
            Summary = "The backend owns current gate and action truth.",
            AcceptanceCriteria = ["The contract is visible.", "Missing collaboration stays empty."],
            LinkedFilePaths = ["src/One.cs", "src/Two.cs"],
            Priority = "High",
            Type = "Task"
        });
        Assert.AreEqual(HttpStatusCode.OK, createTicket.StatusCode);
        var ticket = await createTicket.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(ticket);

        var response = await client.GetAsync($"/api/projects/{project.Id}/work-items/{ticket!.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var workItem = await response.Content.ReadFromJsonAsync<ProjectWorkItemReadModel>();
        Assert.IsNotNull(workItem);
        Assert.AreEqual(project.Id, workItem.ProjectId);
        Assert.AreEqual(ticket.Id, workItem.WorkItemId);
        Assert.AreEqual(2, workItem.Contract.AcceptanceCriterionCount);
        Assert.AreEqual(2, workItem.Contract.AffectedFileCount);
        Assert.IsNull(workItem.Collaboration.Assignee);
        Assert.AreEqual(0, workItem.Collaboration.Followers.Count);
        Assert.AreEqual(ProjectWorkItemStages.Shape, workItem.Stage);
        Assert.IsFalse(string.IsNullOrWhiteSpace(workItem.Gate.Reason));
        Assert.IsFalse(string.IsNullOrWhiteSpace(workItem.Gate.NextSafeAction));
        Assert.AreEqual(ProjectWorkItemApplyRecoveryStatuses.NotRequired, workItem.ApplyRecovery.Status);
        Assert.IsFalse(workItem.ApplyRecovery.Required);
        Assert.IsFalse(workItem.ApplyRecovery.RetryAllowed);
        Assert.AreEqual(ProjectWorkItemExecutionProofStatuses.NoRun, workItem.ExecutionProof.Status);
        Assert.IsFalse(workItem.ExecutionProof.HasRunRecord);
        Assert.IsFalse(workItem.ExecutionProof.ArtifactEvidenceProvesExecution);

        var otherProjectResponse = await client.PostAsJsonAsync("/api/projects", new Project
        {
            Name = "Other Work Item Project",
            LocalPath = @"C:\Temp\OtherWorkItemProject"
        });
        Assert.AreEqual(HttpStatusCode.Created, otherProjectResponse.StatusCode);
        var otherProject = await otherProjectResponse.Content.ReadFromJsonAsync<Project>();
        Assert.IsNotNull(otherProject);

        var wrongProjectRead = await client.GetAsync($"/api/projects/{otherProject!.Id}/work-items/{ticket.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectRead.StatusCode);
    }

    [TestMethod]
    public async Task ProjectsTicketsMemoryAndChat_ShouldRoundTripThroughApiBoundary()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var createProject = await client.PostAsJsonAsync("/api/projects", new Project
        {
            Name = "Boundary Endpoint Test",
            Description = "Created by API endpoint contract test.",
            LocalPath = @"C:\Temp\BoundaryEndpointTest"
        });
        Assert.AreEqual(HttpStatusCode.Created, createProject.StatusCode);

        var project = await createProject.Content.ReadFromJsonAsync<Project>();
        Assert.IsNotNull(project);
        Assert.IsTrue(project!.Id > 0);

        var projects = await client.GetFromJsonAsync<Project[]>("/api/projects");
        Assert.IsTrue(projects?.Any(p => p.Id == project.Id) == true);

        project.Description = "Updated by API endpoint contract test.";
        var updateProject = await client.PatchAsJsonAsync($"/api/projects/{project.Id}", project);
        Assert.AreEqual(HttpStatusCode.OK, updateProject.StatusCode);
        var updatedProject = await updateProject.Content.ReadFromJsonAsync<Project>();
        Assert.AreEqual("Updated by API endpoint contract test.", updatedProject?.Description);

        var saveTicket = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets", new CreateProjectTicketRequest
        {
            Title = "Exercise API ticket boundary",
            Summary = "Ticket state should persist through IronDev.Api.",
            Priority = "Medium",
            Type = "Task"
        });
        Assert.AreEqual(HttpStatusCode.OK, saveTicket.StatusCode);

        var ticket = await saveTicket.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(ticket);
        Assert.IsTrue(ticket!.Id > 0);

        var getTicket = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}");
        Assert.AreEqual(HttpStatusCode.OK, getTicket.StatusCode);
        var fetchedTicket = await getTicket.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.AreEqual("Exercise API ticket boundary", fetchedTicket?.Title);

        fetchedTicket!.Title = "Exercise API ticket boundary update";
        var updateTicket = await client.PatchAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}", fetchedTicket);
        Assert.AreEqual(HttpStatusCode.OK, updateTicket.StatusCode);
        var updatedTicket = await updateTicket.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.AreEqual("Exercise API ticket boundary update", updatedTicket?.Title);

        var saveSummary = await client.PostAsJsonAsync($"/api/projects/{project.Id}/memory/summary", new ProjectSummary
        {
            ProjectId = project.Id,
            Summary = "API boundary summary."
        });
        Assert.AreEqual(HttpStatusCode.OK, saveSummary.StatusCode);

        var summary = await client.GetFromJsonAsync<ProjectSummary>($"/api/projects/{project.Id}/memory/summary");
        Assert.AreEqual("API boundary summary.", summary?.Summary);

        var chat = await client.PostAsJsonAsync($"/api/projects/{project.Id}/chat/complete", new
        {
            projectId = project.Id,
            sessionId = (long?)null,
            prompt = "hello",
            activeModel = "test",
            mode = "projectStateReview"
        });
        Assert.AreEqual(HttpStatusCode.OK, chat.StatusCode);
        using var chatBody = JsonDocument.Parse(await chat.Content.ReadAsStringAsync());
        var chatResponse = chatBody.RootElement.GetProperty("response").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(chatResponse));
        Assert.IsFalse(chatResponse!.Contains("placeholder", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(chatResponse.Contains("Exercise API ticket boundary update", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(chatResponse.Contains("Recommended next actions", StringComparison.OrdinalIgnoreCase));

        var chatContext = chatBody.RootElement.GetProperty("contextSummary").GetString();
        Assert.IsTrue(chatContext?.Contains("ticket", StringComparison.OrdinalIgnoreCase) == true);

        var freeformChat = await client.PostAsJsonAsync($"/api/projects/{project.Id}/chat/complete", new
        {
            projectId = project.Id,
            sessionId = (long?)null,
            prompt = "build me minesweeper",
            activeModel = "test",
            mode = "projectQuestion"
        });
        Assert.AreEqual(HttpStatusCode.OK, freeformChat.StatusCode);
        using var freeformBody = JsonDocument.Parse(await freeformChat.Content.ReadAsStringAsync());
        var freeformResponse = freeformBody.RootElement.GetProperty("response").GetString();
        var freeformMode = freeformBody.RootElement.GetProperty("mode").GetString();
        Assert.IsTrue(freeformResponse?.Contains("Minesweeper", StringComparison.OrdinalIgnoreCase) == true);
        Assert.IsNotNull(freeformMode);
        Assert.IsTrue(
            freeformMode.Equals("Exploration", StringComparison.OrdinalIgnoreCase) ||
            freeformMode.Equals("Formalization", StringComparison.OrdinalIgnoreCase) ||
            freeformMode.Equals("Confirmation", StringComparison.OrdinalIgnoreCase));
        var freeformGate = freeformBody.RootElement.GetProperty("gate");
        Assert.AreEqual(freeformMode, freeformGate.GetProperty("mode").GetString(), ignoreCase: true);
        Assert.IsFalse(string.IsNullOrWhiteSpace(freeformGate.GetProperty("reason").GetString()));
        var gateConfidence = freeformGate.GetProperty("confidence").GetDouble();
        Assert.IsTrue(gateConfidence is >= 0 and <= 1);

        var formalization = freeformMode.Equals("Formalization", StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(formalization, freeformGate.GetProperty("canSaveDiscussion").GetBoolean());
        Assert.AreEqual(formalization, freeformGate.GetProperty("canCreateTicket").GetBoolean());
        Assert.AreEqual(formalization, freeformGate.GetProperty("canViewSources").GetBoolean());
        Assert.AreEqual(formalization, freeformGate.GetProperty("canCopyMarkdown").GetBoolean());

        var wrongProjectChat = await client.PostAsJsonAsync($"/api/projects/{project.Id}/chat/complete", new
        {
            projectId = project.Id + 999,
            sessionId = (long?)null,
            prompt = "hello",
            activeModel = "test"
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, wrongProjectChat.StatusCode);

        var unsupportedChatMode = await client.PostAsJsonAsync($"/api/projects/{project.Id}/chat/complete", new
        {
            projectId = project.Id,
            sessionId = (long?)null,
            prompt = "hello",
            activeModel = "test",
            mode = "generalProjectQuestion"
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, unsupportedChatMode.StatusCode);
    }

    [TestMethod]
    public async Task ProjectScopedTicketDetailAndArchive_ShouldNotLeakAcrossProjects()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Ticket Scope Owner Project");
        var otherProject = await CreateProjectAsync(client, "Ticket Scope Other Project");
        var createResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets", new CreateProjectTicketRequest
        {
            Title = "Project scoped ticket",
            Summary = "Ticket reads and archive actions must enforce the route project.",
            Priority = "Medium",
            Type = "Task"
        });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

        var ticket = await createResponse.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(ticket);

        var wrongProjectRead = await client.GetAsync($"/api/projects/{otherProject.Id}/tickets/{ticket!.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectRead.StatusCode);

        var wrongProjectArchive = await client.PostAsync($"/api/projects/{otherProject.Id}/tickets/{ticket.Id}/archive", null);
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectArchive.StatusCode);

        var archive = await client.PostAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/archive", null);
        Assert.AreEqual(HttpStatusCode.NoContent, archive.StatusCode);
    }

    [TestMethod]
    public async Task StructuredTicketRequests_ShouldPersistCanonicalMetadataThroughApi()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Structured Ticket API Test");

        var createResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets", new CreateProjectTicketRequest
        {
            Title = "Make IronDev tickets canonical",
            Type = "Architecture / Dogfooding",
            Priority = "Critical",
            Summary = "Tickets should be written through IronDev.Api.",
            Problem = "GitHub issues are being treated as canonical work items.",
            ProposedChange = "Create product-shaped ticket write APIs and CLI commands.",
            AcceptanceCriteria =
            [
                "Tickets are stored in IronDev.",
                "External references are linked as metadata."
            ],
            ExternalReferences =
            [
                new ExternalReferenceDto
                {
                    Provider = "github",
                    Kind = "issue",
                    ExternalId = "73",
                    Url = "https://github.com/BigDaddyDread-code/IronDeveloper/issues/73",
                    Title = "UI slice"
                }
            ],
            Provenance = new TicketProvenanceDto
            {
                Source = "design-discussion",
                CreatedBy = "codex",
                Notes = "Created from dogfood discussion."
            }
        });

        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(created);
        Assert.AreEqual("Architecture / Dogfooding", created!.TicketType);
        Assert.AreEqual("Critical", created.Priority);
        StringAssert.Contains(created.AcceptanceCriteria, "Tickets are stored in IronDev.");
        StringAssert.Contains(created.TechnicalNotes, "https://github.com/BigDaddyDread-code/IronDeveloper/issues/73");
        StringAssert.Contains(created.GenerationNote, "Source: design-discussion");

        var importResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/import-external", new ImportExternalTicketRequest
        {
            Title = "Import GitHub issue as reference",
            Type = "Backfill",
            Priority = "High",
            Summary = "GitHub issue becomes an external reference.",
            ExternalReference = new ExternalReferenceDto
            {
                Provider = "github",
                Kind = "issue",
                ExternalId = "73",
                Url = "https://github.com/BigDaddyDread-code/IronDeveloper/issues/73",
                Title = "Issue 73"
            }
        });

        Assert.AreEqual(HttpStatusCode.OK, importResponse.StatusCode);
        var imported = await importResponse.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(imported);
        StringAssert.Contains(imported!.TechnicalNotes, "Issue 73");
        StringAssert.Contains(imported.GenerationNote, "Imported from external tracker.");

        var discussionResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/generate-from-discussion", new GenerateTicketFromDiscussionRequest
        {
            Discussion = "Dogfood discussion should become an IronDev ticket.",
            Provenance = new TicketProvenanceDto { Source = "discussion", CreatedBy = "codex" }
        });

        Assert.AreEqual(HttpStatusCode.OK, discussionResponse.StatusCode);
        var discussionTicket = await discussionResponse.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(discussionTicket);
        Assert.AreEqual("Dogfood discussion should become an IronDev ticket.", discussionTicket!.Title);
    }

    [TestMethod]
    public async Task ProjectScopedDecisions_ShouldCreateUpdateSupersedeAndArchiveWithoutCrossProjectLeak()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Decision Scope Owner Project");
        var otherProject = await CreateProjectAsync(client, "Decision Scope Other Project");

        var createResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/decisions", new ProjectDecision
        {
            Title = "Use project-scoped API routes",
            Detail = "Cockpit data must be loaded through project-scoped endpoints.",
            Reason = "Avoid cross-project leakage.",
            Category = "Architecture",
            Status = "Accepted"
        });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

        var decision = await createResponse.Content.ReadFromJsonAsync<ProjectDecision>();
        Assert.IsNotNull(decision);
        Assert.AreEqual(project.Id, decision!.ProjectId);

        var list = await client.GetFromJsonAsync<ProjectDecision[]>($"/api/projects/{project.Id}/decisions");
        Assert.IsTrue(list?.Any(item => item.Id == decision.Id) == true);

        var wrongProjectRead = await client.GetAsync($"/api/projects/{otherProject.Id}/decisions/{decision.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectRead.StatusCode);

        decision.Detail = "Updated through the project-scoped decisions API.";
        var update = await client.PatchAsJsonAsync($"/api/projects/{project.Id}/decisions/{decision.Id}", decision);
        Assert.AreEqual(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ProjectDecision>();
        Assert.AreEqual("Updated through the project-scoped decisions API.", updated?.Detail);

        var supersede = await client.PostAsJsonAsync($"/api/projects/{project.Id}/decisions/{decision.Id}/supersede", new SupersedeDecisionRequest
        {
            Replacement = new ProjectDecision
            {
                Title = "Use project-scoped API routes v2",
                Detail = "Superseding decisions should preserve project ownership.",
                Category = "Architecture",
                Status = "Accepted"
            }
        });
        Assert.AreEqual(HttpStatusCode.OK, supersede.StatusCode);
        var replacement = await supersede.Content.ReadFromJsonAsync<ProjectDecision>();
        Assert.IsNotNull(replacement);
        Assert.AreEqual(project.Id, replacement!.ProjectId);
        StringAssert.Contains(replacement.Reason, $"Supersedes decision {decision.Id}.");

        var archivedWrongProject = await client.PostAsync($"/api/projects/{otherProject.Id}/decisions/{replacement.Id}/archive", null);
        Assert.AreEqual(HttpStatusCode.NotFound, archivedWrongProject.StatusCode);

        var archive = await client.PostAsync($"/api/projects/{project.Id}/decisions/{replacement.Id}/archive", null);
        Assert.AreEqual(HttpStatusCode.NoContent, archive.StatusCode);
    }

    [TestMethod]
    public async Task MemoryAndServiceStatus_ProjectScopedEndpoints_ShouldReturnRealStatus()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Memory Status Project");

        var search = await client.PostAsJsonAsync($"/api/projects/{project.Id}/memory/search", new MemorySearchRequest
        {
            Query = "architecture decision",
            Take = 5
        });
        Assert.AreEqual(HttpStatusCode.OK, search.StatusCode);
        var searchBody = await search.Content.ReadFromJsonAsync<MemorySearchResponseDto>();
        Assert.IsNotNull(searchBody);
        Assert.AreEqual(project.Id, searchBody!.ProjectId);
        Assert.AreEqual("architecture decision", searchBody.Query);
        Assert.AreNotEqual(Guid.Empty, searchBody.TraceId);

        var memoryStatus = await client.GetAsync($"/api/projects/{project.Id}/memory/status");
        Assert.AreEqual(HttpStatusCode.OK, memoryStatus.StatusCode);
        var memoryBody = await memoryStatus.Content.ReadFromJsonAsync<MemoryStatusDto>();
        Assert.IsNotNull(memoryBody);
        Assert.AreEqual(project.Id, memoryBody!.ProjectId);

        var services = await client.GetAsync($"/api/projects/{project.Id}/services/status");
        Assert.AreEqual(HttpStatusCode.OK, services.StatusCode);
        var servicesBody = await services.Content.ReadFromJsonAsync<ProjectServicesStatusDto>();
        Assert.IsNotNull(servicesBody);
        Assert.AreEqual(project.Id, servicesBody!.ProjectId);
        Assert.AreEqual("healthy", servicesBody.ApiStatus);
        Assert.AreEqual("healthy", servicesBody.DatabaseStatus);

        var missingServices = await client.GetAsync("/api/projects/987654321/services/status");
        Assert.AreEqual(HttpStatusCode.NotFound, missingServices.StatusCode);
    }

    [TestMethod]
    public async Task ProjectScopedDocuments_ShouldCreateReadVersionAndArchiveWithoutCrossProjectLeak()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Document Scope Owner Project");
        var otherProject = await CreateProjectAsync(client, "Document Scope Other Project");

        var createResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/documents", new CreateProjectDocumentRequest
        {
            Title = "Alpha API document",
            DocumentType = "Architecture",
            ContentMarkdown = "# Alpha API document",
            ChangeSummary = "Initial document",
            CreatedBy = "EndpointContractTests"
        });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

        var document = await createResponse.Content.ReadFromJsonAsync<ProjectDocument>();
        Assert.IsNotNull(document);
        Assert.AreEqual(project.Id, document!.ProjectId);
        Assert.IsTrue(document.CurrentVersionId.HasValue);

        var wrongProjectRead = await client.GetAsync($"/api/projects/{otherProject.Id}/documents/{document.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectRead.StatusCode);

        var currentVersion = await client.GetAsync($"/api/projects/{project.Id}/documents/{document.Id}/versions/current");
        Assert.AreEqual(HttpStatusCode.OK, currentVersion.StatusCode);

        var versions = await client.GetAsync($"/api/projects/{project.Id}/documents/{document.Id}/versions");
        Assert.AreEqual(HttpStatusCode.OK, versions.StatusCode);
        var versionList = await versions.Content.ReadFromJsonAsync<ProjectDocumentVersion[]>();
        Assert.IsNotNull(versionList);
        Assert.AreEqual(1, versionList!.Length);

        var wrongProjectArchive = await client.PostAsync($"/api/projects/{otherProject.Id}/documents/{document.Id}/archive", null);
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectArchive.StatusCode);

        var archive = await client.PostAsync($"/api/projects/{project.Id}/documents/{document.Id}/archive", null);
        Assert.AreEqual(HttpStatusCode.NoContent, archive.StatusCode);
    }

    [TestMethod]
    public async Task ProjectDocumentUpload_ShouldPersistUploadedDraftAndRejectUnsupportedFiles()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);
        var project = await CreateProjectAsync(client, "Document Upload Project");

        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("# Upload contract\n\nPersist backend truth."));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/markdown");
        upload.Add(file, "file", "upload-contract.md");
        upload.Add(new StringContent("Upload Contract"), "displayName");
        upload.Add(new StringContent("Architecture"), "documentType");
        upload.Add(new StringContent("Uploaded through the project-scoped API."), "description");

        var response = await client.PostAsync($"/api/projects/{project.Id}/documents/upload", upload);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProjectDocumentUploadResult>();
        Assert.IsNotNull(result);
        Assert.AreEqual("Uploaded", result!.Document.Origin);
        Assert.AreEqual("Draft", result.Document.ProcessingStatus);
        Assert.AreEqual("upload-contract.md", result.Document.OriginalFileName);
        Assert.AreEqual("text/markdown", result.Document.MediaType);
        Assert.AreEqual("Project", result.Document.Visibility);
        Assert.AreEqual("# Upload contract\n\nPersist backend truth.", result.Version.ContentMarkdown);
        StringAssert.Contains(result.Boundary, "not attached to Chat");

        var persistedResponse = await client.GetAsync($"/api/projects/{project.Id}/documents/{result.Document.Id}");
        Assert.AreEqual(HttpStatusCode.OK, persistedResponse.StatusCode);
        var persisted = await persistedResponse.Content.ReadFromJsonAsync<ProjectDocument>();
        Assert.IsNotNull(persisted);
        Assert.AreEqual("Uploaded", persisted!.Origin);
        Assert.AreEqual("Draft", persisted.ProcessingStatus);

        using var unsupported = new MultipartFormDataContent();
        unsupported.Add(new ByteArrayContent([1, 2, 3]), "file", "payload.pdf");
        unsupported.Add(new StringContent("Unsupported Payload"), "displayName");
        var unsupportedResponse = await client.PostAsync($"/api/projects/{project.Id}/documents/upload", unsupported);
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, unsupportedResponse.StatusCode);
    }

    [TestMethod]
    public async Task ProjectDocumentProcessing_ShouldPublishOnlyTheExactReadyVersionToRetrieval()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);
        var project = await CreateProjectAsync(client, "Document Processing Project");
        var uniqueTerm = $"processing-{Guid.NewGuid():N}";

        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"# Processing contract\n\n{uniqueTerm}"));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/markdown");
        upload.Add(file, "file", "processing-contract.md");
        upload.Add(new StringContent("Processing Contract"), "displayName");
        upload.Add(new StringContent("Architecture"), "documentType");

        var uploadResponse = await client.PostAsync($"/api/projects/{project.Id}/documents/upload", upload);
        Assert.AreEqual(HttpStatusCode.Created, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<ProjectDocumentUploadResult>();
        Assert.IsNotNull(uploaded);

        var beforeSearch = await client.PostAsJsonAsync($"/api/projects/{project.Id}/memory/search", new MemorySearchRequest
        {
            Query = uniqueTerm,
            Take = 100,
            IncludeStale = true
        });
        var before = await beforeSearch.Content.ReadFromJsonAsync<MemorySearchResponseDto>();
        Assert.IsNotNull(before);
        Assert.IsFalse(before!.Results.Any(result => result.SourceId == uploaded!.Version.Id.ToString()));

        var processResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/documents/{uploaded.Document.Id}/process",
            new { });
        Assert.AreEqual(HttpStatusCode.OK, processResponse.StatusCode);
        var processed = await processResponse.Content.ReadFromJsonAsync<ProjectDocumentProcessingResult>();
        Assert.IsNotNull(processed);
        Assert.IsTrue(processed!.Succeeded);
        Assert.AreEqual("Ready", processed.Status);
        Assert.AreEqual(uploaded.Version.Id, processed.Version.Id);
        Assert.IsTrue(processed.ContextDocumentId > 0);
        Assert.IsNotNull(processed.Document.ProcessingStartedAtUtc);
        Assert.IsNotNull(processed.Document.ProcessingCompletedAtUtc);
        Assert.IsNull(processed.Document.ProcessingFailureReason);

        await using var connection = new SqlConnection(ConnectionString);
        var context = await connection.QuerySingleAsync<(long Id, string Source, string Status)>(
            """
            SELECT Id, Source, Status
            FROM dbo.ProjectContextDocuments
            WHERE Id = @ContextDocumentId AND ProjectId = @ProjectId;
            """,
            new { processed.ContextDocumentId, ProjectId = project.Id });
        Assert.AreEqual($"ProjectDocumentVersion:{uploaded.Version.Id}", context.Source);
        Assert.AreEqual("Active", context.Status);

        var linkCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(1)
            FROM dbo.ProjectDocumentLinks
            WHERE DocumentVersionId = @VersionId
              AND LinkedEntityType = 'ProjectContextDocument'
              AND LinkedEntityId = @ContextDocumentId
              AND LinkType = 'IndexedAs';
            """,
            new { VersionId = uploaded.Version.Id, processed.ContextDocumentId });
        Assert.AreEqual(1, linkCount);

        var afterSearch = await client.PostAsJsonAsync($"/api/projects/{project.Id}/memory/search", new MemorySearchRequest
        {
            Query = uniqueTerm,
            Take = 100,
            IncludeStale = true
        });
        var after = await afterSearch.Content.ReadFromJsonAsync<MemorySearchResponseDto>();
        Assert.IsNotNull(after);
        Assert.IsTrue(after!.Results.Any(result =>
            result.SourceType == "ProjectDocumentVersion"
            && result.SourceId == uploaded.Version.Id.ToString()));
    }

    [TestMethod]
    public async Task ChatDocumentContext_ShouldAttachReadyExactVersionAndReplayWithoutCrossProjectLeak()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);
        var project = await CreateProjectAsync(client, "Chat Document Context Project");
        var otherProject = await CreateProjectAsync(client, "Other Chat Document Context Project");

        var ready = await UploadDocumentAsync(client, project.Id, "Chat Context Contract", "chat-context-contract.md");
        var draft = await UploadDocumentAsync(client, project.Id, "Draft Must Stay Hidden", "draft-hidden.md");
        var otherReady = await UploadDocumentAsync(client, otherProject.Id, "Other Project Secret", "other-secret.md");

        var processReady = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/documents/{ready.Document.Id}/process",
            new { });
        Assert.AreEqual(HttpStatusCode.OK, processReady.StatusCode);
        var processOther = await client.PostAsJsonAsync(
            $"/api/projects/{otherProject.Id}/documents/{otherReady.Document.Id}/process",
            new { });
        Assert.AreEqual(HttpStatusCode.OK, processOther.StatusCode);

        var available = await client.GetFromJsonAsync<ChatDocumentSource[]>(
            $"/api/projects/{project.Id}/chat/document-sources");
        Assert.IsNotNull(available);
        Assert.AreEqual(1, available!.Length);
        Assert.AreEqual(ready.Version.Id, available[0].DocumentVersionId);
        Assert.AreEqual("v0.1", available[0].VersionLabel);
        Assert.AreEqual("Ready", available[0].Status);
        Assert.IsFalse(available.Any(source => source.DocumentVersionId == draft.Version.Id));
        Assert.IsFalse(available.Any(source => source.DocumentVersionId == otherReady.Version.Id));

        var sessionResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/chat/sessions",
            new ProjectChatSession
            {
                ProjectId = project.Id,
                Title = "Exact document context"
            });
        Assert.AreEqual(HttpStatusCode.OK, sessionResponse.StatusCode);
        var sessionId = await sessionResponse.Content.ReadFromJsonAsync<long>();

        const string prompt = "Use the attached contract as exact context.";
        var userResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/chat/sessions/{sessionId}/messages",
            new ChatMessage
            {
                ProjectId = project.Id,
                ChatSessionId = sessionId,
                Role = "user",
                Message = prompt,
                DocumentVersionIds = [ready.Version.Id]
            });
        Assert.AreEqual(HttpStatusCode.OK, userResponse.StatusCode);
        var userMessageId = await userResponse.Content.ReadFromJsonAsync<long>();

        var completionResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/chat/complete",
            new
            {
                projectId = project.Id,
                sessionId,
                prompt,
                activeModel = "test",
                mode = "projectQuestion",
                sourceMessageId = userMessageId
            });
        Assert.AreEqual(HttpStatusCode.OK, completionResponse.StatusCode);
        using var completionBody = JsonDocument.Parse(await completionResponse.Content.ReadAsStringAsync());
        var completionSource = completionBody.RootElement.GetProperty("documentSources")[0];
        Assert.AreEqual(ready.Version.Id, completionSource.GetProperty("documentVersionId").GetInt64());
        Assert.AreEqual("v0.1", completionSource.GetProperty("versionLabel").GetString());

        var assistantResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/chat/sessions/{sessionId}/messages",
            new ChatMessage
            {
                ProjectId = project.Id,
                ChatSessionId = sessionId,
                Role = "assistant",
                Message = completionBody.RootElement.GetProperty("response").GetString() ?? "Response",
                ReplyToMessageId = userMessageId
            });
        Assert.AreEqual(HttpStatusCode.OK, assistantResponse.StatusCode);

        var replay = await client.GetFromJsonAsync<ChatMessage[]>(
            $"/api/projects/{project.Id}/chat/sessions/{sessionId}/messages");
        Assert.IsNotNull(replay);
        Assert.AreEqual(2, replay!.Length);
        Assert.IsTrue(replay.All(message => message.DocumentSources.Count == 1));
        Assert.IsTrue(replay.All(message => message.DocumentSources[0].DocumentVersionId == ready.Version.Id));
        Assert.AreEqual(userMessageId, replay.Single(message => message.Role == "assistant").ReplyToMessageId);

        await using var connection = new SqlConnection(ConnectionString);
        var durableLink = await connection.QuerySingleAsync<(long DocumentVersionId, string CreatedBy)>(
            """
            SELECT DocumentVersionId, CreatedBy
            FROM dbo.ProjectDocumentLinks
            WHERE LinkedEntityType = 'ChatMessage'
              AND LinkedEntityId = @UserMessageId
              AND LinkType = 'ChatContext';
            """,
            new { UserMessageId = userMessageId });
        Assert.AreEqual(ready.Version.Id, durableLink.DocumentVersionId);
        Assert.AreEqual(AdminEmail, durableLink.CreatedBy);

        var messageCountBeforeRefusal = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM dbo.ChatMessages WHERE ChatSessionId = @SessionId;",
            new { SessionId = sessionId });
        var crossProjectAttachment = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/chat/sessions/{sessionId}/messages",
            new ChatMessage
            {
                ProjectId = project.Id,
                ChatSessionId = sessionId,
                Role = "user",
                Message = "Do not persist this turn.",
                DocumentVersionIds = [otherReady.Version.Id]
            });
        Assert.AreEqual(HttpStatusCode.Conflict, crossProjectAttachment.StatusCode);
        Assert.IsFalse(
            (await crossProjectAttachment.Content.ReadAsStringAsync()).Contains(
                otherReady.Document.Title,
                StringComparison.OrdinalIgnoreCase));
        var messageCountAfterRefusal = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM dbo.ChatMessages WHERE ChatSessionId = @SessionId;",
            new { SessionId = sessionId });
        Assert.AreEqual(messageCountBeforeRefusal, messageCountAfterRefusal);
    }

    [TestMethod]
    public async Task ProjectTools_ShouldExposeRegisteredDefinitionWithoutGrantingDirectInvocation()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);
        var project = await CreateProjectAsync(client, "Governed Tool Catalogue Project");

        var catalogueResponse = await client.GetAsync($"/api/projects/{project.Id}/tools");
        Assert.AreEqual(HttpStatusCode.OK, catalogueResponse.StatusCode);
        var catalogue = await catalogueResponse.Content.ReadFromJsonAsync<ProjectToolCatalogueResponse>();
        Assert.IsNotNull(catalogue);
        Assert.AreEqual(project.Id, catalogue!.ProjectId);
        Assert.AreEqual(project.Name, catalogue.ProjectName);
        Assert.AreEqual(1, catalogue.Tools.Count);

        var summary = catalogue.Tools.Single();
        Assert.AreEqual("code_standards.analyse_patch", summary.ToolId);
        Assert.AreEqual("Code standards analysis", summary.DisplayName);
        Assert.AreEqual("Testing and validation", summary.Category);
        Assert.AreEqual("Registered", summary.RegistrationStatus);
        Assert.AreEqual("Not required", summary.ConnectionStatus);
        Assert.AreEqual("Governed workflows only", summary.ProjectUseStatus);
        Assert.AreEqual("Not implemented", summary.DirectInvocationStatus);
        Assert.AreEqual("Not checked", summary.HealthStatus);
        StringAssert.Contains(summary.EffectiveScopeSummary, "Read-only");
        StringAssert.Contains(catalogue.Boundary, "Registration is not project enablement");

        var detailResponse = await client.GetAsync(
            $"/api/projects/{project.Id}/tools/code_standards.analyse_patch");
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<ProjectToolDetailResponse>();
        Assert.IsNotNull(detail);
        Assert.AreEqual("1", detail!.DefinitionVersion);
        Assert.AreEqual("CodeStandardsAnalysisInput", detail.InputContract);
        Assert.AreEqual("CodeStandardsAnalysisResult", detail.OutputContract);
        CollectionAssert.AreEquivalent(
            new[] { "BuilderAgent", "TestingAgent", "TesterAgent" },
            detail.AllowedCallers.ToArray());
        CollectionAssert.AreEqual(new[] { "CodeStandardsFinding" }, detail.EvidenceKinds.ToArray());
        Assert.IsFalse(detail.Capabilities.MutatesState);
        Assert.IsFalse(detail.Capabilities.AllowsNestedCalls);
        Assert.IsFalse(detail.Capabilities.AllowsFileWrites);
        Assert.IsFalse(detail.Capabilities.AllowsProcessExecution);
        Assert.IsFalse(detail.Capabilities.AllowsNetworkAccess);
        Assert.IsFalse(detail.Capabilities.AllowsWorkspaceMutation);

        var unknownProject = await client.GetAsync($"/api/projects/{project.Id + 999999}/tools");
        Assert.AreEqual(HttpStatusCode.NotFound, unknownProject.StatusCode);
        var unknownTool = await client.GetAsync($"/api/projects/{project.Id}/tools/not.registered");
        Assert.AreEqual(HttpStatusCode.NotFound, unknownTool.StatusCode);

        var directInvocation = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tools/code_standards.analyse_patch",
            new { patch = "not executable from this surface" });
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, directInvocation.StatusCode);
    }

    [TestMethod]
    public async Task ProjectMembers_ShouldExposeProjectSpecificVisibilityWithoutInventingAuthority()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);
        var project = await CreateProjectAsync(client, "Project Member Directory");

        var response = await client.GetAsync($"/api/projects/{project.Id}/members");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var directory = await response.Content.ReadFromJsonAsync<ProjectMemberDirectoryResponse>();
        Assert.IsNotNull(directory);
        Assert.AreEqual(project.Id, directory!.ProjectId);
        Assert.AreEqual(project.Name, directory.ProjectName);
        Assert.AreEqual(AssignedTenantId, directory.TenantId);
        Assert.AreEqual("Owner", directory.CurrentUserTenantRole);
        Assert.IsTrue(directory.CanAdministerTenantMembership);
        Assert.IsTrue(directory.CanAdministerProjectMembership);
        CollectionAssert.AreEqual(
            new[] { "Owner", "TenantAdmin", "Approver", "Reviewer", "Operator", "Viewer", "Member" },
            directory.AvailableTenantRoles.ToArray());
        CollectionAssert.AreEqual(new[] { "Owner", "Contributor", "Viewer" }, directory.AvailableProjectRoles.ToArray());
        Assert.AreEqual("1 active member", directory.ProjectMembershipStatus);
        Assert.AreEqual("No active channels", directory.ChannelMembershipStatus);
        Assert.IsTrue(directory.CanAdministerChannelMembership);
        Assert.AreEqual(0, directory.Channels.Count);

        var currentUser = directory.Members.Single(member => member.IsCurrentUser);
        Assert.AreEqual(AdminEmail, currentUser.Email);
        Assert.AreEqual("Owner", currentUser.TenantRole);
        Assert.AreEqual("Owner", currentUser.ProjectRole);
        Assert.IsTrue(currentUser.IsProjectMember);
        Assert.AreEqual("Project member", currentUser.ProjectAccessStatus);
        Assert.AreEqual("No explicit memberships", currentUser.ChannelMembershipSummary);
        StringAssert.Contains(directory.Boundary, "none of these grants approval");

        var unknownProject = await client.GetAsync($"/api/projects/{project.Id + 999999}/members");
        Assert.AreEqual(HttpStatusCode.NotFound, unknownProject.StatusCode);
    }

    [TestMethod]
    public async Task ProjectChannelMembership_ShouldPersistRoleAndNotificationWithoutGrantingAuthority()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);
        var project = await CreateProjectAsync(client, "Project Channel Membership");

        var memberEmail = $"channel-member-{Guid.NewGuid():N}@irondev.local";
        const string memberPassword = "channel-test-password";
        var createMember = await client.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users", new
        {
            email = memberEmail,
            displayName = "Channel Member",
            password = memberPassword,
            role = "Viewer"
        });
        Assert.AreEqual(HttpStatusCode.OK, createMember.StatusCode);
        var createdMember = await createMember.Content.ReadFromJsonAsync<JsonElement>();
        var memberUserId = createdMember.GetProperty("id").GetInt32();

        long channelId;
        await using (var connection = new SqlConnection(ConnectionString))
        {
            channelId = await connection.QuerySingleAsync<long>("""
                INSERT INTO dbo.ProjectChannels
                    (TenantId, ProjectId, Name, Slug, Description, ChannelKind, Visibility, Status, CreatedByUserId)
                OUTPUT inserted.Id
                VALUES
                    (@TenantId, @ProjectId, 'Architecture', 'architecture', 'Restricted architecture discussion.', 'Architecture', 'MembersOnly', 'Active', 1);
                """, new { TenantId = AssignedTenantId, ProjectId = project.Id });
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ProjectChannelMembers
                    (TenantId, ProjectId, ChannelId, UserId, ChannelRole, NotificationLevel, Status, AddedByUserId)
                VALUES
                    (@TenantId, @ProjectId, @ChannelId, 1, 'Owner', 'All', 'Active', 1);
                """, new { TenantId = AssignedTenantId, ProjectId = project.Id, ChannelId = channelId });
        }

        var initialResponse = await client.GetAsync($"/api/projects/{project.Id}/members");
        Assert.AreEqual(HttpStatusCode.OK, initialResponse.StatusCode);
        var initial = await initialResponse.Content.ReadFromJsonAsync<ProjectMemberDirectoryResponse>();
        Assert.IsNotNull(initial);
        Assert.AreEqual("1 active channel", initial!.ChannelMembershipStatus);
        var channel = initial.Channels.Single();
        Assert.AreEqual("MembersOnly", channel.Visibility);
        Assert.AreEqual("Owner", channel.Members.Single().ChannelRole);

        var addMembership = await client.PutAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{channelId}/members/{memberUserId}",
            new { channelRole = "Moderator", notificationLevel = "Mentions" });
        Assert.AreEqual(HttpStatusCode.OK, addMembership.StatusCode);

        var addProjectMembership = await client.PutAsJsonAsync(
            $"/api/projects/{project.Id}/members/{memberUserId}",
            new { projectRole = "Viewer" });
        Assert.AreEqual(HttpStatusCode.OK, addProjectMembership.StatusCode);

        var memberToken = await SelectTenantAsync(await LoginAsync(memberEmail, memberPassword));
        using var memberClient = GetAuthedClient(memberToken);
        var denied = await memberClient.PutAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{channelId}/members/{memberUserId}",
            new { channelRole = "Owner", notificationLevel = "All" });
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);

        var lastOwnerRefusal = await client.PutAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{channelId}/members/1",
            new { channelRole = "Member", notificationLevel = "All" });
        Assert.AreEqual(HttpStatusCode.Conflict, lastOwnerRefusal.StatusCode);

        var promoteMember = await client.PutAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{channelId}/members/{memberUserId}",
            new { channelRole = "Owner", notificationLevel = "All" });
        Assert.AreEqual(HttpStatusCode.OK, promoteMember.StatusCode);
        var demoteOriginalOwner = await client.PutAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{channelId}/members/1",
            new { channelRole = "Member", notificationLevel = "None" });
        Assert.AreEqual(HttpStatusCode.OK, demoteOriginalOwner.StatusCode);

        var removeLastOwner = await client.DeleteAsync(
            $"/api/projects/{project.Id}/channels/{channelId}/members/{memberUserId}");
        Assert.AreEqual(HttpStatusCode.Conflict, removeLastOwner.StatusCode);

        var finalResponse = await client.GetAsync($"/api/projects/{project.Id}/members");
        var final = await finalResponse.Content.ReadFromJsonAsync<ProjectMemberDirectoryResponse>();
        var finalChannel = final!.Channels.Single();
        Assert.AreEqual("Owner", finalChannel.Members.Single(member => member.UserId == memberUserId).ChannelRole);
        Assert.AreEqual("Member", finalChannel.Members.Single(member => member.UserId == 1).ChannelRole);
        Assert.AreEqual("None", finalChannel.Members.Single(member => member.UserId == 1).NotificationLevel);
        StringAssert.Contains(finalChannel.Boundary, "not approval");
    }

    [TestMethod]
    public async Task ProjectChannelChat_ShouldEnforceVisibilityPersistHumanMessagesAndRefuseAssistantAuthority()
    {
        var tenantToken = await SelectTenantAsync(await LoginAsync());
        using var ownerClient = GetAuthedClient(tenantToken);
        var project = await CreateProjectAsync(ownerClient, "Shared Channel Chat");

        var memberEmail = $"channel-reader-{Guid.NewGuid():N}@irondev.local";
        const string memberPassword = "channel-reader-password";
        var createMember = await ownerClient.PostAsJsonAsync($"/api/tenants/{AssignedTenantId}/users", new
        {
            email = memberEmail,
            displayName = "Channel Reader",
            password = memberPassword,
            role = "Viewer"
        });
        Assert.AreEqual(HttpStatusCode.OK, createMember.StatusCode);
        var memberUserId = (await createMember.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var createGeneral = await ownerClient.PostAsJsonAsync($"/api/projects/{project.Id}/channels", new
        {
            name = "General",
            description = "Project-wide human discussion.",
            visibility = "Project"
        });
        Assert.AreEqual(HttpStatusCode.Created, createGeneral.StatusCode);
        var general = await createGeneral.Content.ReadFromJsonAsync<ProjectChannelChatSummary>();
        Assert.IsNotNull(general);
        Assert.AreEqual("general", general!.Slug);
        Assert.IsTrue(general.CanPostMessages);

        var createPrivate = await ownerClient.PostAsJsonAsync($"/api/projects/{project.Id}/channels", new
        {
            name = "Product planning",
            description = "Restricted human discussion.",
            visibility = "MembersOnly"
        });
        Assert.AreEqual(HttpStatusCode.Created, createPrivate.StatusCode);
        var privateChannel = await createPrivate.Content.ReadFromJsonAsync<ProjectChannelChatSummary>();
        Assert.IsNotNull(privateChannel);

        var duplicate = await ownerClient.PostAsJsonAsync($"/api/projects/{project.Id}/channels", new
        {
            name = "GENERAL",
            description = (string?)null,
            visibility = "Project"
        });
        Assert.AreEqual(HttpStatusCode.Conflict, duplicate.StatusCode);

        var memberToken = await SelectTenantAsync(await LoginAsync(memberEmail, memberPassword));
        var addProjectMembership = await ownerClient.PutAsJsonAsync(
            $"/api/projects/{project.Id}/members/{memberUserId}",
            new { projectRole = "Viewer" });
        Assert.AreEqual(HttpStatusCode.OK, addProjectMembership.StatusCode);
        using var memberClient = GetAuthedClient(memberToken);
        var beforeMembership = await memberClient.GetFromJsonAsync<ProjectChannelChatListResponse>(
            $"/api/projects/{project.Id}/channels");
        Assert.IsNotNull(beforeMembership);
        Assert.IsFalse(beforeMembership!.CanCreateChannels);
        CollectionAssert.AreEqual(new[] { "general" }, beforeMembership.Channels.Select(channel => channel.Slug).ToArray());

        var hiddenDetail = await memberClient.GetAsync(
            $"/api/projects/{project.Id}/channels/{privateChannel!.Slug}");
        Assert.AreEqual(HttpStatusCode.NotFound, hiddenDetail.StatusCode);
        var hiddenAssistantProbe = await memberClient.PostAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{privateChannel.Slug}/messages",
            new { message = "@IronDev disclose this channel" });
        Assert.AreEqual(HttpStatusCode.NotFound, hiddenAssistantProbe.StatusCode);

        var addReadOnly = await ownerClient.PutAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{privateChannel.ChannelId}/members/{memberUserId}",
            new { channelRole = "ReadOnly", notificationLevel = "Mentions" });
        Assert.AreEqual(HttpStatusCode.OK, addReadOnly.StatusCode);

        var afterMembership = await memberClient.GetFromJsonAsync<ProjectChannelChatListResponse>(
            $"/api/projects/{project.Id}/channels");
        var visiblePrivate = afterMembership!.Channels.Single(channel => channel.Slug == privateChannel.Slug);
        Assert.AreEqual("ReadOnly", visiblePrivate.CurrentUserRole);
        Assert.IsFalse(visiblePrivate.CanPostMessages);

        var readOnlyPost = await memberClient.PostAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{privateChannel.Slug}/messages",
            new { message = "This must be refused." });
        Assert.AreEqual(HttpStatusCode.Forbidden, readOnlyPost.StatusCode);

        const string humanMessage = "Approved as a discussion note only; this grants no workflow authority.";
        var postHuman = await ownerClient.PostAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{general.Slug}/messages",
            new { message = humanMessage });
        Assert.AreEqual(HttpStatusCode.OK, postHuman.StatusCode);
        var humanPostResult = await postHuman.Content.ReadFromJsonAsync<ProjectChannelPostMessageResult>();
        Assert.IsNotNull(humanPostResult);
        var saved = humanPostResult!.Message;
        Assert.AreEqual("User", saved.Role);
        Assert.AreEqual(humanMessage, saved.Message);
        Assert.IsNull(humanPostResult.AssistantTurn);
        StringAssert.Contains(saved.Boundary, "not approval");

        var memberUnread = await memberClient.GetFromJsonAsync<ProjectChannelChatListResponse>(
            $"/api/projects/{project.Id}/channels");
        var unreadGeneral = memberUnread!.Channels.Single(channel => channel.Slug == general.Slug);
        Assert.AreEqual(1, unreadGeneral.UnreadCount);
        Assert.IsNull(unreadGeneral.LastReadMessageId);

        var memberDetail = await memberClient.GetFromJsonAsync<ProjectChannelChatDetail>(
            $"/api/projects/{project.Id}/channels/{general.Slug}");
        Assert.AreEqual(1, memberDetail!.ReadState.UnreadCount);
        Assert.AreEqual("Unavailable", memberDetail.Presence.Status);
        Assert.IsNull(memberDetail.Presence.ActiveViewerCount);
        StringAssert.Contains(memberDetail.Presence.Reason, "no active viewer count is inferred");

        var markRead = await memberClient.PostAsync(
            $"/api/projects/{project.Id}/channels/{general.Slug}/read",
            null);
        Assert.AreEqual(HttpStatusCode.OK, markRead.StatusCode);
        var readState = await markRead.Content.ReadFromJsonAsync<ProjectChannelReadState>();
        Assert.IsNotNull(readState);
        Assert.AreEqual(0, readState!.UnreadCount);
        Assert.AreEqual(saved.MessageId, readState.LastReadMessageId);
        StringAssert.Contains(readState.Boundary, "unread-count convenience");

        var afterRead = await memberClient.GetFromJsonAsync<ProjectChannelChatListResponse>(
            $"/api/projects/{project.Id}/channels");
        Assert.AreEqual(0, afterRead!.Channels.Single(channel => channel.Slug == general.Slug).UnreadCount);

        var mentionSource = await ownerClient.GetFromJsonAsync<ProjectChannelChatDetail>(
            $"/api/projects/{project.Id}/channels/{general.Slug}");
        var mentionCandidate = mentionSource!.MentionCandidates.Single(candidate => candidate.UserId == memberUserId);
        Assert.AreEqual("Channel Reader", mentionCandidate.DisplayName);
        Assert.AreEqual("channel-reader", mentionCandidate.Handle);

        var postMention = await ownerClient.PostAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{general.Slug}/messages",
            new { message = $"@{mentionCandidate.Handle} please review this discussion." });
        Assert.AreEqual(HttpStatusCode.OK, postMention.StatusCode);
        var mentionMessage = (await postMention.Content.ReadFromJsonAsync<ProjectChannelPostMessageResult>())!.Message;

        var mentionNotifications = await memberClient.GetFromJsonAsync<ProjectNotificationListResponse>(
            $"/api/projects/{project.Id}/notifications");
        Assert.AreEqual(1, mentionNotifications!.UnreadCount);
        var mentionNotification = mentionNotifications.Notifications.Single();
        Assert.AreEqual("Mention", mentionNotification.Kind);
        Assert.AreEqual(mentionMessage.MessageId, mentionNotification.MessageId);
        StringAssert.Contains(mentionNotification.Title, "mentioned you");
        StringAssert.Contains(mentionNotification.Boundary, "not approval");

        var wrongRecipientRead = await ownerClient.PostAsync(
            $"/api/projects/{project.Id}/notifications/{mentionNotification.NotificationId}/read",
            null);
        Assert.AreEqual(HttpStatusCode.NotFound, wrongRecipientRead.StatusCode);

        var ownerNotifications = await ownerClient.GetFromJsonAsync<ProjectNotificationListResponse>(
            $"/api/projects/{project.Id}/notifications");
        Assert.AreEqual(0, ownerNotifications!.Notifications.Count);

        var markNotificationRead = await memberClient.PostAsync(
            $"/api/projects/{project.Id}/notifications/{mentionNotification.NotificationId}/read",
            null);
        Assert.AreEqual(HttpStatusCode.NoContent, markNotificationRead.StatusCode);
        var afterNotificationRead = await memberClient.GetFromJsonAsync<ProjectNotificationListResponse>(
            $"/api/projects/{project.Id}/notifications");
        Assert.AreEqual(0, afterNotificationRead!.UnreadCount);
        Assert.IsTrue(afterNotificationRead.Notifications.Single().IsRead);

        var enableAll = await ownerClient.PutAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{general.ChannelId}/members/{memberUserId}",
            new { channelRole = "Member", notificationLevel = "All" });
        Assert.AreEqual(HttpStatusCode.OK, enableAll.StatusCode);
        var postForAll = await ownerClient.PostAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{general.Slug}/messages",
            new { message = "Plain channel update for subscribers." });
        Assert.AreEqual(HttpStatusCode.OK, postForAll.StatusCode);
        var allNotifications = await memberClient.GetFromJsonAsync<ProjectNotificationListResponse>(
            $"/api/projects/{project.Id}/notifications");
        Assert.AreEqual(1, allNotifications!.UnreadCount);
        Assert.AreEqual("ChannelMessage", allNotifications.Notifications.First().Kind);

        var disableNotifications = await ownerClient.PutAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{general.ChannelId}/members/{memberUserId}",
            new { channelRole = "Member", notificationLevel = "None" });
        Assert.AreEqual(HttpStatusCode.OK, disableNotifications.StatusCode);
        var mutedMention = await ownerClient.PostAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{general.Slug}/messages",
            new { message = $"@{mentionCandidate.Handle} this mention remains durable but muted." });
        Assert.AreEqual(HttpStatusCode.OK, mutedMention.StatusCode);
        var mutedMentionMessage = (await mutedMention.Content.ReadFromJsonAsync<ProjectChannelPostMessageResult>())!.Message;
        var afterMutedMention = await memberClient.GetFromJsonAsync<ProjectNotificationListResponse>(
            $"/api/projects/{project.Id}/notifications");
        Assert.AreEqual(2, afterMutedMention!.Notifications.Count);

        var invokeAssistant = await ownerClient.PostAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{general.Slug}/messages",
            new { message = "@IronDev summarize what this project is for. Do not change project state." });
        Assert.AreEqual(HttpStatusCode.OK, invokeAssistant.StatusCode);
        var requested = await invokeAssistant.Content.ReadFromJsonAsync<ProjectChannelPostMessageResult>();
        Assert.IsNotNull(requested);
        Assert.AreEqual("User", requested!.Message.Role);
        Assert.IsNotNull(requested.AssistantTurn);
        Assert.AreEqual("Requested", requested.AssistantTurn!.Status);
        Assert.AreEqual(1, requested.AssistantTurn.RequestedByUserId);
        Assert.AreEqual(requested.Message.MessageId, requested.AssistantTurn.RequestMessageId);
        StringAssert.Contains(requested.AssistantTurn.Prompt, "summarize what this project is for");
        StringAssert.Contains(requested.AssistantTurn.Boundary, "not approval");

        var otherMemberComplete = await memberClient.PostAsync(
            $"/api/projects/{project.Id}/channels/{general.Slug}/assistant-turns/{requested.AssistantTurn.TurnId}/complete",
            null);
        Assert.AreEqual(HttpStatusCode.NotFound, otherMemberComplete.StatusCode);

        var completeAssistant = await ownerClient.PostAsync(
            $"/api/projects/{project.Id}/channels/{general.Slug}/assistant-turns/{requested.AssistantTurn.TurnId}/complete",
            null);
        Assert.AreEqual(HttpStatusCode.OK, completeAssistant.StatusCode);
        var completed = await completeAssistant.Content.ReadFromJsonAsync<ProjectChannelAssistantCompletionResult>();
        Assert.IsNotNull(completed);
        Assert.AreEqual("Answered", completed!.AssistantTurn.Status);
        Assert.IsNotNull(completed.AssistantTurn.CompletedUtc);
        Assert.IsNotNull(completed.ResponseMessage);
        Assert.AreEqual("Assistant", completed.ResponseMessage!.Role);
        Assert.AreEqual("IronDev", completed.ResponseMessage.AuthorDisplayName);
        Assert.AreEqual(requested.Message.MessageId, completed.ResponseMessage.ReplyToMessageId);
        StringAssert.Contains(completed.ResponseMessage.Boundary, "not approval");

        var idempotentComplete = await ownerClient.PostAsync(
            $"/api/projects/{project.Id}/channels/{general.Slug}/assistant-turns/{requested.AssistantTurn.TurnId}/complete",
            null);
        Assert.AreEqual(HttpStatusCode.OK, idempotentComplete.StatusCode);
        var idempotent = await idempotentComplete.Content.ReadFromJsonAsync<ProjectChannelAssistantCompletionResult>();
        Assert.AreEqual(completed.ResponseMessage.MessageId, idempotent!.ResponseMessage!.MessageId);

        var detail = await ownerClient.GetFromJsonAsync<ProjectChannelChatDetail>(
            $"/api/projects/{project.Id}/channels/{general.Slug}");
        Assert.IsNotNull(detail);
        Assert.AreEqual(6, detail!.Messages.Count);
        Assert.AreEqual(humanMessage, detail.Messages[0].Message);
        Assert.AreEqual("Answered", detail.AssistantTurns.Single().Status);
        Assert.AreEqual(completed.ResponseMessage.MessageId, detail.AssistantTurns.Single().ResponseMessageId);
        StringAssert.Contains(detail.AssistantParticipationStatus, "explicitly mentions @IronDev");

        await using var connection = new SqlConnection(ConnectionString);
        var persistedRoles = (await connection.QueryAsync<string>(
            "SELECT Role FROM dbo.ProjectChannelMessages WHERE ChannelId = @ChannelId ORDER BY Id",
            new { general.ChannelId })).ToArray();
        CollectionAssert.AreEqual(new[] { "User", "User", "User", "User", "User", "Assistant" }, persistedRoles);
        var persistedMentionUsers = (await connection.QueryAsync<int>(
            "SELECT MentionedUserId FROM dbo.ProjectChannelMessageMentions WHERE MessageId IN (@MentionMessageId, @MutedMentionMessageId) ORDER BY MessageId",
            new { MentionMessageId = mentionMessage.MessageId, MutedMentionMessageId = mutedMentionMessage.MessageId })).ToArray();
        CollectionAssert.AreEqual(new[] { memberUserId, memberUserId }, persistedMentionUsers);
        var persistedTurn = await connection.QuerySingleAsync<(int RequestedByUserId, string Status, long RequestMessageId, long? ResponseMessageId)>(
            "SELECT RequestedByUserId, Status, RequestMessageId, ResponseMessageId FROM dbo.ProjectChannelAssistantTurns WHERE Id = @TurnId",
            new { TurnId = requested.AssistantTurn.TurnId });
        Assert.AreEqual(1, persistedTurn.RequestedByUserId);
        Assert.AreEqual("Answered", persistedTurn.Status);
        Assert.AreEqual(requested.Message.MessageId, persistedTurn.RequestMessageId);
        Assert.AreEqual(completed.ResponseMessage.MessageId, persistedTurn.ResponseMessageId);
    }

    [TestMethod]
    public async Task ProjectChannelAssistantFailure_ShouldPersistFailedTurnAndSavedRequest()
    {
        var tenantToken = await SelectTenantAsync(await LoginAsync());
        using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IronDev.Core.Interfaces.IProjectChatResponseService>();
                services.AddScoped<IronDev.Core.Interfaces.IProjectChatResponseService, FailingProjectChatResponseService>();
            });
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tenantToken);

        var project = await CreateProjectAsync(client, "Shared Channel Assistant Failure");
        var createChannel = await client.PostAsJsonAsync($"/api/projects/{project.Id}/channels", new
        {
            name = "General",
            description = "Failure-state contract.",
            visibility = "Project"
        });
        Assert.AreEqual(HttpStatusCode.Created, createChannel.StatusCode);
        var channel = await createChannel.Content.ReadFromJsonAsync<ProjectChannelChatSummary>();

        var post = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/channels/{channel!.Slug}/messages",
            new { message = "@IronDev inspect this failure path" });
        Assert.AreEqual(HttpStatusCode.OK, post.StatusCode);
        var requested = await post.Content.ReadFromJsonAsync<ProjectChannelPostMessageResult>();
        Assert.AreEqual("Requested", requested!.AssistantTurn!.Status);

        var complete = await client.PostAsync(
            $"/api/projects/{project.Id}/channels/{channel.Slug}/assistant-turns/{requested.AssistantTurn.TurnId}/complete",
            null);
        Assert.AreEqual(HttpStatusCode.OK, complete.StatusCode);
        var failed = await complete.Content.ReadFromJsonAsync<ProjectChannelAssistantCompletionResult>();
        Assert.AreEqual("Failed", failed!.AssistantTurn.Status);
        Assert.IsNull(failed.ResponseMessage);
        StringAssert.Contains(failed.AssistantTurn.FailureReason, "message is saved");

        var detail = await client.GetFromJsonAsync<ProjectChannelChatDetail>(
            $"/api/projects/{project.Id}/channels/{channel.Slug}");
        Assert.AreEqual("Failed", detail!.AssistantTurns.Single().Status);
        Assert.AreEqual("User", detail.Messages.Single().Role);
        Assert.AreEqual("@IronDev inspect this failure path", detail.Messages.Single().Message);
    }

    [TestMethod]
    public async Task TicketBuildRuns_ProjectScopedEndpoints_ShouldStartListAndReturnRunDetails()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Ticket Build Runs API Project");
        var createResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets", new CreateProjectTicketRequest
        {
            Title = "Expose project-scoped disposable run endpoints",
            Summary = "The alpha cockpit should never use a global run endpoint for ticket work.",
            Priority = "High",
            Type = "Workflow"
        });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

        var ticket = await createResponse.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(ticket);

        var startResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tickets/{ticket!.Id}/build-runs/disposable",
            new StartTicketBuildRunRequest { MaxRetries = 1 });
        Assert.AreEqual(HttpStatusCode.OK, startResponse.StatusCode);

        var started = await startResponse.Content.ReadFromJsonAsync<TicketBuildRunDto>();
        Assert.IsNotNull(started);
        Assert.AreEqual(project.Id, started!.ProjectId);
        Assert.AreEqual(ticket.Id, started.TicketId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(started.RunId));

        var listResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/build-runs");
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var runs = await listResponse.Content.ReadFromJsonAsync<TicketBuildRunSummaryDto[]>();
        Assert.IsNotNull(runs);
        Assert.AreEqual(1, runs!.Length);
        Assert.AreEqual(started.RunId, runs[0].RunId);
        Assert.IsTrue(runs[0].IsDisposable);

        var detailResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/build-runs/{started.RunId}");
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<TicketBuildRunDetailDto>();
        Assert.IsNotNull(detail);
        Assert.AreEqual(started.RunId, detail!.RunId);
        Assert.AreEqual(project.Id, detail.ProjectId);
        Assert.AreEqual(ticket.Id, detail.TicketId);
        Assert.IsTrue(detail.IsDisposable);
        Assert.IsTrue(detail.Events.Count > 0);
    }

    [TestMethod]
    public async Task TicketBuildRuns_DisposableEndpoint_ShouldExecuteBackendOwnedCommandsAndPersistEvidence()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);
        var fixture = CreateDotNetProjectFixture(broken: false);

        try
        {
            var project = await CreateProjectAsync(client, "Disposable Ticket Build E2E Project", fixture.SourcePath);
            var createResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets", new CreateProjectTicketRequest
            {
                Title = "Prove disposable ticket build run",
                Summary = "The endpoint should create a durable run, execute backend-owned commands, and persist evidence.",
                Priority = "High",
                Type = "Workflow"
            });
            Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

            var ticket = await createResponse.Content.ReadFromJsonAsync<ProjectTicket>();
            Assert.IsNotNull(ticket);

            var startResponse = await client.PostAsJsonAsync(
                $"/api/projects/{project.Id}/tickets/{ticket!.Id}/build-runs/disposable",
                new StartTicketBuildRunRequest { MaxRetries = 1 });
            Assert.AreEqual(HttpStatusCode.OK, startResponse.StatusCode);

            var started = await startResponse.Content.ReadFromJsonAsync<TicketBuildRunDto>();
            Assert.IsNotNull(started);
            Assert.AreEqual("Completed", started!.Status);

            await using var connection = new SqlConnection(ConnectionString);
            var runCount = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(1) FROM dbo.Runs WHERE RunId = @RunId AND ProjectId = @ProjectId AND TicketId = @TicketId AND IsDisposable = 1",
                new { started.RunId, ProjectId = project.Id, TicketId = ticket.Id });
            Assert.AreEqual(1, runCount);

            var eventTypes = (await connection.QueryAsync<string>(
                "SELECT EventType FROM dbo.RunEvents WHERE RunId = @RunId ORDER BY TimestampUtc, Id",
                new { started.RunId })).ToArray();
            CollectionAssert.Contains(eventTypes, "CodeStandardsCompleted");
            CollectionAssert.Contains(eventTypes, "DisposableWorkspaceCreated");
            CollectionAssert.Contains(eventTypes, "DisposableCommandStarted");
            CollectionAssert.Contains(eventTypes, "DisposableCommandCompleted");
            CollectionAssert.Contains(eventTypes, "RunCompleted");

            var detailResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/build-runs/{started.RunId}");
            Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
            var detail = await detailResponse.Content.ReadFromJsonAsync<TicketBuildRunDetailDto>();
            Assert.IsNotNull(detail);
            Assert.IsTrue(detail!.Evidence.Any(item => item.Path.EndsWith(".stdout.log", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(detail.Evidence.Any(item => item.Path.EndsWith("code-standards.json", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteIfExists(fixture.RootPath);
        }
    }

    [TestMethod]
    public async Task TicketBuildRuns_FailedDisposableEndpoint_ShouldPreserveWorkspaceAndEvidence()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);
        var fixture = CreateDotNetProjectFixture(broken: true);

        try
        {
            var project = await CreateProjectAsync(client, "Disposable Ticket Build Failure Project", fixture.SourcePath);
            var createResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets", new CreateProjectTicketRequest
            {
                Title = "Prove disposable failure evidence",
                Summary = "The endpoint should preserve the failed disposable workspace and command logs.",
                Priority = "High",
                Type = "Workflow"
            });
            Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

            var ticket = await createResponse.Content.ReadFromJsonAsync<ProjectTicket>();
            Assert.IsNotNull(ticket);

            var startResponse = await client.PostAsJsonAsync(
                $"/api/projects/{project.Id}/tickets/{ticket!.Id}/build-runs/disposable",
                new StartTicketBuildRunRequest { MaxRetries = 1 });
            Assert.AreEqual(HttpStatusCode.OK, startResponse.StatusCode);

            var started = await startResponse.Content.ReadFromJsonAsync<TicketBuildRunDto>();
            Assert.IsNotNull(started);
            Assert.AreEqual("Failed", started!.Status);

            var detailResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/build-runs/{started.RunId}");
            Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
            var detail = await detailResponse.Content.ReadFromJsonAsync<TicketBuildRunDetailDto>();
            Assert.IsNotNull(detail);
            Assert.IsFalse(string.IsNullOrWhiteSpace(detail!.FailureReason));
            Assert.IsTrue(detail.Evidence.Any(item => item.Path.EndsWith(".stderr.log", StringComparison.OrdinalIgnoreCase)));

            var preserved = detail.Events.FirstOrDefault(runEvent => runEvent.EventType == "DisposableWorkspacePreserved");
            Assert.IsNotNull(preserved);
            Assert.IsTrue(preserved!.Payload.TryGetValue("workspacePath", out var workspacePath));
            Assert.IsTrue(Directory.Exists(workspacePath), "Failed disposable workspace should be preserved for debugging.");
        }
        finally
        {
            DeleteIfExists(fixture.RootPath);
        }
    }

    [TestMethod]
    public async Task TicketBuildRuns_WrongProjectWrongTicketOrMissingRun_ShouldReturnNotFound()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Ticket Build Runs Owner Project");
        var otherProject = await CreateProjectAsync(client, "Ticket Build Runs Other Project");
        var createResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets", new CreateProjectTicketRequest
        {
            Title = "Protect build run boundaries",
            Summary = "Run details must remain scoped to the project and ticket route.",
            Priority = "Medium",
            Type = "Workflow"
        });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

        var ticket = await createResponse.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(ticket);

        var startResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tickets/{ticket!.Id}/build-runs/disposable",
            new StartTicketBuildRunRequest { MaxRetries = 1 });
        Assert.AreEqual(HttpStatusCode.OK, startResponse.StatusCode);

        var started = await startResponse.Content.ReadFromJsonAsync<TicketBuildRunDto>();
        Assert.IsNotNull(started);

        var wrongProjectStart = await client.PostAsJsonAsync(
            $"/api/projects/{otherProject.Id}/tickets/{ticket.Id}/build-runs/disposable",
            new StartTicketBuildRunRequest { MaxRetries = 1 });
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectStart.StatusCode);

        var wrongProjectList = await client.GetAsync($"/api/projects/{otherProject.Id}/tickets/{ticket.Id}/build-runs");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectList.StatusCode);

        var wrongTicketDetail = await client.GetAsync($"/api/projects/{project.Id}/tickets/987654321/build-runs/{started!.RunId}");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongTicketDetail.StatusCode);

        var missingRun = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/build-runs/missing-run");
        Assert.AreEqual(HttpStatusCode.NotFound, missingRun.StatusCode);
    }

    [TestMethod]
    public async Task TicketEvidenceSummary_WithNoLinkedRun_ShouldReturnHonestEmptyEvidence()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Ticket Evidence Summary Test");
        var saveTicket = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/legacy", new ProjectTicket
        {
            ProjectId = project.Id,
            SessionId = Guid.NewGuid(),
            Title = "Evidence summary has no linked run",
            Summary = "The summary should not invent run evidence.",
            Content = "No run report source relationship exists yet.",
            SourceChatSessionId = 44,
            SourceChatMessageId = 45,
            SourceDocumentVersionId = 12
        });
        Assert.AreEqual(HttpStatusCode.OK, saveTicket.StatusCode);

        var ticket = await saveTicket.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(ticket);

        var response = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket!.Id}/evidence-summary");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<TicketEvidenceSummaryDto>();
        Assert.IsNotNull(summary);
        Assert.AreEqual(ticket.Id, summary!.TicketId);
        Assert.IsNull(summary.LatestRun);
        Assert.IsNull(summary.LatestPromotionPackage);
        Assert.AreEqual(0, summary.LinkedRunCount);
        Assert.AreEqual(1, summary.LinkedDocumentCount);
        Assert.AreEqual(2, summary.LinkedTraceCount);
        CollectionAssert.Contains(summary.BlockedActions.ToList(), "No execution run is linked to this ticket yet.");
    }

    [TestMethod]
    public async Task TicketEvidenceSummary_ForMissingOrWrongProjectTicket_ShouldReturnNotFound()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Ticket Evidence Owner Project");
        var otherProject = await CreateProjectAsync(client, "Ticket Evidence Wrong Project");
        var createResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets", new CreateProjectTicketRequest
        {
            Title = "Project-owned evidence ticket",
            Summary = "Evidence summary must enforce the project route boundary.",
            Priority = "Medium",
            Type = "Task"
        });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

        var ticket = await createResponse.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(ticket);

        var wrongProjectResponse = await client.GetAsync($"/api/projects/{otherProject.Id}/tickets/{ticket!.Id}/evidence-summary");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectResponse.StatusCode);

        var missingResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/987654321/evidence-summary");
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [TestMethod]
    public async Task TicketRunReview_ForLinkedRun_ShouldReturnTicketScopedReview()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Ticket Run Review Project");
        var createResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets", new CreateProjectTicketRequest
        {
            Title = "Review latest disposable run",
            Summary = "Run review should be scoped to the source ticket.",
            Priority = "High",
            Type = "Workflow"
        });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

        var ticket = await createResponse.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(ticket);

        var runResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tickets/{ticket!.Id}/build-runs",
            new StartTicketBuildRunRequest { MaxRetries = 1 });
        Assert.AreEqual(HttpStatusCode.OK, runResponse.StatusCode);

        var run = await runResponse.Content.ReadFromJsonAsync<TicketBuildRunDto>();
        Assert.IsNotNull(run);
        Assert.IsFalse(string.IsNullOrWhiteSpace(run!.RunId));

        var evidenceResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/evidence-summary");
        Assert.AreEqual(HttpStatusCode.OK, evidenceResponse.StatusCode);
        var evidence = await evidenceResponse.Content.ReadFromJsonAsync<TicketEvidenceSummaryDto>();
        Assert.IsNotNull(evidence);
        Assert.AreEqual(run.RunId, evidence!.LatestRun?.RunId);

        var reviewResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/build-runs/{run.RunId}/review");
        Assert.AreEqual(HttpStatusCode.OK, reviewResponse.StatusCode);

        var review = await reviewResponse.Content.ReadFromJsonAsync<TicketRunReviewDto>();
        Assert.IsNotNull(review);
        Assert.AreEqual(run.RunId, review!.RunId);
        Assert.AreEqual(project.Id, review.ProjectId);
        Assert.AreEqual(ticket.Id, review.TicketId);
        Assert.AreEqual("Review latest disposable run", review.TicketTitle);
        Assert.IsTrue(review.IsDisposableRun);
        Assert.IsTrue(review.Events.Count > 0);
    }

    [TestMethod]
    public async Task TicketRunReview_ForMissingOrWrongScope_ShouldReturnNotFound()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Ticket Run Review Owner");
        var otherProject = await CreateProjectAsync(client, "Ticket Run Review Other Project");
        var createResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets", new CreateProjectTicketRequest
        {
            Title = "Protect ticket run review",
            Summary = "Run details must not leak across project or ticket boundaries.",
            Priority = "Medium",
            Type = "Workflow"
        });
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

        var ticket = await createResponse.Content.ReadFromJsonAsync<ProjectTicket>();
        Assert.IsNotNull(ticket);

        var runResponse = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/tickets/{ticket!.Id}/build-runs",
            new StartTicketBuildRunRequest { MaxRetries = 1 });
        Assert.AreEqual(HttpStatusCode.OK, runResponse.StatusCode);

        var run = await runResponse.Content.ReadFromJsonAsync<TicketBuildRunDto>();
        Assert.IsNotNull(run);

        var missingRun = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/build-runs/missing-run/review");
        Assert.AreEqual(HttpStatusCode.NotFound, missingRun.StatusCode);

        var wrongProject = await client.GetAsync($"/api/projects/{otherProject.Id}/tickets/{ticket.Id}/build-runs/{run!.RunId}/review");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProject.StatusCode);

        var wrongTicket = await client.GetAsync($"/api/projects/{project.Id}/tickets/987654321/build-runs/{run.RunId}/review");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongTicket.StatusCode);
    }

    [TestMethod]
    [TestCategory("ProcessExecution")]
    public async Task DiscussionCodeLoop_ShouldUseGenericProposalRunAndReviewPackagePipeline()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Alpha Discussion To Code Project");
        var scenariosResponse = await client.GetAsync($"/api/projects/{project.Id}/code-scenarios");
        Assert.AreEqual(HttpStatusCode.OK, scenariosResponse.StatusCode);
        var scenarios = await scenariosResponse.Content.ReadFromJsonAsync<IReadOnlyList<BuildScenario>>();
        Assert.IsNotNull(scenarios);
        CollectionAssert.IsSubsetOf(
            new[] { "console.hello-world", "console.calculator", "aspnet.health-api" },
            scenarios!.Select(item => item.ScenarioId).ToArray());

        var discussion = await client.PostAsJsonAsync($"/api/projects/{project.Id}/discussions", new SaveDiscussionRequest
        {
            Title = "Hello World Alpha discussion",
            Content = "Create a tiny C# console app that prints Hello from IronDev Alpha."
        });
        Assert.AreEqual(HttpStatusCode.OK, discussion.StatusCode);
        var discussionBody = await discussion.Content.ReadFromJsonAsync<SaveDiscussionResponse>();
        Assert.IsNotNull(discussionBody);
        Assert.IsTrue(discussionBody!.DocumentId > 0);
        Assert.IsTrue(discussionBody.DocumentVersionId > 0);

        var ticketResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/documents/{discussionBody.DocumentVersionId}/tickets", new CreateTicketFromDocumentRequest());
        Assert.AreEqual(HttpStatusCode.OK, ticketResponse.StatusCode);
        var ticketBody = await ticketResponse.Content.ReadFromJsonAsync<CreateTicketFromDocumentResponse>();
        Assert.IsNotNull(ticketBody);
        Assert.AreEqual(discussionBody.DocumentVersionId, ticketBody!.SourceDocumentVersionId);

        var ticket = await client.GetFromJsonAsync<ProjectTicket>($"/api/projects/{project.Id}/tickets/{ticketBody.TicketId}");
        Assert.IsNotNull(ticket);
        Assert.AreEqual("Build Hello World Console App", ticket!.Title);
        Assert.AreEqual(discussionBody.DocumentVersionId, ticket.SourceDocumentVersionId);

        var reviewResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/review", new RunTicketReviewRequest());
        Assert.AreEqual(HttpStatusCode.OK, reviewResponse.StatusCode);
        var review = await reviewResponse.Content.ReadFromJsonAsync<RunTicketReviewResponse>();
        Assert.IsNotNull(review);
        Assert.IsTrue(review!.Result.Decision.Proceed);
        CollectionAssert.AreEquivalent(
            new[] { "Plan", "Proposal", "Validation", "Governance" },
            review.Result.Contributions.Select(item => item.Role).ToArray());

        var runResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/disposable-code-runs", new StartDisposableCodeRunRequest
        {
            ReviewId = review.ReviewId
        });
        Assert.AreEqual(HttpStatusCode.OK, runResponse.StatusCode);
        var run = await runResponse.Content.ReadFromJsonAsync<StartDisposableCodeRunResponse>();
        Assert.IsNotNull(run);
        Assert.AreEqual("PausedForApproval", run!.State);
        Assert.IsTrue(run.IsDisposable);

        await using var connection = new SqlConnection(ConnectionString);
        var durableRunCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(1) FROM dbo.Runs WHERE RunId = @RunId AND ProjectId = @ProjectId AND TicketId = @TicketId AND State = 'PausedForApproval' AND IsDisposable = 1",
            new { run.RunId, ProjectId = project.Id, TicketId = ticket.Id });
        Assert.AreEqual(1, durableRunCount);

        var events = (await connection.QueryAsync<string>(
            "SELECT EventType FROM dbo.RunEvents WHERE RunId = @RunId ORDER BY TimestampUtc, Id",
            new { run.RunId })).ToArray();
        foreach (var expected in new[]
        {
            "RunCreated",
            "ReviewLinked",
            "WorkspacePreparing",
            "WorkspaceReady",
            "CodeGenerationStarted",
            "CodeGenerationCompleted",
            "CommandStarted",
            "CommandCompleted",
            "OutputVerified",
            "CodeStandardsStarted",
            "CodeStandardsCompleted",
            "RunPausedForApproval"
        })
        {
            CollectionAssert.Contains(events, expected);
        }

        var detailResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/build-runs/{run.RunId}");
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<TicketBuildRunDetailDto>();
        Assert.IsNotNull(detail);
        Assert.AreEqual("PausedForApproval", detail!.Status);
        Assert.IsTrue(detail.RequiresHumanApproval);
        Assert.IsTrue(detail.Evidence.Any(item => item.Path.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(detail.Evidence.Any(item => item.Path.EndsWith("dotnet-run.stdout.log", StringComparison.OrdinalIgnoreCase)));

        var programEvidence = detail.Evidence.First(item => item.Path.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase));
        var programText = await File.ReadAllTextAsync(programEvidence.Path);
        StringAssert.Contains(programText, "Hello from IronDev Alpha");

        var runOutput = await File.ReadAllTextAsync(detail.Evidence.First(item => item.Path.EndsWith("dotnet-run.stdout.log", StringComparison.OrdinalIgnoreCase)).Path);
        StringAssert.Contains(runOutput, "Hello from IronDev Alpha");

        var workspaceEvent = detail.Events.First(item => item.EventType == "WorkspaceReady");
        Assert.IsTrue(workspaceEvent.Payload.TryGetValue("workspacePath", out var workspacePath));
        Assert.IsTrue(workspacePath!.Contains("IronDevTestWorkspaces", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(workspacePath.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase));

        var packageResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/build-runs/{run.RunId}/review-package");
        Assert.AreEqual(HttpStatusCode.OK, packageResponse.StatusCode);
        var package = await packageResponse.Content.ReadFromJsonAsync<RunReviewPackage>();
        Assert.IsNotNull(package);
        Assert.AreEqual("PausedForApproval", package!.State);
        Assert.IsTrue(package.GeneratedFiles.Any(item => item.RelativePath == "HelloWorldAlpha/Program.cs"));
        Assert.IsTrue(package.GeneratedFiles.Any(item => item.RelativePath == "HelloWorldAlpha/HelloWorldAlpha.csproj"));
        Assert.IsTrue(package.CommandEvidence.Any(item => string.Equals(item.Command, "dotnet build", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(package.CommandEvidence.Any(item => string.Equals(item.Command, "dotnet run", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(package.OutputVerification.Verified);
        Assert.IsFalse(string.IsNullOrWhiteSpace(package.CodeStandards.Summary));
        Assert.IsFalse(string.IsNullOrWhiteSpace(package.FileSetHash));
        Assert.IsTrue(package.Risks.Count > 0);
        Assert.IsTrue(package.HumanReviewChecklist.Count > 0);

        var otherProject = await CreateProjectAsync(client, "Wrong Project Review Package Guard");
        var wrongProjectPackage = await client.GetAsync($"/api/projects/{otherProject.Id}/tickets/{ticket.Id}/build-runs/{run.RunId}/review-package");
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectPackage.StatusCode);
    }

    [TestMethod]
    [TestCategory("ProcessExecution")]
    public async Task DiscussionCodeLoop_CalculatorScenario_ShouldUseSameProposalRunPipeline()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Calculator Scenario Project");
        var discussion = await client.PostAsJsonAsync($"/api/projects/{project.Id}/discussions", new SaveDiscussionRequest
        {
            Title = "Calculator console discussion",
            Content = "Create a calculator console app that adds two numbers."
        });
        Assert.AreEqual(HttpStatusCode.OK, discussion.StatusCode);
        var discussionBody = await discussion.Content.ReadFromJsonAsync<SaveDiscussionResponse>();
        Assert.IsNotNull(discussionBody);

        var ticketResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/documents/{discussionBody!.DocumentVersionId}/tickets", new CreateTicketFromDocumentRequest());
        Assert.AreEqual(HttpStatusCode.OK, ticketResponse.StatusCode);
        var ticketBody = await ticketResponse.Content.ReadFromJsonAsync<CreateTicketFromDocumentResponse>();
        Assert.IsNotNull(ticketBody);

        var ticket = await client.GetFromJsonAsync<ProjectTicket>($"/api/projects/{project.Id}/tickets/{ticketBody!.TicketId}");
        Assert.IsNotNull(ticket);
        Assert.AreEqual("Build Calculator Console App", ticket!.Title);

        var reviewResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/review", new RunTicketReviewRequest());
        Assert.AreEqual(HttpStatusCode.OK, reviewResponse.StatusCode);
        var review = await reviewResponse.Content.ReadFromJsonAsync<RunTicketReviewResponse>();
        Assert.IsNotNull(review);
        Assert.AreEqual("console.calculator", review!.Result.ScenarioId);
        Assert.IsTrue(review.Result.Decision.Proceed);

        var runResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/disposable-code-runs", new StartDisposableCodeRunRequest
        {
            ReviewId = review.ReviewId
        });
        Assert.AreEqual(HttpStatusCode.OK, runResponse.StatusCode);
        var run = await runResponse.Content.ReadFromJsonAsync<StartDisposableCodeRunResponse>();
        Assert.IsNotNull(run);
        Assert.AreEqual("PausedForApproval", run!.State);

        var packageResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/build-runs/{run.RunId}/review-package");
        Assert.AreEqual(HttpStatusCode.OK, packageResponse.StatusCode);
        var package = await packageResponse.Content.ReadFromJsonAsync<RunReviewPackage>();
        Assert.IsNotNull(package);
        Assert.AreEqual("PausedForApproval", package!.State);
        Assert.IsTrue(package.GeneratedFiles.Any(item => item.RelativePath.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(package.OutputVerification.Verified);
        Assert.AreEqual(2, package.OutputVerifications.Count);
        Assert.IsTrue(package.OutputVerifications.All(item => item.Verified));
        Assert.IsTrue(package.OutputVerifications.Any(item => item.Expected == "5" && item.Actual.Contains("5", StringComparison.Ordinal)));
        Assert.IsTrue(package.OutputVerifications.Any(item => item.Expected == "6" && item.Actual.Contains("6", StringComparison.Ordinal)));
        Assert.IsTrue(package.CommandEvidence.Any(item => string.Equals(item.Command, "dotnet build", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(package.CommandEvidence.Any(item => string.Equals(item.Command, "dotnet run -- add 2 3", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(package.CommandEvidence.Any(item => string.Equals(item.Command, "dotnet run -- subtract 10 4", StringComparison.OrdinalIgnoreCase)));

        var overrideRun = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/disposable-code-runs", new StartDisposableCodeRunRequest
        {
            ReviewId = review.ReviewId,
            ExpectedOutput = "mutate calculator output"
        });
        Assert.AreEqual(HttpStatusCode.OK, overrideRun.StatusCode);
        var rejectedRun = await overrideRun.Content.ReadFromJsonAsync<StartDisposableCodeRunResponse>();
        Assert.IsNotNull(rejectedRun);
        Assert.AreEqual("Failed", rejectedRun!.State);
    }

    [TestMethod]
    [TestCategory("ProcessExecution")]
    public async Task DiscussionCodeLoop_HealthApiScenario_ShouldUseSameProposalRunPipeline()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Health API Scenario Project");
        var discussion = await client.PostAsJsonAsync($"/api/projects/{project.Id}/discussions", new SaveDiscussionRequest
        {
            Title = "Tiny ASP.NET health API discussion",
            Content = "Create a minimal ASP.NET Core API with a GET /health endpoint that returns healthy."
        });
        Assert.AreEqual(HttpStatusCode.OK, discussion.StatusCode);
        var discussionBody = await discussion.Content.ReadFromJsonAsync<SaveDiscussionResponse>();
        Assert.IsNotNull(discussionBody);

        var ticketResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/documents/{discussionBody!.DocumentVersionId}/tickets", new CreateTicketFromDocumentRequest());
        Assert.AreEqual(HttpStatusCode.OK, ticketResponse.StatusCode);
        var ticketBody = await ticketResponse.Content.ReadFromJsonAsync<CreateTicketFromDocumentResponse>();
        Assert.IsNotNull(ticketBody);

        var ticket = await client.GetFromJsonAsync<ProjectTicket>($"/api/projects/{project.Id}/tickets/{ticketBody!.TicketId}");
        Assert.IsNotNull(ticket);
        Assert.AreEqual("Build Tiny ASP.NET Health API", ticket!.Title);

        var reviewResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/review", new RunTicketReviewRequest());
        Assert.AreEqual(HttpStatusCode.OK, reviewResponse.StatusCode);
        var review = await reviewResponse.Content.ReadFromJsonAsync<RunTicketReviewResponse>();
        Assert.IsNotNull(review);
        Assert.AreEqual("aspnet.health-api", review!.Result.ScenarioId);
        Assert.IsTrue(review.Result.Decision.Proceed);

        var runResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/disposable-code-runs", new StartDisposableCodeRunRequest
        {
            ReviewId = review.ReviewId
        });
        Assert.AreEqual(HttpStatusCode.OK, runResponse.StatusCode);
        var run = await runResponse.Content.ReadFromJsonAsync<StartDisposableCodeRunResponse>();
        Assert.IsNotNull(run);
        Assert.AreEqual("PausedForApproval", run!.State);

        var packageResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticket.Id}/build-runs/{run.RunId}/review-package");
        Assert.AreEqual(HttpStatusCode.OK, packageResponse.StatusCode);
        var package = await packageResponse.Content.ReadFromJsonAsync<RunReviewPackage>();
        Assert.IsNotNull(package);
        Assert.AreEqual("PausedForApproval", package!.State);
        Assert.IsTrue(package.GeneratedFiles.Any(item => item.RelativePath.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(package.GeneratedFiles.Any(item => item.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(1, package.OutputVerifications.Count);
        Assert.IsTrue(package.OutputVerifications[0].Verified);
        Assert.AreEqual("healthy", package.OutputVerifications[0].Expected);
        Assert.AreEqual("healthy", package.OutputVerifications[0].Actual.Trim());
        Assert.IsTrue(package.CommandEvidence.Any(item => string.Equals(item.Command, "dotnet build", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(package.CommandEvidence.Any(item => string.Equals(item.Command, "dotnet run web", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task DiscussionCodeLoop_ShouldRejectRunWithoutReviewOrProceedDecision()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Alpha Debate Guard Project");
        var discussion = await client.PostAsJsonAsync($"/api/projects/{project.Id}/discussions", new SaveDiscussionRequest
        {
            Title = "Non hello discussion",
            Content = "Create a broad undefined feature."
        });
        Assert.AreEqual(HttpStatusCode.OK, discussion.StatusCode);
        var discussionBody = await discussion.Content.ReadFromJsonAsync<SaveDiscussionResponse>();
        Assert.IsNotNull(discussionBody);

        var ticketResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/documents/{discussionBody!.DocumentVersionId}/tickets", new CreateTicketFromDocumentRequest());
        Assert.AreEqual(HttpStatusCode.OK, ticketResponse.StatusCode);
        var ticketBody = await ticketResponse.Content.ReadFromJsonAsync<CreateTicketFromDocumentResponse>();
        Assert.IsNotNull(ticketBody);

        var wrongProject = await CreateProjectAsync(client, "Wrong Project Discussion Code Guard");
        var wrongProjectReview = await client.PostAsJsonAsync($"/api/projects/{wrongProject.Id}/tickets/{ticketBody!.TicketId}/review", new RunTicketReviewRequest());
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectReview.StatusCode);

        var wrongProjectRun = await client.PostAsJsonAsync($"/api/projects/{wrongProject.Id}/tickets/{ticketBody.TicketId}/disposable-code-runs", new StartDisposableCodeRunRequest
        {
            ReviewId = "missing-review"
        });
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectRun.StatusCode);

        var missingReviewRun = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticketBody!.TicketId}/disposable-code-runs", new StartDisposableCodeRunRequest
        {
            ReviewId = "missing-review"
        });
        Assert.AreEqual(HttpStatusCode.NotFound, missingReviewRun.StatusCode);

        var reviewResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticketBody.TicketId}/review", new RunTicketReviewRequest());
        Assert.AreEqual(HttpStatusCode.OK, reviewResponse.StatusCode);
        var review = await reviewResponse.Content.ReadFromJsonAsync<RunTicketReviewResponse>();
        Assert.IsNotNull(review);
        Assert.IsFalse(review!.Result.Decision.Proceed);

        var blockedRun = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticketBody.TicketId}/disposable-code-runs", new StartDisposableCodeRunRequest
        {
            ReviewId = review.ReviewId
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, blockedRun.StatusCode);

        var wrongProjectTicket = await client.PostAsJsonAsync($"/api/projects/{wrongProject.Id}/documents/{discussionBody.DocumentVersionId}/tickets", new CreateTicketFromDocumentRequest());
        Assert.AreEqual(HttpStatusCode.NotFound, wrongProjectTicket.StatusCode);
    }

    [TestMethod]
    [TestCategory("ProcessExecution")]
    public async Task DiscussionCodeLoop_FailedCommand_ShouldPersistFailedRunAndEvidence()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        using var client = GetAuthedClient(tenantToken);

        var project = await CreateProjectAsync(client, "Alpha Failed Command Project");
        var discussion = await client.PostAsJsonAsync($"/api/projects/{project.Id}/discussions", new SaveDiscussionRequest
        {
            Title = "Hello World Alpha discussion",
            Content = "Create a tiny C# console app that prints Hello from IronDev Alpha."
        });
        var discussionBody = await discussion.Content.ReadFromJsonAsync<SaveDiscussionResponse>();
        Assert.IsNotNull(discussionBody);
        var ticketResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/documents/{discussionBody!.DocumentVersionId}/tickets", new CreateTicketFromDocumentRequest());
        var ticketBody = await ticketResponse.Content.ReadFromJsonAsync<CreateTicketFromDocumentResponse>();
        Assert.IsNotNull(ticketBody);
        var reviewResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticketBody!.TicketId}/review", new RunTicketReviewRequest());
        var review = await reviewResponse.Content.ReadFromJsonAsync<RunTicketReviewResponse>();
        Assert.IsNotNull(review);

        var runResponse = await client.PostAsJsonAsync($"/api/projects/{project.Id}/tickets/{ticketBody.TicketId}/disposable-code-runs", new StartDisposableCodeRunRequest
        {
            ReviewId = review!.ReviewId,
            ExpectedOutput = "Hello from IronDev Alpha\r\nthis breaks generated C#"
        });
        Assert.AreEqual(HttpStatusCode.OK, runResponse.StatusCode);
        var run = await runResponse.Content.ReadFromJsonAsync<StartDisposableCodeRunResponse>();
        Assert.IsNotNull(run);
        Assert.AreEqual("Failed", run!.State);

        var detailResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticketBody.TicketId}/build-runs/{run.RunId}");
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<TicketBuildRunDetailDto>();
        Assert.IsNotNull(detail);
        Assert.AreEqual("Failed", detail!.Status);
        Assert.IsFalse(string.IsNullOrWhiteSpace(detail.FailureReason));
        Assert.IsTrue(detail.Events.Any(item => item.EventType == "RunFailed"));
        Assert.IsTrue(detail.Events.Any(item => item.EventType == "CommandCompleted"));
        Assert.IsTrue(detail.Evidence.Any(item => item.Path.EndsWith("dotnet-build.stderr.log", StringComparison.OrdinalIgnoreCase)));

        var packageResponse = await client.GetAsync($"/api/projects/{project.Id}/tickets/{ticketBody.TicketId}/build-runs/{run.RunId}/review-package");
        Assert.AreEqual(HttpStatusCode.OK, packageResponse.StatusCode);
        var package = await packageResponse.Content.ReadFromJsonAsync<RunReviewPackage>();
        Assert.IsNotNull(package);
        Assert.AreEqual("Failed", package!.State);
        Assert.IsTrue(package.Risks.Any(item => item.Contains("failed", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<ProjectDocumentUploadResult> UploadDocumentAsync(
        HttpClient client,
        int projectId,
        string title,
        string fileName)
    {
        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes($"# {title}\n\nExact immutable context for Chat."));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/markdown");
        upload.Add(file, "file", fileName);
        upload.Add(new StringContent(title), "displayName");
        upload.Add(new StringContent("Architecture"), "documentType");

        var response = await client.PostAsync($"/api/projects/{projectId}/documents/upload", upload);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProjectDocumentUploadResult>();
        Assert.IsNotNull(result);
        return result!;
    }

    private static async Task<Project> CreateProjectAsync(HttpClient client, string name, string? localPath = null)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new Project
        {
            Name = name,
            Description = "Created by API endpoint contract test.",
            LocalPath = localPath ?? $@"C:\Temp\{name.Replace(' ', '_')}"
        });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<Project>();
        Assert.IsNotNull(project);
        return project!;
    }

    private static DisposableProjectFixture CreateDotNetProjectFixture(bool broken)
    {
        var root = Path.Combine(Path.GetTempPath(), $"irondev-api-disposable-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "DisposableFixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(source, "FixtureMarker.cs"), broken
            ? "namespace DisposableFixture; public static class FixtureMarker { public static string Broken() => "
            : "namespace DisposableFixture; public static class FixtureMarker { public static string Ready() => \"ready\"; }");
        return new DisposableProjectFixture(root, source);
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed record DisposableProjectFixture(string RootPath, string SourcePath);

    private sealed class FailingProjectChatResponseService : IronDev.Core.Interfaces.IProjectChatResponseService
    {
        public Task<IronDev.Core.Chat.ProjectChatResponseResult?> RespondAsync(
            int projectId,
            string prompt,
            IronDev.Core.Chat.ChatGovernanceMode? explicitMode = null,
            string? dogfoodTraceId = null,
            string? recentConversationSummary = null,
            long? sessionId = null,
            long? sourceMessageId = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Deterministic assistant failure.");
    }
}
