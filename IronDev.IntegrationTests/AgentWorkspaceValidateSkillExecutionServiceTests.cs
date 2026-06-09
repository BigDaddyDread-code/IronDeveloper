using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Core.Workspaces;
using IronDev.Infrastructure.Services.Agents.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentWorkspaceValidateSkillExecutionServiceTests
{
    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_GovernedContextAllowsWorkspaceMutation_ValidatesWorkspace()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        AssertNoAuthorityExceptWorkspaceValidation(result);
        Assert.IsTrue(result.WorkspaceMutated);
        Assert.IsTrue(result.ShellCommandRun);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspaceValidateExecutionPayload));
        var payload = (AgentSkillWorkspaceValidateExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.ValidationAttempted);
        Assert.IsTrue(payload.ValidationSucceeded);
        Assert.AreEqual("IronDev", payload.ProjectId);
        Assert.AreEqual("run-1", payload.RunId);
        Assert.AreEqual("C:\\workspaces\\run-1", payload.WorkspacePath);
        Assert.AreEqual("dotnet-build-test", payload.ProfileId);
        Assert.AreEqual("succeeded", payload.ValidationStatus);
        Assert.AreEqual(0, payload.ExitCode);
        Assert.IsTrue(payload.MetadataWritten);
        Assert.AreEqual(2, payload.Steps.Count);
        CollectionAssert.Contains(payload.EvidencePaths.ToArray(), "validation.json");
        Assert.AreEqual(1, validation.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_BlocksWhenWorkspaceMutationNotAllowed()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { WorkspaceMutationAllowed = false }));

        AssertBlockedBeforeValidation(result);
        Assert.IsTrue(result.Blockers.Any(item => item.Contains("WorkspaceMutationAllowed", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, validation.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_BlocksWhenSourceMutationAllowed()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { SourceMutationAllowed = true }));

        AssertBlockedBeforeValidation(result);
        Assert.IsFalse(result.SourceMutated);
        Assert.AreEqual(0, validation.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_BlocksWhenApprovalRequired()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                HumanApprovalRequired = true,
                ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired,
                RecommendedNextAction = AgentSkillRequestContextRecommendedActions.RequestSeparateApproval
            }));

        AssertBlockedBeforeValidation(result);
        Assert.AreEqual(0, validation.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_BlocksWhenDangerousCapability()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                DangerousCapability = true,
                RiskTier = ProjectApprovalRiskTiers.SourceMutation
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedDangerousCapability, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, validation.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_BlocksWhenPolicyBlocked()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

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
        Assert.AreEqual(0, validation.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_BlocksWhenReviewStatusNotReady()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired }));

        AssertBlockedBeforeValidation(result);
        Assert.AreEqual(0, validation.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_BlocksWhenRecommendedActionNotReviewRequest()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { RecommendedNextAction = AgentSkillRequestContextRecommendedActions.CollectMissingEvidence }));

        AssertBlockedBeforeValidation(result);
        Assert.AreEqual(0, validation.CallCount);
    }

    [DataTestMethod]
    [DataRow("workspacePath")]
    [DataRow("runId")]
    [DataRow("projectId")]
    public async Task AgentWorkspaceValidateSkillExecution_MissingRequiredInput_BlocksBeforeValidation(string missing)
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);
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
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var result = await service.ExecuteAsync(request);

        AssertBlockedBeforeValidation(result);
        Assert.IsTrue(result.Blockers.Any(item => item.Contains(missing, StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, validation.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_DefaultProfileIsDotnetBuildTest()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest() with
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1"
            }
        });

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.AreEqual("dotnet-build-test", validation.LastRequest?.ProfileId);
        var payload = (AgentSkillWorkspaceValidateExecutionPayload)result.Payload!;
        Assert.AreEqual("dotnet-build-test", payload.ProfileId);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_ProfileIdParameterIsPassedThrough()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded", profileId: "custom-profile"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest() with
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1",
                ["profileId"] = "custom-profile"
            }
        });

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.AreEqual("custom-profile", validation.LastRequest?.ProfileId);
        var payload = (AgentSkillWorkspaceValidateExecutionPayload)result.Payload!;
        Assert.AreEqual("custom-profile", payload.ProfileId);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_FailedValidationReturnsFailedExecutedResult()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("failed", exitCode: 1, errors: ["Build failed."]));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        AssertNoAuthorityExceptWorkspaceValidation(result);
        Assert.IsTrue(result.WorkspaceMutated);
        Assert.IsTrue(result.ShellCommandRun);
        var payload = (AgentSkillWorkspaceValidateExecutionPayload)result.Payload!;
        Assert.IsFalse(payload.ValidationSucceeded);
        CollectionAssert.Contains(payload.Errors.ToArray(), "Build failed.");
        Assert.AreEqual(1, validation.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_BlockedValidationMapsToBlockedByContext()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult(
            "blocked",
            steps: [],
            evidencePaths: [],
            validationMetadataPath: null,
            errors: ["profile not allowlisted"]));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.WorkspaceMutated);
        Assert.IsFalse(result.ShellCommandRun);
        var payload = (AgentSkillWorkspaceValidateExecutionPayload)result.Payload!;
        Assert.IsFalse(payload.ValidationAttempted);
        Assert.IsFalse(payload.ValidationSucceeded);
        CollectionAssert.Contains(payload.Blockers.ToArray(), "profile not allowlisted");
        Assert.AreEqual(1, validation.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_ServiceThrows_FailsConservatively()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"))
        {
            ThrowOnValidate = true
        };
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        Assert.IsFalse(result.SourceMutated);
        Assert.IsTrue(result.WorkspaceMutated);
        Assert.IsTrue(result.ShellCommandRun);
        Assert.IsFalse(result.ExternalSystemCalled);
        Assert.IsFalse(result.TicketCreated);
        Assert.IsFalse(result.MemoryWritten);
        Assert.IsFalse(result.ApprovalGranted);
        Assert.IsTrue(result.Warnings.Any(item => item.Contains("Synthetic workspace validation failure.", StringComparison.Ordinal)));
        Assert.AreEqual(1, validation.CallCount);
    }

    [DataTestMethod]
    [DataRow(AgentSkillIds.WorkspaceFailurePackage)]
    [DataRow(AgentSkillIds.WorkspaceApplyCopy)]
    public async Task AgentWorkspaceValidateSkillExecution_UnsupportedWorkspaceSkillsRemainBlocked(string skillId)
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(skillId)));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedUnsupportedSkill, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, validation.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceValidateSkillExecution_ApplyCopyRemainsImpossible()
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext(AgentSkillIds.WorkspaceApplyCopy) with
            {
                SourceMutationAllowed = true,
                WorkspaceMutationAllowed = true
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedUnsupportedSkill, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, validation.CallCount);
    }

    [DataTestMethod]
    [DataRow(AgentSkillIds.GitCommit)]
    [DataRow(AgentSkillIds.GitHubPullRequestCreate)]
    [DataRow(AgentSkillIds.TicketCreate)]
    [DataRow("memory.write_context")]
    [DataRow("external.call")]
    public async Task AgentWorkspaceValidateSkillExecution_GitGithubTicketMemoryExternalRemainBlocked(string skillId)
    {
        var validation = new FakeDisposableWorkspaceValidationService(BuildValidationResult("succeeded"));
        var service = BuildService(validation);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(skillId)));

        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, validation.CallCount);
    }

    [TestMethod]
    public void AgentWorkspaceValidateSkillExecutionService_DoesNotRunProcessDirectly()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentSkillExecutionService.cs"));

        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("new DisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("new DisposableWorkspaceValidationService", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentSkillExecutionService_DoesNotDirectlyCopyOrDeleteFiles()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentSkillExecutionService.cs"));

        Assert.IsFalse(source.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Directory.Delete", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentWorkspaceValidateSkillExecutionService_IsNotWiredIntoAgents()
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

    private static AgentSkillExecutionService BuildService(FakeDisposableWorkspaceValidationService validation) =>
        AgentSkillExecutionTestServices.Create(
            new FakeAgentWorkspaceApplyContextService(),
            workspaceCheck: new FakeAgentWorkspaceCheckService(),
            workspacePrepare: new FakeAgentWorkspacePrepareService(),
            validation: validation);

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
            SourceRepo = null,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1"
            }
        };

    private static AgentSkillRequestContext BuildAllowedContext(string skillId = AgentSkillIds.WorkspaceValidate) =>
        new()
        {
            ContextId = $"skill-context-{skillId.Replace('.', '-')}",
            RequestId = "skill-request-1",
            ReviewId = "skill-review-1",
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = skillId,
            Purpose = "Validate a disposable workspace.",
            SkillKnown = true,
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            ReviewStatus = AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            RiskTier = string.Equals(skillId, AgentSkillIds.WorkspaceValidate, StringComparison.Ordinal)
                ? ProjectApprovalRiskTiers.WorkspaceValidation
                : ProjectApprovalRiskTiers.WorkspacePreparation,
            Category = AgentSkillCategories.WorkspaceCommand,
            HumanReviewRequired = true,
            HumanApprovalRequired = false,
            PolicyAllowed = true,
            PolicyBlocked = false,
            DangerousCapability = false,
            ExecutionCanStartFromContext = false,
            ApprovalCanBeGrantedByContext = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = string.Equals(skillId, AgentSkillIds.WorkspaceValidate, StringComparison.Ordinal),
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            RecommendedNextAction = AgentSkillRequestContextRecommendedActions.ReviewRequest,
            EvidencePaths = ["skill-context.json"],
            ParametersSummary =
            [
                "runId=run-1",
                "workspacePath=C:\\workspaces\\run-1",
                "profileId=dotnet-build-test"
            ],
            ReviewChecklist = ["Confirm validation targets only a disposable workspace."],
            Blockers = [],
            Warnings = [],
            Interpretation = ["Workspace validate can mutate disposable workspace evidence only."]
        };

    private static DisposableWorkspaceValidationResult BuildValidationResult(
        string status,
        int? exitCode = null,
        string profileId = "dotnet-build-test",
        IReadOnlyList<DisposableWorkspaceValidationStep>? steps = null,
        IReadOnlyList<string>? evidencePaths = null,
        string? validationMetadataPath = "validation.json",
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? warnings = null)
    {
        var succeeded = string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase);
        var resultSteps = steps ??
        [
            BuildStep("dotnet-build", "succeeded", 0, true),
            BuildStep("dotnet-test", status, exitCode ?? (succeeded ? 0 : 1), succeeded)
        ];
        var resultEvidencePaths = evidencePaths ?? ["build.stdout.txt", "test.stdout.txt", "validation.json"];
        var resultErrors = errors ?? [];
        var resultWarnings = warnings ?? [];

        return new DisposableWorkspaceValidationResult
        {
            Status = status,
            Summary = succeeded ? "Workspace validation completed." : "Workspace validation did not complete successfully.",
            ExitCode = exitCode ?? (succeeded ? 0 : 1),
            Data = new DisposableWorkspaceValidationData
            {
                RunId = "run-1",
                WorkspacePath = "C:\\workspaces\\run-1",
                ProfileId = profileId,
                Status = status,
                Succeeded = succeeded,
                Steps = resultSteps,
                EvidencePaths = resultEvidencePaths,
                ValidationMetadataPath = validationMetadataPath,
                Errors = resultErrors,
                Warnings = resultWarnings
            },
            Errors = resultErrors,
            Warnings = resultWarnings
        };
    }

    private static DisposableWorkspaceValidationStep BuildStep(
        string commandId,
        string status,
        int exitCode,
        bool succeeded) =>
        new()
        {
            CommandId = commandId,
            Status = status,
            ExitCode = exitCode,
            Succeeded = succeeded,
            EvidencePaths = [$"{commandId}.stdout.txt"],
            Errors = succeeded ? [] : [$"{commandId} failed."],
            Warnings = []
        };

    private static void AssertBlockedBeforeValidation(AgentSkillExecutionResult result)
    {
        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
    }

    private static void AssertNoAuthorityExceptWorkspaceValidation(AgentSkillExecutionResult result)
    {
        Assert.IsFalse(result.SourceMutated);
        Assert.IsFalse(result.ExternalSystemCalled);
        Assert.IsFalse(result.TicketCreated);
        Assert.IsFalse(result.MemoryWritten);
        Assert.IsFalse(result.ApprovalGranted);
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

    private sealed class FakeDisposableWorkspaceValidationService : IDisposableWorkspaceValidationService
    {
        private readonly DisposableWorkspaceValidationResult _result;

        public FakeDisposableWorkspaceValidationService(DisposableWorkspaceValidationResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }
        public bool ThrowOnValidate { get; init; }
        public DisposableWorkspaceValidationRequest? LastRequest { get; private set; }

        public Task<DisposableWorkspaceValidationResult> ValidateAsync(
            DisposableWorkspaceValidationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            if (ThrowOnValidate)
                throw new InvalidOperationException("Synthetic workspace validation failure.");

            return Task.FromResult(_result with
            {
                Data = _result.Data with
                {
                    RunId = request.RunId,
                    WorkspacePath = request.WorkspacePath,
                    ProfileId = request.ProfileId
                }
            });
        }
    }

    private sealed class FakeAgentWorkspaceApplyContextService : IAgentWorkspaceApplyContextService
    {
        public Task<AgentWorkspaceApplyContext> CreateAsync(
            AgentWorkspaceApplyContextRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentWorkspaceApplyContext
            {
                ProjectId = request.ProjectId,
                RunId = request.RunId,
                WorkspacePath = request.WorkspacePath,
                ContextAvailable = false,
                EvidencePaths = [],
                Warnings = ["Workspace apply context should not be read for validate."]
            });
    }

    private sealed class FakeAgentWorkspaceCheckService : IAgentWorkspaceCheckService
    {
        public Task<AgentWorkspaceCheckResult> CheckAsync(
            AgentWorkspaceCheckRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Workspace check should not be called for validate.");
    }

    private sealed class FakeAgentWorkspacePrepareService : IAgentWorkspacePrepareService
    {
        public Task<AgentWorkspacePrepareResult> PrepareAsync(
            AgentWorkspacePrepareRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Workspace prepare should not be called for validate.");
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
}
