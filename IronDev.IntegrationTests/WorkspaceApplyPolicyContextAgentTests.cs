using System.Net;
using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Interfaces;
using IronDev.Core.RunReports;
using IronDev.Infrastructure.Services.Agents;
using IronDev.Infrastructure.Services.Agents.ApprovalPolicy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class WorkspaceApplyPolicyContextAgentTests
{
    [TestMethod]
    public void WorkspaceApplyPolicyContext_SuccessReview_MapsToWorkspaceIntentAllowedByDefault()
    {
        var context = BuildContext(WorkspaceApplyRequestedActions.HumanReviewSourceChanges);

        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, context.Decision);
        Assert.AreEqual(ProjectApprovalRiskTiers.WorkspaceIntent, context.RiskTier);
        Assert.AreEqual(WorkspaceApplyPolicyActionTypes.WorkspaceApplyActionReview, context.ActionType);
        Assert.AreEqual(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, context.RequestedAction);
        Assert.IsFalse(context.HumanApprovalRequired);
        Assert.IsTrue(context.AutomaticExecutionAllowed);
        Assert.IsFalse(context.SourceMutationAllowed);
        Assert.IsFalse(context.ExecutionCanStartFromPolicyContext);
        Assert.IsFalse(context.ApprovalCanBeGrantedByPolicyContext);
    }

    [TestMethod]
    public void WorkspaceApplyPolicyContext_FailureReview_MapsToWorkspaceIntentAllowedByDefault()
    {
        var context = BuildContext(WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry, sourceRepoMutated: true);

        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, context.Decision);
        Assert.AreEqual(ProjectApprovalRiskTiers.WorkspaceIntent, context.RiskTier);
        Assert.IsTrue(context.AutomaticExecutionAllowed);
        Assert.IsFalse(context.ExecutionCanStartFromPolicyContext);
        Assert.IsFalse(context.SourceMutationAllowed);
        Assert.IsTrue(context.RiskNotes.Any(item => item.Contains("may already have been mutated", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void WorkspaceApplyPolicyContext_CollectMissingEvidence_MapsToWorkspaceReporting()
    {
        var context = BuildContext(WorkspaceApplyRequestedActions.CollectMissingEvidence, sourceRepoMutated: false);

        Assert.AreEqual(ProjectApprovalRiskTiers.WorkspaceReporting, context.RiskTier);
        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, context.Decision);
        Assert.IsTrue(context.AutomaticExecutionAllowed);
        Assert.IsFalse(context.ExecutionCanStartFromPolicyContext);
    }

    [TestMethod]
    public void WorkspaceApplyPolicyContext_CustomPolicyBlocksRequestedAction()
    {
        var policy = BuildPolicy(ProjectApprovalModes.AlwaysBlock);
        var context = BuildContext(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, policy: policy);

        Assert.AreEqual(ProjectApprovalDecisions.BlockedByPolicy, context.Decision);
        Assert.IsFalse(context.AutomaticExecutionAllowed);
        Assert.IsFalse(context.ExecutionCanStartFromPolicyContext);
        Assert.IsFalse(context.ApprovalCanBeGrantedByPolicyContext);
    }

    [TestMethod]
    public void WorkspaceApplyPolicyContext_CustomPolicyAsksEveryTime()
    {
        var policy = BuildPolicy(ProjectApprovalModes.AskEveryTime);
        var context = BuildContext(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, policy: policy);

        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, context.Decision);
        Assert.IsTrue(context.HumanApprovalRequired);
        Assert.IsFalse(context.ExecutionCanStartFromPolicyContext);
    }

    [TestMethod]
    public void WorkspaceApplyPolicyContext_UnknownRequestedAction_MapsConservativelyToReporting()
    {
        var context = BuildContext("surprising_action", sourceRepoMutated: false);

        Assert.AreEqual(ProjectApprovalRiskTiers.WorkspaceReporting, context.RiskTier);
        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, context.Decision);
        Assert.IsTrue(context.Warnings.Any(item => item.Contains("Unknown workspace apply requested action", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(context.ExecutionCanStartFromPolicyContext);
    }

    [TestMethod]
    public async Task SupervisorAgent_EmitsPolicyContextOnSuccess()
    {
        var result = await RunSupervisorWithWorkspaceApplySummaryAsync(BuildSuccessSummary());

        using var document = JsonDocument.Parse(result.OutputJson);
        var policyContext = document.RootElement.GetProperty("workspaceApplyPolicyContext");

        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, GetStringPropertyIgnoreCase(policyContext, "decision"));
        Assert.AreEqual(ProjectApprovalRiskTiers.WorkspaceIntent, GetStringPropertyIgnoreCase(policyContext, "riskTier"));
        Assert.AreEqual(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, GetStringPropertyIgnoreCase(policyContext, "requestedAction"));
        Assert.IsFalse(GetBoolPropertyIgnoreCase(policyContext, "executionCanStartFromPolicyContext"));
    }

    [TestMethod]
    public async Task SupervisorAgent_EmitsPolicyContextOnFailureReview()
    {
        var result = await RunSupervisorWithWorkspaceApplySummaryAsync(BuildCriticalFailureSummary());

        using var document = JsonDocument.Parse(result.OutputJson);
        var policyContext = document.RootElement.GetProperty("workspaceApplyPolicyContext");

        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, GetStringPropertyIgnoreCase(policyContext, "decision"));
        Assert.AreEqual(WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry, GetStringPropertyIgnoreCase(policyContext, "requestedAction"));
        Assert.IsFalse(GetBoolPropertyIgnoreCase(policyContext, "sourceMutationAllowed"));
    }

    [TestMethod]
    public async Task SupervisorAgent_NoWorkspaceReportInput_LeavesPolicyContextNull()
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
        Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("workspaceApplyPolicyContext").ValueKind);
    }

    [TestMethod]
    public void WorkspaceApplyPolicyContextBoundary_DoesNotAddMutationOrWorkspaceCommandCapabilities()
    {
        var repoRoot = FindRepositoryRoot();
        var serviceSource = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "ApprovalPolicy", "WorkspaceApplyPolicyContextService.cs"));
        var supervisorSource = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "SupervisorAgent.cs"));

        AssertNoMutationOrWorkspaceCommandCapabilities(serviceSource);
        AssertNoMutationOrWorkspaceCommandCapabilities(supervisorSource);
    }

    private static void AssertNoMutationOrWorkspaceCommandCapabilities(string source)
    {
        Assert.IsFalse(source.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspacePrepareService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceValidationService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspacePromotionApprovalService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceApplyPreflightService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceApplyDryRunService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceApplyVerifyService", StringComparison.Ordinal));
    }

    private static WorkspaceApplyPolicyContext BuildContext(
        string requestedAction,
        bool sourceRepoMutated = true,
        ProjectApprovalPolicy? policy = null)
    {
        var service = new WorkspaceApplyPolicyContextService(new ProjectApprovalPolicyEvaluator());
        var input = BuildInput(requestedAction, sourceRepoMutated, policy);
        return service.Create(input);
    }

    private static WorkspaceApplyPolicyContextInput BuildInput(
        string requestedAction,
        bool sourceRepoMutated = true,
        ProjectApprovalPolicy? policy = null)
    {
        var report = sourceRepoMutated ? BuildCriticalFailureSummary() : BuildPreMutationFailureSummary();
        var recommendation = new WorkspaceApplyRecommendation
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
                _ => "unknown_recommendation"
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
        };
        var actionRequest = new WorkspaceApplyActionRequest
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
        };
        var actionReview = new WorkspaceApplyActionReview
        {
            ReviewStatus = WorkspaceApplyActionReviewStatuses.ReadyForHumanReview,
            Summary = "Review summary.",
            HumanReviewRequired = true,
            ApprovalCanBeGrantedByThisPackage = false,
            ExecutionCanStartFromThisPackage = false,
            SourceRepoMayBeMutated = sourceRepoMutated,
            RequestedAction = requestedAction,
            RecommendedAction = recommendation.RecommendedAction,
            ReviewChecklist = ["human reviews evidence"],
            EvidencePaths = ["C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\failure-package.json"],
            RiskNotes = ["Review package does not approve future execution."],
            Blockers = [],
            Warnings = []
        };

        return new WorkspaceApplyPolicyContextInput
        {
            ProjectId = "IronDev",
            Report = report,
            Recommendation = recommendation,
            ActionRequest = actionRequest,
            ActionReview = actionReview,
            Policy = policy ?? ProjectApprovalPolicy.CreateDefault("IronDev")
        };
    }

    private static ProjectApprovalPolicy BuildPolicy(string mode) =>
        ProjectApprovalPolicy.CreateDefault("IronDev") with
        {
            Rules =
            [
                new ProjectApprovalPolicyRule
                {
                    RiskTier = ProjectApprovalRiskTiers.WorkspaceIntent,
                    RequestedAction = WorkspaceApplyRequestedActions.HumanReviewSourceChanges,
                    Mode = mode
                }
            ]
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
                Purpose = "Test workspace apply policy context consumption.",
                DefaultModelProfile = "cheap-runner"
            },
            new AgentModelResolver(),
            FindRepositoryRoot(),
            runReportReader,
            processRunner: processRunner,
            workspaceApplyReportReader: workspaceApplyReportReader,
            workspaceApplyRecommendationService: new WorkspaceApplyRecommendationService(),
            workspaceApplyActionRequestService: new WorkspaceApplyActionRequestService(),
            workspaceApplyActionReviewService: new WorkspaceApplyActionReviewService(),
            workspaceApplyPolicyContextService: new WorkspaceApplyPolicyContextService(new ProjectApprovalPolicyEvaluator()));
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
            SourceRepo = "C:\\repo\\IronDeveloper",
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
            SourceRepo = "C:\\repo\\IronDeveloper",
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
            SourceRepo = "C:\\repo\\IronDeveloper",
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
