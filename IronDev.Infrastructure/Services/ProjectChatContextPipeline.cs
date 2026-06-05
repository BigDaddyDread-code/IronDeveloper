using IronDev.Core.Interfaces;
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
    ContextAgentRouteDecision RouteDecision,
    IReadOnlyList<string> RouteSignals,
    ContextAgentResult ContextAgentResult);

public sealed class ProjectChatContextPipeline
{
    private readonly IProjectService _projects;
    private readonly ITicketService _tickets;
    private readonly IProjectMemoryService _memory;
    private readonly IContextAgentRouteJudge _routeJudge;
    private readonly IContextAgentService _contextAgent;

    public ProjectChatContextPipeline(
        IProjectService projects,
        ITicketService tickets,
        IProjectMemoryService memory,
        IContextAgentRouteJudge routeJudge,
        IContextAgentService contextAgent)
    {
        _projects = projects;
        _tickets = tickets;
        _memory = memory;
        _routeJudge = routeJudge;
        _contextAgent = contextAgent;
    }

    public async Task<ProjectChatContextPipelineResult?> RunAsync(
        int projectId,
        long? sessionId,
        string prompt,
        string recentConversationSummary,
        string traceGroupId,
        CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var contextAgentTickets = await _tickets.GetRecentTicketsAsync(projectId, 20, cancellationToken).ConfigureAwait(false);
        var contextAgentDecisions = await _memory.GetRecentDecisionsAsync(projectId, 20, cancellationToken).ConfigureAwait(false);
        var summaryTickets = contextAgentTickets.Take(5).ToList();
        var summaryDecisions = contextAgentDecisions.Take(5).ToList();
        var rules = await _memory.GetProjectRulesAsync(projectId, cancellationToken).ConfigureAwait(false);
        var documents = await _memory.GetContextDocumentsAsync(projectId, status: "Active", take: 5, cancellationToken: cancellationToken).ConfigureAwait(false);

        var routeDecision = await ResolveContextRouteAsync(
            projectId,
            sessionId,
            prompt,
            recentConversationSummary,
            traceGroupId,
            cancellationToken).ConfigureAwait(false);

        var routeSignals = BuildRouteSignals(routeDecision);

        var contextAgentResult = await RunContextAgentAsync(
            projectId,
            sessionId,
            prompt,
            recentConversationSummary,
            traceGroupId,
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
            routeDecision,
            routeSignals,
            contextAgentResult);
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
                Warnings = "Context agent execution failed.",
                SuggestedActions = []
            };
        }
    }

    private static IReadOnlyList<string> BuildRouteSignals(ContextAgentRouteDecision decision)
    {
        var routeSignals = new List<string>
        {
            $"Context route hint: Kind={decision.RequestKind}",
            $"Route confidence: {decision.Confidence:0.00}",
            $"Route reason: {(string.IsNullOrWhiteSpace(decision.Reason) ? "no explicit reason from router" : decision.Reason)}",
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
}
