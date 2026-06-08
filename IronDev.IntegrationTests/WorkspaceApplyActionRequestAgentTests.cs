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
public sealed class WorkspaceApplyActionRequestAgentTests
{
    [TestMethod]
    public void WorkspaceApplyActionRequest_HumanReviewRecommendation_CreatesReviewActionRequest()
    {
        var service = new WorkspaceApplyActionRequestService();

        var request = service.Create(BuildInput(WorkspaceApplyRecommendedActions.HumanReviewOrCommit));

        Assert.AreEqual(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, request.RequestedAction);
        Assert.IsTrue(request.HumanApprovalRequired);
        Assert.IsFalse(request.AutomaticExecutionAllowed);
        Assert.IsFalse(request.MutatesSourceRepo);
        Assert.IsTrue(request.RequiresFreshHumanDecision);
        Assert.IsNull(request.SuggestedCommand);
        Assert.IsTrue(request.Warnings.Any(warning => warning.Contains("Commit/PR creation is outside", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void WorkspaceApplyActionRequest_CriticalFailureRecommendation_CreatesSourceReviewRequest()
    {
        var service = new WorkspaceApplyActionRequestService();

        var request = service.Create(BuildInput(WorkspaceApplyRecommendedActions.DoNotRetryUntilSourceReviewed));

        Assert.AreEqual(WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry, request.RequestedAction);
        Assert.IsFalse(request.AutomaticExecutionAllowed);
        Assert.IsFalse(request.MutatesSourceRepo);
        Assert.IsTrue(request.Preconditions.Any(item => item.Contains("source repo", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void WorkspaceApplyActionRequest_ValidationFailureRecommendation_CreatesFixValidationRequest()
    {
        var service = new WorkspaceApplyActionRequestService();

        var request = service.Create(BuildInput(WorkspaceApplyRecommendedActions.FixValidationFailure));

        Assert.AreEqual(WorkspaceApplyRequestedActions.FixValidationFailure, request.RequestedAction);
        Assert.IsFalse(request.AutomaticExecutionAllowed);
        Assert.IsTrue(request.Preconditions.Any(item => item.Contains("post-apply-validation evidence", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void WorkspaceApplyActionRequest_RetryRecommendation_CreatesRetryRequestButDoesNotExecute()
    {
        var service = new WorkspaceApplyActionRequestService();

        var request = service.Create(BuildInput(WorkspaceApplyRecommendedActions.RetryAfterFixingBlockers));

        Assert.AreEqual(WorkspaceApplyRequestedActions.RetryGovernedSpineAfterFixingBlockers, request.RequestedAction);
        Assert.IsFalse(request.AutomaticExecutionAllowed);
        Assert.IsTrue(request.Preconditions.Any(item => item.Contains("human explicitly chooses to retry", StringComparison.OrdinalIgnoreCase)));
        Assert.IsNull(request.SuggestedCommand);
    }

    [TestMethod]
    public void WorkspaceApplyActionRequest_MissingEvidenceRecommendation_CreatesCollectEvidenceRequest()
    {
        var service = new WorkspaceApplyActionRequestService();

        var request = service.Create(BuildInput(WorkspaceApplyRecommendedActions.CollectMissingEvidence));

        Assert.AreEqual(WorkspaceApplyRequestedActions.CollectMissingEvidence, request.RequestedAction);
        Assert.IsFalse(request.AutomaticExecutionAllowed);
    }

    [TestMethod]
    public void WorkspaceApplyActionRequest_NoReportRecommendation_CreatesNoActionRequest()
    {
        var service = new WorkspaceApplyActionRequestService();

        var request = service.Create(BuildInput(WorkspaceApplyRecommendedActions.NoWorkspaceApplyReport));

        Assert.AreEqual(WorkspaceApplyRequestedActions.NoActionAvailable, request.RequestedAction);
        Assert.IsFalse(request.AutomaticExecutionAllowed);
    }

    [TestMethod]
    public void WorkspaceApplyActionRequest_UnknownRecommendation_FailsClosed()
    {
        var service = new WorkspaceApplyActionRequestService();

        var request = service.Create(BuildInput("bad_action"));

        Assert.AreEqual(WorkspaceApplyRequestedActions.CollectMissingEvidence, request.RequestedAction);
        Assert.IsFalse(request.AutomaticExecutionAllowed);
        Assert.IsTrue(request.Warnings.Any(warning => warning.Contains("Unknown workspace apply recommendation", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task SupervisorAgent_IncludesWorkspaceApplyActionRequestOnSuccess()
    {
        var result = await RunSupervisorWithWorkspaceApplySummaryAsync(BuildSuccessSummary());

        using var document = JsonDocument.Parse(result.OutputJson);
        var root = document.RootElement;
        var workspaceApply = root.GetProperty("workspaceApply");
        var recommendation = root.GetProperty("workspaceApplyRecommendation");
        var actionRequest = root.GetProperty("workspaceApplyActionRequest");

        Assert.AreEqual("success", GetStringPropertyIgnoreCase(workspaceApply, "outcome"));
        Assert.AreEqual(WorkspaceApplyRecommendedActions.HumanReviewOrCommit, GetStringPropertyIgnoreCase(recommendation, "recommendedAction"));
        Assert.AreEqual(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, GetStringPropertyIgnoreCase(actionRequest, "requestedAction"));
        Assert.IsFalse(GetBoolPropertyIgnoreCase(actionRequest, "automaticExecutionAllowed"));
        Assert.IsFalse(GetBoolPropertyIgnoreCase(actionRequest, "mutatesSourceRepo"));
    }

    [TestMethod]
    public async Task SupervisorAgent_IncludesWorkspaceApplyActionRequestOnFailure()
    {
        var result = await RunSupervisorWithWorkspaceApplySummaryAsync(BuildCriticalFailureSummary());

        using var document = JsonDocument.Parse(result.OutputJson);
        var root = document.RootElement;
        var workspaceApply = root.GetProperty("workspaceApply");
        var recommendation = root.GetProperty("workspaceApplyRecommendation");
        var actionRequest = root.GetProperty("workspaceApplyActionRequest");

        Assert.AreEqual("failure", GetStringPropertyIgnoreCase(workspaceApply, "outcome"));
        Assert.AreEqual(WorkspaceApplyRecommendedActions.DoNotRetryUntilSourceReviewed, GetStringPropertyIgnoreCase(recommendation, "recommendedAction"));
        Assert.AreEqual(WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry, GetStringPropertyIgnoreCase(actionRequest, "requestedAction"));
        Assert.IsFalse(GetBoolPropertyIgnoreCase(actionRequest, "automaticExecutionAllowed"));
    }

    [TestMethod]
    public async Task SupervisorAgent_NoWorkspaceReportInput_LeavesActionRequestNull()
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
        Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("workspaceApplyActionRequest").ValueKind);
    }

    [TestMethod]
    public void WorkspaceApplyActionRequestAgentBoundary_DoesNotAddMutationOrWorkspaceCommandCapabilities()
    {
        var repoRoot = FindRepositoryRoot();
        var serviceSource = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "WorkspaceApplyActionRequestService.cs"));
        var supervisorSource = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "SupervisorAgent.cs"));

        Assert.IsFalse(serviceSource.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IDisposableWorkspacePrepareService", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IDisposableWorkspaceValidationService", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IDisposableWorkspacePromotionApprovalService", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IDisposableWorkspaceApplyPreflightService", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IDisposableWorkspaceApplyDryRunService", StringComparison.Ordinal));
        Assert.IsFalse(serviceSource.Contains("IDisposableWorkspaceApplyVerifyService", StringComparison.Ordinal));

        Assert.IsFalse(supervisorSource.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspacePrepareService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspaceValidationService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspacePromotionApprovalService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspaceApplyPreflightService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspaceApplyDryRunService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IDisposableWorkspaceApplyVerifyService", StringComparison.Ordinal));
    }

    private static WorkspaceApplyActionRequestInput BuildInput(string recommendedAction) =>
        new()
        {
            Report = BuildSuccessSummary(),
            Recommendation = new WorkspaceApplyRecommendation
            {
                RecommendedAction = recommendedAction,
                Reason = "Recommendation reason.",
                HumanReviewRequired = true,
                SafeToRetry = false,
                SafeToCommitAfterReview = string.Equals(recommendedAction, WorkspaceApplyRecommendedActions.HumanReviewOrCommit, StringComparison.Ordinal),
                SourceReviewRequiredBeforeRetry = string.Equals(recommendedAction, WorkspaceApplyRecommendedActions.DoNotRetryUntilSourceReviewed, StringComparison.Ordinal),
                BlocksAutomaticExecution = true,
                EvidencePaths = ["C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\source-report.json"],
                RiskNotes = ["Human should review changed files before commit/PR."],
                Warnings = []
            }
        };

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
                Purpose = "Test workspace apply action request consumption.",
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

    private static WorkspaceApplyReportSummary BuildSuccessSummary() =>
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
            DeleteCount = 0,
            Files =
            [
                new WorkspaceApplyChangedFileSummary { Operation = "add", RelativePath = "Feature.cs", Applied = true, Verified = true },
                new WorkspaceApplyChangedFileSummary { Operation = "modify", RelativePath = "Program.cs", Applied = true, Verified = true }
            ],
            SourceReportPath = "C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\source-report.json",
            EvidencePaths = ["C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\source-report.json"],
            RiskNotes = ["Human should review changed files before commit/PR."]
        };

    private static WorkspaceApplyReportSummary BuildCriticalFailureSummary() =>
        new()
        {
            RunId = "apply-run",
            WorkspacePath = "C:\\workspaces\\apply-run",
            Outcome = "failure",
            FailedStage = "apply-copy",
            FailureSeverity = "critical",
            RecommendedNextAction = "do_not_retry_until_source_reviewed",
            SourceRepoMutated = true,
            ApplyVerified = false,
            PostApplyValidationSucceeded = false,
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
