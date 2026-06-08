using System.Net;
using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using IronDev.Core.RunReports;
using IronDev.Infrastructure.Services.Agents;
using IronDev.Infrastructure.Services.Agents.WorkspaceApply;
using IronDev.Infrastructure.Services.Agents.ApprovalPolicy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkspaceApplyReportAgentTests
{
    [TestMethod]
    public async Task WorkspaceApplyReportReader_SourceReport_ReturnsSuccessSummary()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-report-success");
        try
        {
            var workspacePath = CreateRunDirectory(testRoot, "run-1");
            await WriteSourceReportAsync(workspacePath, "run-1");
            var reader = new WorkspaceApplyReportReader();

            var result = await reader.ReadAsync(new WorkspaceApplyReportRequest
            {
                RunId = "run-1",
                WorkspacePath = workspacePath
            });

            Assert.AreEqual("success", result.Outcome);
            Assert.AreEqual(1, result.AddCount);
            Assert.AreEqual(1, result.ModifyCount);
            Assert.IsTrue(result.SourceRepoMutated);
            Assert.IsTrue(result.ApplyVerified);
            Assert.IsTrue(result.PostApplyValidationSucceeded);
            Assert.AreEqual("ready_for_human_review_or_commit", result.Recommendation);
            Assert.AreEqual(2, result.Files.Count);
            Assert.IsTrue(result.Files.Any(file => file.Operation == "add" && file.RelativePath == "Feature.cs"));
            Assert.IsTrue(result.Files.Any(file => file.Operation == "modify" && file.RelativePath == "Program.cs"));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyReportReader_FailurePackage_ReturnsFailureSummary()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-report-failure");
        try
        {
            var workspacePath = CreateRunDirectory(testRoot, "run-1");
            await WriteFailurePackageAsync(workspacePath, "run-1");
            var reader = new WorkspaceApplyReportReader();

            var result = await reader.ReadAsync(new WorkspaceApplyReportRequest
            {
                RunId = "run-1",
                WorkspacePath = workspacePath
            });

            Assert.AreEqual("failure", result.Outcome);
            Assert.AreEqual("apply-copy", result.FailedStage);
            Assert.AreEqual("critical", result.FailureSeverity);
            Assert.AreEqual("do_not_retry_until_source_reviewed", result.RecommendedNextAction);
            Assert.IsTrue(result.SourceRepoMutated);
            Assert.IsFalse(result.ApplyVerified);
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyReportReader_SourceReportWinsOverOlderFailurePackage()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-report-precedence");
        try
        {
            var workspacePath = CreateRunDirectory(testRoot, "run-1");
            await WriteSourceReportAsync(workspacePath, "run-1");
            await WriteFailurePackageAsync(workspacePath, "run-1");
            var reader = new WorkspaceApplyReportReader();

            var result = await reader.ReadAsync(new WorkspaceApplyReportRequest
            {
                RunId = "run-1",
                WorkspacePath = workspacePath
            });

            Assert.AreEqual("success", result.Outcome);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.SourceReportPath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.FailurePackagePath));
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("source-report.json won", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyReportReader_NoReports_ReturnsUnavailable()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-report-unavailable");
        try
        {
            var workspacePath = CreateRunDirectory(testRoot, "run-1");
            var reader = new WorkspaceApplyReportReader();

            var result = await reader.ReadAsync(new WorkspaceApplyReportRequest
            {
                RunId = "run-1",
                WorkspacePath = workspacePath
            });

            Assert.AreEqual("unavailable", result.Outcome);
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("No source-report.json or failure-package.json", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task WorkspaceApplyReportReader_UnreadableSourceReport_FallsBackToFailurePackage()
    {
        var testRoot = CreateTemporaryDirectory("irondev-workspace-apply-report-fallback");
        try
        {
            var workspacePath = CreateRunDirectory(testRoot, "run-1");
            var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", "run-1");
            await File.WriteAllTextAsync(Path.Combine(runDirectory, "source-report.json"), "{not-json");
            await WriteFailurePackageAsync(workspacePath, "run-1");
            var reader = new WorkspaceApplyReportReader();

            var result = await reader.ReadAsync(new WorkspaceApplyReportRequest
            {
                RunId = "run-1",
                WorkspacePath = workspacePath
            });

            Assert.AreEqual("failure", result.Outcome);
            Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("source-report.json could not be read", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    [TestMethod]
    public async Task SupervisorAgent_IncludesWorkspaceApplySuccessReportSummary()
    {
        var summary = BuildSuccessSummary("apply-run", "C:\\workspaces\\apply-run");
        var result = await RunSupervisorWithWorkspaceApplySummaryAsync(summary);

        using var document = JsonDocument.Parse(result.OutputJson);
        var workspaceApply = document.RootElement.GetProperty("workspaceApply");
        Assert.AreEqual("success", GetStringPropertyIgnoreCase(workspaceApply, "outcome"));
        Assert.AreEqual(1, GetIntPropertyIgnoreCase(workspaceApply, "addCount"));
        Assert.AreEqual(1, GetIntPropertyIgnoreCase(workspaceApply, "modifyCount"));
        Assert.AreEqual("ready_for_human_review_or_commit", GetStringPropertyIgnoreCase(workspaceApply, "recommendation"));
    }

    [TestMethod]
    public async Task SupervisorAgent_IncludesWorkspaceApplyFailureReportSummary()
    {
        var summary = BuildFailureSummary("apply-run", "C:\\workspaces\\apply-run");
        var result = await RunSupervisorWithWorkspaceApplySummaryAsync(summary);

        using var document = JsonDocument.Parse(result.OutputJson);
        var workspaceApply = document.RootElement.GetProperty("workspaceApply");
        Assert.AreEqual("failure", GetStringPropertyIgnoreCase(workspaceApply, "outcome"));
        Assert.AreEqual("critical", GetStringPropertyIgnoreCase(workspaceApply, "failureSeverity"));
        Assert.AreEqual("do_not_retry_until_source_reviewed", GetStringPropertyIgnoreCase(workspaceApply, "recommendedNextAction"));
    }

    [TestMethod]
    public void WorkspaceApplyReportAgentBoundary_DoesNotAddMutationOrProcessCapabilities()
    {
        var repoRoot = FindRepositoryRoot();
        var readerSource = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "WorkspaceApplyReportReader.cs"));
        var supervisorSource = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "SupervisorAgent.cs"));

        Assert.IsFalse(readerSource.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(readerSource.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(readerSource.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(readerSource.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(readerSource.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));

        Assert.IsFalse(supervisorSource.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspacePrepareService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
    }

    private static async Task<AgentResult> RunSupervisorWithWorkspaceApplySummaryAsync(WorkspaceApplyReportSummary summary)
    {
        var fakeRunReportReader = new FakeRunReportContractReader("agent-run-tester");
        var fakeWorkspaceReader = new FakeWorkspaceApplyReportReader(summary);
        var fakeRunner = new FakeAgentProcessRunner(
            [
                new AgentProcessRunResult(0, """{"status":"Succeeded","contextPackage":{"Matches":[{"DocumentTitle":"Memory title"}],"SemanticTraceId":"trace-mem","WeightedContextBundle":{"summaryForAgent":"Memory context."}}}""", string.Empty, false, "retriever"),
                new AgentProcessRunResult(0, """{"review":{"decision":"Allow"}}""", string.Empty, false, "conscience"),
                new AgentProcessRunResult(0, """{"thoughtLedger":{}}""", string.Empty, false, "thoughtLedger"),
                new AgentProcessRunResult(0, """{"report":{"status":"Passed"}}""", string.Empty, false, "tester-run-plan")
            ]);
        var agent = new SupervisorAgent(
            new AgentDefinition
            {
                Name = "SupervisorAgent",
                Purpose = "Test workspace apply report consumption.",
                DefaultModelProfile = "cheap-runner"
            },
            new AgentModelResolver(),
            FindRepositoryRoot(),
            fakeRunReportReader,
            processRunner: fakeRunner,
            workspaceApplyContextService: BuildWorkspaceApplyContextService(fakeWorkspaceReader));

        return await agent.RunAsync(new AgentRequest
        {
            AgentName = "SupervisorAgent",
            GoalId = "test-goal",
            DogfoodRunId = "agent-run",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "IronDev",
                ["query"] = "Summarize governed workspace apply.",
                ["plan_path"] = "TestPlan.md",
                ["live_llm"] = "false",
                ["workspace_apply_run_id"] = summary.RunId,
                ["workspace_apply_workspace_path"] = summary.WorkspacePath
            }
        });
    }

    private static WorkspaceApplyReportSummary BuildSuccessSummary(string runId, string workspacePath) =>
        new()
        {
            RunId = runId,
            WorkspacePath = workspacePath,
            Outcome = "success",
            Recommendation = "ready_for_human_review_or_commit",
            SourceRepoMutated = true,
            ApplyVerified = true,
            SourceMatchesWorkspace = true,
            PostApplyValidationSucceeded = true,
            AddCount = 1,
            ModifyCount = 1,
            Files =
            [
                new WorkspaceApplyChangedFileSummary { Operation = "add", RelativePath = "Feature.cs", Applied = true, Verified = true },
                new WorkspaceApplyChangedFileSummary { Operation = "modify", RelativePath = "Program.cs", Applied = true, Verified = true }
            ],
            SourceReportPath = Path.Combine(workspacePath, ".irondev", "runs", runId, "source-report.json"),
            EvidencePaths = [Path.Combine(workspacePath, ".irondev", "runs", runId, "source-report.json")]
        };

    private static WorkspaceApplyReportSummary BuildFailureSummary(string runId, string workspacePath) =>
        new()
        {
            RunId = runId,
            WorkspacePath = workspacePath,
            Outcome = "failure",
            FailedStage = "apply-copy",
            FailureSeverity = "critical",
            RecommendedNextAction = "do_not_retry_until_source_reviewed",
            SourceRepoMutated = true,
            ApplyVerified = false,
            FailurePackagePath = Path.Combine(workspacePath, ".irondev", "runs", runId, "failure-package.json"),
            EvidencePaths = [Path.Combine(workspacePath, ".irondev", "runs", runId, "failure-package.json")]
        };

    private static async Task WriteSourceReportAsync(string workspacePath, string runId)
    {
        var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", runId);
        await File.WriteAllTextAsync(
            Path.Combine(runDirectory, "source-report.json"),
            JsonSerializer.Serialize(
                new
                {
                    runId,
                    workspacePath,
                    sourceRepo = Path.Combine(Path.GetDirectoryName(workspacePath)!, "source"),
                    sourceRepoMutated = true,
                    applyVerified = true,
                    sourceMatchesWorkspace = true,
                    postApplyValidationSucceeded = true,
                    recommendation = "ready_for_human_review_or_commit",
                    files = new[]
                    {
                        new { operation = "add", relativePath = "Feature.cs", applied = true, verified = true },
                        new { operation = "modify", relativePath = "Program.cs", applied = true, verified = true }
                    },
                    addCount = 1,
                    modifyCount = 1,
                    deleteCount = 0,
                    riskNotes = new[] { "Human should review changed files before commit/PR." },
                    evidencePaths = new[] { Path.Combine(runDirectory, "source-report.json") },
                    warnings = Array.Empty<string>(),
                    errors = Array.Empty<string>()
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static async Task WriteFailurePackageAsync(string workspacePath, string runId)
    {
        var runDirectory = Path.Combine(workspacePath, ".irondev", "runs", runId);
        await File.WriteAllTextAsync(
            Path.Combine(runDirectory, "failure-package.json"),
            JsonSerializer.Serialize(
                new
                {
                    runId,
                    workspacePath,
                    sourceRepo = Path.Combine(Path.GetDirectoryName(workspacePath)!, "source"),
                    failedStage = "apply-copy",
                    sourceRepoMutated = true,
                    applyVerified = false,
                    postApplyValidationSucceeded = false,
                    failureSeverity = "critical",
                    recommendedNextAction = "do_not_retry_until_source_reviewed",
                    riskNotes = new[] { "Applied source state has not been verified." },
                    evidencePaths = new[] { Path.Combine(runDirectory, "failure-package.json") },
                    warnings = Array.Empty<string>(),
                    errors = Array.Empty<string>()
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static string CreateRunDirectory(string testRoot, string runId)
    {
        var workspacePath = Path.Combine(testRoot, "workspace");
        Directory.CreateDirectory(Path.Combine(workspacePath, ".irondev", "runs", runId));
        return workspacePath;
    }

    private static string CreateTemporaryDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.Infrastructure")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }

    private static string? GetStringPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        Assert.IsTrue(TryGetPropertyIgnoreCase(element, propertyName, out var value), $"Expected property '{propertyName}'.");
        return value.GetString();
    }

    private static int GetIntPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        Assert.IsTrue(TryGetPropertyIgnoreCase(element, propertyName, out var value), $"Expected property '{propertyName}'.");
        return value.GetInt32();
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
    private static AgentWorkspaceApplyContextService? BuildWorkspaceApplyContextService(IWorkspaceApplyReportReader? workspaceApplyReportReader) =>
        workspaceApplyReportReader is null
            ? null
            : new AgentWorkspaceApplyContextService(
                workspaceApplyReportReader,
                new WorkspaceApplyRecommendationService(),
                new WorkspaceApplyActionRequestService(),
                new WorkspaceApplyActionReviewService(),
                new WorkspaceApplyPolicyContextService(new ProjectApprovalPolicyEvaluator()));
    private sealed class FakeWorkspaceApplyReportReader : IWorkspaceApplyReportReader
    {
        private readonly WorkspaceApplyReportSummary _summary;

        public FakeWorkspaceApplyReportReader(WorkspaceApplyReportSummary summary)
        {
            _summary = summary;
        }

        public Task<WorkspaceApplyReportSummary> ReadAsync(
            WorkspaceApplyReportRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_summary);
        }
    }

    private sealed class FakeRunReportContractReader : IRunReportContractReader
    {
        private readonly string _expectedRunId;

        public FakeRunReportContractReader(string expectedRunId)
        {
            _expectedRunId = expectedRunId;
        }

        public Task<RunReportContractReadResult> ReadAsync(string runId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(runId, _expectedRunId, StringComparison.Ordinal))
                return Task.FromResult(RunReportContractMapper.MapFromApiFailure(runId, HttpStatusCode.NotFound, "{}"));

            return Task.FromResult(BuildRunReportContractReadResult(runId));
        }
    }

    private sealed class FakeAgentProcessRunner : IAgentProcessRunner
    {
        private readonly Queue<AgentProcessRunResult> _scriptedResults;

        public FakeAgentProcessRunner(IEnumerable<AgentProcessRunResult> scriptedResults)
        {
            _scriptedResults = new Queue<AgentProcessRunResult>(scriptedResults);
        }

        public Task<AgentProcessRunResult> RunAsync(
            string fileName,
            string[] arguments,
            string workingDirectory,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var command = string.Join(' ', arguments.Prepend(fileName));
            return Task.FromResult(_scriptedResults.Count == 0
                ? new AgentProcessRunResult(-1, string.Empty, "No scripted subprocess result was configured.", false, command)
                : _scriptedResults.Dequeue());
        }
    }

    private static RunReportContractReadResult BuildRunReportContractReadResult(string runId)
    {
        var envelope = new RunReportContractEnvelope
        {
            Status = "succeeded",
            Command = "runs report",
            TraceId = "trace-run",
            Summary = "Tester run completed.",
            Data = new RunReportContractData
            {
                RunId = runId,
                RunStatus = "Completed",
                AgentName = "TesterAgent",
                TraceId = "trace-run",
                Governance = new RunReportGovernanceContractData
                {
                    Decision = "derived",
                    ApprovalDecision = "not_required"
                },
                Evidence =
                [
                    new RunReportEvidenceContractData
                    {
                        Kind = "tool-call",
                        Path = "test-results/tester-evidence.json"
                    }
                ]
            },
            Errors = [],
            Warnings = []
        };

        return RunReportContractMapper.MapToReadResult(envelope);
    }
}
