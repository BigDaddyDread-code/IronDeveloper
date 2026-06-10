using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Infrastructure.Services.Agents.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentWorkspacePrepareSkillExecutionServiceTests
{
    [TestMethod]
    public async Task AgentWorkspacePrepareSkillExecution_GovernedContextAllowsWorkspaceMutation_PreparesWorkspace()
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        AssertNoAuthorityExceptWorkspaceMutation(result);
        Assert.IsTrue(result.WorkspaceMutated);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspacePrepareExecutionPayload));
        var payload = (AgentSkillWorkspacePrepareExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.PrepareAttempted);
        Assert.IsTrue(payload.Prepared);
        Assert.AreEqual("IronDev", payload.ProjectId);
        Assert.AreEqual("run-1", payload.RunId);
        Assert.AreEqual("C:\\workspaces\\run-1", payload.WorkspacePath);
        Assert.AreEqual("C:\\repo\\IronDeveloper", payload.SourceRepo);
        Assert.AreEqual(7, payload.FilesCopied);
        Assert.AreEqual(3, payload.DirectoriesCreated);
        CollectionAssert.Contains(payload.EvidencePaths.ToArray(), "workspace.json");
        Assert.AreEqual(1, prepare.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspacePrepareSkillExecution_BlocksWhenWorkspaceMutationNotAllowed()
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { WorkspaceMutationAllowed = false }));

        AssertBlockedBeforePrepare(result);
        Assert.IsTrue(result.Blockers.Any(item => item.Contains("WorkspaceMutationAllowed", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, prepare.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspacePrepareSkillExecution_BlocksWhenSourceMutationAllowed()
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { SourceMutationAllowed = true }));

        AssertBlockedBeforePrepare(result);
        Assert.IsFalse(result.SourceMutated);
        Assert.AreEqual(0, prepare.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspacePrepareSkillExecution_BlocksWhenApprovalRequired()
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                HumanApprovalRequired = true,
                ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired,
                RecommendedNextAction = AgentSkillRequestContextRecommendedActions.RequestSeparateApproval
            }));

        AssertBlockedBeforePrepare(result);
        Assert.AreEqual(0, prepare.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspacePrepareSkillExecution_BlocksWhenDangerousCapability()
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                DangerousCapability = true,
                RiskTier = ProjectApprovalRiskTiers.SourceMutation
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedDangerousCapability, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, prepare.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspacePrepareSkillExecution_BlocksWhenPolicyBlocked()
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                PolicyAllowed = false,
                PolicyBlocked = true,
                Decision = ProjectApprovalDecisions.BlockedByPolicy
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByPolicy, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, prepare.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspacePrepareSkillExecution_BlocksWhenReviewStatusNotReady()
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired }));

        AssertBlockedBeforePrepare(result);
        Assert.AreEqual(0, prepare.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspacePrepareSkillExecution_BlocksWhenRecommendedActionNotReviewRequest()
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { RecommendedNextAction = AgentSkillRequestContextRecommendedActions.CollectMissingEvidence }));

        AssertBlockedBeforePrepare(result);
        Assert.AreEqual(0, prepare.CallCount);
    }

    [DataTestMethod]
    [DataRow("sourceRepo")]
    [DataRow("workspacePath")]
    [DataRow("runId")]
    [DataRow("projectId")]
    public async Task AgentWorkspacePrepareSkillExecution_MissingRequiredInput_BlocksBeforePrepare(string missing)
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);
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

        AssertBlockedBeforePrepare(result);
        Assert.IsTrue(result.Blockers.Any(item => item.Contains(missing, StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, prepare.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspacePrepareSkillExecution_PrepareServiceBlockerResult_DoesNotMutate()
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(
            prepared: false,
            prepareAttempted: false,
            blockers: ["Source repo missing."]));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        Assert.IsFalse(result.SourceMutated);
        Assert.IsFalse(result.WorkspaceMutated);
        var payload = (AgentSkillWorkspacePrepareExecutionPayload)result.Payload!;
        Assert.IsFalse(payload.PrepareAttempted);
        Assert.IsFalse(payload.Prepared);
        CollectionAssert.Contains(payload.Blockers.ToArray(), "Source repo missing.");
        Assert.AreEqual(1, prepare.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspacePrepareSkillExecution_PrepareServiceThrows_FailsConservatively()
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true))
        {
            ThrowOnPrepare = true
        };
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        Assert.IsFalse(result.SourceMutated);
        Assert.IsTrue(result.WorkspaceMutated);
        Assert.IsFalse(result.ExternalSystemCalled);
        Assert.IsFalse(result.TicketCreated);
        Assert.IsFalse(result.MemoryWritten);
        Assert.IsFalse(result.ApprovalGranted);
        Assert.IsFalse(result.ShellCommandRun);
        Assert.IsTrue(result.Warnings.Any(item => item.Contains("Synthetic workspace prepare failure.", StringComparison.Ordinal)));
        Assert.AreEqual(1, prepare.CallCount);
    }

    [DataTestMethod]
    [DataRow(AgentSkillIds.WorkspaceApplyCopy)]
    public async Task AgentWorkspacePrepareSkillExecution_UnsupportedWorkspaceSkillsRemainBlocked(string skillId)
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(skillId)));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedUnsupportedSkill, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, prepare.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspacePrepareSkillExecution_ApplyCopyRemainsImpossible()
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext(AgentSkillIds.WorkspaceApplyCopy) with
            {
                SourceMutationAllowed = true,
                WorkspaceMutationAllowed = true
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedUnsupportedSkill, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, prepare.CallCount);
    }

    [DataTestMethod]
    [DataRow(AgentSkillIds.GitCommit)]
    [DataRow(AgentSkillIds.GitHubPullRequestCreate)]
    [DataRow(AgentSkillIds.TicketCreate)]
    [DataRow("memory.write_context")]
    public async Task AgentWorkspacePrepareSkillExecution_GitGithubTicketMemoryRemainBlocked(string skillId)
    {
        var prepare = new FakeAgentWorkspacePrepareService(BuildPrepareResult(prepared: true));
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(), workspaceCheck: new FakeAgentWorkspaceCheckService(), workspacePrepare: prepare);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(skillId)));

        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, prepare.CallCount);
    }

    [TestMethod]
    public void AgentWorkspacePrepareSkillExecutionService_DoesNotRunShellOrProcess()
    {
        var root = FindRepositoryRoot();
        var executionSource = File.ReadAllText(Path.Combine(
            root,
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentSkillExecutionService.cs"));
        var prepareSource = File.ReadAllText(Path.Combine(
            root,
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentWorkspacePrepareService.cs"));
        var combined = executionSource + Environment.NewLine + prepareSource;

        Assert.IsFalse(combined.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IGitHub", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("ITicket", StringComparison.Ordinal));
        Assert.IsTrue(executionSource.Contains("IMemoryExecutionGate", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IAgentMemorySilo", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IMemoryIndexingService", StringComparison.Ordinal));
        Assert.IsFalse(combined.Contains("IMemoryImprovementProposalService", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentSkillExecutionService_DoesNotDirectlyCopyOrDeleteFiles()
    {
        var executionSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentSkillExecutionService.cs"));

        Assert.IsFalse(executionSource.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(executionSource.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(executionSource.Contains("Directory.Delete", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentWorkspacePrepareSkillExecutionService_IsNotWiredIntoAgents()
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

    private static AgentSkillRequestContext BuildAllowedContext(string skillId = AgentSkillIds.WorkspacePrepare) =>
        new()
        {
            ContextId = $"skill-context-{skillId.Replace('.', '-')}",
            RequestId = "skill-request-1",
            ReviewId = "skill-review-1",
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = skillId,
            Purpose = "Prepare a disposable workspace.",
            SkillKnown = true,
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            ReviewStatus = AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            RiskTier = ProjectApprovalRiskTiers.WorkspacePreparation,
            Category = AgentSkillCategories.WorkspaceCommand,
            HumanReviewRequired = true,
            HumanApprovalRequired = false,
            PolicyAllowed = true,
            PolicyBlocked = false,
            DangerousCapability = false,
            ExecutionCanStartFromContext = false,
            ApprovalCanBeGrantedByContext = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = string.Equals(skillId, AgentSkillIds.WorkspacePrepare, StringComparison.Ordinal),
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
            ReviewChecklist = ["Confirm this prepare operation targets only a disposable workspace."],
            Blockers = [],
            Warnings = [],
            Interpretation = ["Workspace prepare can mutate the disposable workspace only."]
        };

    private static AgentWorkspacePrepareResult BuildPrepareResult(
        bool prepared,
        bool? prepareAttempted = null,
        IReadOnlyList<string>? blockers = null) =>
        new()
        {
            Status = prepared ? AgentSkillExecutionStatuses.Succeeded : AgentSkillExecutionStatuses.BlockedByContext,
            PrepareAttempted = prepareAttempted ?? prepared,
            Prepared = prepared,
            ProjectId = "IronDev",
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            SourceRepo = "C:\\repo\\IronDeveloper",
            SourceRepoExists = true,
            WorkspacePathExists = prepared,
            SourceAndWorkspaceAreDistinct = true,
            FilesCopied = prepared ? 7 : 0,
            DirectoriesCreated = prepared ? 3 : 0,
            EvidencePaths = prepared ? ["workspace.json"] : [],
            Warnings = [],
            Blockers = blockers ?? []
        };

    private static void AssertBlockedBeforePrepare(AgentSkillExecutionResult result)
    {
        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
    }

    private static void AssertNoAuthorityExceptWorkspaceMutation(AgentSkillExecutionResult result)
    {
        Assert.IsFalse(result.SourceMutated);
        Assert.IsFalse(result.ExternalSystemCalled);
        Assert.IsFalse(result.TicketCreated);
        Assert.IsFalse(result.MemoryWritten);
        Assert.IsFalse(result.ApprovalGranted);
        Assert.IsFalse(result.ShellCommandRun);
    }

    private static void AssertNoAuthorityFlags(AgentSkillExecutionResult result)
    {
        AssertNoAuthorityExceptWorkspaceMutation(result);
        Assert.IsFalse(result.WorkspaceMutated);
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

    private sealed class FakeAgentWorkspacePrepareService : IAgentWorkspacePrepareService
    {
        private readonly AgentWorkspacePrepareResult _result;

        public FakeAgentWorkspacePrepareService(AgentWorkspacePrepareResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public bool ThrowOnPrepare { get; init; }

        public Task<AgentWorkspacePrepareResult> PrepareAsync(
            AgentWorkspacePrepareRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ThrowOnPrepare)
                throw new InvalidOperationException("Synthetic workspace prepare failure.");

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

    private sealed class FakeAgentWorkspaceCheckService : IAgentWorkspaceCheckService
    {
        public Task<AgentWorkspaceCheckResult> CheckAsync(
            AgentWorkspaceCheckRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Workspace check service should not be called directly by these tests.");
    }

    private sealed class FakeAgentWorkspaceApplyContextService : IAgentWorkspaceApplyContextService
    {
        public Task<AgentWorkspaceApplyContext> CreateAsync(
            AgentWorkspaceApplyContextRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Workspace apply context service should not be called by workspace.prepare.");
    }
}
