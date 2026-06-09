using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentWorkspaceDiffPackageSkillExecutionServiceTests
{
    [TestMethod]
    public async Task AgentWorkspaceDiffPackageSkillExecution_DiffSucceeded_WritesWorkspaceEvidenceOnly()
    {
        var diff = new FakeDisposableWorkspaceDiffService(BuildDiffResult("succeeded", changed: true));
        var package = new FakeDisposableWorkspacePromotionPackageService(BuildPromotionPackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(diff: diff, promotionPackage: package);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(AgentSkillIds.WorkspaceDiff)));

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        AssertWorkspaceLocalMutationOnly(result);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspaceDiffExecutionPayload));
        var payload = (AgentSkillWorkspaceDiffExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.DiffAttempted);
        Assert.IsTrue(payload.DiffSucceeded);
        Assert.IsTrue(payload.MetadataWritten);
        Assert.IsTrue(payload.Changed);
        Assert.AreEqual(1, payload.AddedCount);
        Assert.AreEqual(1, payload.ModifiedCount);
        Assert.AreEqual(0, payload.DeletedCount);
        CollectionAssert.Contains(payload.EvidencePaths.ToArray(), "diff.json");
        Assert.AreEqual(1, diff.CallCount);
        Assert.AreEqual(0, package.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceDiffPackageSkillExecution_DiffNoChanges_StillWritesDiffEvidence()
    {
        var diff = new FakeDisposableWorkspaceDiffService(BuildDiffResult("succeeded", changed: false, evidencePaths: ["diff.json"]));
        var service = AgentSkillExecutionTestServices.Create(diff: diff);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(AgentSkillIds.WorkspaceDiff)));

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.WorkspaceMutated);
        var payload = (AgentSkillWorkspaceDiffExecutionPayload)result.Payload!;
        Assert.IsFalse(payload.Changed);
        Assert.IsTrue(payload.MetadataWritten);
        Assert.AreEqual(0, payload.AddedCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceDiffPackageSkillExecution_DiffBlockedBeforeMetadata_DoesNotMutateWorkspace()
    {
        var diff = new FakeDisposableWorkspaceDiffService(BuildDiffResult(
            "blocked",
            changed: false,
            diffMetadataPath: null,
            evidencePaths: [],
            errors: ["Workspace preparation metadata was not found."]));
        var service = AgentSkillExecutionTestServices.Create(diff: diff);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(AgentSkillIds.WorkspaceDiff)));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        AssertNoAuthorityFlags(result);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspaceDiffExecutionPayload));
        var payload = (AgentSkillWorkspaceDiffExecutionPayload)result.Payload!;
        Assert.IsFalse(payload.DiffAttempted);
        Assert.IsFalse(payload.MetadataWritten);
        CollectionAssert.Contains(payload.Blockers.ToArray(), "Workspace preparation metadata was not found.");
        Assert.AreEqual(1, diff.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceDiffPackageSkillExecution_DiffServiceThrows_FailsConservatively()
    {
        var diff = new FakeDisposableWorkspaceDiffService(BuildDiffResult("succeeded", changed: true))
        {
            ThrowOnDiff = true
        };
        var service = AgentSkillExecutionTestServices.Create(diff: diff);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(AgentSkillIds.WorkspaceDiff)));

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.SourceMutated);
        Assert.IsTrue(result.WorkspaceMutated);
        Assert.IsFalse(result.ShellCommandRun);
        Assert.IsTrue(result.Warnings.Any(item => item.Contains("Synthetic workspace diff failure.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AgentWorkspaceDiffPackageSkillExecution_PromotionPackageSucceeded_WritesWorkspaceEvidenceOnly()
    {
        var diff = new FakeDisposableWorkspaceDiffService(BuildDiffResult("succeeded", changed: true));
        var package = new FakeDisposableWorkspacePromotionPackageService(BuildPromotionPackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(diff: diff, promotionPackage: package);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(AgentSkillIds.WorkspacePromotionPackage)));

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        AssertWorkspaceLocalMutationOnly(result);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspacePromotionPackageExecutionPayload));
        var payload = (AgentSkillWorkspacePromotionPackageExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.PackageAttempted);
        Assert.IsTrue(payload.PackageCreated);
        Assert.IsTrue(payload.MetadataWritten);
        Assert.AreEqual("ready_for_human_review", payload.Recommendation);
        Assert.IsTrue(payload.RequiresHumanApproval);
        Assert.IsFalse(payload.CanApplyToSourceRepo);
        Assert.IsFalse(payload.AutoPromotionAllowed);
        Assert.AreEqual("validation.json", payload.ValidationReportPath);
        Assert.AreEqual("source-report.json", payload.SourceReportPath);
        CollectionAssert.Contains(payload.EvidencePaths.ToArray(), "promotion-package.json");
        Assert.AreEqual(0, diff.CallCount);
        Assert.AreEqual(1, package.CallCount);
    }

    [DataTestMethod]
    [DataRow("validationReportPath")]
    [DataRow("sourceReportPath")]
    public async Task AgentWorkspaceDiffPackageSkillExecution_PromotionPackageMissingEvidencePath_BlocksBeforeService(string missing)
    {
        var package = new FakeDisposableWorkspacePromotionPackageService(BuildPromotionPackageResult("succeeded"));
        var context = BuildAllowedContext(AgentSkillIds.WorkspacePromotionPackage) with
        {
            ParametersSummary =
            [
                "runId=run-1",
                "workspacePath=C:\\workspaces\\run-1",
                "sourceRepo=C:\\repo\\IronDeveloper"
            ]
        };
        var request = BuildExecutionRequest(context) with
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1",
                ["sourceRepo"] = "C:\\repo\\IronDeveloper",
                ["validationReportPath"] = missing == "validationReportPath" ? string.Empty : "validation.json",
                ["sourceReportPath"] = missing == "sourceReportPath" ? string.Empty : "source-report.json"
            }
        };
        var service = AgentSkillExecutionTestServices.Create(promotionPackage: package);

        var result = await service.ExecuteAsync(request);

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        AssertNoAuthorityFlags(result);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspacePromotionPackageExecutionPayload));
        var payload = (AgentSkillWorkspacePromotionPackageExecutionPayload)result.Payload!;
        Assert.IsFalse(payload.PackageAttempted);
        Assert.IsTrue(payload.Blockers.Any(item => item.Contains(missing, StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, package.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceDiffPackageSkillExecution_PromotionPackageBlockedBeforeMetadata_DoesNotMutateWorkspace()
    {
        var package = new FakeDisposableWorkspacePromotionPackageService(BuildPromotionPackageResult(
            "blocked",
            promotionPackagePath: null,
            evidencePaths: [],
            errors: ["Workspace validation package was not found."]));
        var service = AgentSkillExecutionTestServices.Create(promotionPackage: package);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(AgentSkillIds.WorkspacePromotionPackage)));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.ReadOnlyExecution);
        AssertNoAuthorityFlags(result);
        var payload = (AgentSkillWorkspacePromotionPackageExecutionPayload)result.Payload!;
        Assert.IsFalse(payload.PackageAttempted);
        CollectionAssert.Contains(payload.Blockers.ToArray(), "Workspace validation package was not found.");
        Assert.AreEqual(1, package.CallCount);
    }

    [TestMethod]
    public async Task AgentWorkspaceDiffPackageSkillExecution_PromotionPackageServiceThrows_FailsConservatively()
    {
        var package = new FakeDisposableWorkspacePromotionPackageService(BuildPromotionPackageResult("succeeded"))
        {
            ThrowOnCreate = true
        };
        var service = AgentSkillExecutionTestServices.Create(promotionPackage: package);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(AgentSkillIds.WorkspacePromotionPackage)));

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.SourceMutated);
        Assert.IsTrue(result.WorkspaceMutated);
        Assert.IsFalse(result.ShellCommandRun);
        Assert.IsTrue(result.Warnings.Any(item => item.Contains("Synthetic workspace promotion package failure.", StringComparison.Ordinal)));
    }

    [DataTestMethod]
    [DataRow(AgentSkillIds.WorkspaceDiff)]
    [DataRow(AgentSkillIds.WorkspacePromotionPackage)]
    public async Task AgentWorkspaceDiffPackageSkillExecution_BlocksWhenWorkspaceMutationNotAllowed(string skillId)
    {
        var diff = new FakeDisposableWorkspaceDiffService(BuildDiffResult("succeeded", changed: true));
        var package = new FakeDisposableWorkspacePromotionPackageService(BuildPromotionPackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(diff: diff, promotionPackage: package);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext(skillId) with { WorkspaceMutationAllowed = false }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, diff.CallCount);
        Assert.AreEqual(0, package.CallCount);
    }

    [DataTestMethod]
    [DataRow(AgentSkillIds.WorkspaceApplyCopy)]
    [DataRow(AgentSkillIds.GitCommit)]
    [DataRow(AgentSkillIds.GitHubPullRequestCreate)]
    [DataRow(AgentSkillIds.TicketCreate)]
    [DataRow(AgentSkillIds.MemorySearch)]
    public async Task AgentWorkspaceDiffPackageSkillExecution_StillBlockedSkillsDoNotExecute(string skillId)
    {
        var diff = new FakeDisposableWorkspaceDiffService(BuildDiffResult("succeeded", changed: true));
        var package = new FakeDisposableWorkspacePromotionPackageService(BuildPromotionPackageResult("succeeded"));
        var service = AgentSkillExecutionTestServices.Create(diff: diff, promotionPackage: package);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildAllowedContext(skillId)));

        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, diff.CallCount);
        Assert.AreEqual(0, package.CallCount);
    }

    [TestMethod]
    public void AgentWorkspaceDiffPackageSkillExecutionService_HasNoSourceMutationExternalOrProcessDependencies()
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
        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IGitHub", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("ITicket", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IMemory", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentWorkspaceDiffPackageSkillExecutionService_IsNotWiredIntoAgents()
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
                ["validationReportPath"] = "validation.json",
                ["sourceReportPath"] = "source-report.json"
            }
        };

    private static AgentSkillRequestContext BuildAllowedContext(string skillId) =>
        new()
        {
            ContextId = $"skill-context-{skillId.Replace('.', '-')}",
            RequestId = "skill-request-1",
            ReviewId = "skill-review-1",
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = skillId,
            Purpose = "Run governed workspace diff/package skill.",
            SkillKnown = true,
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            ReviewStatus = AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            RiskTier = string.Equals(skillId, AgentSkillIds.WorkspacePromotionPackage, StringComparison.Ordinal)
                ? ProjectApprovalRiskTiers.WorkspacePackaging
                : ProjectApprovalRiskTiers.WorkspaceReporting,
            Category = AgentSkillCategories.WorkspaceCommand,
            HumanReviewRequired = true,
            HumanApprovalRequired = false,
            PolicyAllowed = true,
            PolicyBlocked = false,
            DangerousCapability = false,
            ExecutionCanStartFromContext = false,
            ApprovalCanBeGrantedByContext = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = string.Equals(skillId, AgentSkillIds.WorkspaceDiff, StringComparison.Ordinal) ||
                string.Equals(skillId, AgentSkillIds.WorkspacePromotionPackage, StringComparison.Ordinal),
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
                "validationReportPath=validation.json",
                "sourceReportPath=source-report.json"
            ],
            ReviewChecklist = ["Confirm this skill writes only disposable workspace evidence."],
            Blockers = [],
            Warnings = [],
            Interpretation = ["Workspace diff/package can mutate disposable workspace evidence only."]
        };

    private static DisposableWorkspaceDiffResult BuildDiffResult(
        string status,
        bool changed,
        string? diffMetadataPath = "diff.json",
        IReadOnlyList<string>? evidencePaths = null,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? warnings = null)
    {
        var resultEvidencePaths = evidencePaths ?? ["diff.json"];
        var addedFiles = changed ? ["new-file.txt"] : Array.Empty<string>();
        var modifiedFiles = changed ? ["modified-file.txt"] : Array.Empty<string>();
        return new DisposableWorkspaceDiffResult
        {
            Status = status,
            Summary = status == "succeeded" ? "Workspace diff completed." : "Workspace diff was blocked.",
            ExitCode = status == "succeeded" ? 0 : 1,
            Data = new DisposableWorkspaceDiffData
            {
                RunId = "run-1",
                WorkspacePath = "C:\\workspaces\\run-1",
                SourceRepo = "C:\\repo\\IronDeveloper",
                Changed = changed,
                AddedFiles = addedFiles,
                ModifiedFiles = modifiedFiles,
                DeletedFiles = [],
                UnchangedFileCount = changed ? 2 : 4,
                DiffMetadataPath = diffMetadataPath,
                EvidencePaths = resultEvidencePaths,
                Errors = errors ?? [],
                Warnings = warnings ?? []
            },
            Errors = errors ?? [],
            Warnings = warnings ?? []
        };
    }

    private static DisposableWorkspacePromotionPackageResult BuildPromotionPackageResult(
        string status,
        string? promotionPackagePath = "promotion-package.json",
        IReadOnlyList<string>? evidencePaths = null,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? warnings = null)
    {
        var resultEvidencePaths = evidencePaths ?? ["workspace.json", "validation.json", "diff.json", "promotion-package.json"];
        return new DisposableWorkspacePromotionPackageResult
        {
            Status = status,
            Summary = status == "succeeded" ? "Workspace promotion package created." : "Workspace promotion package was blocked.",
            ExitCode = status == "succeeded" ? 0 : 1,
            Data = new DisposableWorkspacePromotionPackageData
            {
                RunId = "run-1",
                WorkspacePath = "C:\\workspaces\\run-1",
                SourceRepo = "C:\\repo\\IronDeveloper",
                ValidationStatus = "succeeded",
                ValidationSucceeded = true,
                DiffChanged = true,
                AddedFiles = ["new-file.txt"],
                ModifiedFiles = ["modified-file.txt"],
                DeletedFiles = [],
                RequiresHumanApproval = true,
                CanApplyToSourceRepo = false,
                AutoPromotionAllowed = false,
                Recommendation = status == "succeeded" ? "ready_for_human_review" : "not_ready_missing_evidence",
                PromotionPackagePath = promotionPackagePath,
                EvidencePaths = resultEvidencePaths,
                RiskNotes = ["Promotion package is advisory only and cannot apply changes."],
                Errors = errors ?? [],
                Warnings = warnings ?? []
            },
            Errors = errors ?? [],
            Warnings = warnings ?? []
        };
    }

    private static void AssertWorkspaceLocalMutationOnly(AgentSkillExecutionResult result)
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

    private sealed class FakeDisposableWorkspaceDiffService : IDisposableWorkspaceDiffService
    {
        private readonly DisposableWorkspaceDiffResult _result;

        public FakeDisposableWorkspaceDiffService(DisposableWorkspaceDiffResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public bool ThrowOnDiff { get; init; }

        public Task<DisposableWorkspaceDiffResult> DiffAsync(
            DisposableWorkspaceDiffRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ThrowOnDiff)
                throw new InvalidOperationException("Synthetic workspace diff failure.");

            return Task.FromResult(_result with
            {
                Data = _result.Data with
                {
                    RunId = request.RunId,
                    WorkspacePath = request.WorkspacePath
                }
            });
        }
    }

    private sealed class FakeDisposableWorkspacePromotionPackageService : IDisposableWorkspacePromotionPackageService
    {
        private readonly DisposableWorkspacePromotionPackageResult _result;

        public FakeDisposableWorkspacePromotionPackageService(DisposableWorkspacePromotionPackageResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public bool ThrowOnCreate { get; init; }

        public Task<DisposableWorkspacePromotionPackageResult> CreateAsync(
            DisposableWorkspacePromotionPackageRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ThrowOnCreate)
                throw new InvalidOperationException("Synthetic workspace promotion package failure.");

            return Task.FromResult(_result with
            {
                Data = _result.Data with
                {
                    RunId = request.RunId,
                    WorkspacePath = request.WorkspacePath
                }
            });
        }
    }
}
