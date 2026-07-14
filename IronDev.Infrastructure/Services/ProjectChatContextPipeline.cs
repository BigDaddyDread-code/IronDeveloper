using IronDev.Core.Interfaces;
using IronDev.Core.Chat;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed record ProjectChatContextPipelineResult(
    Project Project,
    IReadOnlyList<ProjectTicket> Tickets,
    IReadOnlyList<ProjectDecision> Decisions,
    IReadOnlyList<ProjectRule> Rules,
    IReadOnlyList<ProjectContextDocument> Documents,
    IReadOnlyList<MemoryEvidence> SemanticMemoryEvidence,
    ContextAgentRouteDecision RouteDecision,
    IReadOnlyList<string> RouteSignals,
    ContextAgentResult ContextAgentResult,
    EffectiveChatRoute? EffectiveRoute = null,
    IReadOnlyList<ChatDocumentSource>? AttachedDocumentSources = null);

public sealed class ProjectChatContextPipeline
{
    private readonly IProjectService _projects;
    private readonly ITicketService _tickets;
    private readonly IProjectMemoryService _memory;
    private readonly ISemanticMemoryEvidenceProvider _semanticMemoryEvidenceProvider;
    private readonly IContextAgentRouteJudge _routeJudge;
    private readonly IContextAgentService _contextAgent;
    private readonly IProjectMembershipService _projectMembership;

    public ProjectChatContextPipeline(
        IProjectService projects,
        ITicketService tickets,
        IProjectMemoryService memory,
        ISemanticMemoryEvidenceProvider semanticMemoryEvidenceProvider,
        IContextAgentRouteJudge routeJudge,
        IContextAgentService contextAgent,
        IProjectMembershipService projectMembership)
    {
        _projects = projects;
        _tickets = tickets;
        _memory = memory;
        _semanticMemoryEvidenceProvider = semanticMemoryEvidenceProvider;
        _routeJudge = routeJudge;
        _contextAgent = contextAgent;
        _projectMembership = projectMembership;
    }

