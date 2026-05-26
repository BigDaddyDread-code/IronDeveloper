using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IronDev.Core.Models;
using IronDev.Data.Models;
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
            "/api/projects/{projectId}/tickets",
            "/api/projects/{projectId}/tickets/{ticketId}",
            "/api/projects/{projectId}/tickets/import-external",
            "/api/projects/{projectId}/tickets/generate-from-discussion",
            "/api/projects/{projectId}/tickets/{ticketId}/build-runs",
            "/api/projects/{projectId}/documents",
            "/api/projects/{projectId}/documents/{documentId}",
            "/api/projects/{projectId}/documents/{documentId}/resolve",
            "/api/projects/{projectId}/memory/search",
            "/api/projects/{projectId}/chat/complete",
            "/api/run-reports",
            "/api/runs/{runId}",
            "/api/runs/{runId}/report",
            "/api/runs/{runId}/events"
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
            "/api/projects/1/tickets",
            "/api/projects/1/documents",
            "/api/projects/1/memory/search?q=architecture",
            "/api/projects/1/chat/sessions"
        })
        {
            var response = await Client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, $"{path} should require a token.");
        }
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
            activeModel = "test"
        });
        Assert.AreEqual(HttpStatusCode.OK, chat.StatusCode);
        using var chatBody = JsonDocument.Parse(await chat.Content.ReadAsStringAsync());
        Assert.IsFalse(string.IsNullOrWhiteSpace(chatBody.RootElement.GetProperty("response").GetString()));
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

    private static async Task<Project> CreateProjectAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/projects", new Project
        {
            Name = name,
            Description = "Created by API endpoint contract test.",
            LocalPath = $@"C:\Temp\{name.Replace(' ', '_')}"
        });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<Project>();
        Assert.IsNotNull(project);
        return project!;
    }
}
