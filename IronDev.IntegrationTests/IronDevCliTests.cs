using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using IronDev.Client.Tickets;
using IronDev.Core.Agents;
using IronDev.Core.RunReports;
using IronDev.Cli;
using IronDev.Core.Models;
using IronDev.Core.Workspaces;
using IronDev.Infrastructure.Services.Workspaces;
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
        Assert.AreEqual("Completed", data.GetProperty("runStatus").GetString());
        var governance = data.GetProperty("governance");
        Assert.AreEqual("derived", governance.GetProperty("decision").GetString());
        AssertEqualsIgnoreCase("not_required", governance.GetProperty("approvalDecision").GetString());
        Assert.AreEqual(false, governance.GetProperty("requiresHumanApproval").GetBoolean());
        Assert.IsTrue(data.GetProperty("evidence").GetArrayLength() > 0);
        Assert.AreEqual(0, data.GetProperty("toolCalls").GetArrayLength());
        Assert.AreEqual(0, data.GetProperty("processCommands").GetArrayLength());
        Assert.AreNotEqual(0, data.GetProperty("warnings").GetArrayLength());
        Assert.AreEqual(data.GetProperty("warnings").GetArrayLength(), root.GetProperty("warnings").GetArrayLength());
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
        Assert.AreEqual(0, root.GetProperty("data").GetProperty("toolCalls").GetArrayLength());
        Assert.AreEqual(0, root.GetProperty("data").GetProperty("processCommands").GetArrayLength());

        if (runStatus == "Failed")
            Assert.IsTrue(root.GetProperty("errors").GetArrayLength() > 0);
        else
            Assert.AreEqual(0, root.GetProperty("errors").GetArrayLength());

        var governance = root.GetProperty("data").GetProperty("governance");
        if (runStatus == "PausedForApproval")
        {
            AssertEqualsIgnoreCase("required", governance.GetProperty("approvalDecision").GetString());
            Assert.IsTrue(governance.GetProperty("requiresHumanApproval").GetBoolean());
        }
        else
        {
            AssertEqualsIgnoreCase("denied", governance.GetProperty("approvalDecision").GetString());
        }
    }

    [TestMethod]
    public async Task RunsReport_WhenRunIsMissing_ReturnsNonZeroAndWritesContractEnvelope()
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
        AssertJsonWasWritten(output);

        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.AreEqual("failed", root.GetProperty("status").GetString());
        Assert.AreEqual("runs report", root.GetProperty("command").GetString());
        var data = root.GetProperty("data");
        Assert.AreEqual("run-missing", data.GetProperty("runId").GetString());
        Assert.AreEqual("not_found", data.GetProperty("runStatus").GetString());
        Assert.AreEqual(0, data.GetProperty("toolCalls").GetArrayLength());
        Assert.AreEqual(0, data.GetProperty("processCommands").GetArrayLength());
        Assert.AreEqual(0, data.GetProperty("evidence").GetArrayLength());
        AssertArrayNotEmpty(root.GetProperty("errors"));
    }

    [TestMethod]
    public async Task WorkspaceCheck_MissingRequiredOptions_WithJson_ReturnsFailureEnvelope()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await IronDevCli.RunAsync(
            ["workspace", "check", "--json"],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(2, result, error.ToString());
        AssertJsonWasWritten(output);

        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.AreEqual("failed", root.GetProperty("status").GetString());
        Assert.AreEqual("workspace check", root.GetProperty("command").GetString());
        Assert.AreEqual(JsonValueKind.Object, root.GetProperty("data").ValueKind);
        AssertArrayNotEmpty(root.GetProperty("errors"));
    }

    [TestMethod]
    public async Task WorkspaceCheck_ValidWorkspacePath_ReturnsSucceededReadyEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-valid");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            var output = new StringWriter();
            var error = new StringWriter();
            var result = await IronDevCli.RunAsync(
                [
                    "workspace", "check",
                    "--run-id", "run-123",
                    "--source-repo", sourceRepo,
                    "--workspace-root", workspaceRoot,
                    "--json"
                ],
                output,
                error,
                handler: null,
                CancellationToken.None);

            Assert.AreEqual(0, result, error.ToString());
            using var doc = JsonDocument.Parse(output.ToString());
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace check", root.GetProperty("command").GetString());
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("traceId").ValueKind);
            Assert.AreEqual(0, root.GetProperty("errors").GetArrayLength());

            var data = root.GetProperty("data");
            Assert.AreEqual("run-123", data.GetProperty("runId").GetString());
            Assert.AreEqual(Path.GetFullPath(sourceRepo), data.GetProperty("sourceRepo").GetString());
            Assert.AreEqual(Path.GetFullPath(workspaceRoot), data.GetProperty("workspaceRoot").GetString());
            Assert.AreEqual(Path.Combine(Path.GetFullPath(workspaceRoot), "run-123"), data.GetProperty("workspacePath").GetString());
            Assert.IsTrue(data.GetProperty("sourceRepoExists").GetBoolean());
            Assert.IsTrue(data.GetProperty("workspaceRootExists").GetBoolean());
            Assert.IsFalse(data.GetProperty("workspacePathExists").GetBoolean());
            Assert.IsFalse(data.GetProperty("isInsideSourceRepo").GetBoolean());
            Assert.IsTrue(data.GetProperty("gitStatusClean").GetBoolean());
            Assert.IsTrue(data.GetProperty("canCreateWorkspaceDirectory").GetBoolean());
            Assert.IsTrue(data.GetProperty("ready").GetBoolean());
            AssertArrayNotEmpty(data.GetProperty("checks"));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspaceRoot, "run-123")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_WorkspaceInsideSourceRepo_ReturnsBlocked()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-inside-source");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspaceRoot = Path.Combine(sourceRepo, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("ready").GetBoolean());
            Assert.IsTrue(data.GetProperty("isInsideSourceRepo").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_WorkspaceRootSameAsSourceRepo_ReturnsBlocked()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-root-same");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, sourceRepo, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("ready").GetBoolean());
            Assert.IsTrue(data.GetProperty("workspaceRootSameAsSourceRepo").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_MissingSourceRepo_ReturnsFailed()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-missing-source");
        try
        {
            var sourceRepo = Path.Combine(testRoot, "missing-source");
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("failed", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("sourceRepoExists").GetBoolean());
            Assert.IsFalse(data.GetProperty("ready").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_DirtyGitStatus_ReturnsBlocked()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-dirty");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "dirty.txt"), "untracked");
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("gitStatusClean").GetBoolean());
            Assert.IsFalse(data.GetProperty("ready").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_NonEmptyWorkspacePath_ReturnsBlocked()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-nonempty");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            var workspacePath = Path.Combine(workspaceRoot, "run-123");
            Directory.CreateDirectory(workspacePath);
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "existing.txt"), "existing");

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsTrue(data.GetProperty("workspacePathExists").GetBoolean());
            Assert.IsFalse(data.GetProperty("ready").GetBoolean());
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceCheck_JsonOutput_UsesStandardEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-check-envelope");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspaceCheckAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 0);
            var root = doc.RootElement;
            var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
            var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEqual(
                expectedTopLevelKeys.OrderBy(item => item).ToArray(),
                topLevelProperties.OrderBy(item => item).ToArray());
            Assert.AreEqual("workspace check", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePrepare_ReadinessFailure_DoesNotCreateWorkspace()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-prepare-readiness-failure");
        try
        {
            var sourceRepo = Path.Combine(testRoot, "missing-source");
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspacePrepareAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("failed", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace prepare", root.GetProperty("command").GetString());
            var data = root.GetProperty("data");
            Assert.AreEqual("failed", data.GetProperty("readinessStatus").GetString());
            Assert.IsFalse(data.GetProperty("prepared").GetBoolean());
            Assert.IsFalse(Directory.Exists(data.GetProperty("workspacePath").GetString()));
            AssertArrayNotEmpty(root.GetProperty("errors"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePrepare_SucceedsAfterReadinessPassesAndWritesMetadata()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-prepare-success");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryWithTrackedFilesAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspacePrepareAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 0);
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace prepare", root.GetProperty("command").GetString());
            Assert.AreEqual(0, root.GetProperty("errors").GetArrayLength());

            var data = root.GetProperty("data");
            var workspacePath = data.GetProperty("workspacePath").GetString()!;
            var metadataPath = data.GetProperty("metadataPath").GetString()!;
            Assert.AreEqual("succeeded", data.GetProperty("readinessStatus").GetString());
            Assert.IsTrue(data.GetProperty("prepared").GetBoolean());
            Assert.AreEqual("copy", data.GetProperty("preparationMethod").GetString());
            Assert.AreEqual(2, data.GetProperty("filesCopied").GetInt32());
            Assert.IsFalse(data.GetProperty("sourceRepoMutated").GetBoolean());
            AssertArrayNotEmpty(data.GetProperty("checks"));

            Assert.IsTrue(Directory.Exists(workspacePath));
            Assert.IsTrue(workspacePath.StartsWith(Path.GetFullPath(workspaceRoot), StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(workspacePath.StartsWith(Path.GetFullPath(sourceRepo), StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(File.Exists(Path.Combine(workspacePath, "README.md")));
            Assert.IsTrue(File.Exists(Path.Combine(workspacePath, "src", "app.cs")));
            Assert.IsTrue(File.Exists(metadataPath));

            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
            var metadataRoot = metadata.RootElement;
            Assert.AreEqual("run-123", metadataRoot.GetProperty("runId").GetString());
            Assert.AreEqual(Path.GetFullPath(sourceRepo), metadataRoot.GetProperty("sourceRepo").GetString());
            Assert.AreEqual(workspacePath, metadataRoot.GetProperty("workspacePath").GetString());
            Assert.AreEqual("copy", metadataRoot.GetProperty("preparationMethod").GetString());
            Assert.IsFalse(metadataRoot.GetProperty("sourceRepoMutated").GetBoolean());

            Assert.AreEqual(string.Empty, await GetGitStatusAsync(sourceRepo));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePrepare_ExcludesJunkDirectoriesFromCopy()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-prepare-exclusions");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryWithTrackedFilesAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspacePrepareAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 0);
            var workspacePath = doc.RootElement.GetProperty("data").GetProperty("workspacePath").GetString()!;

            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".git")));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, "bin")));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, "obj")));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".vs")));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, "TestResults")));
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, "tools", "dogfood", "runs")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspacePrepare_JsonOutput_UsesStandardEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-prepare-envelope");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryWithTrackedFilesAsync(testRoot);
            var workspaceRoot = Path.Combine(testRoot, "workspaces");
            Directory.CreateDirectory(workspaceRoot);

            using var doc = await RunWorkspacePrepareAsync("run-123", sourceRepo, workspaceRoot, expectedExitCode: 0);
            var root = doc.RootElement;
            var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
            var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEqual(
                expectedTopLevelKeys.OrderBy(item => item).ToArray(),
                topLevelProperties.OrderBy(item => item).ToArray());
            Assert.AreEqual("workspace prepare", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public void WorkspacePrepareService_MustNotRunBuildTestOrAgentCommands()
    {
        var repoRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IronDev.Infrastructure",
            "Services",
            "Workspaces",
            "DisposableWorkspacePrepareService.cs"));

        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("RunCommandAsync", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("dotnet", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("IAgent", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("SupervisorAgent", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("TesterAgent", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("QualityAgent", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task WorkspaceRun_UnknownCommand_ReturnsBlockedAndDoesNotStartProcess()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-unknown-command");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "powershell", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace run", root.GetProperty("command").GetString());
            AssertArrayNotEmpty(root.GetProperty("errors"));
            AssertStringArrayContains(root.GetProperty("errors"), "not allowlisted");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "powershell")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_MissingMetadata_BlocksExecution()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-missing-metadata");
        try
        {
            var workspacePath = Path.Combine(testRoot, "workspace");
            Directory.CreateDirectory(workspacePath);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "metadata");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_RunIdMismatch_BlocksExecution()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-runid-mismatch");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-a", sourceRepo);

            using var doc = await RunWorkspaceCommandAsync("run-b", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "runId mismatch");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-b", "dotnet-build")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_MetadataMissingSourceRepo_BlocksExecution()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-missing-source-repo");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", sourceRepo, workspacePath, includeSourceRepo: false);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "missing sourceRepo");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-build")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_MetadataMissingWorkspacePath_BlocksExecution()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-missing-workspace-path");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", sourceRepo, workspacePath, includeWorkspacePath: false);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "missing workspacePath");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-build")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_MetadataSourceRepoEqualsWorkspace_BlocksExecution()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-source-equals-workspace");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", workspacePath, workspacePath);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "isolated from the source repository");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-build")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_CommandTimeoutFailsClosed()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-timeout");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedDotnetWorkspaceAsync(testRoot, "run-1", sourceRepo, brokenProgram: false);
            var service = new DisposableWorkspaceCommandService(TimeSpan.Zero);

            var result = await service.RunAsync(
                new DisposableWorkspaceCommandRequest
                {
                    RunId = "run-1",
                    WorkspacePath = workspacePath,
                    CommandId = "dotnet-build"
                },
                CancellationToken.None);

            Assert.AreEqual("failed", result.Status);
            Assert.AreEqual(1, result.ExitCode);
            Assert.AreEqual(-1, result.Data.ExitCode);
            Assert.IsFalse(result.Data.Succeeded);
            Assert.IsTrue(result.Errors.Any(error => error.Contains("timed out", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_DotnetBuildSucceedsInPreparedWorkspaceAndWritesEvidence()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-build-success");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedDotnetWorkspaceAsync(testRoot, "run-1", sourceRepo, brokenProgram: false);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 0);
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace run", root.GetProperty("command").GetString());
            Assert.AreEqual(0, root.GetProperty("errors").GetArrayLength());

            var data = root.GetProperty("data");
            Assert.AreEqual("run-1", data.GetProperty("runId").GetString());
            Assert.AreEqual("dotnet-build", data.GetProperty("commandId").GetString());
            Assert.AreEqual(Path.GetFullPath(workspacePath), data.GetProperty("workingDirectory").GetString());
            Assert.AreEqual(0, data.GetProperty("exitCode").GetInt32());
            Assert.IsTrue(data.GetProperty("succeeded").GetBoolean());

            var stdoutPath = data.GetProperty("stdoutPath").GetString()!;
            var stderrPath = data.GetProperty("stderrPath").GetString()!;
            var commandMetadataPath = data.GetProperty("commandMetadataPath").GetString()!;
            Assert.IsTrue(File.Exists(stdoutPath));
            Assert.IsTrue(File.Exists(stderrPath));
            Assert.IsTrue(File.Exists(commandMetadataPath));
            Assert.AreEqual(3, data.GetProperty("evidencePaths").GetArrayLength());

            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(commandMetadataPath));
            var commandMetadata = metadata.RootElement;
            Assert.AreEqual("run-1", commandMetadata.GetProperty("runId").GetString());
            Assert.AreEqual("dotnet-build", commandMetadata.GetProperty("commandId").GetString());
            Assert.AreEqual(Path.GetFullPath(workspacePath), commandMetadata.GetProperty("workingDirectory").GetString());
            Assert.AreEqual("dotnet", commandMetadata.GetProperty("executable").GetString());
            Assert.AreEqual(0, commandMetadata.GetProperty("exitCode").GetInt32());

            Assert.AreEqual(string.Empty, await GetGitStatusAsync(sourceRepo));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_DotnetBuildFailureReturnsFailedEnvelopeAndEvidence()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-build-failure");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedDotnetWorkspaceAsync(testRoot, "run-1", sourceRepo, brokenProgram: true);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "dotnet-build", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("failed", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "non-zero exit code");

            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("succeeded").GetBoolean());
            Assert.AreNotEqual(0, data.GetProperty("exitCode").GetInt32());
            Assert.IsTrue(File.Exists(data.GetProperty("stdoutPath").GetString()!));
            Assert.IsTrue(File.Exists(data.GetProperty("stderrPath").GetString()!));
            Assert.IsTrue(File.Exists(data.GetProperty("commandMetadataPath").GetString()!));
            Assert.AreEqual(3, data.GetProperty("evidencePaths").GetArrayLength());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceRun_JsonOutput_UsesStandardEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-run-envelope");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);

            using var doc = await RunWorkspaceCommandAsync("run-1", workspacePath, "unknown-command", expectedExitCode: 1);
            var root = doc.RootElement;
            var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
            var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEqual(
                expectedTopLevelKeys.OrderBy(item => item).ToArray(),
                topLevelProperties.OrderBy(item => item).ToArray());
            Assert.AreEqual("workspace run", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public void WorkspaceCommandService_MustUseAllowlistedNoShellExecution()
    {
        var repoRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IronDev.Infrastructure",
            "Services",
            "Workspaces",
            "DisposableWorkspaceCommandService.cs"));

        StringAssert.Contains(source, "UseShellExecute = false");
        StringAssert.Contains(source, "ArgumentList");
        Assert.IsFalse(source.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("/bin/sh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("-Command", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("IAgent", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("SupervisorAgent", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task WorkspaceValidate_UnknownProfile_ReturnsBlockedAndDoesNotRunCommands()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-validate-unknown-profile");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);

            using var doc = await RunWorkspaceValidateAsync("run-1", workspacePath, "arbitrary", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace validate", root.GetProperty("command").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "profile");
            Assert.IsFalse(Directory.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-build")));
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "validation.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceValidate_DotnetBuildTestProfileSucceedsAndWritesPackage()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-validate-success");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedDotnetTestWorkspaceAsync(testRoot, "run-1", sourceRepo, brokenBuild: false);

            using var doc = await RunWorkspaceValidateAsync("run-1", workspacePath, "dotnet-build-test", expectedExitCode: 0);
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace validate", root.GetProperty("command").GetString());
            Assert.AreEqual(0, root.GetProperty("errors").GetArrayLength());

            var data = root.GetProperty("data");
            Assert.AreEqual("run-1", data.GetProperty("runId").GetString());
            Assert.AreEqual("dotnet-build-test", data.GetProperty("profileId").GetString());
            Assert.AreEqual("succeeded", data.GetProperty("status").GetString());
            Assert.IsTrue(data.GetProperty("succeeded").GetBoolean());

            var steps = data.GetProperty("steps").EnumerateArray().ToArray();
            Assert.AreEqual(2, steps.Length);
            Assert.AreEqual("dotnet-build", steps[0].GetProperty("commandId").GetString());
            Assert.AreEqual("succeeded", steps[0].GetProperty("status").GetString());
            Assert.AreEqual("dotnet-test", steps[1].GetProperty("commandId").GetString());
            Assert.AreEqual("succeeded", steps[1].GetProperty("status").GetString());

            var validationMetadataPath = data.GetProperty("validationMetadataPath").GetString()!;
            Assert.IsTrue(File.Exists(validationMetadataPath));
            Assert.IsTrue(data.GetProperty("evidencePaths").GetArrayLength() >= 7);
            Assert.IsTrue(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-build", "command.json")));
            Assert.IsTrue(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-test", "command.json")));

            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(validationMetadataPath));
            var metadataRoot = metadata.RootElement;
            Assert.AreEqual("run-1", metadataRoot.GetProperty("runId").GetString());
            Assert.AreEqual("dotnet-build-test", metadataRoot.GetProperty("profileId").GetString());
            Assert.AreEqual("succeeded", metadataRoot.GetProperty("status").GetString());
            Assert.AreEqual(2, metadataRoot.GetProperty("steps").GetArrayLength());

            Assert.AreEqual(string.Empty, await GetGitStatusAsync(sourceRepo));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceValidate_BuildFailureStopsBeforeTest()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-validate-build-failure");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedDotnetTestWorkspaceAsync(testRoot, "run-1", sourceRepo, brokenBuild: true);

            using var doc = await RunWorkspaceValidateAsync("run-1", workspacePath, "dotnet-build-test", expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("failed", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "non-zero exit code");

            var data = root.GetProperty("data");
            var steps = data.GetProperty("steps").EnumerateArray().ToArray();
            Assert.AreEqual(1, steps.Length);
            Assert.AreEqual("dotnet-build", steps[0].GetProperty("commandId").GetString());
            Assert.AreEqual("failed", steps[0].GetProperty("status").GetString());
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "dotnet-test", "command.json")));
            Assert.IsTrue(File.Exists(data.GetProperty("validationMetadataPath").GetString()!));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceValidate_CommandBlockedStopsValidation()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-validate-command-blocked");
        try
        {
            var workspacePath = Path.Combine(testRoot, "workspace");
            Directory.CreateDirectory(workspacePath);
            var commandService = new FakeWorkspaceCommandService
            {
                OnRunAsync = request => Task.FromResult(new DisposableWorkspaceCommandExecutionResult
                {
                    Status = "blocked",
                    Summary = "Blocked by fake command service.",
                    ExitCode = 1,
                    Data = new DisposableWorkspaceCommandData
                    {
                        RunId = request.RunId,
                        WorkspacePath = request.WorkspacePath,
                        CommandId = request.CommandId,
                        WorkingDirectory = request.WorkspacePath,
                        ExitCode = -1,
                        Succeeded = false,
                        EvidencePaths = [],
                        Errors = ["fake blocked command"],
                        Warnings = []
                    },
                    Errors = ["fake blocked command"],
                    Warnings = []
                })
            };
            var service = new DisposableWorkspaceValidationService(commandService);

            var result = await service.ValidateAsync(
                new DisposableWorkspaceValidationRequest
                {
                    RunId = "run-1",
                    WorkspacePath = workspacePath,
                    ProfileId = "dotnet-build-test"
                },
                CancellationToken.None);

            Assert.AreEqual("blocked", result.Status);
            Assert.AreEqual(1, result.Data.Steps.Count);
            Assert.AreEqual("dotnet-build", result.Data.Steps[0].CommandId);
            CollectionAssert.AreEqual(new[] { "dotnet-build" }, commandService.CommandIds.ToArray());
            Assert.IsTrue(File.Exists(result.Data.ValidationMetadataPath!));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceValidate_JsonOutput_UsesStandardEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-validate-envelope");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);

            using var doc = await RunWorkspaceValidateAsync("run-1", workspacePath, "unknown-profile", expectedExitCode: 1);
            var root = doc.RootElement;
            var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
            var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEqual(
                expectedTopLevelKeys.OrderBy(item => item).ToArray(),
                topLevelProperties.OrderBy(item => item).ToArray());
            Assert.AreEqual("workspace validate", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public void WorkspaceValidationService_MustNotStartProcessesDirectly()
    {
        var repoRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IronDev.Infrastructure",
            "Services",
            "Workspaces",
            "DisposableWorkspaceValidationService.cs"));

        StringAssert.Contains(source, "IDisposableWorkspaceCommandService");
        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("/bin/sh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("-Command", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task WorkspaceDiff_MissingMetadata_BlocksDiff()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-missing-metadata");
        try
        {
            var workspacePath = Path.Combine(testRoot, "workspace");
            Directory.CreateDirectory(workspacePath);

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            Assert.AreEqual("workspace diff", root.GetProperty("command").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "metadata");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "diff.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_RunIdMismatch_BlocksDiff()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-runid-mismatch");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-a", sourceRepo);

            using var doc = await RunWorkspaceDiffAsync("run-b", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "runId mismatch");
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_MetadataMissingSourceRepo_BlocksDiff()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-missing-source-repo");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", sourceRepo, workspacePath, includeSourceRepo: false);

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "missing sourceRepo");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "diff.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_MetadataMissingWorkspacePath_BlocksDiff()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-missing-workspace-path");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", sourceRepo, workspacePath, includeWorkspacePath: false);

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "missing workspacePath");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "diff.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_MetadataSourceRepoInsideWorkspace_BlocksDiff()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-source-inside-workspace");
        try
        {
            var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
            var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, "run-1", sourceRepo);
            var nestedSourceRepo = Path.Combine(workspacePath, "nested-source");
            Directory.CreateDirectory(nestedSourceRepo);
            await WriteWorkspaceMetadataAsync("run-1", nestedSourceRepo, workspacePath);

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 1);
            var root = doc.RootElement;
            Assert.AreEqual("blocked", root.GetProperty("status").GetString());
            AssertStringArrayContains(root.GetProperty("errors"), "isolated");
            Assert.IsFalse(File.Exists(Path.Combine(workspacePath, ".irondev", "runs", "run-1", "diff.json")));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_NoChanges_ReturnsSucceededUnchanged()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-unchanged");
        try
        {
            var (sourceRepo, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var root = doc.RootElement;
            Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
            var data = root.GetProperty("data");
            Assert.IsFalse(data.GetProperty("changed").GetBoolean());
            Assert.AreEqual(0, data.GetProperty("addedFiles").GetArrayLength());
            Assert.AreEqual(0, data.GetProperty("modifiedFiles").GetArrayLength());
            Assert.AreEqual(0, data.GetProperty("deletedFiles").GetArrayLength());
            Assert.IsTrue(data.GetProperty("unchangedFileCount").GetInt32() > 0);
            Assert.IsTrue(File.Exists(data.GetProperty("diffMetadataPath").GetString()!));
            Assert.AreEqual(1, data.GetProperty("evidencePaths").GetArrayLength());
            Assert.AreEqual(string.Empty, await GetGitStatusAsync(sourceRepo));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_AddedFileIsDetected()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-added");
        try
        {
            var (_, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "new-file.txt"), "new");

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            Assert.IsTrue(data.GetProperty("changed").GetBoolean());
            AssertStringArrayContains(data.GetProperty("addedFiles"), "new-file.txt");
            Assert.AreEqual("succeeded", doc.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_ModifiedFileIsDetected()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-modified");
        try
        {
            var (_, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");
            await File.WriteAllTextAsync(Path.Combine(workspacePath, "README.md"), "changed");

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            Assert.IsTrue(data.GetProperty("changed").GetBoolean());
            AssertStringArrayContains(data.GetProperty("modifiedFiles"), "README.md");
            Assert.AreEqual("succeeded", doc.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_DeletedFileIsDetected()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-deleted");
        try
        {
            var (_, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");
            File.Delete(Path.Combine(workspacePath, "README.md"));

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            Assert.IsTrue(data.GetProperty("changed").GetBoolean());
            AssertStringArrayContains(data.GetProperty("deletedFiles"), "README.md");
            Assert.AreEqual("succeeded", doc.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_IgnoresIronDevEvidence()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-ignore-evidence");
        try
        {
            var (_, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");
            var evidencePath = Path.Combine(workspacePath, ".irondev", "runs", "run-1", "validation.json");
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            await File.WriteAllTextAsync(evidencePath, "{}");

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var data = doc.RootElement.GetProperty("data");
            Assert.IsFalse(data.GetProperty("changed").GetBoolean());
            Assert.AreEqual(0, data.GetProperty("addedFiles").GetArrayLength());
            Assert.AreEqual("succeeded", doc.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceDiff_JsonOutput_UsesStandardEnvelope()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-diff-envelope");
        try
        {
            var (_, workspacePath) = await PrepareTrackedWorkspaceForDiffAsync(testRoot, "run-1");

            using var doc = await RunWorkspaceDiffAsync("run-1", workspacePath, expectedExitCode: 0);
            var root = doc.RootElement;
            var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
            var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

            CollectionAssert.AreEqual(
                expectedTopLevelKeys.OrderBy(item => item).ToArray(),
                topLevelProperties.OrderBy(item => item).ToArray());
            Assert.AreEqual("workspace diff", root.GetProperty("command").GetString());
            Assert.IsFalse(root.TryGetProperty("loopReport", out _));
            Assert.IsFalse(root.TryGetProperty("processRun", out _));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public void WorkspaceDiffService_MustNotExecuteProcesses()
    {
        var repoRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IronDev.Infrastructure",
            "Services",
            "Workspaces",
            "DisposableWorkspaceDiffService.cs"));

        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("RunGit", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("powershell", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("/bin/sh", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("IAgent", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AgentRunSupervisor_MissingRequiredOptions_WithJson_ReturnsFailureEnvelope()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var result = await IronDevCli.RunAsync(
            ["agent", "run", "supervisor", "--json"],
            output,
            error,
            handler: null,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(2, result, error.ToString());
        AssertJsonWasWritten(output);

        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.AreEqual("failed", root.GetProperty("status").GetString());
        Assert.AreEqual("agent run supervisor", root.GetProperty("command").GetString());
        AssertArrayNotEmpty(root.GetProperty("errors"));
        Assert.AreEqual(string.Empty, root.GetProperty("data").GetProperty("runId").GetString());
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AgentRunSupervisor_PassesRequestToService(bool liveLlm)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                "succeeded",
                "succeeded",
                "not_required",
                false,
                0,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var arguments = new List<string>
        {
            "agent", "run", "supervisor",
            "--project", "IronDev",
            "--query", "check current run health",
            "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
            "--run-id", "AgentRunProof001",
            "--json"
        };
        if (liveLlm)
            arguments.AddRange(["--live-llm", "true"]);

        var result = await IronDevCli.RunAsync(
            arguments.ToArray(),
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(0, result, error.ToString());
        Assert.IsNotNull(fakeService.LastRequest);
        Assert.AreEqual("IronDev", fakeService.LastRequest!.Project);
        Assert.AreEqual("check current run health", fakeService.LastRequest.Query);
        Assert.AreEqual("tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json", fakeService.LastRequest.PlanPath);
        Assert.AreEqual("AgentRunProof001", fakeService.LastRequest.RunId);
        Assert.AreEqual(liveLlm, fakeService.LastRequest.LiveLlm);
    }

    [DataTestMethod]
    [DataRow("succeeded", "Succeeded", "not_required", false, 0)]
    [DataRow("blocked", "Blocked", "required", true, 1)]
    [DataRow("failed", "Failed", "denied", false, 1)]
    public async Task AgentRunSupervisor_ServiceResult_StatusMapsToContractEnvelope(
        string serviceStatus,
        string expectedAgentStatus,
        string expectedApprovalDecision,
        bool expectedRequiresHumanApproval,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                serviceStatus,
                serviceStatus,
                expectedApprovalDecision,
                expectedRequiresHumanApproval,
                expectedExitCode,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var result = await IronDevCli.RunAsync(
            [
                "agent", "run", "supervisor",
                "--project", "IronDev",
                "--query", "check current run health",
                "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                "--run-id", "AgentRunProof001",
                "--json"
            ],
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, error.ToString());

        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        Assert.AreEqual(serviceStatus, root.GetProperty("status").GetString());
        Assert.AreEqual("agent run supervisor", root.GetProperty("command").GetString());

        var data = root.GetProperty("data");
        Assert.AreEqual("SupervisorAgent", data.GetProperty("agent").GetString());
        Assert.AreEqual("AgentRunProof001", data.GetProperty("runId").GetString());
        Assert.AreEqual(expectedAgentStatus, data.GetProperty("agentStatus").GetString());
        Assert.AreEqual("report_ready", data.GetProperty("decision").GetString());
        Assert.AreEqual(serviceStatus, data.GetProperty("tester").GetProperty("commandStatus").GetString());

        var governance = data.GetProperty("tester").GetProperty("governance");
        Assert.AreEqual(expectedApprovalDecision, governance.GetProperty("approvalDecision").GetString());
        Assert.AreEqual(expectedRequiresHumanApproval, governance.GetProperty("requiresHumanApproval").GetBoolean());

        var failurePackage = data.GetProperty("failurePackage");
        if (serviceStatus == "succeeded")
        {
            Assert.AreEqual(JsonValueKind.Null, failurePackage.ValueKind);
        }
        else
        {
            Assert.AreEqual(JsonValueKind.Object, failurePackage.ValueKind);
            Assert.AreEqual("AgentRunProof001", failurePackage.GetProperty("runId").GetString());
            Assert.AreEqual(serviceStatus, failurePackage.GetProperty("status").GetString());
            Assert.AreEqual("report_ready", failurePackage.GetProperty("decision").GetString());
            Assert.AreEqual(serviceStatus, failurePackage.GetProperty("testerCommandStatus").GetString());
            AssertArrayNotEmpty(failurePackage.GetProperty("errors"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(failurePackage.GetProperty("recommendedNextAction").GetString()));
            var recoveryPlan = failurePackage.GetProperty("recoveryPlan");
            Assert.AreEqual(JsonValueKind.Object, recoveryPlan.ValueKind);
            Assert.IsFalse(recoveryPlan.GetProperty("allowsPatching").GetBoolean());
            Assert.IsFalse(recoveryPlan.GetProperty("allowsExecution").GetBoolean());
            AssertArrayNotEmpty(recoveryPlan.GetProperty("proposedSteps"));
            AssertArrayNotEmpty(recoveryPlan.GetProperty("stopConditions"));
        }
    }

    [TestMethod]
    public async Task AgentRunSupervisor_SucceededRun_HasNoFailurePackage()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                "succeeded",
                "succeeded",
                "not_required",
                false,
                0,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var result = await IronDevCli.RunAsync(
            [
                "agent", "run", "supervisor",
                "--project", "IronDev",
                "--query", "check current run health",
                "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                "--run-id", "AgentRunProof001",
                "--json"
            ],
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(0, result, error.ToString());
        using var doc = JsonDocument.Parse(output.ToString());
        var data = doc.RootElement.GetProperty("data");
        Assert.AreEqual(JsonValueKind.Null, data.GetProperty("failurePackage").ValueKind);
    }

    [TestMethod]
    public async Task AgentRunSupervisor_BlockedRun_IncludesFailurePackageWithoutAutoPatchRecommendation()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                "blocked",
                "blocked",
                "required",
                true,
                1,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var result = await IronDevCli.RunAsync(
            [
                "agent", "run", "supervisor",
                "--project", "IronDev",
                "--query", "check current run health",
                "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                "--run-id", "AgentRunProof001",
                "--json"
            ],
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, result, error.ToString());
        using var doc = JsonDocument.Parse(output.ToString());
        var failurePackage = doc.RootElement.GetProperty("data").GetProperty("failurePackage");
        Assert.AreEqual("blocked", failurePackage.GetProperty("status").GetString());
        Assert.AreEqual("AwaitingHumanApproval", failurePackage.GetProperty("blockedReason").GetString());
        StringAssert.Contains(failurePackage.GetProperty("recommendedNextAction").GetString(), "Do not patch automatically");
        var recoveryPlan = failurePackage.GetProperty("recoveryPlan");
        Assert.IsFalse(recoveryPlan.GetProperty("allowsPatching").GetBoolean());
        Assert.IsFalse(recoveryPlan.GetProperty("allowsExecution").GetBoolean());
        AssertStringArrayContains(recoveryPlan.GetProperty("requiredHumanChecks"), "approval");
        AssertStringArrayContains(recoveryPlan.GetProperty("stopConditions"), "Do not patch automatically");
    }

    [TestMethod]
    public async Task AgentRunSupervisor_MissingTesterContract_FailurePackageRecommendsContractInspection()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                "failed",
                "not_available",
                "not_available",
                false,
                1,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var result = await IronDevCli.RunAsync(
            [
                "agent", "run", "supervisor",
                "--project", "IronDev",
                "--query", "check current run health",
                "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                "--run-id", "AgentRunProof001",
                "--json"
            ],
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, result, error.ToString());
        using var doc = JsonDocument.Parse(output.ToString());
        var data = doc.RootElement.GetProperty("data");
        Assert.AreEqual("not_available", data.GetProperty("tester").GetProperty("commandStatus").GetString());
        var recommendedNextAction = data.GetProperty("failurePackage").GetProperty("recommendedNextAction").GetString();
        Assert.IsTrue(
            recommendedNextAction?.Contains("tester run output", StringComparison.OrdinalIgnoreCase) == true ||
            recommendedNextAction?.Contains("run-report contract", StringComparison.OrdinalIgnoreCase) == true,
            $"Unexpected recommended next action: {recommendedNextAction}");
        var recoveryPlan = data.GetProperty("failurePackage").GetProperty("recoveryPlan");
        StringAssert.Contains(recoveryPlan.GetProperty("problemSummary").GetString(), "Tester run-report contract");
        AssertStringArrayContains(recoveryPlan.GetProperty("proposedSteps"), "tester run output");
        AssertStringArrayContains(recoveryPlan.GetProperty("proposedSteps"), "run-report contract");
        Assert.IsFalse(recoveryPlan.GetProperty("allowsPatching").GetBoolean());
        Assert.IsFalse(recoveryPlan.GetProperty("allowsExecution").GetBoolean());
    }

    [TestMethod]
    public async Task AgentRunSupervisor_FailedTesterRecoveryPlan_TargetsEvidenceInspection()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                "failed",
                "failed",
                "denied",
                false,
                1,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var result = await IronDevCli.RunAsync(
            [
                "agent", "run", "supervisor",
                "--project", "IronDev",
                "--query", "check current run health",
                "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                "--run-id", "AgentRunProof001",
                "--json"
            ],
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, result, error.ToString());
        using var doc = JsonDocument.Parse(output.ToString());
        var failurePackage = doc.RootElement.GetProperty("data").GetProperty("failurePackage");
        Assert.AreEqual("failed", failurePackage.GetProperty("testerCommandStatus").GetString());
        var recoveryPlan = failurePackage.GetProperty("recoveryPlan");
        AssertStringArrayContains(recoveryPlan.GetProperty("evidenceToInspect"), "logs/tester-evidence.log");
        AssertStringArrayContains(recoveryPlan.GetProperty("proposedSteps"), "evidence");
        AssertStringArrayContains(recoveryPlan.GetProperty("proposedSteps"), "failing build/test command");
        Assert.IsFalse(recoveryPlan.GetProperty("allowsPatching").GetBoolean());
        Assert.IsFalse(recoveryPlan.GetProperty("allowsExecution").GetBoolean());
    }

    [TestMethod]
    public async Task AgentRunSupervisor_JsonOutput_IsTypedContractEnvelope()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var fakeService = new FakeSupervisorAgentRunService
        {
            OnRunAsync = (_, _) => Task.FromResult(BuildSupervisorRunResult(
                "succeeded",
                "succeeded",
                "not_required",
                false,
                0,
                "AgentRunProof001",
                "IronDev",
                "check current run health",
                "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json"))
        };

        var result = await IronDevCli.RunAsync(
            [
                "agent", "run", "supervisor",
                "--project", "IronDev",
                "--query", "check current run health",
                "--plan", "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                "--run-id", "AgentRunProof001",
                "--json"
            ],
            output,
            error,
            handler: null,
            supervisorAgentRunService: fakeService,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(0, result, error.ToString());
        AssertJsonWasWritten(output);

        using var doc = JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
        var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEqual(
            expectedTopLevelKeys.OrderBy(item => item).ToArray(),
            topLevelProperties.OrderBy(item => item).ToArray());
        Assert.IsFalse(root.TryGetProperty("loopReport", out _));
        Assert.IsFalse(root.TryGetProperty("processRun", out _));
        Assert.AreEqual("succeeded", root.GetProperty("status").GetString());
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
    public void IronDevCli_ProjectReferencesSupervisorServiceButNotReplayRunner()
    {
        var repoRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(repoRoot, "tools", "IronDev.Cli", "IronDev.Cli.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.IsTrue(project.Contains("IronDev.Infrastructure", StringComparison.Ordinal), "CLI must reference Infrastructure for supervisor run orchestration.");
        Assert.IsFalse(project.Contains("IronDev.ReplayRunner", StringComparison.Ordinal), "CLI must not reference ReplayRunner directly.");
    }

    [TestMethod]
    public void IronDevCli_SourceMustNotShellOutToReplayRunnerForSupervisorCommand()
    {
        var repoRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "tools", "IronDev.Cli", "IronDevCli.cs"));

        Assert.IsFalse(source.Contains("RunReplayRunnerAsync", StringComparison.Ordinal), "Product CLI supervisor command must not invoke ReplayRunner.");
        Assert.IsFalse(source.Contains("BuildAgentRunSupervisorReplayArguments", StringComparison.Ordinal), "Product CLI supervisor command must not build ReplayRunner arguments.");
        Assert.IsFalse(source.Contains("tools/IronDev.ReplayRunner", StringComparison.Ordinal), "Product CLI supervisor command must not reference ReplayRunner project paths.");
        Assert.IsFalse(source.Contains("tools\\\\IronDev.ReplayRunner", StringComparison.Ordinal), "Product CLI supervisor command must not reference ReplayRunner project paths.");
    }

    [TestMethod]
    public void SupervisorCliProofOutput_ShouldBeContractEnvelope()
    {
        var repoRoot = FindRepositoryRoot();
        var proofPath = Path.Combine(
            repoRoot,
            "tools",
            "dogfood",
            "proofs",
            "supervisor-cli-proof",
            "supervisor-cli-proof-001.json");

        using var document = JsonDocument.Parse(File.ReadAllText(proofPath));
        var root = document.RootElement;
        var expectedTopLevelKeys = new[] { "status", "command", "traceId", "summary", "data", "errors", "warnings" };
        var topLevelProperties = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        CollectionAssert.AreEqual(
            expectedTopLevelKeys.OrderBy(item => item).ToArray(),
            topLevelProperties.OrderBy(item => item).ToArray());
        Assert.AreEqual("agent run supervisor", root.GetProperty("command").GetString());
        Assert.IsFalse(root.TryGetProperty("loopReport", out _));
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

    private static async Task<JsonDocument> RunWorkspaceCheckAsync(
        string runId,
        string sourceRepo,
        string workspaceRoot,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "workspace", "check",
                "--run-id", runId,
                "--source-repo", sourceRepo,
                "--workspace-root", workspaceRoot,
                "--json"
            ],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, error.ToString());
        AssertJsonWasWritten(output);
        return JsonDocument.Parse(output.ToString());
    }

    private static async Task<JsonDocument> RunWorkspacePrepareAsync(
        string runId,
        string sourceRepo,
        string workspaceRoot,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "workspace", "prepare",
                "--run-id", runId,
                "--source-repo", sourceRepo,
                "--workspace-root", workspaceRoot,
                "--json"
            ],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, error.ToString());
        AssertJsonWasWritten(output);
        return JsonDocument.Parse(output.ToString());
    }

    private static async Task<JsonDocument> RunWorkspaceCommandAsync(
        string runId,
        string workspacePath,
        string commandId,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "workspace", "run",
                "--run-id", runId,
                "--workspace-path", workspacePath,
                "--command", commandId,
                "--json"
            ],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, error.ToString());
        AssertJsonWasWritten(output);
        return JsonDocument.Parse(output.ToString());
    }

    private static async Task<JsonDocument> RunWorkspaceValidateAsync(
        string runId,
        string workspacePath,
        string profileId,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "workspace", "validate",
                "--run-id", runId,
                "--workspace-path", workspacePath,
                "--profile", profileId,
                "--json"
            ],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, error.ToString());
        AssertJsonWasWritten(output);
        return JsonDocument.Parse(output.ToString());
    }

    private static async Task<JsonDocument> RunWorkspaceDiffAsync(
        string runId,
        string workspacePath,
        int expectedExitCode)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var result = await IronDevCli.RunAsync(
            [
                "workspace", "diff",
                "--run-id", runId,
                "--workspace-path", workspacePath,
                "--json"
            ],
            output,
            error,
            handler: null,
            CancellationToken.None);

        Assert.AreEqual(expectedExitCode, result, error.ToString());
        AssertJsonWasWritten(output);
        return JsonDocument.Parse(output.ToString());
    }

    private static string CreateTemporaryDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> CreateTemporaryGitRepositoryAsync(string testRoot)
    {
        var sourceRepo = Path.Combine(testRoot, "source");
        Directory.CreateDirectory(sourceRepo);
        await RunGitForTestAsync(sourceRepo, "init", "-q");
        return sourceRepo;
    }

    private static async Task<string> CreateTemporaryGitRepositoryWithTrackedFilesAsync(string testRoot)
    {
        var sourceRepo = await CreateTemporaryGitRepositoryAsync(testRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceRepo, "README.md"), "hello");
        Directory.CreateDirectory(Path.Combine(sourceRepo, "src"));
        await File.WriteAllTextAsync(Path.Combine(sourceRepo, "src", "app.cs"), "public static class App { }");
        Directory.CreateDirectory(Path.Combine(sourceRepo, "bin"));
        await File.WriteAllTextAsync(Path.Combine(sourceRepo, "bin", "ignored.dll"), "bin");
        Directory.CreateDirectory(Path.Combine(sourceRepo, "obj"));
        await File.WriteAllTextAsync(Path.Combine(sourceRepo, "obj", "ignored.obj"), "obj");
        Directory.CreateDirectory(Path.Combine(sourceRepo, ".vs"));
        await File.WriteAllTextAsync(Path.Combine(sourceRepo, ".vs", "state.json"), "{}");
        Directory.CreateDirectory(Path.Combine(sourceRepo, "TestResults"));
        await File.WriteAllTextAsync(Path.Combine(sourceRepo, "TestResults", "result.trx"), "test");
        Directory.CreateDirectory(Path.Combine(sourceRepo, "tools", "dogfood", "runs"));
        await File.WriteAllTextAsync(Path.Combine(sourceRepo, "tools", "dogfood", "runs", "run.json"), "{}");

        await RunGitForTestAsync(sourceRepo, "config", "user.email", "irondev-tests@example.local");
        await RunGitForTestAsync(sourceRepo, "config", "user.name", "IronDev Tests");
        await RunGitForTestAsync(sourceRepo, "add", ".");
        await RunGitForTestAsync(sourceRepo, "commit", "-m", "initial", "-q");
        return sourceRepo;
    }

    private static async Task<string> CreatePreparedWorkspaceAsync(
        string testRoot,
        string runId,
        string sourceRepo)
    {
        var workspacePath = Path.GetFullPath(Path.Combine(testRoot, "workspaces", runId));
        Directory.CreateDirectory(Path.Combine(workspacePath, ".irondev"));
        await WriteWorkspaceMetadataAsync(runId, sourceRepo, workspacePath);
        return workspacePath;
    }

    private static async Task<(string SourceRepo, string WorkspacePath)> PrepareTrackedWorkspaceForDiffAsync(
        string testRoot,
        string runId)
    {
        var sourceRepo = await CreateTemporaryGitRepositoryWithTrackedFilesAsync(testRoot);
        var workspaceRoot = Path.Combine(testRoot, "workspaces");
        Directory.CreateDirectory(workspaceRoot);
        using var doc = await RunWorkspacePrepareAsync(runId, sourceRepo, workspaceRoot, expectedExitCode: 0);
        return (sourceRepo, doc.RootElement.GetProperty("data").GetProperty("workspacePath").GetString()!);
    }

    private static async Task<string> CreatePreparedDotnetWorkspaceAsync(
        string testRoot,
        string runId,
        string sourceRepo,
        bool brokenProgram)
    {
        var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, runId, sourceRepo);
        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "TestApp.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "Program.cs"),
            brokenProgram
                ? "Console.WriteLine(\"broken\""
                : "Console.WriteLine(\"hello from disposable workspace\");");
        await RunDotnetForTestAsync(workspacePath, "restore", "--nologo");
        return workspacePath;
    }

    private static async Task<string> CreatePreparedDotnetTestWorkspaceAsync(
        string testRoot,
        string runId,
        string sourceRepo,
        bool brokenBuild)
    {
        var workspacePath = await CreatePreparedWorkspaceAsync(testRoot, runId, sourceRepo);
        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "ValidationSample.Tests.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <LangVersion>latest</LangVersion>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="MSTest" Version="4.0.2" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "SampleTests.cs"),
            brokenBuild
                ? "using Microsoft.VisualStudio.TestTools.UnitTesting; [TestClass] public sealed class SampleTests { [TestMethod] public void Passes() { Assert.AreEqual(2, 1 + ); } }"
                : "using Microsoft.VisualStudio.TestTools.UnitTesting; [TestClass] public sealed class SampleTests { [TestMethod] public void Passes() { Assert.AreEqual(2, 1 + 1); } }");
        await RunDotnetForTestAsync(workspacePath, "restore", "--nologo");
        return workspacePath;
    }

    private static async Task WriteWorkspaceMetadataAsync(
        string runId,
        string sourceRepo,
        string workspacePath,
        bool includeSourceRepo = true,
        bool includeWorkspacePath = true)
    {
        var metadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        var metadata = new Dictionary<string, object?>
        {
            ["runId"] = runId,
            ["createdUtc"] = DateTimeOffset.UtcNow,
            ["preparationMethod"] = "copy",
            ["sourceRepoMutated"] = false
        };
        if (includeSourceRepo)
            metadata["sourceRepo"] = Path.GetFullPath(sourceRepo);
        if (includeWorkspacePath)
            metadata["workspacePath"] = Path.GetFullPath(workspacePath);

        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(
                metadata,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                }));
    }

    private static async Task RunGitForTestAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {await stdout} {await stderr}");
        }
    }

    private static async Task RunDotnetForTestAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {await stdout} {await stderr}");
        }
    }

    private static async Task<string> GetGitStatusAsync(string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("status");
        startInfo.ArgumentList.Add("--porcelain");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git status failed with exit code {process.ExitCode}: {await stderr}");

        return (await stdout).Trim();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup for temp test directories on Windows.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup for temp test directories on Windows.
        }
    }

    private static SupervisorAgentRunResult BuildSupervisorRunResult(
        string serviceStatus,
        string commandStatus,
        string approvalDecision,
        bool requiresHumanApproval,
        int exitCode,
        string runId,
        string project,
        string query,
        string planPath)
    {
        return new SupervisorAgentRunResult
        {
            Status = serviceStatus,
            Summary = $"Supervisor run '{runId}' completed with status {serviceStatus}.",
            TraceId = "trace-supervisor",
            ExitCode = exitCode,
            Data = new AgentRunSupervisorContractData
            {
                Agent = "SupervisorAgent",
                RunId = runId,
                Project = project,
                Query = query,
                PlanPath = planPath,
                AgentStatus = serviceStatus == "succeeded"
                    ? "Succeeded"
                    : serviceStatus == "blocked"
                        ? "Blocked"
                        : "Failed",
                ExitCode = exitCode,
                Decision = "report_ready",
                DecisionReason = "Report is ready.",
                Tester = new AgentRunSupervisorTesterData
                {
                    RunId = "AgentRunProof001-tester",
                    TraceId = "trace-supervisor-tester",
                    CommandStatus = commandStatus,
                    RunStatus = commandStatus == "blocked"
                        ? "PausedForApproval"
                        : commandStatus == "not_available"
                            ? "not_available"
                            : "Completed",
                    Governance = new AgentRunSupervisorGovernanceData
                    {
                        Decision = "derived",
                        ApprovalDecision = approvalDecision,
                        BlockedReason = commandStatus == "blocked" ? "AwaitingHumanApproval" : null,
                        RequiresHumanApproval = requiresHumanApproval
                    },
                    Warnings = []
                },
                EvidencePaths = commandStatus == "failed" ? ["logs/tester-evidence.log"] : [],
                CommandsRun = ["dotnet test"],
                Warnings = [],
                FailurePackage = serviceStatus == "succeeded"
                    ? null
                    : new SupervisorFailurePackage
                    {
                        RunId = runId,
                        Status = serviceStatus,
                        Decision = "report_ready",
                        DecisionReason = "Report is ready.",
                        TesterCommandStatus = commandStatus,
                        TesterRunStatus = commandStatus == "blocked"
                            ? "PausedForApproval"
                            : commandStatus == "not_available"
                                ? "not_available"
                                : "Completed",
                        BlockedReason = commandStatus == "blocked" ? "AwaitingHumanApproval" : null,
                        Warnings = [],
                        Errors = ["Supervisor run failed."],
                        EvidencePaths = commandStatus == "failed" ? ["logs/tester-evidence.log"] : [],
                        CommandsRun = ["dotnet test"],
                        RecommendedNextAction = commandStatus == "blocked"
                            ? "Review approval/block reason before continuing. Do not patch automatically."
                            : commandStatus == "not_available"
                                ? "Inspect tester run output and restore a valid tester run-report contract before patching."
                                : "Inspect tester evidence paths and produce a fix plan before patching.",
                        RecoveryPlan = BuildSupervisorRecoveryPlanForTest(serviceStatus, commandStatus, runId)
                    }
            },
            Errors = exitCode == 0 ? [] : ["Supervisor run failed."],
            Warnings = []
        };
    }

    private static SupervisorRecoveryPlan BuildSupervisorRecoveryPlanForTest(
        string serviceStatus,
        string commandStatus,
        string runId)
    {
        if (commandStatus == "blocked")
        {
            return new SupervisorRecoveryPlan
            {
                RunId = runId,
                Status = "planned",
                SourceFailureStatus = serviceStatus,
                ProblemSummary = "Supervisor run is blocked and requires human review.",
                EvidenceToInspect = [],
                SuspectedCauses = ["The tester or governance layer reported a blocked state."],
                ProposedSteps = ["Review blocked reason.", "Confirm approval boundary.", "Do not patch automatically."],
                StopConditions = ["Do not patch automatically.", "Do not execute recovery without explicit follow-up approval."],
                RequiredHumanChecks = ["Review approval/block reason before continuing."],
                AllowsPatching = false,
                AllowsExecution = false
            };
        }

        if (commandStatus == "not_available")
        {
            return new SupervisorRecoveryPlan
            {
                RunId = runId,
                Status = "planned",
                SourceFailureStatus = serviceStatus,
                ProblemSummary = "Tester run-report contract was unavailable.",
                EvidenceToInspect = [],
                SuspectedCauses = ["TesterAgent output may not have produced a run-report contract."],
                ProposedSteps = ["Inspect tester run output.", "Inspect evidence paths.", "Restore valid tester run-report contract before any patching."],
                StopConditions = ["Do not patch automatically.", "Do not execute recovery without explicit follow-up approval."],
                RequiredHumanChecks = [],
                AllowsPatching = false,
                AllowsExecution = false
            };
        }

        return new SupervisorRecoveryPlan
        {
            RunId = runId,
            Status = "planned",
            SourceFailureStatus = serviceStatus,
            ProblemSummary = "Tester run failed and requires evidence inspection.",
            EvidenceToInspect = ["logs/tester-evidence.log"],
            SuspectedCauses = ["A build, test, or quality command may have failed."],
            ProposedSteps = ["Inspect tester evidence paths.", "Identify failing build/test command.", "Produce a fix plan."],
            StopConditions = ["Do not patch automatically.", "Do not execute recovery without explicit follow-up approval."],
            RequiredHumanChecks = [],
            AllowsPatching = false,
            AllowsExecution = false
        };
    }

    private sealed class FakeSupervisorAgentRunService : ISupervisorAgentRunService
    {
        public SupervisorAgentRunRequest? LastRequest { get; private set; }
        public Func<SupervisorAgentRunRequest, CancellationToken, Task<SupervisorAgentRunResult>>? OnRunAsync { get; set; }

        public Task<SupervisorAgentRunResult> RunAsync(SupervisorAgentRunRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (OnRunAsync is null)
                throw new InvalidOperationException("No fake supervisor service handler was configured.");

            return OnRunAsync(request, cancellationToken);
        }
    }

    private sealed class FakeWorkspaceCommandService : IDisposableWorkspaceCommandService
    {
        public List<string> CommandIds { get; } = [];
        public Func<DisposableWorkspaceCommandRequest, Task<DisposableWorkspaceCommandExecutionResult>>? OnRunAsync { get; set; }

        public Task<DisposableWorkspaceCommandExecutionResult> RunAsync(
            DisposableWorkspaceCommandRequest request,
            CancellationToken cancellationToken = default)
        {
            CommandIds.Add(request.CommandId);
            if (OnRunAsync is null)
                throw new InvalidOperationException("No fake workspace command handler was configured.");

            return OnRunAsync(request);
        }
    }

    private static void AssertEqualsIgnoreCase(string expected, string? actual)
    {
        Assert.IsTrue(
            string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase),
            $"Expected '{expected}' ignoring case but got '{actual}'.");
    }

    private static void AssertJsonWasWritten(StringWriter output)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(output.ToString()));
    }

    private static void AssertArrayNotEmpty(JsonElement element)
    {
        Assert.IsTrue(element.GetArrayLength() > 0);
    }

    private static void AssertStringArrayContains(JsonElement element, string expected)
    {
        Assert.IsTrue(
            element.ValueKind == JsonValueKind.Array &&
            element.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String &&
                item.GetString()?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true),
            $"Expected string array to contain '{expected}'.");
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
