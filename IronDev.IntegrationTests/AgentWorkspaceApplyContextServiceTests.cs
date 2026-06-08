using System.Net;
using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Core.Interfaces;
using IronDev.Core.RunReports;
using IronDev.Infrastructure.Services.Agents;
using IronDev.Infrastructure.Services.Agents.ApprovalPolicy;
using IronDev.Infrastructure.Services.Agents.WorkspaceApply;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentWorkspaceApplyContextServiceTests
{
    [TestMethod]
    public async Task AgentWorkspaceApplyContext_SuccessReport_BuildsFullChain()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteSourceReport();
        var service = BuildService();

        var context = await service.CreateAsync(workspace.Request);

        Assert.IsTrue(context.ContextAvailable);
        Assert.AreEqual("success", context.WorkspaceApply?.Outcome);
        Assert.AreEqual(WorkspaceApplyRecommendedActions.HumanReviewOrCommit, context.WorkspaceApplyRecommendation?.RecommendedAction);
        Assert.AreEqual(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, context.WorkspaceApplyActionRequest?.RequestedAction);
        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.ReadyForHumanReview, context.WorkspaceApplyActionReview?.ReviewStatus);
        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, context.WorkspaceApplyPolicyContext?.Decision);
    }

    [TestMethod]
    public async Task AgentWorkspaceApplyContext_FailurePackage_BuildsFullFailureChain()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFailurePackage(sourceRepoMutated: true, applyVerified: false, postApplyValidationSucceeded: false);
        var service = BuildService();

        var context = await service.CreateAsync(workspace.Request);

        Assert.IsTrue(context.ContextAvailable);
        Assert.AreEqual("failure", context.WorkspaceApply?.Outcome);
        Assert.AreEqual(WorkspaceApplyRecommendedActions.DoNotRetryUntilSourceReviewed, context.WorkspaceApplyRecommendation?.RecommendedAction);
        Assert.AreEqual(WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry, context.WorkspaceApplyActionRequest?.RequestedAction);
        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.BlockedForSourceReview, context.WorkspaceApplyActionReview?.ReviewStatus);
        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, context.WorkspaceApplyPolicyContext?.Decision);
    }

    [TestMethod]
    public async Task AgentWorkspaceApplyContext_UnavailableReport_ReturnsUnavailableCompleteChain()
    {
        using var workspace = TestWorkspace.Create();
        var service = BuildService();

        var context = await service.CreateAsync(workspace.Request);

        Assert.IsFalse(context.ContextAvailable);
        Assert.AreEqual("unavailable", context.WorkspaceApply?.Outcome);
        Assert.AreEqual(WorkspaceApplyRecommendedActions.NoWorkspaceApplyReport, context.WorkspaceApplyRecommendation?.RecommendedAction);
        Assert.AreEqual(WorkspaceApplyRequestedActions.NoActionAvailable, context.WorkspaceApplyActionRequest?.RequestedAction);
        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.BlockedForEvidence, context.WorkspaceApplyActionReview?.ReviewStatus);
        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, context.WorkspaceApplyPolicyContext?.Decision);
        Assert.IsFalse(context.WorkspaceApplyPolicyContext?.ExecutionCanStartFromPolicyContext ?? true);
    }

    [TestMethod]
    public async Task AgentWorkspaceApplyContext_CustomPolicyBlocksContext()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteSourceReport();
        var service = BuildService();
        var policy = ProjectApprovalPolicy.CreateDefault(workspace.ProjectId) with
        {
            Rules =
            [
                new ProjectApprovalPolicyRule
                {
                    RiskTier = ProjectApprovalRiskTiers.WorkspaceIntent,
                    RequestedAction = WorkspaceApplyRequestedActions.HumanReviewSourceChanges,
                    Mode = ProjectApprovalModes.AlwaysBlock
                }
            ]
        };

        var context = await service.CreateAsync(workspace.Request with { Policy = policy });

        Assert.AreEqual(ProjectApprovalDecisions.BlockedByPolicy, context.WorkspaceApplyPolicyContext?.Decision);
        Assert.IsFalse(context.WorkspaceApplyPolicyContext?.ExecutionCanStartFromPolicyContext ?? true);
        Assert.IsFalse(context.WorkspaceApplyPolicyContext?.ApprovalCanBeGrantedByPolicyContext ?? true);
    }

    [TestMethod]
    public async Task AgentWorkspaceApplyContext_PolicyContextFailure_FailsClosed()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteSourceReport();
        var service = new AgentWorkspaceApplyContextService(
            new WorkspaceApplyReportReader(),
            new WorkspaceApplyRecommendationService(),
            new WorkspaceApplyActionRequestService(),
            new WorkspaceApplyActionReviewService(),
            new ThrowingWorkspaceApplyPolicyContextService());

        var context = await service.CreateAsync(workspace.Request);

        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, context.WorkspaceApplyPolicyContext?.Decision);
        Assert.IsFalse(context.WorkspaceApplyPolicyContext?.ExecutionCanStartFromPolicyContext ?? true);
        Assert.IsFalse(context.WorkspaceApplyPolicyContext?.ApprovalCanBeGrantedByPolicyContext ?? true);
        Assert.IsTrue(context.WorkspaceApplyPolicyContext?.Warnings.Any(item => item.Contains("could not be produced", StringComparison.OrdinalIgnoreCase)) ?? false);
    }

    [TestMethod]
    public async Task SupervisorAgent_UsesSharedWorkspaceApplyContextService()
    {
        var fakeRunReportReader = new FakeRunReportContractReader("agent-run-tester");
        var fakeRunner = BuildSuccessfulProcessRunner();
        var fakeContextService = new FakeAgentWorkspaceApplyContextService(BuildFakeContext());
        var agent = BuildSupervisorAgent(fakeRunReportReader, fakeRunner, fakeContextService);

        var result = await agent.RunAsync(BuildRequest(includeWorkspaceInput: true));

        using var document = JsonDocument.Parse(result.OutputJson);
        var root = document.RootElement;
        Assert.AreEqual("success", GetStringPropertyIgnoreCase(root.GetProperty("workspaceApply"), "outcome"));
        Assert.AreEqual(WorkspaceApplyRecommendedActions.HumanReviewOrCommit, GetStringPropertyIgnoreCase(root.GetProperty("workspaceApplyRecommendation"), "recommendedAction"));
        Assert.AreEqual(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, GetStringPropertyIgnoreCase(root.GetProperty("workspaceApplyActionRequest"), "requestedAction"));
        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.ReadyForHumanReview, GetStringPropertyIgnoreCase(root.GetProperty("workspaceApplyActionReview"), "reviewStatus"));
        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, GetStringPropertyIgnoreCase(root.GetProperty("workspaceApplyPolicyContext"), "decision"));
        Assert.AreEqual(1, fakeContextService.CallCount);
    }

    [TestMethod]
    public async Task SupervisorAgent_WithoutWorkspaceInput_LeavesWorkspaceContextNull()
    {
        var fakeRunReportReader = new FakeRunReportContractReader("agent-run-tester");
        var fakeRunner = BuildSuccessfulProcessRunner();
        var fakeContextService = new FakeAgentWorkspaceApplyContextService(BuildFakeContext());
        var agent = BuildSupervisorAgent(fakeRunReportReader, fakeRunner, fakeContextService);

        var result = await agent.RunAsync(BuildRequest(includeWorkspaceInput: false));

        using var document = JsonDocument.Parse(result.OutputJson);
        var root = document.RootElement;
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("workspaceApply").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("workspaceApplyRecommendation").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("workspaceApplyActionRequest").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("workspaceApplyActionReview").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("workspaceApplyPolicyContext").ValueKind);
        Assert.AreEqual(0, fakeContextService.CallCount);
    }

    [TestMethod]
    public void AgentWorkspaceApplyContextServiceBoundary_DoesNotAddMutationOrWorkspaceCommandCapabilities()
    {
        var repoRoot = FindRepositoryRoot();
        var serviceSource = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "WorkspaceApply", "AgentWorkspaceApplyContextService.cs"));

        AssertNoMutationOrWorkspaceCommandCapabilities(serviceSource);
    }

    [TestMethod]
    public void SupervisorAgentBoundary_UsesSharedWorkspaceApplyContextServiceOnly()
    {
        var repoRoot = FindRepositoryRoot();
        var supervisorSource = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "SupervisorAgent.cs"));

        Assert.IsFalse(supervisorSource.Contains("IWorkspaceApplyReportReader", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IWorkspaceApplyRecommendationService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IWorkspaceApplyActionRequestService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IWorkspaceApplyActionReviewService", StringComparison.Ordinal));
        Assert.IsFalse(supervisorSource.Contains("IWorkspaceApplyPolicyContextService", StringComparison.Ordinal));
        Assert.IsTrue(supervisorSource.Contains("IAgentWorkspaceApplyContextService", StringComparison.Ordinal));
        AssertNoMutationOrWorkspaceCommandCapabilities(supervisorSource);
    }

    private static AgentWorkspaceApplyContextService BuildService() =>
        new(
            new WorkspaceApplyReportReader(),
            new WorkspaceApplyRecommendationService(),
            new WorkspaceApplyActionRequestService(),
            new WorkspaceApplyActionReviewService(),
            new WorkspaceApplyPolicyContextService(new ProjectApprovalPolicyEvaluator()));

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

    private static AgentWorkspaceApplyContext BuildFakeContext()
    {
        var report = BuildSuccessSummary();
        var recommendation = new WorkspaceApplyRecommendation
        {
            RecommendedAction = WorkspaceApplyRecommendedActions.HumanReviewOrCommit,
            Reason = "Human review is required before commit.",
            HumanReviewRequired = true,
            SafeToRetry = false,
            SafeToCommitAfterReview = true,
            SourceReviewRequiredBeforeRetry = false,
            BlocksAutomaticExecution = true,
            EvidencePaths = report.EvidencePaths,
            RiskNotes = report.RiskNotes,
            Warnings = []
        };
        var actionRequest = new WorkspaceApplyActionRequest
        {
            RequestedAction = WorkspaceApplyRequestedActions.HumanReviewSourceChanges,
            Reason = "Review source changes.",
            HumanApprovalRequired = true,
            AutomaticExecutionAllowed = false,
            MutatesSourceRepo = false,
            RequiresFreshHumanDecision = true,
            EvidencePaths = report.EvidencePaths,
            RiskNotes = report.RiskNotes
        };
        var actionReview = new WorkspaceApplyActionReview
        {
            ReviewStatus = WorkspaceApplyActionReviewStatuses.ReadyForHumanReview,
            Summary = "Ready for human review.",
            HumanReviewRequired = true,
            ApprovalCanBeGrantedByThisPackage = false,
            ExecutionCanStartFromThisPackage = false,
            SourceRepoMayBeMutated = true,
            RequestedAction = actionRequest.RequestedAction,
            RecommendedAction = recommendation.RecommendedAction,
            EvidencePaths = report.EvidencePaths,
            RiskNotes = report.RiskNotes
        };
        var policyContext = new WorkspaceApplyPolicyContext
        {
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            Reason = "Allowed by project policy.",
            RiskTier = ProjectApprovalRiskTiers.WorkspaceIntent,
            ActionType = WorkspaceApplyPolicyActionTypes.WorkspaceApplyActionReview,
            RequestedAction = actionRequest.RequestedAction,
            HumanApprovalRequired = false,
            AutomaticExecutionAllowed = true,
            SourceMutationAllowed = false,
            ExecutionCanStartFromPolicyContext = false,
            ApprovalCanBeGrantedByPolicyContext = false,
            EvidencePaths = report.EvidencePaths,
            RiskNotes = report.RiskNotes
        };

        return new AgentWorkspaceApplyContext
        {
            ProjectId = "IronDev",
            RunId = report.RunId,
            WorkspacePath = report.WorkspacePath,
            WorkspaceApply = report,
            WorkspaceApplyRecommendation = recommendation,
            WorkspaceApplyActionRequest = actionRequest,
            WorkspaceApplyActionReview = actionReview,
            WorkspaceApplyPolicyContext = policyContext,
            ContextAvailable = true,
            EvidencePaths = report.EvidencePaths
        };
    }

    private static AgentRequest BuildRequest(bool includeWorkspaceInput)
    {
        var inputs = new Dictionary<string, string>
        {
            ["project"] = "IronDev",
            ["query"] = "Summarize governed workspace apply.",
            ["plan_path"] = "TestPlan.md",
            ["live_llm"] = "false"
        };

        if (includeWorkspaceInput)
        {
            inputs["workspace_apply_run_id"] = "apply-run";
            inputs["workspace_apply_workspace_path"] = "C:\\workspaces\\apply-run";
        }

        return new AgentRequest
        {
            AgentName = "SupervisorAgent",
            GoalId = "test-goal",
            DogfoodRunId = "agent-run",
            Inputs = inputs
        };
    }

    private static SupervisorAgent BuildSupervisorAgent(
        IRunReportContractReader runReportReader,
        IAgentProcessRunner processRunner,
        IAgentWorkspaceApplyContextService? workspaceApplyContextService)
    {
        return new SupervisorAgent(
            new AgentDefinition
            {
                Name = "SupervisorAgent",
                Purpose = "Test shared workspace apply context consumption.",
                DefaultModelProfile = "cheap-runner"
            },
            new AgentModelResolver(),
            FindRepositoryRoot(),
            runReportReader,
            processRunner: processRunner,
            workspaceApplyContextService: workspaceApplyContextService);
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
                new WorkspaceApplyChangedFileSummary { Operation = "add", RelativePath = "Feature.cs", Applied = true, Verified = true }
            ],
            EvidencePaths = ["C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\source-report.json"],
            RiskNotes = ["Human should review changed files before commit/PR."]
        };

    private static string? GetStringPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        Assert.IsTrue(TryGetPropertyIgnoreCase(element, propertyName, out var value), $"Expected property '{propertyName}'.");
        return value.GetString();
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

    private sealed class ThrowingWorkspaceApplyPolicyContextService : IWorkspaceApplyPolicyContextService
    {
        public WorkspaceApplyPolicyContext Create(WorkspaceApplyPolicyContextInput input) =>
            throw new InvalidOperationException("policy context test failure");
    }

    private sealed class FakeAgentWorkspaceApplyContextService : IAgentWorkspaceApplyContextService
    {
        private readonly AgentWorkspaceApplyContext _context;

        public FakeAgentWorkspaceApplyContextService(AgentWorkspaceApplyContext context)
        {
            _context = context;
        }

        public int CallCount { get; private set; }

        public Task<AgentWorkspaceApplyContext> CreateAsync(
            AgentWorkspaceApplyContextRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_context);
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

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string root, string workspacePath, string sourceRepo, string runId)
        {
            Root = root;
            WorkspacePath = workspacePath;
            SourceRepo = sourceRepo;
            RunId = runId;
            Directory.CreateDirectory(RunDirectory);
        }

        public string ProjectId => "IronDev";
        public string Root { get; }
        public string WorkspacePath { get; }
        public string SourceRepo { get; }
        public string RunId { get; }
        public string RunDirectory => Path.Combine(WorkspacePath, ".irondev", "runs", RunId);
        public string SourceReportPath => Path.Combine(RunDirectory, "source-report.json");
        public string FailurePackagePath => Path.Combine(RunDirectory, "failure-package.json");
        public AgentWorkspaceApplyContextRequest Request => new()
        {
            ProjectId = ProjectId,
            RunId = RunId,
            WorkspacePath = WorkspacePath
        };

        public static TestWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "IronDev-AgentWorkspaceApplyContextTests", Guid.NewGuid().ToString("N"));
            return new TestWorkspace(
                root,
                Path.Combine(root, "workspace"),
                Path.Combine(root, "source"),
                "apply-run");
        }

        public void WriteSourceReport()
        {
            var payload = new
            {
                status = "succeeded",
                data = new
                {
                    runId = RunId,
                    workspacePath = WorkspacePath,
                    sourceRepo = SourceRepo,
                    sourceRepoMutated = true,
                    applyVerified = true,
                    sourceMatchesWorkspace = true,
                    postApplyValidationSucceeded = true,
                    addCount = 1,
                    modifyCount = 1,
                    deleteCount = 0,
                    files = new[]
                    {
                        new { operation = "add", relativePath = "Feature.cs", applied = true, verified = true },
                        new { operation = "modify", relativePath = "Program.cs", applied = true, verified = true }
                    },
                    evidencePaths = new[] { SourceReportPath },
                    riskNotes = new[] { "Human should review changed files before commit/PR." },
                    warnings = Array.Empty<string>()
                },
                errors = Array.Empty<string>(),
                warnings = Array.Empty<string>()
            };

            File.WriteAllText(SourceReportPath, JsonSerializer.Serialize(payload));
        }

        public void WriteFailurePackage(
            bool sourceRepoMutated,
            bool applyVerified,
            bool postApplyValidationSucceeded)
        {
            var payload = new
            {
                status = "succeeded",
                data = new
                {
                    runId = RunId,
                    workspacePath = WorkspacePath,
                    sourceRepo = SourceRepo,
                    failedStage = "apply-copy",
                    failureSeverity = "critical",
                    recommendedNextAction = "do_not_retry_until_source_reviewed",
                    sourceRepoMutated,
                    applyVerified,
                    postApplyValidationSucceeded,
                    evidencePaths = new[] { FailurePackagePath },
                    riskNotes = new[] { "Failure package is advisory and does not repair or roll back changes." },
                    aggregatedErrors = Array.Empty<string>(),
                    aggregatedWarnings = Array.Empty<string>()
                },
                errors = Array.Empty<string>(),
                warnings = Array.Empty<string>()
            };

            File.WriteAllText(FailurePackagePath, JsonSerializer.Serialize(payload));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
