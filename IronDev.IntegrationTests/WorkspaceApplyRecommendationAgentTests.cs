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
public sealed class WorkspaceApplyRecommendationAgentTests
{
    [TestMethod]
    public void WorkspaceApplyRecommendation_Success_RecommendsHumanReviewOrCommit()
    {
        var service = new WorkspaceApplyRecommendationService();

        var recommendation = service.Recommend(new WorkspaceApplyRecommendationRequest
        {
            Report = BuildSuccessSummary(deleteCount: 0)
        });

        Assert.AreEqual(WorkspaceApplyRecommendedActions.HumanReviewOrCommit, recommendation.RecommendedAction);
        Assert.IsTrue(recommendation.HumanReviewRequired);
        Assert.IsTrue(recommendation.SafeToCommitAfterReview);
        Assert.IsFalse(recommendation.SafeToRetry);
        Assert.IsTrue(recommendation.BlocksAutomaticExecution);
    }

    [TestMethod]
    public void WorkspaceApplyRecommendation_CriticalFailure_RecommendsSourceReviewBeforeRetry()
    {
        var service = new WorkspaceApplyRecommendationService();

        var recommendation = service.Recommend(new WorkspaceApplyRecommendationRequest
        {
            Report = BuildFailureSummary(sourceRepoMutated: true, applyVerified: false)
        });

        Assert.AreEqual(WorkspaceApplyRecommendedActions.DoNotRetryUntilSourceReviewed, recommendation.RecommendedAction);
        Assert.IsTrue(recommendation.SourceReviewRequiredBeforeRetry);
        Assert.IsFalse(recommendation.SafeToRetry);
        Assert.IsFalse(recommendation.SafeToCommitAfterReview);
    }

    [TestMethod]
    public void WorkspaceApplyRecommendation_PostApplyValidationFailure_RecommendsFixValidationFailure()
    {
        var service = new WorkspaceApplyRecommendationService();

        var recommendation = service.Recommend(new WorkspaceApplyRecommendationRequest
        {
            Report = BuildFailureSummary(sourceRepoMutated: true, applyVerified: true, postApplyValidationSucceeded: false)
        });

        Assert.AreEqual(WorkspaceApplyRecommendedActions.FixValidationFailure, recommendation.RecommendedAction);
        Assert.IsTrue(recommendation.HumanReviewRequired);
        Assert.IsFalse(recommendation.SafeToRetry);
    }

    [TestMethod]
    public void WorkspaceApplyRecommendation_PreMutationFailure_RecommendsRetryAfterBlockers()
    {
        var service = new WorkspaceApplyRecommendationService();

        var recommendation = service.Recommend(new WorkspaceApplyRecommendationRequest
        {
            Report = BuildFailureSummary(sourceRepoMutated: false, applyVerified: false)
        });

        Assert.AreEqual(WorkspaceApplyRecommendedActions.RetryAfterFixingBlockers, recommendation.RecommendedAction);
        Assert.IsTrue(recommendation.SafeToRetry);
        Assert.IsFalse(recommendation.SafeToCommitAfterReview);
        Assert.IsTrue(recommendation.BlocksAutomaticExecution);
    }

    [TestMethod]
    public void WorkspaceApplyRecommendation_UnavailableReport_RecommendsNoWorkspaceApplyReport()
    {
        var service = new WorkspaceApplyRecommendationService();

        var recommendation = service.Recommend(new WorkspaceApplyRecommendationRequest
        {
            Report = new WorkspaceApplyReportSummary
            {
                RunId = "run-1",
                WorkspacePath = "C:\\workspaces\\run-1",
                Outcome = "unavailable"
            }
        });

        Assert.AreEqual(WorkspaceApplyRecommendedActions.NoWorkspaceApplyReport, recommendation.RecommendedAction);
        Assert.IsTrue(recommendation.HumanReviewRequired);
        Assert.IsFalse(recommendation.SafeToRetry);
    }

