using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentSkillExecutionService : IAgentSkillExecutionService
{
    private const string SupportedSkillId = AgentSkillIds.WorkspaceReadApplyContext;

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

        if (!string.Equals(context.SkillId, SupportedSkillId, StringComparison.Ordinal))
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

            var payload = BuildPayload(workspaceContext);
            var evidencePaths = Merge(context.EvidencePaths, payload.EvidencePaths);
            var warnings = Merge(context.Warnings, payload.Warnings);

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

    private static AgentSkillWorkspaceApplyContextExecutionPayload BuildPayload(AgentWorkspaceApplyContext context)
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

        return new AgentSkillWorkspaceApplyContextExecutionPayload
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
        IReadOnlyList<string> blockers) =>
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
            EvidencePaths = context.EvidencePaths,
            Warnings = context.Warnings,
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
}
