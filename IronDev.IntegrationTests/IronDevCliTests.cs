using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using IronDev.Client.Tickets;
using IronDev.Cli;
using IronDev.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class IronDevCliTests
{
    [TestMethod]
    public void ResolveApiBaseUrl_UsesArgumentBeforeEnvironmentAndConfig()
    {
        var environment = new Dictionary<string, string?>
        {
            ["IRONDEV_API_BASE_URL"] = "http://env.example:5000/"
        };

        var actual = IronDevCli.ResolveApiBaseUrl(
            "http://argument.example:5000/",
            environment);

        Assert.AreEqual("http://argument.example:5000", actual);
    }

    [TestMethod]
    public void ResolveApiBaseUrl_UsesLocalhostDefault()
    {
        var actual = IronDevCli.ResolveApiBaseUrl(
            argumentValue: null,
            new Dictionary<string, string?>());

        Assert.AreEqual("http://localhost:5000", actual);
    }

    [TestMethod]
    public async Task TicketCreate_WhenApiHealthFails_ReturnsClearStartupMessage()
    {
        var ticketPath = Path.Combine(Path.GetTempPath(), $"irondev-ticket-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(ticketPath, """
            {
              "title": "Make IronDev tickets canonical",
              "type": "Architecture",
              "priority": "Critical"
            }
            """);

        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "ticket", "create",
                "--project-id", "1",
                "--file", ticketPath,
                "--api-base-url", "http://localhost:5000",
                "--json"
            ],
            output,
            error,
            new ThrowingHandler(),
            CancellationToken.None);

        Assert.AreEqual(1, result);
        StringAssert.Contains(error.ToString(), "IronDev.Api is not reachable at http://localhost:5000.");
        StringAssert.Contains(error.ToString(), "dotnet run --project IronDev.Api");
    }

    [TestMethod]
    public async Task TicketCreate_PostsToApiBoundaryAfterHealthPasses()
    {
        var ticketPath = Path.Combine(Path.GetTempPath(), $"irondev-ticket-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(ticketPath, """
            {
              "title": "Make IronDev tickets canonical",
              "type": "Architecture",
              "priority": "Critical",
              "summary": "IronDev tickets are the source of truth.",
              "provenance": {
                "source": "design-discussion",
                "createdBy": "codex"
              }
            }
            """);

        var handler = new RecordingHandler();
        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "ticket", "create",
                "--project-id", "42",
                "--file", ticketPath,
                "--api-base-url", "http://localhost:5000",
                "--token", "test-token",
                "--json"
            ],
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, result, error.ToString());
        CollectionAssert.AreEqual(
            new[] { "/health", "/api/projects/42/tickets" },
            handler.Requests.Select(request => request.RequestUri?.AbsolutePath).ToArray());
        Assert.AreEqual("Bearer", handler.Requests[1].Headers.Authorization?.Scheme);
        Assert.AreEqual("test-token", handler.Requests[1].Headers.Authorization?.Parameter);
        StringAssert.Contains(await handler.Requests[1].Content!.ReadAsStringAsync(), "\"type\": \"Architecture\"");
        StringAssert.Contains(output.ToString(), "\"id\": 123");
    }

    [TestMethod]
    public async Task TicketListShowAndImport_UseProductApiEndpoints()
    {
        var importPath = Path.Combine(Path.GetTempPath(), $"irondev-github-import-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(importPath, """
            {
              "title": "Import GitHub issue",
              "type": "Backfill",
              "priority": "High",
              "externalReference": {
                "provider": "github",
                "kind": "issue",
                "externalId": "73",
                "url": "https://github.com/BigDaddyDread-code/IronDeveloper/issues/73",
                "title": "Issue 73"
              }
            }
            """);

        var handler = new RecordingHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var list = await IronDevCli.RunAsync(
            ["ticket", "list", "--project-id", "42", "--api-base-url", "http://localhost:5000", "--token", "test-token", "--json"],
            output,
            error,
            handler,
            CancellationToken.None);
        Assert.AreEqual(0, list, error.ToString());

        var show = await IronDevCli.RunAsync(
            ["ticket", "show", "--project-id", "42", "--ticket-id", "123", "--api-base-url", "http://localhost:5000", "--token", "test-token", "--json"],
            output,
            error,
            handler,
            CancellationToken.None);
        Assert.AreEqual(0, show, error.ToString());

        var import = await IronDevCli.RunAsync(
            ["ticket", "import-github-issue", "--project-id", "42", "--file", importPath, "--api-base-url", "http://localhost:5000", "--token", "test-token", "--json"],
            output,
            error,
            handler,
            CancellationToken.None);
        Assert.AreEqual(0, import, error.ToString());

        CollectionAssert.AreEqual(
            new[]
            {
                "/health",
                "/api/projects/42/tickets",
                "/health",
                "/api/projects/42/tickets/123",
                "/health",
                "/api/projects/42/tickets/import-external"
            },
            handler.Requests.Select(request => request.RequestUri?.AbsolutePath).ToArray());
    }

    [TestMethod]
    public async Task TicketsApiClient_CallsStructuredTicketEndpoints()
    {
        var handler = new RecordingHandler { IncludeApiClientBasePath = true };
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5000/api/")
        };
        var client = new TicketsApiClient(http);

        await client.CreateTicketAsync(42, new CreateProjectTicketRequest { Title = "Create" });
        await client.ImportExternalTicketAsync(42, new ImportExternalTicketRequest
        {
            Title = "Import",
            ExternalReference = new ExternalReferenceDto { Provider = "github", Kind = "issue", ExternalId = "73" }
        });
        await client.GenerateTicketFromDiscussionAsync(42, new GenerateTicketFromDiscussionRequest { Discussion = "Discuss" });

        CollectionAssert.AreEqual(
            new[]
            {
                "/api/projects/42/tickets",
                "/api/projects/42/tickets/import-external",
                "/api/projects/42/tickets/generate-from-discussion"
            },
            handler.Requests.Select(request => request.RequestUri?.AbsolutePath).ToArray());
    }

    [TestMethod]
    public void IronDevCli_ProjectMustNotReferenceInfrastructure()
    {
        var repoRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repoRoot, "tools", "IronDev.Cli", "IronDev.Cli.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.IsFalse(project.Contains("IronDev.Infrastructure", StringComparison.Ordinal), "CLI must not reference Infrastructure.");
        Assert.IsFalse(project.Contains("IronDev.Services", StringComparison.Ordinal), "CLI must not reference service implementations.");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IronDev.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused.");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public bool IncludeApiClientBasePath { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await CloneAsync(request, cancellationToken));

            if (request.RequestUri?.AbsolutePath == "/health")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":"healthy"}""", Encoding.UTF8, "application/json")
                };

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath.EndsWith("/tickets", StringComparison.Ordinal) == true)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        [
                          {
                            "id": 123,
                            "projectId": 42,
                            "title": "Make IronDev tickets canonical",
                            "ticketType": "Architecture",
                            "priority": "Critical",
                            "status": "Draft"
                          }
                        ]
                        """,
                        Encoding.UTF8,
                        "application/json")
                };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "id": 123,
                      "projectId": 42,
                      "title": "Make IronDev tickets canonical",
                      "ticketType": "Architecture",
                      "priority": "Critical",
                      "status": "Draft"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }

        private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (request.Content is not null)
            {
                var content = await request.Content.ReadAsStringAsync(cancellationToken);
                clone.Content = new StringContent(content, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType);
            }

            return clone;
        }
    }
}
