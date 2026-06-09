using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentWorkspaceFailurePackageSkillExecutionServiceTests
{
    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_GovernedContextAllowsWorkspaceMutation_CreatesFailurePackage()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        AssertWorkspaceLocalFailurePackageOnly(result);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspaceFailurePackageExecutionPayload));
        var payload = (AgentSkillWorkspaceFailurePackageExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.PackageAttempted);
        Assert.IsTrue(payload.PackageCreated);
        Assert.IsTrue(payload.MetadataWritten);
        Assert.AreEqual("IronDev", payload.ProjectId);
        Assert.AreEqual("run-1", payload.RunId);
        Assert.AreEqual("C:\\workspaces\\run-1", payload.WorkspacePath);
        Assert.AreEqual("C:\\repo\\IronDeveloper", payload.SourceRepo);
        Assert.AreEqual("validation failed", payload.FailureReason);
        Assert.IsTrue(payload.RequiresHumanReview);
        Assert.IsFalse(payload.CanRetryAutomatically);
        Assert.IsFalse(payload.CanApplyToSourceRepo);
        Assert.AreEqual("inspect_evidence_before_retry", payload.RecommendedNextAction);
        CollectionAssert.Contains(payload.EvidencePaths.ToArray(), "failure-package.json");
        Assert.AreEqual("apply-preflight", failurePackage.LastRequest!.FailedStage);
        Assert.AreEqual(1, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_BlocksWhenWorkspaceMutationNotAllowed()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { WorkspaceMutationAllowed = false }));

        AssertBlockedBeforeFailurePackage(result);
        Assert.IsTrue(result.Blockers.Any(item => item.Contains("WorkspaceMutationAllowed", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_BlocksWhenSourceMutationAllowed()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { SourceMutationAllowed = true }));

        AssertBlockedBeforeFailurePackage(result);
        Assert.AreEqual(0, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_BlocksWhenPolicyBlocked()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

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
        Assert.AreEqual(0, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_BlocksWhenDangerousCapability()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                DangerousCapability = true,
                RiskTier = ProjectApprovalRiskTiers.SourceMutation
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedDangerousCapability, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_BlocksWhenApprovalRequired()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                HumanApprovalRequired = true,
                ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired,
                RecommendedNextAction = AgentSkillRequestContextRecommendedActions.RequestSeparateApproval
            }));

        AssertBlockedBeforeFailurePackage(result);
        Assert.AreEqual(0, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_BlocksWhenReviewStatusNotReady()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired }));

        AssertBlockedBeforeFailurePackage(result);
        Assert.AreEqual(0, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_BlocksWhenRecommendedActionNotReviewRequest()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { RecommendedNextAction = AgentSkillRequestContextRecommendedActions.CollectMissingEvidence }));

        AssertBlockedBeforeFailurePackage(result);
        Assert.AreEqual(0, failurePackage.CallCount);
    }

    [DataTestMethod]
    [DataRow("projectId")]
    [DataRow("runId")]
    [DataRow("workspacePath")]
    [DataRow("sourceRepo")]
    public async Task AgentWorkspaceFailurePackageSkillExecution_MissingRequiredInput_BlocksBeforeService(string missing)
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);
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

        AssertBlockedBeforeFailurePackage(result);
        Assert.IsTrue(result.Blockers.Any(item => item.Contains(missing, StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_OptionalEvidenceMayBeMissing()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult(
            "succeeded",
            warnings: ["validation report missing", "diff report missing", "promotion package missing"]));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);
        var request = BuildExecutionRequest() with
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1",
                ["sourceRepo"] = "C:\\repo\\IronDeveloper",
                ["failedStage"] = "validate"
            }
        };

        var result = await service.ExecuteAsync(request);

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsTrue(result.WorkspaceMutated);
        var payload = (AgentSkillWorkspaceFailurePackageExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.PackageCreated);
        Assert.IsTrue(payload.Warnings.Any(item => item.Contains("validation report missing", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(payload.Warnings.Any(item => item.Contains("promotion package missing", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(1, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_ServiceBlockedResultMapsToBlockedWithoutMutation()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult(
            "blocked",
            failurePackagePath: null,
            evidencePaths: [],
            errors: ["Workspace metadata was not found."]));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        AssertNoAuthorityFlags(result);
        var payload = (AgentSkillWorkspaceFailurePackageExecutionPayload)result.Payload!;
        Assert.IsFalse(payload.PackageAttempted);
        Assert.IsFalse(payload.PackageCreated);
        CollectionAssert.Contains(payload.Blockers.ToArray(), "Workspace metadata was not found.");
        Assert.AreEqual(1, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_ServiceFailedResultMapsToFailedConservatively()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult(
            "failed",
            errors: ["Workspace failure package could not be written."]));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        Assert.IsFalse(result.SourceMutated);
        Assert.IsTrue(result.WorkspaceMutated);
        Assert.IsFalse(result.ShellCommandRun);
        var payload = (AgentSkillWorkspaceFailurePackageExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.PackageAttempted);
        Assert.IsFalse(payload.PackageCreated);
        Assert.IsTrue(payload.Errors.Any(item => item.Contains("could not be written", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(1, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_ServiceThrows_FailsConservatively()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"))
        {
            ThrowOnCreate = true
        };
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        Assert.IsFalse(result.SourceMutated);
        Assert.IsTrue(result.WorkspaceMutated);
        Assert.IsFalse(result.ShellCommandRun);
        Assert.IsTrue(result.Warnings.Any(item => item.Contains("Synthetic workspace failure package failure.", StringComparison.Ordinal)));
        Assert.AreEqual(1, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_ApplyCopyRemainsBlocked()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext(AgentSkillIds.WorkspaceApplyCopy) with
            {
                SourceMutationAllowed = true,
                WorkspaceMutationAllowed = true
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedUnsupportedSkill, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, failurePackage.CallCount);
    }

    [DataTestMethod]
    [DataRow(AgentSkillIds.GitCommit)]
    [DataRow(AgentSkillIds.GitHubPullRequestCreate)]
    [DataRow(AgentSkillIds.TicketCreate)]
    [DataRow("memory.write_context")]
    [DataRow("external.call")]
    public async Task AgentWorkspaceFailurePackageSkillExecution_GitGithubTicketMemoryExternalRemainBlocked(string skillId)
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(skillId)));

        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, failurePackage.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceFailurePackageSkillExecution_DoesNotImplyRetryOrApply()
    {
        var failurePackage = new FakeDisposableWorkspaceFailurePackageService(BuildFailurePackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(failurePackage: failurePackage);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        var payload = (AgentSkillWorkspaceFailurePackageExecutionPayload)result.Payload!;
        Assert.IsFalse(payload.CanRetryAutomatically);
        Assert.IsFalse(payload.CanApplyToSourceRepo);
        Assert.IsTrue(payload.RequiresHumanReview);
    }

    [TestMethod]
    public void AgentWorkspaceFailurePackageSkillExecutionService_DoesNotRunProcessDirectly()
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
    }

    [TestMethod]
    public void AgentWorkspaceFailurePackageSkillExecutionService_DoesNotDirectlyApplyOrCopySource()
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
        Assert.IsFalse(source.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("git commit", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("pull request", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AgentWorkspaceFailurePackageSkillExecutionService_IsNotWiredIntoAgents()
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
                ["sourceRepo"] = "C:\\repo\\IronDeveloper",
                ["failedStage"] = "apply-preflight",
                ["failureReason"] = "validation failed",
                ["validationReportPath"] = "validation.json",
                ["sourceReportPath"] = "source-report.json",
                ["promotionPackagePath"] = "promotion-package.json"
            }
        };

    private static AgentSkillRequestContext BuildAllowedContext(string skillId = AgentSkillIds.WorkspaceFailurePackage) =>
        new()
        {
            ContextId = $"skill-context-{skillId.Replace('.', '-')}",
            RequestId = "skill-request-1",
            ReviewId = "skill-review-1",
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = skillId,
            Purpose = "Create governed workspace failure package evidence.",
            SkillKnown = true,
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            ReviewStatus = AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            RiskTier = string.Equals(skillId, AgentSkillIds.WorkspaceFailurePackage, StringComparison.Ordinal)
                ? ProjectApprovalRiskTiers.WorkspacePackaging
                : ProjectApprovalRiskTiers.SourceMutation,
            Category = AgentSkillCategories.WorkspaceApply,
            HumanReviewRequired = true,
            HumanApprovalRequired = false,
            PolicyAllowed = true,
            PolicyBlocked = false,
            DangerousCapability = false,
            ExecutionCanStartFromContext = false,
            ApprovalCanBeGrantedByContext = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = string.Equals(skillId, AgentSkillIds.WorkspaceFailurePackage, StringComparison.Ordinal),
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            RecommendedNextAction = AgentSkillRequestContextRecommendedActions.ReviewRequest,
            EvidencePaths = ["skill-context.json"],
            ParametersSummary =
            [
                "runId=run-1",
                "workspacePath=C:\\workspaces\\run-1",
                "sourceRepo=C:\\repo\\IronDeveloper",
                "failedStage=apply-preflight",
                "failureReason=validation failed"
            ],
            ReviewChecklist = ["Confirm this skill writes only disposable workspace failure evidence."],
            Blockers = [],
            Warnings = [],
            Interpretation = ["Workspace failure package can mutate disposable workspace evidence only."]
        };

    private static DisposableWorkspaceFailurePackageResult BuildFailurePackageResult(
        string status,
        string? failurePackagePath = "failure-package.json",
        IReadOnlyList<string>? evidencePaths = null,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? warnings = null)
    {
        var resultEvidencePaths = evidencePaths ?? ["workspace.json", "validation.json", "failure-package.json"];
        var resultErrors = errors ?? [];
        var resultWarnings = warnings ?? [];
        return new DisposableWorkspaceFailurePackageResult
        {
            Status = status,
            Summary = status == "succeeded" ? "Workspace failure package created." : "Workspace failure package was not created.",
            ExitCode = status == "succeeded" ? 0 : 1,
            Data = new DisposableWorkspaceFailurePackageData
            {
                RunId = "run-1",
                WorkspacePath = "C:\\workspaces\\run-1",
                SourceRepo = "C:\\repo\\IronDeveloper",
                FailedStage = "apply-preflight",
                SourceRepoMutated = false,
                ApplyCopyAttempted = false,
                ApplyCopySucceeded = false,
                ApplyVerified = false,
                PostApplyValidationSucceeded = false,
                FailureSeverity = "warning",
                RecommendedNextAction = "inspect_evidence_before_retry",
                MissingEvidence = ["diff.json", "promotion-package.json"],
                ExistingEvidencePaths = ["workspace.json", "validation.json"],
                AggregatedErrors = resultErrors,
                AggregatedWarnings = resultWarnings,
                AggregatedBlockers = [],
                RiskNotes = ["Failure package is advisory and does not repair or roll back changes."],
                WorkspaceMetadataPath = "workspace.json",
                FailurePackagePath = failurePackagePath,
                EvidencePaths = resultEvidencePaths,
                Errors = resultErrors,
                Warnings = resultWarnings
            },
            Errors = resultErrors,
            Warnings = resultWarnings
        };
    }

    private static void AssertBlockedBeforeFailurePackage(AgentSkillExecutionResult result)
    {
        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
    }

    private static void AssertWorkspaceLocalFailurePackageOnly(AgentSkillExecutionResult result)
    {
        Assert.IsFalse(result.SourceMutated);
        Assert.IsTrue(result.WorkspaceMutated);
        Assert.IsFalse(result.ExternalSystemCalled);
        Assert.IsFalse(result.TicketCreated);
        Assert.IsFalse(result.MemoryWritten);
        Assert.IsFalse(result.ApprovalGranted);
        Assert.IsFalse(result.ShellCommandRun);
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

    private sealed class FakeDisposableWorkspaceFailurePackageService : IDisposableWorkspaceFailurePackageService
    {
        private readonly DisposableWorkspaceFailurePackageResult _result;

        public FakeDisposableWorkspaceFailurePackageService(DisposableWorkspaceFailurePackageResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public bool ThrowOnCreate { get; init; }

        public DisposableWorkspaceFailurePackageRequest? LastRequest { get; private set; }

        public Task<DisposableWorkspaceFailurePackageResult> CreateAsync(
            DisposableWorkspaceFailurePackageRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            if (ThrowOnCreate)
                throw new InvalidOperationException("Synthetic workspace failure package failure.");

            return Task.FromResult(_result with
            {
                Data = _result.Data with
                {
                    RunId = request.RunId,
                    WorkspacePath = request.WorkspacePath,
                    FailedStage = request.FailedStage
                }
            });
        }
    }
}
