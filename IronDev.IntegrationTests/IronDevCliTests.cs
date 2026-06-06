using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using IronDev.Client.Tickets;
using IronDev.Core.RunReports;
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
        StringAssert.Contains(await handler.Requests[1].Content!.ReadAsStringAsync(), "\"type\":\"Architecture\"");
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
    public async Task RunsStatusAndReport_UseProductRunApiEndpoints()
    {
        var handler = new RecordingHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var status = await IronDevCli.RunAsync(
            ["runs", "status", "--run-id", "run-123", "--api-base-url", "http://localhost:5000", "--token", "test-token", "--json"],
            output,
            error,
            handler,
            CancellationToken.None);
        Assert.AreEqual(0, status, error.ToString());

        var report = await IronDevCli.RunAsync(
            ["runs", "report", "--run-id", "run-123", "--api-base-url", "http://localhost:5000", "--token", "test-token", "--json"],
            output,
            error,
            handler,
            CancellationToken.None);
        Assert.AreEqual(0, report, error.ToString());

        var stream = await IronDevCli.RunAsync(
            ["runs", "stream", "--run-id", "run-123", "--api-base-url", "http://localhost:5000", "--token", "test-token"],
            output,
            error,
            handler,
            CancellationToken.None);
        Assert.AreEqual(0, stream, error.ToString());

        CollectionAssert.AreEqual(
            new[]
            {
                "/health",
                "/api/runs/run-123",
                "/health",
                "/api/runs/run-123/report",
                "/health",
                "/api/runs/run-123/events"
            },
            handler.Requests.Select(request => request.RequestUri?.AbsolutePath).ToArray());
        StringAssert.Contains(output.ToString(), "\"runId\": \"run-123\"");
        StringAssert.Contains(output.ToString(), "RunCompleted run-123");
    }

    [TestMethod]
    public async Task RunsReport_WithJson_ReturnsCliContractEnvelope()
    {
        var handler = new RunReportContractHandler
        {
            RunId = "run-123",
            Status = "Completed",
            Recommendation = "Review",
            TraceId = "trace-run-123",
            ToolCallPaths = ["logs/process.json", "logs/verification.json"]
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await IronDevCli.RunAsync(
            ["runs", "report", "--run-id", "run-123", "--api-base-url", "http://localhost:5000", "--token", "test-token", "--json"],
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, result, error.ToString());

        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.AreEqual("runs report", root.GetProperty("command").GetString());
        Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
        Assert.AreEqual("trace-run-123", root.GetProperty("traceId").GetString());

        var data = root.GetProperty("data");
        Assert.AreEqual("run-123", data.GetProperty("runId").GetString());
        Assert.AreEqual("Completed", data.GetProperty("status").GetString());
        StringAssert.AreEqualIgnoringCase("not_required", data.GetProperty("approvalDecision").GetString());
        Assert.IsTrue(data.GetProperty("evidencePaths").GetArrayLength() > 0);
        Assert.IsTrue(data.GetProperty("toolCalls").GetArrayLength() > 0);
        Assert.AreEqual(0, data.GetProperty("warnings").GetArrayLength());
        Assert.AreEqual(0, root.GetProperty("errors").GetArrayLength());
    }

    [DataTestMethod]
    [DataRow("PausedForApproval", "blocked")]
    [DataRow("Failed", "failed")]
    public async Task RunsReport_BlockedOrFailedStates_ReturnNonZero(string runStatus, string expectedCommandStatus)
    {
        var handler = new RunReportContractHandler
        {
            RunId = "run-123",
            Status = runStatus,
            Recommendation = runStatus == "Failed" ? "Execution failed" : "Approval required",
            TraceId = "trace-run-123",
            ToolCallPaths = ["logs/process.json"]
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await IronDevCli.RunAsync(
            ["runs", "report", "--run-id", "run-123", "--api-base-url", "http://localhost:5000", "--token", "test-token", "--json"],
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(1, result, error.ToString());

        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.AreEqual(expectedCommandStatus, root.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task RunsReport_WhenRunIsMissing_ReturnsNonZeroAndWritesApiError()
    {
        var handler = new RunReportContractHandler
        {
            RunId = "run-missing",
            NotFound = true
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await IronDevCli.RunAsync(
            ["runs", "report", "--run-id", "run-missing", "--api-base-url", "http://localhost:5000", "--token", "test-token", "--json"],
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(1, result, error.ToString());
        StringAssert.Contains(error.ToString(), "404");
        Assert.AreEqual(string.Empty, output.ToString());
    }

    [TestMethod]
    public async Task TicketBuild_UsesProductBuildRunEndpoint()
    {
        var handler = new RecordingHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await IronDevCli.RunAsync(
            ["tickets", "build", "--project-id", "42", "--ticket-id", "123", "--api-base-url", "http://localhost:5000", "--token", "test-token", "--json"],
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, result, error.ToString());
        CollectionAssert.AreEqual(
            new[]
            {
                "/health",
                "/api/projects/42/tickets/123/build-runs"
            },
            handler.Requests.Select(request => request.RequestUri?.AbsolutePath).ToArray());
        StringAssert.Contains(output.ToString(), "\"runId\": \"11111111-1111-1111-1111-111111111111\"");
    }

    [TestMethod]
    public async Task ExerciseChatToBuild_DrivesReusableSpineAndWritesProofReport()
    {
        var reportDir = Path.Combine(Path.GetTempPath(), $"irondev-process-proof-{Guid.NewGuid():N}");
        var handler = new RecordingHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await IronDevCli.RunAsync(
            [
                "exercise", "chat-to-build",
                "--project-id", "42",
                "--input", "Create a tiny C# console application that prints \"Hello from IronDev Alpha\".",
                "--title", "Hello World proof",
                "--report-dir", reportDir,
                "--api-base-url", "http://localhost:5000",
                "--token", "test-token"
            ],
            output,
            error,
            handler,
            CancellationToken.None);

        Assert.AreEqual(0, result, error.ToString());
        CollectionAssert.AreEqual(
            new[]
            {
                "/health",
                "/api/projects/42/discussions",
                "/api/projects/42/documents/1001/tickets",
                "/api/projects/42/tickets/123/review",
                "/api/projects/42/tickets/123/disposable-code-runs",
                "/api/runs/run-proof-1",
                "/api/projects/42/tickets/123/build-runs/run-proof-1/review-package"
            },
            handler.Requests.Select(request => request.RequestUri?.AbsolutePath).ToArray());

        StringAssert.Contains(output.ToString(), "PASS chat-to-build process exercise");
        StringAssert.Contains(output.ToString(), "Run: run-proof-1 state=PausedForApproval");

        var jsonReport = Directory.EnumerateFiles(reportDir, "report.json", SearchOption.AllDirectories).Single();
        var markdownReport = Directory.EnumerateFiles(reportDir, "report.md", SearchOption.AllDirectories).Single();
        StringAssert.Contains(await File.ReadAllTextAsync(jsonReport), "\"reviewPackageAvailable\": true");
        StringAssert.Contains(await File.ReadAllTextAsync(markdownReport), "IronDev Chat-To-Build Proof Report");
    }

    [TestMethod]
    public async Task ScenarioCommands_UseScenarioCatalogAndProjectScopedReviewPackage()
    {
        var reportDir = Path.Combine(Path.GetTempPath(), $"irondev-scenario-proof-{Guid.NewGuid():N}");
        var handler = new RecordingHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var list = await IronDevCli.RunAsync(
            ["scenario", "list", "--project-id", "42", "--api-base-url", "http://localhost:5000", "--token", "test-token"],
            output,
            error,
            handler,
            CancellationToken.None);
        Assert.AreEqual(0, list, error.ToString());
        StringAssert.Contains(output.ToString(), "console.hello-world");

        var run = await IronDevCli.RunAsync(
            ["scenario", "run", "console.hello-world", "--project-id", "42", "--report-dir", reportDir, "--api-base-url", "http://localhost:5000", "--token", "test-token"],
            output,
            error,
            handler,
            CancellationToken.None);
        Assert.AreEqual(0, run, error.ToString());

        var report = await IronDevCli.RunAsync(
            ["scenario", "report", "run-proof-1", "--project-id", "42", "--ticket-id", "123", "--api-base-url", "http://localhost:5000", "--token", "test-token"],
            output,
            error,
            handler,
            CancellationToken.None);
        Assert.AreEqual(0, report, error.ToString());

        CollectionAssert.AreEqual(
            new[]
            {
                "/health",
                "/api/projects/42/code-scenarios",
                "/health",
                "/api/projects/42/code-scenarios",
                "/health",
                "/api/projects/42/discussions",
                "/api/projects/42/documents/1001/tickets",
                "/api/projects/42/tickets/123/review",
                "/api/projects/42/tickets/123/disposable-code-runs",
                "/api/runs/run-proof-1",
                "/api/projects/42/tickets/123/build-runs/run-proof-1/review-package",
                "/health",
                "/api/projects/42/tickets/123/build-runs/run-proof-1/review-package"
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

    [TestMethod]
    public void IronDevApi_MustNotReferenceCliReplayRunnerOrPowerShellForTicketCreation()
    {
        var repoRoot = FindRepositoryRoot();
        var apiProject = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Api", "IronDev.Api.csproj"));
        var apiSources = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "IronDev.Api"), "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .ToArray();

        Assert.IsFalse(apiProject.Contains("IronDev.Cli", StringComparison.Ordinal), "API must not reference the CLI project.");
        Assert.IsFalse(apiProject.Contains("IronDev.ReplayRunner", StringComparison.Ordinal), "API must not reference ReplayRunner.");

        var forbidden = new[]
        {
            "IronDev.Cli",
            "IronDev.ReplayRunner",
            "ReplayRunner",
            "PowerShell",
            "Invoke-TestAgentPlan",
            ".ps1",
            "ProcessStartInfo",
            "System.Diagnostics.Process"
        };

        foreach (var source in apiSources)
        {
            foreach (var token in forbidden)
            {
                Assert.IsFalse(
                    source.Text.Contains(token, StringComparison.Ordinal),
                    $"API source must not route ticket/product persistence through CLI, ReplayRunner, or shell wrappers. Forbidden token '{token}' found in {source.Path}.");
            }
        }
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

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/runs/run-123")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "runId": "run-123",
                          "project": "IronDev",
                          "title": "Boundary hardening",
                          "status": "Completed",
                          "recommendation": "Review"
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/runs/run-proof-1")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "runId": "run-proof-1",
                          "project": "42",
                          "title": "Hello World proof",
                          "status": "PausedForApproval",
                          "recommendation": "Approval required"
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/projects/42/code-scenarios")
                return JsonResponse(
                    """
                    [
                      {
                        "scenarioId": "console.hello-world",
                        "name": "Hello World console",
                        "discussionText": "Create a tiny C# console application that prints \"Hello from IronDev Alpha\".",
                        "runtimeProfileId": "dotnet.console",
                        "verifications": [
                          {
                            "kind": "StdoutContains",
                            "description": "Output contains expected greeting.",
                            "parameters": {
                              "expected": "Hello from IronDev Alpha"
                            }
                          }
                        ]
                      }
                    ]
                    """);

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/runs/run-123/report")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "status": {
                            "runId": "run-123",
                            "project": "IronDev",
                            "title": "Boundary hardening",
                            "status": "Completed",
                            "recommendation": "Review"
                          },
                          "report": {
                            "runId": "run-123",
                            "project": "IronDev",
                            "title": "Boundary hardening",
                            "status": "Completed",
                            "summary": "Run completed.",
                            "recommendation": "Review"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/runs/run-123/events")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        event: RunStarted
                        data: {"timestampUtc":"2026-05-26T00:00:00Z","runId":"run-123","eventType":"RunStarted","message":"Run started","payload":{}}

                        event: RunCompleted
                        data: {"timestampUtc":"2026-05-26T00:01:00Z","runId":"run-123","eventType":"RunCompleted","message":"Run completed","payload":{"status":"Completed"}}

                        """,
                        Encoding.UTF8,
                        "text/event-stream")
                };

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/projects/42/tickets/123/build-runs")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "runId": "11111111-1111-1111-1111-111111111111",
                          "projectId": 42,
                          "ticketId": 123,
                          "status": "AwaitingCodeApproval",
                          "currentNode": "RequestCodeApproval",
                          "requiresHumanApproval": true,
                          "message": "Review generated code proposal."
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/projects/42/discussions")
                return JsonResponse(
                    """
                    {
                      "documentId": 1000,
                      "documentVersionId": 1001
                    }
                    """);

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/projects/42/documents/1001/tickets")
                return JsonResponse(
                    """
                    {
                      "ticketId": 123,
                      "sourceDocumentVersionId": 1001
                    }
                    """);

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/projects/42/tickets/123/review")
                return JsonResponse(
                    """
                    {
                      "reviewId": "review-proof-1",
                      "result": {
                        "reviewId": "review-proof-1",
                        "projectId": 42,
                        "ticketId": 123,
                        "scenarioId": "console.hello-world",
                        "createdUtc": "2026-05-26T00:00:00Z",
                        "contributions": [
                          {
                            "role": "Planner",
                            "summary": "Build the smallest console app.",
                            "concerns": [],
                            "recommendations": ["Use a disposable workspace."]
                          }
                        ],
                        "decision": {
                          "proceed": true,
                          "recommendedNextStep": "Start disposable code run.",
                          "guardrails": ["Do not mutate the real repo."]
                        }
                      }
                    }
                    """);

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/projects/42/tickets/123/disposable-code-runs")
                return JsonResponse(
                    """
                    {
                      "runId": "run-proof-1",
                      "state": "PausedForApproval",
                      "isDisposable": true
                    }
                    """);

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/projects/42/tickets/123/build-runs/run-proof-1/review-package")
                return JsonResponse(
                    """
                    {
                      "runId": "run-proof-1",
                      "projectId": 42,
                      "ticketId": 123,
                      "state": "PausedForApproval",
                      "generatedFiles": [
                        {
                          "relativePath": "HelloWorldAlpha/Program.cs",
                          "content": "Console.WriteLine(\"Hello from IronDev Alpha\");",
                          "sha256": "abc"
                        }
                      ],
                      "commandEvidence": [
                        {
                          "command": "dotnet build",
                          "exitCode": "0",
                          "stdoutPath": "build.stdout.log",
                          "stderrPath": "build.stderr.log",
                          "durationMs": "1200"
                        }
                      ],
                      "outputVerification": {
                        "expected": "Hello from IronDev Alpha",
                        "actual": "Hello from IronDev Alpha",
                        "verified": true,
                        "evidencePath": "output-verification.json"
                      },
                      "outputVerifications": [
                        {
                          "expected": "Hello from IronDev Alpha",
                          "actual": "Hello from IronDev Alpha",
                          "verified": true,
                          "evidencePath": "output-verification.json"
                        }
                      ],
                      "codeStandards": {
                        "status": "Passed",
                        "summary": "No blocking standards findings.",
                        "evidencePath": "code-standards.json"
                      },
                      "fileSetHash": "fileset",
                      "risks": ["Generated code requires human review."],
                      "humanReviewChecklist": ["Confirm generated files match the ticket."],
                      "events": [
                        {
                          "eventType": "RunPausedForApproval",
                          "message": "Run paused for approval.",
                          "timestampUtc": "2026-05-26T00:01:00Z"
                        }
                      ]
                    }
                    """);

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

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

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

    private sealed class RunReportContractHandler : HttpMessageHandler
    {
        public string RunId { get; init; } = string.Empty;
        public string Status { get; init; } = "Completed";
        public string Recommendation { get; init; } = "Review";
        public string? TraceId { get; init; } = "trace";
        public string[] ToolCallPaths { get; init; } = [];
        public bool NotFound { get; init; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            if (request.RequestUri?.AbsolutePath == "/health")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"status":"healthy"}""", Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == $"/api/runs/{RunId}/report")
            {
                if (NotFound)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent($"{{\"detail\":\"Run '{RunId}' not found.\"}}", Encoding.UTF8, "application/json")
                    });
                }

                var payload = new RunReportDto
                {
                    Status = new RunStatusDto
                    {
                        RunId = RunId,
                        TraceId = TraceId,
                        Project = "IronDev",
                        Title = "Run Contract",
                        Status = Status,
                        Recommendation = Recommendation
                    },
                    Report = new RunReportDetail
                    {
                        RunId = RunId,
                        TraceId = TraceId,
                        Project = "IronDev",
                        Title = "Run Contract",
                        Status = Status,
                        Summary = "Run contract validation.",
                        Recommendation = Recommendation,
                        Evidence = ToolCallPaths.Select(path => new RunEvidenceItem
                        {
                            Type = "tool-call",
                            Path = path,
                            Summary = "Process command summary"
                        }).ToArray(),
                        Stages = [
                            new RunStageStatus
                            {
                                StageName = "Governed agent",
                                AgentName = "quality",
                                Status = "Done",
                                Summary = "Governed process review stage."
                            }
                        ]
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