    public async Task<ProjectChatContextPipelineResult?> RunAsync(
        int projectId,
        long? sessionId,
        string prompt,
        string recentConversationSummary,
        string traceGroupId,
        MemoryRetrievalRequestContext memoryRetrievalContext,
        CancellationToken cancellationToken,
        ChatGovernanceMode? explicitMode = null,
        IReadOnlyList<AttachedChatDocumentContext>? attachedDocumentContexts = null)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;
        if (memoryRetrievalContext is null || memoryRetrievalContext.ProjectId != projectId ||
            memoryRetrievalContext.TenantId != project.TenantId || memoryRetrievalContext.ActorUserId <= 0 ||
            string.IsNullOrWhiteSpace(memoryRetrievalContext.Consumer) ||
            memoryRetrievalContext.AllowedAuthorityClasses.Count == 0 ||
            memoryRetrievalContext.AsOfUtc.Kind != DateTimeKind.Utc ||
            !await _projectMembership.HasAccessAsync(memoryRetrievalContext.TenantId, projectId, memoryRetrievalContext.ActorUserId, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Project chat memory retrieval requires an authorized explicit security context.");

        var contextAgentTickets = await _tickets.GetRecentTicketsAsync(projectId, 20, cancellationToken).ConfigureAwait(false);
        var contextAgentDecisions = await _memory.GetRecentDecisionsAsync(projectId, 20, cancellationToken).ConfigureAwait(false);
        var summaryTickets = contextAgentTickets.Take(5).ToList();
        var summaryDecisions = contextAgentDecisions.Take(5).ToList();
        var rules = await _memory.GetProjectRulesAsync(projectId, cancellationToken).ConfigureAwait(false);
        var projectDocuments = await _memory.GetContextDocumentsAsync(projectId, status: "Active", take: 5, cancellationToken: cancellationToken).ConfigureAwait(false);
        var attachedContexts = attachedDocumentContexts ?? [];
        var documents = attachedContexts
            .Select(item => item.ContextDocument)
            .Concat(projectDocuments)
            .GroupBy(document => document.Id)
            .Select(group => group.First())
            .ToList();
        var semanticMemoryEvidence = await _semanticMemoryEvidenceProvider.GetEvidenceAsync(
            projectId,
            prompt,
            recentConversationSummary,
            cancellationToken).ConfigureAwait(false);

        var routeDecision = await ResolveContextRouteAsync(
            projectId,
            sessionId,
            prompt,
            recentConversationSummary,
            traceGroupId,
            cancellationToken).ConfigureAwait(false);

        var effectiveRoute = EffectiveChatRoute.FromRouteDecision(
            routeDecision,
            explicitMode ?? EffectiveChatRoute.InferMode(routeDecision),
            source: "ProjectChatContextPipeline",
            inputsUsed: BuildEffectiveRouteInputs(routeDecision, recentConversationSummary));
        var routeSignals = BuildRouteSignals(effectiveRoute);

        var contextAgentResult = await RunContextAgentAsync(
            projectId,
            sessionId,
            prompt,
            recentConversationSummary,
            traceGroupId,
            effectiveRoute,
            memoryRetrievalContext,
            contextAgentTickets,
            contextAgentDecisions,
            rules,
            cancellationToken).ConfigureAwait(false);

        return new ProjectChatContextPipelineResult(
            project,
            summaryTickets,
            summaryDecisions,
            rules,
            documents,
            semanticMemoryEvidence,
            routeDecision,
            routeSignals,
            contextAgentResult,
            effectiveRoute,
            attachedContexts.Select(item => item.Source).ToList());
    }

    private async Task<ContextAgentRouteDecision> ResolveContextRouteAsync(
        int projectId,
        long? sessionId,
        string prompt,
        string recentConversationSummary,
        string traceGroupId,
        CancellationToken cancellationToken)
    {
        return await _routeJudge.DecideRouteAsync(new ContextAgentRouteRequest
        {
            TraceGroupId = traceGroupId,
            ProjectId = projectId,
            SessionId = sessionId ?? 0,
            UserRequest = prompt,
            RecentConversationSummary = recentConversationSummary,
            InitialIntentFromPromptContextBuilder = string.Empty
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ContextAgentResult> RunContextAgentAsync(
        int projectId,
        long? sessionId,
        string prompt,
        string recentConversationSummary,
        string traceGroupId,
        EffectiveChatRoute effectiveRoute,
        MemoryRetrievalRequestContext memoryRetrievalContext,
        IReadOnlyList<ProjectTicket> contextAgentTickets,
        IReadOnlyList<ProjectDecision> contextAgentDecisions,
        IReadOnlyList<ProjectRule> rules,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _contextAgent.RunAsync(new ContextAgentRequest
            {
                ProjectId = projectId,
                SessionId = sessionId ?? 0,
                TraceGroupId = traceGroupId,
                UserRequest = prompt,
                RecentConversationSummary = recentConversationSummary,
                EffectiveRoute = effectiveRoute,
                MemoryRetrievalContext = memoryRetrievalContext,
                RecentTickets = contextAgentTickets,
                RecentDecisions = contextAgentDecisions,
                ProjectRules = rules,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return new ContextAgentResult
            {
                IsClarificationRequired = true,
                ClarificationQuestions = ["Context agent pipeline failed before final assembly."],
                AllowsProseResponse = false,
                WasSuccessful = false,
                TraceGroupId = traceGroupId,
                ContextSummary = $"Context agent pipeline failed for project {projectId}.",
                Warnings = "Context agent execution failed."
            };
        }
    }

    private static IReadOnlyList<string> BuildRouteSignals(EffectiveChatRoute effectiveRoute)
    {
        var decision = effectiveRoute.RouteDecision;
        var routeSignals = new List<string>
        {
            $"Effective route source: {effectiveRoute.Source}",
            $"Effective route mode: {effectiveRoute.Mode}",
            $"Context route hint: Kind={decision.RequestKind}",
            $"Route confidence: {decision.Confidence:0.00}",
            $"Route reason: {(string.IsNullOrWhiteSpace(decision.Reason) ? "no explicit reason from router" : decision.Reason)}",
            $"DecisionTagAllowed={effectiveRoute.AllowsDecisionTagOutput}; DecisionCaptureAllowed={effectiveRoute.AllowsDecisionCapture}; TicketDraftingAllowed={effectiveRoute.AllowsTicketDrafting}",
            $"ContextModeHint={decision.ContextModeHint}; allowTicketCreation={decision.AllowTicketCreation}; allowConflictBlocking={decision.AllowConflictBlocking}; allowDeepLookup={decision.AllowDeepLookup}",
            $"Route machinery: UsedConversationResolver={decision.UsedConversationContextResolver}; UsedLlmJudge={decision.UsedLlmJudge}; UsedFallbackRules={decision.UsedFallbackRules}"
        };

        if (decision.NeedsClarification && decision.ClarificationQuestions.Count > 0)
            routeSignals.Add($"Route clarification required: {string.Join(" | ", decision.ClarificationQuestions)}");

        routeSignals.Add(decision.EvidenceUsed.Count > 0
            ? $"Route evidence used: {string.Join(" | ", decision.EvidenceUsed)}"
            : "Route evidence used: none");

        if (decision.Risks.Count > 0)
            routeSignals.Add($"Route risks: {string.Join(" | ", decision.Risks)}");

        return routeSignals;
    }

    private static IReadOnlyList<string> BuildEffectiveRouteInputs(
        ContextAgentRouteDecision decision,
        string recentConversationSummary)
    {
        var inputs = new List<string> { "CurrentPrompt", "ProjectState" };
        if (!string.IsNullOrWhiteSpace(recentConversationSummary))
            inputs.Add("RecentConversationSummary");
        if (decision.EvidenceUsed.Count > 0)
            inputs.AddRange(decision.EvidenceUsed.Select(e => $"RouteEvidence:{e}"));
        return inputs;
    }
}
