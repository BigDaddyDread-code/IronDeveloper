using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Core.Workspaces;
using IronDev.Infrastructure.Services.Agents.Skills;

namespace IronDev.IntegrationTests;

internal static class AgentSkillExecutionTestServices
{
    public static AgentSkillExecutionService Create(
        IAgentWorkspaceApplyContextService? workspaceApplyContextService = null,
        IAgentWorkspaceCheckService? workspaceCheck = null,
        IAgentWorkspacePrepareService? workspacePrepare = null,
        IDisposableWorkspaceValidationService? validation = null,
        IDisposableWorkspaceDiffService? diff = null,
        IDisposableWorkspacePromotionPackageService? promotionPackage = null,
        IDisposableWorkspaceFailurePackageService? failurePackage = null) =>
        new(
            workspaceApplyContextService ?? new AgentSkillExecutionThrowingApplyContextService(),
            workspaceCheck ?? new AgentSkillExecutionThrowingWorkspaceCheckService(),
            workspacePrepare ?? new AgentSkillExecutionThrowingWorkspacePrepareService(),
            validation ?? new AgentSkillExecutionTestValidationService(),
            diff ?? new AgentSkillExecutionTestDiffService(),
            promotionPackage ?? new AgentSkillExecutionTestPromotionPackageService(),
            failurePackage ?? new AgentSkillExecutionTestFailurePackageService());
}

internal sealed class AgentSkillExecutionTestDiffService : IDisposableWorkspaceDiffService
{
    public int CallCount { get; private set; }

    public Task<DisposableWorkspaceDiffResult> DiffAsync(
        DisposableWorkspaceDiffRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(new DisposableWorkspaceDiffResult
        {
            Status = "blocked",
            Summary = "Test diff service should not be called by this test.",
            ExitCode = 1,
            Data = new DisposableWorkspaceDiffData
            {
                RunId = request.RunId,
                WorkspacePath = request.WorkspacePath,
                SourceRepo = string.Empty,
                Changed = false,
                EvidencePaths = [],
                Errors = ["Test diff service should not be called by this test."],
                Warnings = []
            },
            Errors = ["Test diff service should not be called by this test."],
            Warnings = []
        });
    }
}

internal sealed class AgentSkillExecutionTestPromotionPackageService : IDisposableWorkspacePromotionPackageService
{
    public int CallCount { get; private set; }

    public Task<DisposableWorkspacePromotionPackageResult> CreateAsync(
        DisposableWorkspacePromotionPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(new DisposableWorkspacePromotionPackageResult
        {
            Status = "blocked",
            Summary = "Test promotion package service should not be called by this test.",
            ExitCode = 1,
            Data = new DisposableWorkspacePromotionPackageData
            {
                RunId = request.RunId,
                WorkspacePath = request.WorkspacePath,
                SourceRepo = string.Empty,
                ValidationStatus = "not_available",
                ValidationSucceeded = false,
                DiffChanged = false,
                RequiresHumanApproval = true,
                CanApplyToSourceRepo = false,
                AutoPromotionAllowed = false,
                Recommendation = "not_ready_missing_evidence",
                EvidencePaths = [],
                Errors = ["Test promotion package service should not be called by this test."],
                Warnings = []
            },
            Errors = ["Test promotion package service should not be called by this test."],
            Warnings = []
        });
    }
}

internal sealed class AgentSkillExecutionTestFailurePackageService : IDisposableWorkspaceFailurePackageService
{
    public int CallCount { get; private set; }

    public Task<DisposableWorkspaceFailurePackageResult> CreateAsync(
        DisposableWorkspaceFailurePackageRequest request,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(new DisposableWorkspaceFailurePackageResult
        {
            Status = "blocked",
            Summary = "Test failure package service should not be called by this test.",
            ExitCode = 1,
            Data = new DisposableWorkspaceFailurePackageData
            {
                RunId = request.RunId,
                WorkspacePath = request.WorkspacePath,
                SourceRepo = string.Empty,
                FailedStage = request.FailedStage,
                SourceRepoMutated = false,
                ApplyCopyAttempted = false,
                ApplyCopySucceeded = false,
                ApplyVerified = false,
                PostApplyValidationSucceeded = false,
                FailureSeverity = "blocked",
                RecommendedNextAction = "inspect_evidence_before_retry",
                EvidencePaths = [],
                Errors = ["Test failure package service should not be called by this test."],
                Warnings = []
            },
            Errors = ["Test failure package service should not be called by this test."],
            Warnings = []
        });
    }
}

internal sealed class AgentSkillExecutionThrowingApplyContextService : IAgentWorkspaceApplyContextService
{
    public Task<AgentWorkspaceApplyContext> CreateAsync(
        AgentWorkspaceApplyContextRequest request,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Workspace apply context service should not be called by this test.");
}

internal sealed class AgentSkillExecutionThrowingWorkspaceCheckService : IAgentWorkspaceCheckService
{
    public Task<AgentWorkspaceCheckResult> CheckAsync(
        AgentWorkspaceCheckRequest request,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Workspace check service should not be called by this test.");
}

internal sealed class AgentSkillExecutionThrowingWorkspacePrepareService : IAgentWorkspacePrepareService
{
    public Task<AgentWorkspacePrepareResult> PrepareAsync(
        AgentWorkspacePrepareRequest request,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Workspace prepare service should not be called by this test.");
}