    [TestMethod]
    public void WorkspaceApplyRecommendation_SuccessWithDeleteCount_BlocksCommitRecommendation()
    {
        var service = new WorkspaceApplyRecommendationService();

        var recommendation = service.Recommend(new WorkspaceApplyRecommendationRequest
        {
            Report = BuildSuccessSummary(deleteCount: 1)
        });

        Assert.AreEqual(WorkspaceApplyRecommendedActions.InspectFailurePackage, recommendation.RecommendedAction);
        Assert.IsFalse(recommendation.SafeToCommitAfterReview);
        Assert.IsTrue(recommendation.SourceReviewRequiredBeforeRetry);
        Assert.IsTrue(recommendation.Warnings.Any(warning => warning.Contains("delete operations", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task SupervisorAgent_IncludesWorkspaceApplySuccessRecommendation()
    {
        var result = await RunSupervisorWithWorkspaceApplySummaryAsync(BuildSuccessSummary(deleteCount: 0));

        using var document = JsonDocument.Parse(result.OutputJson);
        var root = document.RootElement;
        var workspaceApply = root.GetProperty("workspaceApply");
        var recommendation = root.GetProperty("workspaceApplyRecommendation");

        Assert.AreEqual("success", GetStringPropertyIgnoreCase(workspaceApply, "outcome"));
        Assert.AreEqual(WorkspaceApplyRecommendedActions.HumanReviewOrCommit, GetStringPropertyIgnoreCase(recommendation, "recommendedAction"));
        Assert.IsTrue(GetBoolPropertyIgnoreCase(recommendation, "safeToCommitAfterReview"));
        Assert.IsTrue(GetBoolPropertyIgnoreCase(recommendation, "blocksAutomaticExecution"));
    }

    [TestMethod]
    public async Task SupervisorAgent_IncludesWorkspaceApplyFailureRecommendation()
    {
        var result = await RunSupervisorWithWorkspaceApplySummaryAsync(
            BuildFailureSummary(sourceRepoMutated: true, applyVerified: false));

        using var document = JsonDocument.Parse(result.OutputJson);
        var root = document.RootElement;
        var workspaceApply = root.GetProperty("workspaceApply");
        var recommendation = root.GetProperty("workspaceApplyRecommendation");

        Assert.AreEqual("failure", GetStringPropertyIgnoreCase(workspaceApply, "outcome"));
        Assert.AreEqual(WorkspaceApplyRecommendedActions.DoNotRetryUntilSourceReviewed, GetStringPropertyIgnoreCase(recommendation, "recommendedAction"));
        Assert.IsTrue(GetBoolPropertyIgnoreCase(recommendation, "sourceReviewRequiredBeforeRetry"));
    }

    [TestMethod]
    public async Task SupervisorAgent_NoWorkspaceReportInput_LeavesRecommendationNull()
    {
        var fakeRunReportReader = new FakeRunReportContractReader("agent-run-tester");
        var fakeRunner = BuildSuccessfulProcessRunner();
        var agent = BuildSupervisorAgent(fakeRunReportReader, fakeRunner, workspaceApplyReportReader: null);

        var result = await agent.RunAsync(new AgentRequest
        {
            AgentName = "SupervisorAgent",
            GoalId = "test-goal",
            DogfoodRunId = "agent-run",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "IronDev",
                ["query"] = "Summarize governed workspace apply.",
                ["plan_path"] = "TestPlan.md",
                ["live_llm"] = "false"
            }
        });

        using var document = JsonDocument.Parse(result.OutputJson);
        Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("workspaceApply").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("workspaceApplyRecommendation").ValueKind);
    }

    [TestMethod]
    public void WorkspaceApplyRecommendationAgentBoundary_DoesNotAddMutationOrWorkspaceCommandCapabilities()
    {
        var repoRoot = FindRepositoryRoot();
        var serviceSource = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "WorkspaceApplyRecommendationService.cs"));
        var supervisorSource = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "SupervisorAgent.cs"));

        Assert.IsFalse(serviceSource.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));

        Assert.IsFalse(supervisorSource.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspacePrepareService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspaceValidationService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspacePromotionApprovalService", StringComparison.Ordinal));
    }

    private static async Task<AgentResult> RunSupervisorWithWorkspaceApplySummaryAsync(WorkspaceApplyReportSummary summary)
    {
        var fakeRunReportReader = new FakeRunReportContractReader("agent-run-tester");
        var fakeWorkspaceReader = new FakeWorkspaceApplyReportReader(summary);
        var fakeRunner = BuildSuccessfulProcessRunner();
        var agent = BuildSupervisorAgent(fakeRunReportReader, fakeRunner, fakeWorkspaceReader);

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

    private static SupervisorAgent BuildSupervisorAgent(
        IRunReportContractReader runReportReader,
        IAgentProcessRunner processRunner,
        IWorkspaceApplyReportReader? workspaceApplyReportReader)
    {
        return new SupervisorAgent(
            new AgentDefinition
            {
                Name = "SupervisorAgent",
                Purpose = "Test workspace apply recommendation consumption.",
                DefaultModelProfile = "cheap-runner"
            },
            new AgentModelResolver(),
            FindRepositoryRoot(),
            runReportReader,
            processRunner: processRunner,
            workspaceApplyContextService: BuildWorkspaceApplyContextService(workspaceApplyReportReader));
    }

    private static FakeAgentProcessRunner BuildSuccessfulProcessRunner() =>
        new(
            [
                new AgentProcessRunResult(0, """{"status":"Succeeded","contextPackage":{"Matches":[{"DocumentTitle":"Memory title"}],"SemanticTraceId":"trace-mem","WeightedContextBundle":{"summaryForAgent":"Memory context."}}}""", string.Empty, false, "retriever"),
                new AgentProcessRunResult(0, """{"review":{"decision":"Allow"}}""", string.Empty, false, "conscience"),
                new AgentProcessRunResult(0, """{"thoughtLedger":{}}""", string.Empty, false, "thoughtLedger"),
                new AgentProcessRunResult(0, """{"report":{"status":"Passed"}}""", string.Empty, false, "tester-run-plan")
            ]);

    private static WorkspaceApplyReportSummary BuildSuccessSummary(int deleteCount) =>
        new()
        {
            RunId = "apply-run",
            WorkspacePath = "C:\\workspaces\\apply-run",
            Outcome = "success",
            Recommendation = "ready_for_human_review_or_commit",
            SourceRepoMutated = true,
            ApplyVerified = true,
            SourceMatchesWorkspace = true,
            PostApplyValidationSucceeded = true,
            AddCount = 1,
            ModifyCount = 1,
            DeleteCount = deleteCount,
            Files =
            [
                new WorkspaceApplyChangedFileSummary { Operation = "add", RelativePath = "Feature.cs", Applied = true, Verified = true },
                new WorkspaceApplyChangedFileSummary { Operation = "modify", RelativePath = "Program.cs", Applied = true, Verified = true }
            ],
            SourceReportPath = "C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\source-report.json",
            EvidencePaths = ["C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\source-report.json"],
            RiskNotes = ["Human should review changed files before commit/PR."]
        };

    private static WorkspaceApplyReportSummary BuildFailureSummary(
        bool sourceRepoMutated,
        bool applyVerified,
        bool postApplyValidationSucceeded = false) =>
        new()
        {
            RunId = "apply-run",
            WorkspacePath = "C:\\workspaces\\apply-run",
            Outcome = "failure",
            FailedStage = sourceRepoMutated ? "apply-copy" : "apply-preflight",
            FailureSeverity = sourceRepoMutated && !applyVerified ? "critical" : "warning",
            RecommendedNextAction = sourceRepoMutated ? "do_not_retry_until_source_reviewed" : "retry_after_fixing_blockers",
            SourceRepoMutated = sourceRepoMutated,
            ApplyVerified = applyVerified,
            PostApplyValidationSucceeded = postApplyValidationSucceeded,
            FailurePackagePath = "C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\failure-package.json",
            EvidencePaths = ["C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\failure-package.json"],
            RiskNotes = ["Failure package is advisory and does not repair or roll back changes."]
        };

    private static string? GetStringPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        Assert.IsTrue(TryGetPropertyIgnoreCase(element, propertyName, out var value), $"Expected property '{propertyName}'.");
        return value.GetString();
    }

    private static bool GetBoolPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        Assert.IsTrue(TryGetPropertyIgnoreCase(element, propertyName, out var value), $"Expected property '{propertyName}'.");
        return value.GetBoolean();
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
