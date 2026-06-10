using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Infrastructure.Services.Agents.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentWorkspaceCheckSkillExecutionServiceTests
{
    [TestMethod]
    public async Task AgentWorkspaceCheckSkillExecution_ReadyWorkspace_ReturnsSucceededPayload()
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(readyForPrepare: true));
        var applyContext = new FakeAgentWorkspaceApplyContextService();
        var service = AgentSkillExecutionTestServices.Create(applyContext, workspaceCheck: workspaceCheck);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        AssertSucceededReadOnly(result);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspaceCheckExecutionPayload));
        var payload = (AgentSkillWorkspaceCheckExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.CheckAvailable);
        Assert.AreEqual("IronDev", payload.ProjectId);
        Assert.AreEqual("run-1", payload.RunId);
        Assert.AreEqual("C:\\workspaces\\run-1", payload.WorkspacePath);
        Assert.AreEqual("C:\\repo\\IronDeveloper", payload.SourceRepo);
        Assert.IsTrue(payload.SourceRepoExists);
        Assert.IsFalse(payload.WorkspacePathExists);
        Assert.IsTrue(payload.WorkspaceInsideAllowedRoot);
        Assert.IsTrue(payload.SourceAndWorkspaceAreDistinct);
        Assert.IsTrue(payload.ReadyForPrepare);
        CollectionAssert.Contains(payload.EvidencePaths.ToArray(), "skill-context.json");
        Assert.AreEqual(1, workspaceCheck.CallCount);
        Assert.AreEqual(0, applyContext.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceCheckSkillExecution_NotReadyWorkspace_StillSucceedsWithBlockersInPayload()
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(
            readyForPrepare: false,
            blockers: ["Workspace path already exists: C:\\workspaces\\run-1"],
            warnings: ["Workspace check completed but the workspace is not ready for prepare."]));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        AssertSucceededReadOnly(result);
        Assert.AreEqual(0, result.Blockers.Count);
        var payload = (AgentSkillWorkspaceCheckExecutionPayload)result.Payload!;
        Assert.IsFalse(payload.ReadyForPrepare);
        CollectionAssert.Contains(payload.Blockers.ToArray(), "Workspace path already exists: C:\\workspaces\\run-1");
        CollectionAssert.Contains(payload.Warnings.ToArray(), "Workspace check completed but the workspace is not ready for prepare.");
        Assert.AreEqual(1, workspaceCheck.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceCheckSkillExecution_ResolvesInputsFromParameters()
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(
            runId: "run-from-parameters",
            workspacePath: "C:\\workspaces\\from-parameters",
            sourceRepo: "C:\\repo\\from-parameters",
            readyForPrepare: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);
        var request = BuildExecutionRequest() with
        {
            RunId = null,
            WorkspacePath = null,
            SourceRepo = null,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-from-parameters",
                ["workspacePath"] = "C:\\workspaces\\from-parameters",
                ["sourceRepo"] = "C:\\repo\\from-parameters"
            }
        };

        var result = await service.ExecuteAsync(request);

        AssertSucceededReadOnly(result);
        Assert.AreEqual("run-from-parameters", workspaceCheck.LastRequest!.RunId);
        Assert.AreEqual("C:\\workspaces\\from-parameters", workspaceCheck.LastRequest.WorkspacePath);
        Assert.AreEqual("C:\\repo\\from-parameters", workspaceCheck.LastRequest.SourceRepo);
    }

    [TestMethod]
    public async Task AgentWorkspaceCheckSkillExecution_ResolvesInputsFromContextSummary()
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(
            runId: "run-from-context",
            workspacePath: "C:\\workspaces\\from-context",
            sourceRepo: "C:\\repo\\from-context",
            readyForPrepare: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);
        var context = BuildAllowedContext() with
        {
            ParametersSummary =
            [
                "runId=run-from-context",
                "workspacePath=C:\\workspaces\\from-context",
                "sourceRepo=C:\\repo\\from-context"
            ]
        };
        var request = BuildExecutionRequest(context) with
        {
            RunId = null,
            WorkspacePath = null,
            SourceRepo = null,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var result = await service.ExecuteAsync(request);

        AssertSucceededReadOnly(result);
        Assert.AreEqual("run-from-context", workspaceCheck.LastRequest!.RunId);
        Assert.AreEqual("C:\\workspaces\\from-context", workspaceCheck.LastRequest.WorkspacePath);
        Assert.AreEqual("C:\\repo\\from-context", workspaceCheck.LastRequest.SourceRepo);
    }

    [DataTestMethod]
    [DataRow("projectId")]
    [DataRow("runId")]
    [DataRow("workspacePath")]
    [DataRow("sourceRepo")]
    public async Task AgentWorkspaceCheckSkillExecution_MissingRequiredInput_BlocksBeforeCheck(string missing)
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(readyForPrepare: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);
        var context = BuildAllowedContext() with
        {
            ProjectId = missing == "projectId" ? string.Empty : "IronDev",
            ParametersSummary = []
        };
        var request = BuildExecutionRequest(context) with
        {
            ProjectId = missing == "projectId" ? null : "IronDev",
            RunId = missing == "runId" ? null : "run-1",
            WorkspacePath = missing == "workspacePath" ? null : "C:\\workspaces\\run-1",
            SourceRepo = missing == "sourceRepo" ? null : "C:\\repo\\IronDeveloper",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var result = await service.ExecuteAsync(request);

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.IsTrue(result.Blockers.Any(item => item.Contains(missing, StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, workspaceCheck.CallCount);
    }

    [DataTestMethod]
    [DataRow(AgentSkillIds.WorkspaceApplyCopy)]
    [DataRow("git.commit")]
    [DataRow("github.pull_request.create")]
    [DataRow("ticket.create")]
    [DataRow("memory.search")]
    public async Task AgentWorkspaceCheckSkillExecution_UnsupportedSkillsRemainBlocked(string skillId)
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(readyForPrepare: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(skillId)));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedUnsupportedSkill, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, workspaceCheck.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceCheckSkillExecution_UnknownSkillRemainsBlocked()
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(readyForPrepare: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext("workspace.check.missing") with { SkillKnown = false }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedUnknownSkill, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, workspaceCheck.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceCheckSkillExecution_PolicyBlockedRemainsBlocked()
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(readyForPrepare: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                Decision = ProjectApprovalDecisions.BlockedByPolicy,
                PolicyAllowed = false,
                PolicyBlocked = true
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByPolicy, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, workspaceCheck.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceCheckSkillExecution_DangerousCapabilityRemainsBlocked()
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(readyForPrepare: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                DangerousCapability = true,
                RiskTier = ProjectApprovalRiskTiers.SourceMutation
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedDangerousCapability, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, workspaceCheck.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceCheckSkillExecution_ApprovalRequiredRemainsBlocked()
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(readyForPrepare: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                HumanApprovalRequired = true,
                ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired,
                RecommendedNextAction = AgentSkillRequestContextRecommendedActions.RequestSeparateApproval
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, workspaceCheck.CallCount);
    }

    [DataTestMethod]
    [DataRow("review")]
    [DataRow("action")]
    public async Task AgentWorkspaceCheckSkillExecution_ContextNotReadyRemainsBlocked(string badContext)
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(readyForPrepare: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);
        var context = BuildAllowedContext();
        context = badContext switch
        {
            "review" => context with { ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired },
            "action" => context with { RecommendedNextAction = AgentSkillRequestContextRecommendedActions.CollectMissingEvidence },
            _ => context
        };

        var result = await service.ExecuteAsync(BuildExecutionRequest(context));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, workspaceCheck.CallCount);
    }

    [DataTestMethod]
    [DataRow("source")]
    [DataRow("workspace")]
    [DataRow("external")]
    [DataRow("ticket")]
    [DataRow("memory")]
    [DataRow("approval")]
    [DataRow("execution")]
    public async Task AgentWorkspaceCheckSkillExecution_AuthorityFlagsRemainBlocked(string flag)
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(readyForPrepare: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildContextWithFlag(flag)));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, workspaceCheck.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceCheckSkillExecution_CheckServiceThrows_ReturnsFailedWithoutMutation()
    {
        var workspaceCheck = new FakeAgentWorkspaceCheckService(BuildCheckResult(readyForPrepare: true))
        {
            ThrowOnCheck = true
        };
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: workspaceCheck);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsTrue(result.ReadOnlyExecution);
        AssertNoAuthorityFlags(result);
        Assert.IsTrue(result.Warnings.Any(item => item.Contains("Synthetic workspace check failure.", StringComparison.Ordinal)));
        Assert.AreEqual(1, workspaceCheck.CallCount);
    }

    [TestMethod]
    public void AgentWorkspaceCheckSkillExecutionService_HasNoMutationExternalProcessOrPrepareDependencies()
    {
        var root = FindRepositoryRoot();
        var executionSource = File.ReadAllText(Path.Combine(
            root,
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentSkillExecutionService.cs"));
        var checkSource = File.ReadAllText(Path.Combine(
            root,
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentWorkspaceCheckService.cs"));
        var combined = executionSource + Environment.NewLine + checkSource;

        Assert.IsFalse(combined.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IDisposableWorkspacePrepareService", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IGitHub", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("ITicket", StringComparison.Ordinal));
        Assert.IsTrue(combined.Contains("IMemoryExecutionGate", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IAgentMemorySilo", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IMemoryIndexingService", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IMemoryImprovementProposalService", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("MemoryService", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentWorkspaceCheckSkillExecutionService_IsNotWiredIntoAgents()
    {
        var agentsDirectory = Path.Combine(FindRepositoryRoot(), "IronDev.Infrastructure", "Services", "Agents");
        var wiredAgentFiles = Directory
            .EnumerateFiles(agentsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => File.ReadAllText(path).Contains("IAgentSkillExecutionService", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), wiredAgentFiles);
    }

    private static AgentSkillExecutionRequest BuildExecutionRequest() =>
        BuildExecutionRequest(BuildAllowedContext());

    private static AgentSkillExecutionRequest BuildExecutionRequest(AgentSkillRequestContext context) =>
        new()
        {
            SkillRequestContext = context,
            RequestedByAgent = "CriticAgent",
            ProjectId = "IronDev",
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            SourceRepo = "C:\\repo\\IronDeveloper",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1",
                ["sourceRepo"] = "C:\\repo\\IronDeveloper"
            }
        };

    private static AgentSkillRequestContext BuildContextWithFlag(string flag)
    {
        var context = BuildAllowedContext();
        return flag switch
        {
            "source" => context with { SourceMutationAllowed = true },
            "workspace" => context with { WorkspaceMutationAllowed = true },
            "external" => context with { ExternalSystemAllowed = true },
            "ticket" => context with { CreatesTicketAllowed = true },
            "memory" => context with { WritesMemoryAllowed = true },
            "approval" => context with { ApprovalCanBeGrantedByContext = true },
            "execution" => context with { ExecutionCanStartFromContext = true },
            _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown test flag.")
        };
    }

    private static AgentSkillRequestContext BuildAllowedContext(string skillId = AgentSkillIds.WorkspaceCheck) =>
        new()
        {
            ContextId = $"skill-context-{skillId.Replace('.', '-')}",
            RequestId = "skill-request-1",
            ReviewId = "skill-review-1",
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = skillId,
            Purpose = "Check whether a disposable workspace is ready for prepare.",
            SkillKnown = true,
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            ReviewStatus = AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            RiskTier = ProjectApprovalRiskTiers.WorkspaceReporting,
            Category = AgentSkillCategories.WorkspaceContext,
            HumanReviewRequired = true,
            HumanApprovalRequired = false,
            PolicyAllowed = true,
            PolicyBlocked = false,
            DangerousCapability = false,
            ExecutionCanStartFromContext = false,
            ApprovalCanBeGrantedByContext = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = false,
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            RecommendedNextAction = AgentSkillRequestContextRecommendedActions.ReviewRequest,
            EvidencePaths = ["skill-context.json"],
            ParametersSummary =
            [
                "runId=run-1",
                "workspacePath=C:\\workspaces\\run-1",
                "sourceRepo=C:\\repo\\IronDeveloper"
            ],
            ReviewChecklist = ["Confirm this check does not prepare or mutate a workspace."],
            Blockers = [],
            Warnings = [],
            Interpretation = ["Workspace check may inspect readiness but cannot execute preparation."]
        };

    private static AgentWorkspaceCheckResult BuildCheckResult(
        bool readyForPrepare,
        string runId = "run-1",
        string workspacePath = "C:\\workspaces\\run-1",
        string sourceRepo = "C:\\repo\\IronDeveloper",
        IReadOnlyList<string>? blockers = null,
        IReadOnlyList<string>? warnings = null) =>
        new()
        {
            CheckAvailable = true,
            ProjectId = "IronDev",
            RunId = runId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            SourceRepoExists = true,
            WorkspacePathExists = false,
            WorkspaceInsideAllowedRoot = readyForPrepare,
            SourceAndWorkspaceAreDistinct = readyForPrepare,
            ReadyForPrepare = readyForPrepare,
            EvidencePaths = ["check-evidence.json"],
            Warnings = warnings ?? [],
            Blockers = blockers ?? []
        };

    private static void AssertSucceededReadOnly(AgentSkillExecutionResult result)
    {
        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsTrue(result.ReadOnlyExecution);
        AssertNoAuthorityFlags(result);
    }

    private static void AssertNoAuthorityFlags(AgentSkillExecutionResult result)
    {
        Assert.IsFalse(result.SourceMutated);
        Assert.IsFalse(result.WorkspaceMutated);
        Assert.IsFalse(result.ExternalSystemCalled);
        Assert.IsFalse(result.TicketCreated);
        Assert.IsFalse(result.MemoryWritten);
        Assert.IsFalse(result.ApprovalGranted);
        Assert.IsFalse(result.ShellCommandRun);
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

    private sealed class FakeAgentWorkspaceCheckService : IAgentWorkspaceCheckService
    {
        private readonly AgentWorkspaceCheckResult _result;

        public FakeAgentWorkspaceCheckService(AgentWorkspaceCheckResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public AgentWorkspaceCheckRequest? LastRequest { get; private set; }

        public bool ThrowOnCheck { get; init; }

        public Task<AgentWorkspaceCheckResult> CheckAsync(
            AgentWorkspaceCheckRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;

            if (ThrowOnCheck)
                throw new InvalidOperationException("Synthetic workspace check failure.");

            return Task.FromResult(_result with
            {
                ProjectId = request.ProjectId,
                RunId = request.RunId,
                WorkspacePath = request.WorkspacePath,
                SourceRepo = request.SourceRepo,
                EvidencePaths = request.EvidencePaths.Concat(_result.EvidencePaths).ToArray()
            });
        }
    }

    private sealed class FakeAgentWorkspaceApplyContextService : IAgentWorkspaceApplyContextService
    {
        public int CallCount { get; private set; }

        public Task<AgentWorkspaceApplyContext> CreateAsync(
            AgentWorkspaceApplyContextRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Workspace apply context service should not be called by workspace.check.");
        }
    }
}
