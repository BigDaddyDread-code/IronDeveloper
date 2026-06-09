using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentSkillExecutionService : IAgentSkillExecutionService
{
    private static readonly IReadOnlySet<string> SupportedSkillIds = new HashSet<string>(StringComparer.Ordinal)
    {
        AgentSkillIds.WorkspaceReadApplyContext,
        AgentSkillIds.WorkspaceRecommendApplyAction,
        AgentSkillIds.WorkspaceCreateActionRequest,
        AgentSkillIds.WorkspaceCreateActionReview,
        AgentSkillIds.WorkspaceCheck,
        AgentSkillIds.WorkspacePrepare,
        AgentSkillIds.WorkspaceValidate,
        AgentSkillIds.WorkspaceDiff,
        AgentSkillIds.WorkspacePromotionPackage
    };

    private readonly IAgentWorkspaceApplyContextService _workspaceApplyContextService;
    private readonly IAgentWorkspaceCheckService _workspaceCheckService;
    private readonly IAgentWorkspacePrepareService _workspacePrepareService;
    private readonly IDisposableWorkspaceValidationService _workspaceValidationService;
    private readonly IDisposableWorkspaceDiffService _workspaceDiffService;
    private readonly IDisposableWorkspacePromotionPackageService _workspacePromotionPackageService;

    public AgentSkillExecutionService(
        IAgentWorkspaceApplyContextService workspaceApplyContextService,
        IAgentWorkspaceCheckService workspaceCheckService,
        IAgentWorkspacePrepareService workspacePrepareService,
        IDisposableWorkspaceValidationService workspaceValidationService,
        IDisposableWorkspaceDiffService workspaceDiffService,
        IDisposableWorkspacePromotionPackageService workspacePromotionPackageService)
    {
        _workspaceApplyContextService = workspaceApplyContextService ?? throw new ArgumentNullException(nameof(workspaceApplyContextService));
        _workspaceCheckService = workspaceCheckService ?? throw new ArgumentNullException(nameof(workspaceCheckService));
        _workspacePrepareService = workspacePrepareService ?? throw new ArgumentNullException(nameof(workspacePrepareService));
        _workspaceValidationService = workspaceValidationService ?? throw new ArgumentNullException(nameof(workspaceValidationService));
        _workspaceDiffService = workspaceDiffService ?? throw new ArgumentNullException(nameof(workspaceDiffService));
        _workspacePromotionPackageService = workspacePromotionPackageService ?? throw new ArgumentNullException(nameof(workspacePromotionPackageService));
    }

    public async Task<AgentSkillExecutionResult> ExecuteAsync(
        AgentSkillExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SkillRequestContext);

        var context = request.SkillRequestContext;
        var executionId = BuildExecutionId(context);

        if (!context.SkillKnown)
        {
            return Blocked(
                context,
                executionId,
                AgentSkillExecutionStatuses.BlockedUnknownSkill,
                "Skill is not known to the governed skill registry.",
                ["Skill is unknown."]);
        }

        if (!SupportedSkillIds.Contains(context.SkillId))
        {
            return Blocked(
                context,
                executionId,
                AgentSkillExecutionStatuses.BlockedUnsupportedSkill,
                "Skill is not supported by the read-only execution service.",
                [$"Unsupported read-only skill: {context.SkillId}"]);
        }

        if (context.PolicyBlocked ||
            string.Equals(context.Decision, ProjectApprovalDecisions.BlockedByPolicy, StringComparison.Ordinal))
        {
            return Blocked(
                context,
                executionId,
                AgentSkillExecutionStatuses.BlockedByPolicy,
                "Skill execution was blocked by project policy.",
                Merge(["Project policy blocks this skill."], context.Blockers));
        }

        if (context.DangerousCapability)
        {
            return Blocked(
                context,
                executionId,
                AgentSkillExecutionStatuses.BlockedDangerousCapability,
                "Skill has a dangerous capability and cannot be executed by this read-only service.",
                ["Dangerous skill capability is not executable by this service."]);
        }

        if (context.HumanApprovalRequired)
        {
            return Blocked(
                context,
                executionId,
                AgentSkillExecutionStatuses.BlockedByContext,
                "Skill request context requires separate human approval.",
                ["Human approval is required before execution."]);
        }

        var contextBlockers = ValidateContext(context);
        if (contextBlockers.Count > 0)
        {
            return Blocked(
                context,
                executionId,
                AgentSkillExecutionStatuses.BlockedByContext,
                "Skill request context is not ready for read-only execution.",
                contextBlockers);
        }

        var projectId = FirstNonEmpty(request.ProjectId, context.ProjectId);
        var runId = FirstNonEmpty(request.RunId, TryGetParameter(request.Parameters, "runId"), TryGetSummaryParameter(context.ParametersSummary, "runId"));
        var workspacePath = FirstNonEmpty(
            request.WorkspacePath,
            TryGetParameter(request.Parameters, "workspacePath"),
            TryGetSummaryParameter(context.ParametersSummary, "workspacePath"));
        var sourceRepo = FirstNonEmpty(
            request.SourceRepo,
            TryGetParameter(request.Parameters, "sourceRepo"),
            TryGetSummaryParameter(context.ParametersSummary, "sourceRepo"));
        var validationReportPath = FirstNonEmpty(
            TryGetParameter(request.Parameters, "validationReportPath"),
            TryGetSummaryParameter(context.ParametersSummary, "validationReportPath"));
        var sourceReportPath = FirstNonEmpty(
            TryGetParameter(request.Parameters, "sourceReportPath"),
            TryGetSummaryParameter(context.ParametersSummary, "sourceReportPath"));

        var requestBlockers = new List<string>();
        var workspaceCheckSkill = string.Equals(context.SkillId, AgentSkillIds.WorkspaceCheck, StringComparison.Ordinal);
        var workspacePrepareSkill = string.Equals(context.SkillId, AgentSkillIds.WorkspacePrepare, StringComparison.Ordinal);
        var workspaceValidateSkill = string.Equals(context.SkillId, AgentSkillIds.WorkspaceValidate, StringComparison.Ordinal);
        var workspaceDiffSkill = string.Equals(context.SkillId, AgentSkillIds.WorkspaceDiff, StringComparison.Ordinal);
        var workspacePromotionPackageSkill = string.Equals(context.SkillId, AgentSkillIds.WorkspacePromotionPackage, StringComparison.Ordinal);
        var workspaceCommandSkill = workspaceCheckSkill || workspacePrepareSkill || workspaceValidateSkill || workspaceDiffSkill || workspacePromotionPackageSkill;
        var sourceRepoRequiredSkill = workspaceCheckSkill || workspacePrepareSkill || workspaceDiffSkill || workspacePromotionPackageSkill;
        var profileId = FirstNonEmpty(
            TryGetParameter(request.Parameters, "profileId"),
            TryGetSummaryParameter(context.ParametersSummary, "profileId"),
            "dotnet-build-test");
        if (string.IsNullOrWhiteSpace(projectId))
            requestBlockers.Add(workspaceCommandSkill
                ? $"ProjectId is required for {context.SkillId} execution."
                : "ProjectId is required for workspace apply context execution.");
        if (string.IsNullOrWhiteSpace(runId))
            requestBlockers.Add(workspaceCommandSkill
                ? $"RunId is required for {context.SkillId} execution."
                : "RunId is required for workspace apply context execution.");
        if (string.IsNullOrWhiteSpace(workspacePath))
            requestBlockers.Add(workspaceCommandSkill
                ? $"WorkspacePath is required for {context.SkillId} execution."
                : "WorkspacePath is required for workspace apply context execution.");
        if (sourceRepoRequiredSkill && string.IsNullOrWhiteSpace(sourceRepo))
            requestBlockers.Add($"SourceRepo is required for {context.SkillId} execution.");
        if (workspacePromotionPackageSkill && string.IsNullOrWhiteSpace(validationReportPath))
            requestBlockers.Add("validationReportPath is required for workspace.promotion_package execution.");
        if (workspacePromotionPackageSkill && string.IsNullOrWhiteSpace(sourceReportPath))
            requestBlockers.Add("sourceReportPath is required for workspace.promotion_package execution.");

        if (requestBlockers.Count > 0)
        {
            if (workspacePromotionPackageSkill &&
                !string.IsNullOrWhiteSpace(projectId) &&
                !string.IsNullOrWhiteSpace(runId) &&
                !string.IsNullOrWhiteSpace(workspacePath) &&
                !string.IsNullOrWhiteSpace(sourceRepo))
            {
                return Blocked(
                    context,
                    executionId,
                    AgentSkillExecutionStatuses.BlockedByContext,
                    "workspace.promotion_package request is incomplete.",
                    requestBlockers,
                    payload: BuildPromotionPackageBlockedPayload(
                        projectId!,
                        runId!,
                        workspacePath!,
                        sourceRepo!,
                        validationReportPath,
                        sourceReportPath,
                        context.EvidencePaths,
                        context.Warnings,
                        requestBlockers),
                    readOnlyExecution: false);
            }

            return Blocked(
                context,
                executionId,
                AgentSkillExecutionStatuses.BlockedByContext,
                workspaceCommandSkill
                    ? $"{context.SkillId} request is incomplete."
                    : "Workspace apply context request is incomplete.",
                requestBlockers);
        }

        if (workspaceCheckSkill)
        {
            return await ExecuteWorkspaceCheckAsync(
                context,
                executionId,
                projectId!,
                runId!,
                workspacePath!,
                sourceRepo!,
                cancellationToken).ConfigureAwait(false);
        }

        if (workspacePrepareSkill)
        {
            return await ExecuteWorkspacePrepareAsync(
                context,
                executionId,
                projectId!,
                runId!,
                workspacePath!,
                sourceRepo!,
                cancellationToken).ConfigureAwait(false);
        }

        if (workspaceValidateSkill)
        {
            return await ExecuteWorkspaceValidateAsync(
                context,
                executionId,
                projectId!,
                runId!,
                workspacePath!,
                profileId!,
                cancellationToken).ConfigureAwait(false);
        }

        if (workspaceDiffSkill)
        {
            return await ExecuteWorkspaceDiffAsync(
                context,
                executionId,
                projectId!,
                runId!,
                workspacePath!,
                sourceRepo!,
                cancellationToken).ConfigureAwait(false);
        }

        if (workspacePromotionPackageSkill)
        {
            return await ExecuteWorkspacePromotionPackageAsync(
                context,
                executionId,
                projectId!,
                runId!,
                workspacePath!,
                sourceRepo!,
                validationReportPath!,
                sourceReportPath!,
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var workspaceContext = await _workspaceApplyContextService.CreateAsync(
                new AgentWorkspaceApplyContextRequest
                {
                    ProjectId = projectId!,
                    RunId = runId!,
                    WorkspacePath = workspacePath!
                },
                cancellationToken).ConfigureAwait(false);

            var payloadResult = BuildPayload(context, workspaceContext, request.RequestedByAgent);
            if (payloadResult.Blockers.Count > 0)
            {
                return Blocked(
                    context,
                    executionId,
                    AgentSkillExecutionStatuses.BlockedByContext,
                    payloadResult.Summary,
                    payloadResult.Blockers,
                    Merge(context.EvidencePaths, payloadResult.EvidencePaths),
                    Merge(context.Warnings, payloadResult.Warnings));
            }

            var evidencePaths = Merge(context.EvidencePaths, payloadResult.EvidencePaths);
            var warnings = Merge(context.Warnings, payloadResult.Warnings);

            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = AgentSkillExecutionStatuses.Succeeded,
                Summary = "Read-only skill execution completed.",
                Executed = true,
                ReadOnlyExecution = true,
                SourceMutated = false,
                WorkspaceMutated = false,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = false,
                Payload = payloadResult.Payload,
                EvidencePaths = evidencePaths,
                Warnings = warnings,
                Blockers = []
            };
        }
        catch (Exception exception)
        {
            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = AgentSkillExecutionStatuses.Failed,
                Summary = "Read-only skill execution failed while reading workspace apply context.",
                Executed = false,
                ReadOnlyExecution = true,
                SourceMutated = false,
                WorkspaceMutated = false,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = false,
                Payload = null,
                EvidencePaths = context.EvidencePaths,
                Warnings = Merge(context.Warnings, [$"Workspace apply context read failed: {exception.Message}"]),
                Blockers = []
            };
        }
    }

    private async Task<AgentSkillExecutionResult> ExecuteWorkspaceCheckAsync(
        AgentSkillRequestContext context,
        string executionId,
        string projectId,
        string runId,
        string workspacePath,
        string sourceRepo,
        CancellationToken cancellationToken)
    {
        try
        {
            var check = await _workspaceCheckService.CheckAsync(
                new AgentWorkspaceCheckRequest
                {
                    ProjectId = projectId,
                    RunId = runId,
                    WorkspacePath = workspacePath,
                    SourceRepo = sourceRepo,
                    EvidencePaths = context.EvidencePaths
                },
                cancellationToken).ConfigureAwait(false);

            var evidencePaths = Merge(context.EvidencePaths, check.EvidencePaths);
            var warnings = Merge(context.Warnings, check.Warnings);

            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = AgentSkillExecutionStatuses.Succeeded,
                Summary = "Workspace check skill execution completed.",
                Executed = true,
                ReadOnlyExecution = true,
                SourceMutated = false,
                WorkspaceMutated = false,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = false,
                Payload = new AgentSkillWorkspaceCheckExecutionPayload
                {
                    CheckAvailable = check.CheckAvailable,
                    ProjectId = check.ProjectId,
                    RunId = check.RunId,
                    WorkspacePath = check.WorkspacePath,
                    SourceRepo = check.SourceRepo,
                    SourceRepoExists = check.SourceRepoExists,
                    WorkspacePathExists = check.WorkspacePathExists,
                    WorkspaceInsideAllowedRoot = check.WorkspaceInsideAllowedRoot,
                    SourceAndWorkspaceAreDistinct = check.SourceAndWorkspaceAreDistinct,
                    ReadyForPrepare = check.ReadyForPrepare,
                    EvidencePaths = evidencePaths,
                    Warnings = warnings,
                    Blockers = check.Blockers
                },
                EvidencePaths = evidencePaths,
                Warnings = warnings,
                Blockers = []
            };
        }
        catch (Exception exception)
        {
            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = AgentSkillExecutionStatuses.Failed,
                Summary = "Read-only skill execution failed while running workspace check.",
                Executed = false,
                ReadOnlyExecution = true,
                SourceMutated = false,
                WorkspaceMutated = false,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = false,
                Payload = null,
                EvidencePaths = context.EvidencePaths,
                Warnings = Merge(context.Warnings, [$"Workspace check failed: {exception.Message}"]),
                Blockers = []
            };
        }
    }

    private async Task<AgentSkillExecutionResult> ExecuteWorkspacePrepareAsync(
        AgentSkillRequestContext context,
        string executionId,
        string projectId,
        string runId,
        string workspacePath,
        string sourceRepo,
        CancellationToken cancellationToken)
    {
        try
        {
            var prepare = await _workspacePrepareService.PrepareAsync(
                new AgentWorkspacePrepareRequest
                {
                    ProjectId = projectId,
                    RunId = runId,
                    WorkspacePath = workspacePath,
                    SourceRepo = sourceRepo,
                    EvidencePaths = context.EvidencePaths
                },
                cancellationToken).ConfigureAwait(false);

            var evidencePaths = Merge(context.EvidencePaths, prepare.EvidencePaths);
            var warnings = Merge(context.Warnings, prepare.Warnings);
            var payload = BuildPreparePayload(prepare, evidencePaths, warnings);

            if (!prepare.Prepared)
            {
                return new AgentSkillExecutionResult
                {
                    ExecutionId = executionId,
                    ContextId = context.ContextId,
                    RequestId = context.RequestId,
                    ReviewId = context.ReviewId,
                    SkillId = context.SkillId,
                    Status = prepare.PrepareAttempted
                        ? AgentSkillExecutionStatuses.Failed
                        : AgentSkillExecutionStatuses.BlockedByContext,
                    Summary = prepare.PrepareAttempted
                        ? "Workspace prepare skill execution failed after prepare started."
                        : "Workspace prepare skill execution was blocked before workspace mutation.",
                    Executed = false,
                    ReadOnlyExecution = false,
                    SourceMutated = false,
                    WorkspaceMutated = prepare.PrepareAttempted,
                    ExternalSystemCalled = false,
                    TicketCreated = false,
                    MemoryWritten = false,
                    ApprovalGranted = false,
                    ShellCommandRun = false,
                    Payload = payload,
                    EvidencePaths = evidencePaths,
                    Warnings = warnings,
                    Blockers = prepare.Blockers
                };
            }

            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = AgentSkillExecutionStatuses.Succeeded,
                Summary = "Workspace prepare skill execution completed.",
                Executed = true,
                ReadOnlyExecution = false,
                SourceMutated = false,
                WorkspaceMutated = true,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = false,
                Payload = payload,
                EvidencePaths = evidencePaths,
                Warnings = warnings,
                Blockers = []
            };
        }
        catch (Exception exception)
        {
            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = AgentSkillExecutionStatuses.Failed,
                Summary = "Workspace prepare skill execution failed after prepare was called.",
                Executed = false,
                ReadOnlyExecution = false,
                SourceMutated = false,
                WorkspaceMutated = true,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = false,
                Payload = null,
                EvidencePaths = context.EvidencePaths,
                Warnings = Merge(context.Warnings, [$"Workspace prepare failed: {exception.Message}"]),
                Blockers = []
            };
        }
    }

    private static AgentSkillWorkspacePrepareExecutionPayload BuildPreparePayload(
        AgentWorkspacePrepareResult prepare,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> warnings) =>
        new()
        {
            PrepareAttempted = prepare.PrepareAttempted,
            Prepared = prepare.Prepared,
            ProjectId = prepare.ProjectId,
            RunId = prepare.RunId,
            WorkspacePath = prepare.WorkspacePath,
            SourceRepo = prepare.SourceRepo,
            SourceRepoExists = prepare.SourceRepoExists,
            WorkspacePathExists = prepare.WorkspacePathExists,
            SourceAndWorkspaceAreDistinct = prepare.SourceAndWorkspaceAreDistinct,
            FilesCopied = prepare.FilesCopied,
            DirectoriesCreated = prepare.DirectoriesCreated,
            EvidencePaths = evidencePaths,
            Warnings = warnings,
            Blockers = prepare.Blockers
        };

    private async Task<AgentSkillExecutionResult> ExecuteWorkspaceValidateAsync(
        AgentSkillRequestContext context,
        string executionId,
        string projectId,
        string runId,
        string workspacePath,
        string profileId,
        CancellationToken cancellationToken)
    {
        try
        {
            var validation = await _workspaceValidationService.ValidateAsync(
                new DisposableWorkspaceValidationRequest
                {
                    RunId = runId,
                    WorkspacePath = workspacePath,
                    ProfileId = profileId
                },
                cancellationToken).ConfigureAwait(false);

            var metadataWritten =
                !string.IsNullOrWhiteSpace(validation.Data.ValidationMetadataPath) &&
                validation.Data.EvidencePaths.Contains(validation.Data.ValidationMetadataPath, StringComparer.OrdinalIgnoreCase);
            var hasSteps = validation.Data.Steps.Count > 0;
            var evidencePaths = Merge(context.EvidencePaths, validation.Data.EvidencePaths);
            var warnings = Merge(context.Warnings, validation.Warnings, validation.Data.Warnings);
            var errors = Merge(validation.Errors, validation.Data.Errors);
            var workspaceMutated = metadataWritten || hasSteps || validation.Data.EvidencePaths.Count > 0;
            var shellCommandRun = hasSteps;
            var agentStatus = validation.Status switch
            {
                "succeeded" => AgentSkillExecutionStatuses.Succeeded,
                "failed" => AgentSkillExecutionStatuses.Failed,
                "blocked" => AgentSkillExecutionStatuses.BlockedByContext,
                _ => AgentSkillExecutionStatuses.Failed
            };
            var executed = string.Equals(agentStatus, AgentSkillExecutionStatuses.Succeeded, StringComparison.Ordinal) ||
                string.Equals(agentStatus, AgentSkillExecutionStatuses.Failed, StringComparison.Ordinal) ||
                hasSteps ||
                metadataWritten;
            var payload = BuildValidatePayload(
                validation,
                projectId,
                evidencePaths,
                warnings,
                agentStatus == AgentSkillExecutionStatuses.BlockedByContext ? errors : [],
                metadataWritten,
                validationAttempted: executed || !string.Equals(agentStatus, AgentSkillExecutionStatuses.BlockedByContext, StringComparison.Ordinal));

            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = agentStatus,
                Summary = agentStatus switch
                {
                    AgentSkillExecutionStatuses.Succeeded => "Workspace validate skill execution completed.",
                    AgentSkillExecutionStatuses.BlockedByContext => "Workspace validate skill execution was blocked before validation completed.",
                    _ => "Workspace validate skill execution failed."
                },
                Executed = executed,
                ReadOnlyExecution = false,
                SourceMutated = false,
                WorkspaceMutated = workspaceMutated,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = shellCommandRun,
                Payload = payload,
                EvidencePaths = evidencePaths,
                Warnings = warnings,
                Blockers = agentStatus == AgentSkillExecutionStatuses.BlockedByContext ? errors : []
            };
        }
        catch (Exception exception)
        {
            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = AgentSkillExecutionStatuses.Failed,
                Summary = "Workspace validate skill execution failed after validation was called.",
                Executed = false,
                ReadOnlyExecution = false,
                SourceMutated = false,
                WorkspaceMutated = true,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = true,
                Payload = null,
                EvidencePaths = context.EvidencePaths,
                Warnings = Merge(context.Warnings, [$"Workspace validation failed: {exception.Message}"]),
                Blockers = []
            };
        }
    }

    private async Task<AgentSkillExecutionResult> ExecuteWorkspaceDiffAsync(
        AgentSkillRequestContext context,
        string executionId,
        string projectId,
        string runId,
        string workspacePath,
        string sourceRepo,
        CancellationToken cancellationToken)
    {
        try
        {
            var diff = await _workspaceDiffService.DiffAsync(
                new DisposableWorkspaceDiffRequest
                {
                    RunId = runId,
                    WorkspacePath = workspacePath
                },
                cancellationToken).ConfigureAwait(false);

            var metadataWritten =
                !string.IsNullOrWhiteSpace(diff.Data.DiffMetadataPath) &&
                diff.Data.EvidencePaths.Contains(diff.Data.DiffMetadataPath, StringComparer.OrdinalIgnoreCase);
            var evidencePaths = Merge(context.EvidencePaths, diff.Data.EvidencePaths);
            var warnings = Merge(context.Warnings, diff.Warnings, diff.Data.Warnings);
            var errors = Merge(diff.Errors, diff.Data.Errors);
            var status = MapWorkspaceResultStatus(diff.Status);
            var payload = BuildDiffPayload(
                diff,
                projectId,
                sourceRepo,
                evidencePaths,
                warnings,
                status == AgentSkillExecutionStatuses.BlockedByContext ? errors : [],
                metadataWritten);

            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = status,
                Summary = status switch
                {
                    AgentSkillExecutionStatuses.Succeeded => "Workspace diff skill execution completed.",
                    AgentSkillExecutionStatuses.BlockedByContext => "Workspace diff skill execution was blocked before writing diff evidence.",
                    _ => "Workspace diff skill execution failed."
                },
                Executed = status == AgentSkillExecutionStatuses.Succeeded || metadataWritten,
                ReadOnlyExecution = false,
                SourceMutated = false,
                WorkspaceMutated = metadataWritten,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = false,
                Payload = payload,
                EvidencePaths = evidencePaths,
                Warnings = warnings,
                Blockers = status == AgentSkillExecutionStatuses.BlockedByContext ? errors : []
            };
        }
        catch (Exception exception)
        {
            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = AgentSkillExecutionStatuses.Failed,
                Summary = "Workspace diff skill execution failed after diff was called.",
                Executed = false,
                ReadOnlyExecution = false,
                SourceMutated = false,
                WorkspaceMutated = true,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = false,
                Payload = null,
                EvidencePaths = context.EvidencePaths,
                Warnings = Merge(context.Warnings, [$"Workspace diff failed: {exception.Message}"]),
                Blockers = []
            };
        }
    }

    private async Task<AgentSkillExecutionResult> ExecuteWorkspacePromotionPackageAsync(
        AgentSkillRequestContext context,
        string executionId,
        string projectId,
        string runId,
        string workspacePath,
        string sourceRepo,
        string validationReportPath,
        string sourceReportPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var package = await _workspacePromotionPackageService.CreateAsync(
                new DisposableWorkspacePromotionPackageRequest
                {
                    RunId = runId,
                    WorkspacePath = workspacePath
                },
                cancellationToken).ConfigureAwait(false);

            var metadataWritten =
                !string.IsNullOrWhiteSpace(package.Data.PromotionPackagePath) &&
                package.Data.EvidencePaths.Contains(package.Data.PromotionPackagePath, StringComparer.OrdinalIgnoreCase);
            var evidencePaths = Merge(context.EvidencePaths, package.Data.EvidencePaths);
            var warnings = Merge(context.Warnings, package.Warnings, package.Data.Warnings);
            var errors = Merge(package.Errors, package.Data.Errors);
            var status = MapWorkspaceResultStatus(package.Status);
            var payload = BuildPromotionPackagePayload(
                package,
                projectId,
                sourceRepo,
                validationReportPath,
                sourceReportPath,
                evidencePaths,
                warnings,
                status == AgentSkillExecutionStatuses.BlockedByContext ? errors : [],
                metadataWritten);

            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = status,
                Summary = status switch
                {
                    AgentSkillExecutionStatuses.Succeeded => "Workspace promotion package skill execution completed.",
                    AgentSkillExecutionStatuses.BlockedByContext => "Workspace promotion package skill execution was blocked before writing package evidence.",
                    _ => "Workspace promotion package skill execution failed."
                },
                Executed = status == AgentSkillExecutionStatuses.Succeeded || metadataWritten,
                ReadOnlyExecution = false,
                SourceMutated = false,
                WorkspaceMutated = metadataWritten,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = false,
                Payload = payload,
                EvidencePaths = evidencePaths,
                Warnings = warnings,
                Blockers = status == AgentSkillExecutionStatuses.BlockedByContext ? errors : []
            };
        }
        catch (Exception exception)
        {
            return new AgentSkillExecutionResult
            {
                ExecutionId = executionId,
                ContextId = context.ContextId,
                RequestId = context.RequestId,
                ReviewId = context.ReviewId,
                SkillId = context.SkillId,
                Status = AgentSkillExecutionStatuses.Failed,
                Summary = "Workspace promotion package skill execution failed after package creation was called.",
                Executed = false,
                ReadOnlyExecution = false,
                SourceMutated = false,
                WorkspaceMutated = true,
                ExternalSystemCalled = false,
                TicketCreated = false,
                MemoryWritten = false,
                ApprovalGranted = false,
                ShellCommandRun = false,
                Payload = null,
                EvidencePaths = context.EvidencePaths,
                Warnings = Merge(context.Warnings, [$"Workspace promotion package failed: {exception.Message}"]),
                Blockers = []
            };
        }
    }

    private static AgentSkillWorkspaceValidateExecutionPayload BuildValidatePayload(
        DisposableWorkspaceValidationResult validation,
        string projectId,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> blockers,
        bool metadataWritten,
        bool validationAttempted) =>
        new()
        {
            ValidationAttempted = validationAttempted,
            ValidationSucceeded = validation.Data.Succeeded,
            ProjectId = projectId,
            RunId = validation.Data.RunId,
            WorkspacePath = validation.Data.WorkspacePath,
            ProfileId = validation.Data.ProfileId,
            ValidationStatus = validation.Data.Status,
            ExitCode = validation.ExitCode,
            MetadataWritten = metadataWritten,
            ValidationMetadataPath = validation.Data.ValidationMetadataPath,
            Steps = validation.Data.Steps
                .Select(step => new AgentSkillWorkspaceValidationStepPayload
                {
                    CommandId = step.CommandId,
                    Status = step.Status,
                    ExitCode = step.ExitCode,
                    Succeeded = step.Succeeded,
                    EvidencePaths = step.EvidencePaths,
                    Errors = step.Errors,
                    Warnings = step.Warnings
                })
                .ToArray(),
            EvidencePaths = evidencePaths,
            Errors = Merge(validation.Errors, validation.Data.Errors),
            Warnings = warnings,
            Blockers = blockers
        };

    private static AgentSkillWorkspaceDiffExecutionPayload BuildDiffPayload(
        DisposableWorkspaceDiffResult diff,
        string projectId,
        string sourceRepo,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> blockers,
        bool metadataWritten) =>
        new()
        {
            DiffAttempted = metadataWritten,
            DiffSucceeded = string.Equals(diff.Status, "succeeded", StringComparison.OrdinalIgnoreCase),
            MetadataWritten = metadataWritten,
            ProjectId = projectId,
            RunId = diff.Data.RunId,
            WorkspacePath = diff.Data.WorkspacePath,
            SourceRepo = FirstNonEmpty(diff.Data.SourceRepo, sourceRepo) ?? string.Empty,
            Changed = diff.Data.Changed,
            AddedCount = diff.Data.AddedFiles.Count,
            ModifiedCount = diff.Data.ModifiedFiles.Count,
            DeletedCount = diff.Data.DeletedFiles.Count,
            UnchangedFileCount = diff.Data.UnchangedFileCount,
            DiffMetadataPath = diff.Data.DiffMetadataPath,
            AddedFiles = diff.Data.AddedFiles,
            ModifiedFiles = diff.Data.ModifiedFiles,
            DeletedFiles = diff.Data.DeletedFiles,
            EvidencePaths = evidencePaths,
            Errors = Merge(diff.Errors, diff.Data.Errors),
            Warnings = warnings,
            Blockers = blockers
        };

    private static AgentSkillWorkspacePromotionPackageExecutionPayload BuildPromotionPackagePayload(
        DisposableWorkspacePromotionPackageResult package,
        string projectId,
        string sourceRepo,
        string validationReportPath,
        string sourceReportPath,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> blockers,
        bool metadataWritten) =>
        new()
        {
            PackageAttempted = metadataWritten,
            PackageCreated = string.Equals(package.Status, "succeeded", StringComparison.OrdinalIgnoreCase),
            MetadataWritten = metadataWritten,
            ProjectId = projectId,
            RunId = package.Data.RunId,
            WorkspacePath = package.Data.WorkspacePath,
            SourceRepo = FirstNonEmpty(package.Data.SourceRepo, sourceRepo) ?? string.Empty,
            ValidationReportPath = validationReportPath,
            SourceReportPath = sourceReportPath,
            ValidationStatus = package.Data.ValidationStatus,
            ValidationSucceeded = package.Data.ValidationSucceeded,
            DiffChanged = package.Data.DiffChanged,
            AddedCount = package.Data.AddedFiles.Count,
            ModifiedCount = package.Data.ModifiedFiles.Count,
            DeletedCount = package.Data.DeletedFiles.Count,
            RequiresHumanApproval = package.Data.RequiresHumanApproval,
            CanApplyToSourceRepo = package.Data.CanApplyToSourceRepo,
            AutoPromotionAllowed = package.Data.AutoPromotionAllowed,
            Recommendation = package.Data.Recommendation,
            PromotionPackagePath = package.Data.PromotionPackagePath,
            EvidencePaths = evidencePaths,
            RiskNotes = package.Data.RiskNotes,
            Errors = Merge(package.Errors, package.Data.Errors),
            Warnings = warnings,
            Blockers = blockers
        };

    private static AgentSkillWorkspacePromotionPackageExecutionPayload BuildPromotionPackageBlockedPayload(
        string projectId,
        string runId,
        string workspacePath,
        string sourceRepo,
        string? validationReportPath,
        string? sourceReportPath,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> blockers) =>
        new()
        {
            PackageAttempted = false,
            PackageCreated = false,
            MetadataWritten = false,
            ProjectId = projectId,
            RunId = runId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            ValidationReportPath = validationReportPath,
            SourceReportPath = sourceReportPath,
            ValidationStatus = "not_available",
            ValidationSucceeded = false,
            DiffChanged = false,
            RequiresHumanApproval = true,
            CanApplyToSourceRepo = false,
            AutoPromotionAllowed = false,
            Recommendation = "not_ready_missing_evidence",
            EvidencePaths = evidencePaths,
            RiskNotes = ["Promotion package evidence is missing or unsafe."],
            Errors = blockers,
            Warnings = warnings,
            Blockers = blockers
        };

    private static string MapWorkspaceResultStatus(string status) =>
        status switch
        {
            "succeeded" => AgentSkillExecutionStatuses.Succeeded,
            "blocked" => AgentSkillExecutionStatuses.BlockedByContext,
            _ => AgentSkillExecutionStatuses.Failed
        };

    private static PayloadBuildResult BuildPayload(
        AgentSkillRequestContext skillContext,
        AgentWorkspaceApplyContext workspaceContext,
        string requestedByAgent) =>
        skillContext.SkillId switch
        {
            AgentSkillIds.WorkspaceReadApplyContext => BuildReadApplyContextPayload(workspaceContext),
            AgentSkillIds.WorkspaceRecommendApplyAction => BuildRecommendationPayload(workspaceContext),
            AgentSkillIds.WorkspaceCreateActionRequest => BuildActionRequestPayload(workspaceContext, requestedByAgent),
            AgentSkillIds.WorkspaceCreateActionReview => BuildActionReviewPayload(workspaceContext),
            _ => new PayloadBuildResult
            {
                Summary = "Skill is not supported by the read-only execution service.",
                Blockers = [$"Unsupported read-only skill: {skillContext.SkillId}"]
            }
        };

    private static PayloadBuildResult BuildReadApplyContextPayload(AgentWorkspaceApplyContext context)
    {
        var evidencePaths = Merge(
            context.EvidencePaths,
            context.WorkspaceApply?.EvidencePaths ?? [],
            context.WorkspaceApplyRecommendation?.EvidencePaths ?? [],
            context.WorkspaceApplyActionRequest?.EvidencePaths ?? [],
            context.WorkspaceApplyActionReview?.EvidencePaths ?? [],
            context.WorkspaceApplyPolicyContext?.EvidencePaths ?? []);
        var warnings = Merge(
            context.Warnings,
            context.WorkspaceApply?.Warnings ?? [],
            context.WorkspaceApplyRecommendation?.Warnings ?? [],
            context.WorkspaceApplyActionRequest?.Warnings ?? [],
            context.WorkspaceApplyActionReview?.Warnings ?? [],
            context.WorkspaceApplyPolicyContext?.Warnings ?? []);

        return new PayloadBuildResult
        {
            EvidencePaths = evidencePaths,
            Warnings = warnings,
            Payload = new AgentSkillWorkspaceApplyContextExecutionPayload
            {
                WorkspaceApplyContextAvailable = context.ContextAvailable,
                RunId = context.RunId,
                WorkspacePath = context.WorkspacePath,
                Outcome = context.WorkspaceApply?.Outcome,
                RecommendedAction = context.WorkspaceApplyRecommendation?.RecommendedAction,
                RequestedAction = context.WorkspaceApplyActionRequest?.RequestedAction,
                ReviewStatus = context.WorkspaceApplyActionReview?.ReviewStatus,
                PolicyDecision = context.WorkspaceApplyPolicyContext?.Decision,
                RiskTier = context.WorkspaceApplyPolicyContext?.RiskTier,
                EvidencePaths = evidencePaths,
                Warnings = warnings
            }
        };
    }

    private static PayloadBuildResult BuildRecommendationPayload(AgentWorkspaceApplyContext context)
    {
        if (context.WorkspaceApplyRecommendation is null)
        {
            return new PayloadBuildResult
            {
                Summary = "Workspace apply recommendation was not available.",
                EvidencePaths = context.EvidencePaths,
                Warnings = context.Warnings,
                Blockers = ["Workspace apply recommendation was not available."]
            };
        }

        var recommendation = context.WorkspaceApplyRecommendation;
        var evidencePaths = Merge(context.EvidencePaths, recommendation.EvidencePaths);
        var warnings = Merge(context.Warnings, recommendation.Warnings);

        return new PayloadBuildResult
        {
            EvidencePaths = evidencePaths,
            Warnings = warnings,
            Payload = new AgentSkillWorkspaceApplyRecommendationExecutionPayload
            {
                RecommendationAvailable = true,
                RecommendedAction = recommendation.RecommendedAction,
                Rationale = [recommendation.Reason],
                EvidencePaths = evidencePaths,
                Warnings = warnings,
                RiskNotes = recommendation.RiskNotes
            }
        };
    }

    private static PayloadBuildResult BuildActionRequestPayload(
        AgentWorkspaceApplyContext context,
        string requestedByAgent)
    {
        if (context.WorkspaceApplyActionRequest is null)
        {
            return new PayloadBuildResult
            {
                Summary = "Workspace apply action request was not available.",
                EvidencePaths = context.EvidencePaths,
                Warnings = context.Warnings,
                Blockers = ["Workspace apply action request was not available."]
            };
        }

        var actionRequest = context.WorkspaceApplyActionRequest;
        var evidencePaths = Merge(context.EvidencePaths, actionRequest.EvidencePaths);
        var warnings = Merge(context.Warnings, actionRequest.Warnings);

        return new PayloadBuildResult
        {
            EvidencePaths = evidencePaths,
            Warnings = warnings,
            Payload = new AgentSkillWorkspaceApplyActionRequestExecutionPayload
            {
                ActionRequestAvailable = true,
                RequestedAction = actionRequest.RequestedAction,
                RequestedByAgent = requestedByAgent,
                EvidencePaths = evidencePaths,
                Warnings = warnings,
                RiskNotes = actionRequest.RiskNotes
            }
        };
    }

    private static PayloadBuildResult BuildActionReviewPayload(AgentWorkspaceApplyContext context)
    {
        if (context.WorkspaceApplyActionReview is null)
        {
            return new PayloadBuildResult
            {
                Summary = "Workspace apply action review was not available.",
                EvidencePaths = context.EvidencePaths,
                Warnings = context.Warnings,
                Blockers = ["Workspace apply action review was not available."]
            };
        }

        var actionReview = context.WorkspaceApplyActionReview;
        var evidencePaths = Merge(context.EvidencePaths, actionReview.EvidencePaths);
        var warnings = Merge(context.Warnings, actionReview.Warnings);

        return new PayloadBuildResult
        {
            EvidencePaths = evidencePaths,
            Warnings = warnings,
            Payload = new AgentSkillWorkspaceApplyActionReviewExecutionPayload
            {
                ActionReviewAvailable = true,
                ReviewStatus = actionReview.ReviewStatus,
                SourceRepoMayBeMutated = actionReview.SourceRepoMayBeMutated,
                EvidencePaths = evidencePaths,
                Warnings = warnings,
                RiskNotes = actionReview.RiskNotes
            }
        };
    }

    private static List<string> ValidateContext(AgentSkillRequestContext context)
    {
        var blockers = new List<string>();

        if (!context.PolicyAllowed)
            blockers.Add("PolicyAllowed must be true.");
        if (!string.Equals(context.ReviewStatus, AgentSkillRequestReviewStatuses.ReadyForHumanReview, StringComparison.Ordinal))
            blockers.Add("ReviewStatus must be ready_for_human_review.");
        if (!string.Equals(context.RecommendedNextAction, AgentSkillRequestContextRecommendedActions.ReviewRequest, StringComparison.Ordinal))
            blockers.Add("RecommendedNextAction must be review_request.");
        if (context.ExecutionCanStartFromContext)
            blockers.Add("ExecutionCanStartFromContext must remain false; the context is not execution authority.");
        if (context.ApprovalCanBeGrantedByContext)
            blockers.Add("ApprovalCanBeGrantedByContext must remain false; the context cannot grant approval.");
        if (context.SourceMutationAllowed)
            blockers.Add("Source mutation is not allowed for read-only skill execution.");
        if (string.Equals(context.SkillId, AgentSkillIds.WorkspacePrepare, StringComparison.Ordinal) ||
            string.Equals(context.SkillId, AgentSkillIds.WorkspaceValidate, StringComparison.Ordinal) ||
            string.Equals(context.SkillId, AgentSkillIds.WorkspaceDiff, StringComparison.Ordinal) ||
            string.Equals(context.SkillId, AgentSkillIds.WorkspacePromotionPackage, StringComparison.Ordinal))
        {
            if (!context.WorkspaceMutationAllowed)
                blockers.Add($"WorkspaceMutationAllowed must be true for {context.SkillId} execution.");
        }
        else if (context.WorkspaceMutationAllowed)
        {
            blockers.Add("Workspace mutation is not allowed for read-only skill execution.");
        }
        if (context.ExternalSystemAllowed)
            blockers.Add("External system access is not allowed for read-only skill execution.");
        if (context.CreatesTicketAllowed)
            blockers.Add("Ticket creation is not allowed for read-only skill execution.");
        if (context.WritesMemoryAllowed)
            blockers.Add("Memory writes are not allowed for read-only skill execution.");

        return blockers;
    }

    private static AgentSkillExecutionResult Blocked(
        AgentSkillRequestContext context,
        string executionId,
        string status,
        string summary,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string>? evidencePaths = null,
        IReadOnlyList<string>? warnings = null,
        object? payload = null,
        bool readOnlyExecution = true) =>
        new()
        {
            ExecutionId = executionId,
            ContextId = context.ContextId,
            RequestId = context.RequestId,
            ReviewId = context.ReviewId,
            SkillId = context.SkillId,
            Status = status,
            Summary = summary,
            Executed = false,
            ReadOnlyExecution = readOnlyExecution,
            SourceMutated = false,
            WorkspaceMutated = false,
            ExternalSystemCalled = false,
            TicketCreated = false,
            MemoryWritten = false,
            ApprovalGranted = false,
            ShellCommandRun = false,
            Payload = payload,
            EvidencePaths = evidencePaths ?? context.EvidencePaths,
            Warnings = warnings ?? context.Warnings,
            Blockers = blockers
        };

    private static string BuildExecutionId(AgentSkillRequestContext context) =>
        Sanitize($"skill-execution-{context.ContextId}-{context.SkillId}");

    private static string Sanitize(string value)
    {
        var characters = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray();

        return new string(characters);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? TryGetParameter(IReadOnlyDictionary<string, string> parameters, string key) =>
        parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string? TryGetSummaryParameter(IReadOnlyList<string> parametersSummary, string key)
    {
        var prefix = key + "=";
        var item = parametersSummary.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return item is null ? null : item[prefix.Length..];
    }

    private static IReadOnlyList<string> Merge(params IEnumerable<string>[] values) =>
        values
            .SelectMany(value => value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed record PayloadBuildResult
    {
        public string Summary { get; init; } = "Read-only skill execution completed.";

        public object? Payload { get; init; }

        public IReadOnlyList<string> EvidencePaths { get; init; } = [];

        public IReadOnlyList<string> Warnings { get; init; } = [];

        public IReadOnlyList<string> Blockers { get; init; } = [];
    }
}
