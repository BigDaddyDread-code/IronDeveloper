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
public sealed class WorkspaceApplyActionReviewAgentTests
{
    [TestMethod]
    public void WorkspaceApplyActionReview_HumanReviewSourceChanges_CreatesReadyReview()
    {
        var service = new WorkspaceApplyActionReviewService();

        var review = service.Create(BuildInput(WorkspaceApplyRequestedActions.HumanReviewSourceChanges));

        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.ReadyForHumanReview, review.ReviewStatus);
        Assert.IsTrue(review.HumanReviewRequired);
        Assert.IsFalse(review.ApprovalCanBeGrantedByThisPackage);
        Assert.IsFalse(review.ExecutionCanStartFromThisPackage);
        Assert.IsTrue(review.SourceRepoMayBeMutated);
        Assert.IsTrue(review.ReviewChecklist.Any(item => item.Contains("source-report", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(review.Warnings.Any(item => item.Contains("commit or PR", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void WorkspaceApplyActionReview_SourceReviewBeforeRetry_BlocksForSourceReview()
    {
        var service = new WorkspaceApplyActionReviewService();

        var review = service.Create(BuildInput(WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry, sourceRepoMutated: true));

        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.BlockedForSourceReview, review.ReviewStatus);
        Assert.IsTrue(review.SourceRepoMayBeMutated);
        Assert.IsTrue(review.ReviewChecklist.Any(item => item.Contains("source repository", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(review.ExecutionCanStartFromThisPackage);
    }

    [TestMethod]
    public void WorkspaceApplyActionReview_ValidationFailure_BlocksForValidationFailure()
    {
        var service = new WorkspaceApplyActionReviewService();

        var review = service.Create(BuildInput(WorkspaceApplyRequestedActions.FixValidationFailure, sourceRepoMutated: true));

        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.BlockedForValidationFailure, review.ReviewStatus);
        Assert.IsTrue(review.ReviewChecklist.Any(item => item.Contains("post-apply-validation", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void WorkspaceApplyActionReview_RetryGovernedSpine_RequiresHumanReview()
    {
        var service = new WorkspaceApplyActionReviewService();

        var review = service.Create(BuildInput(WorkspaceApplyRequestedActions.RetryGovernedSpineAfterFixingBlockers, sourceRepoMutated: false));

        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.ReadyForHumanReview, review.ReviewStatus);
        Assert.IsFalse(review.SourceRepoMayBeMutated);
        Assert.IsTrue(review.ReviewChecklist.Any(item => item.Contains("Fix listed blockers", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(review.ReviewChecklist.Any(item => item.Contains("retry the governed spine", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(review.ExecutionCanStartFromThisPackage);
    }

    [TestMethod]
    public void WorkspaceApplyActionReview_CollectMissingEvidence_BlocksForEvidence()
    {
        var service = new WorkspaceApplyActionReviewService();

        var review = service.Create(BuildInput(WorkspaceApplyRequestedActions.CollectMissingEvidence));

        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.BlockedForEvidence, review.ReviewStatus);
        Assert.IsTrue(review.ReviewChecklist.Any(item => item.Contains("Identify missing evidence", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void WorkspaceApplyActionReview_NoActionAvailable_BlocksForEvidence()
    {
        var service = new WorkspaceApplyActionReviewService();

        var review = service.Create(BuildInput(WorkspaceApplyRequestedActions.NoActionAvailable));

        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.BlockedForEvidence, review.ReviewStatus);
        Assert.IsTrue(review.Summary.Contains("No usable workspace apply report", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void WorkspaceApplyActionReview_UnknownRequestedAction_FailsClosed()
    {
        var service = new WorkspaceApplyActionReviewService();

        var review = service.Create(BuildInput("bad_action"));

        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.BlockedForEvidence, review.ReviewStatus);
        Assert.IsFalse(review.ApprovalCanBeGrantedByThisPackage);
        Assert.IsFalse(review.ExecutionCanStartFromThisPackage);
        Assert.IsTrue(review.Blockers.Any(item => item.Contains("Unknown workspace apply requested action", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task SupervisorAgent_EmitsReviewOnSuccessPath()
    {
        var result = await RunSupervisorWithWorkspaceApplySummaryAsync(BuildSuccessSummary());

        using var document = JsonDocument.Parse(result.OutputJson);
        var root = document.RootElement;
        var review = root.GetProperty("workspaceApplyActionReview");

        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.ReadyForHumanReview, GetStringPropertyIgnoreCase(review, "reviewStatus"));
        Assert.AreEqual(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, GetStringPropertyIgnoreCase(review, "requestedAction"));
        Assert.IsFalse(GetBoolPropertyIgnoreCase(review, "executionCanStartFromThisPackage"));
        Assert.IsFalse(GetBoolPropertyIgnoreCase(review, "approvalCanBeGrantedByThisPackage"));
    }

    [TestMethod]
    public async Task SupervisorAgent_EmitsReviewOnCriticalFailurePath()
    {
        var result = await RunSupervisorWithWorkspaceApplySummaryAsync(BuildCriticalFailureSummary());

        using var document = JsonDocument.Parse(result.OutputJson);
        var root = document.RootElement;
        var review = root.GetProperty("workspaceApplyActionReview");

        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.BlockedForSourceReview, GetStringPropertyIgnoreCase(review, "reviewStatus"));
        Assert.AreEqual(WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry, GetStringPropertyIgnoreCase(review, "requestedAction"));
        Assert.IsTrue(GetBoolPropertyIgnoreCase(review, "sourceRepoMayBeMutated"));
    }

    [TestMethod]
    public async Task SupervisorAgent_NoWorkspaceReportInput_LeavesReviewNull()
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
        Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("workspaceApplyActionReview").ValueKind);
    }

    [TestMethod]
    public void WorkspaceApplyActionReviewAgentBoundary_DoesNotAddMutationOrWorkspaceCommandCapabilities()
    {
        var repoRoot = FindRepositoryRoot();
        var serviceSource = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "WorkspaceApplyActionReviewService.cs"));
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

    private static WorkspaceApplyActionReviewInput BuildInput(string requestedAction, bool sourceRepoMutated = true) =>
        new()
        {
            Report = sourceRepoMutated ? BuildCriticalFailureSummary() : BuildPreMutationFailureSummary(),
            Recommendation = new WorkspaceApplyRecommendation
            {
                RecommendedAction = requestedAction switch
                {
                    WorkspaceApplyRequestedActions.HumanReviewSourceChanges => WorkspaceApplyRecommendedActions.HumanReviewOrCommit,
                    WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry => WorkspaceApplyRecommendedActions.DoNotRetryUntilSourceReviewed,
                    WorkspaceApplyRequestedActions.FixValidationFailure => WorkspaceApplyRecommendedActions.FixValidationFailure,
                    WorkspaceApplyRequestedActions.RetryGovernedSpineAfterFixingBlockers => WorkspaceApplyRecommendedActions.RetryAfterFixingBlockers,
                    WorkspaceApplyRequestedActions.InspectFailureEvidence => WorkspaceApplyRecommendedActions.InspectFailurePackage,
                    WorkspaceApplyRequestedActions.CollectMissingEvidence => WorkspaceApplyRecommendedActions.CollectMissingEvidence,
                    WorkspaceApplyRequestedActions.NoActionAvailable => WorkspaceApplyRecommendedActions.NoWorkspaceApplyReport,
                    _ => "bad_recommendation"
                },
                Reason = "Recommendation reason.",
                HumanReviewRequired = true,
                SafeToRetry = false,
                SafeToCommitAfterReview = false,
                SourceReviewRequiredBeforeRetry = sourceRepoMutated,
                BlocksAutomaticExecution = true,
                EvidencePaths = ["C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\failure-package.json"],
                RiskNotes = ["Human review is required before retry."],
                Warnings = []
            },
            ActionRequest = new WorkspaceApplyActionRequest
            {
                RequestedAction = requestedAction,
                Reason = "Action request reason.",
                HumanApprovalRequired = true,
                AutomaticExecutionAllowed = false,
                MutatesSourceRepo = false,
                RequiresFreshHumanDecision = true,
                SuggestedCommand = null,
                SuggestedCommandArguments = [],
                Preconditions = ["human reviews evidence"],
                EvidencePaths = ["C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\failure-package.json"],
                RiskNotes = ["Human review is required before retry."],
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
                Purpose = "Test workspace apply action review consumption.",
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

    private static WorkspaceApplyReportSummary BuildPreMutationFailureSummary() =>
        new()
        {
            RunId = "apply-run",
            WorkspacePath = "C:\\workspaces\\apply-run",
            Outcome = "failure",
            FailedStage = "apply-preflight",
            FailureSeverity = "warning",
            RecommendedNextAction = "retry_after_fixing_blockers",
            SourceRepoMutated = false,
            ApplyVerified = false,
            PostApplyValidationSucceeded = false,
            FailurePackagePath = "C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\failure-package.json",
            EvidencePaths = ["C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\failure-package.json"],
            RiskNotes = ["Failure happened before source mutation."]
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
