using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentSkillExecutionService : IAgentSkillExecutionService
{
    private static readonly IReadOnlySet<string> SupportedSkillIds = new HashSet<string>(StringComparer.Ordinal)
    {
        AgentSkillIds.WorkspaceReadApplyContext,
        AgentSkillIds.WorkspaceRecommendApplyAction,
        AgentSkillIds.WorkspaceCreateActionRequest,
        AgentSkillIds.WorkspaceCreateActionReview
    };

    private readonly IAgentWorkspaceApplyContextService _workspaceApplyContextService;

    public AgentSkillExecutionService(IAgentWorkspaceApplyContextService workspaceApplyContextService)
    {
        _workspaceApplyContextService = workspaceApplyContextService ?? throw new ArgumentNullException(nameof(workspaceApplyContextService));
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

        var requestBlockers = new List<string>();
        if (string.IsNullOrWhiteSpace(projectId))
            requestBlockers.Add("ProjectId is required for workspace apply context execution.");
        if (string.IsNullOrWhiteSpace(runId))
            requestBlockers.Add("RunId is required for workspace apply context execution.");
        if (string.IsNullOrWhiteSpace(workspacePath))
            requestBlockers.Add("WorkspacePath is required for workspace apply context execution.");

        if (requestBlockers.Count > 0)
        {
            return Blocked(
                context,
                executionId,
                AgentSkillExecutionStatuses.BlockedByContext,
                "Workspace apply context request is incomplete.",
                requestBlockers);
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
        if (context.WorkspaceMutationAllowed)
            blockers.Add("Workspace mutation is not allowed for read-only skill execution.");
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
        IReadOnlyList<string>? warnings = null) =>
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
            ReadOnlyExecution = true,
            SourceMutated = false,
            WorkspaceMutated = false,
            ExternalSystemCalled = false,
            TicketCreated = false,
            MemoryWritten = false,
            ApprovalGranted = false,
            ShellCommandRun = false,
            Payload = null,
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
