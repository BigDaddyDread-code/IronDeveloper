using System.Text;
using IronDev.Core;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectChatResponseService : IProjectChatResponseService
{
    private readonly IProjectService _projects;
    private readonly ITicketService _tickets;
    private readonly IProjectMemoryService _memory;
    private readonly IContextAgentRouteJudge _routeJudge;
    private readonly IContextAgentService _contextAgent;
    private readonly IChatModeClassifier _modeClassifier;
    private readonly IChatClarificationMapper _clarificationMapper;
    private readonly IChatPromptTemplateProvider _promptTemplates;
    private readonly ILLMService _llm;
    private readonly ILlmTraceService _traceService;

    public ProjectChatResponseService(
        IProjectService projects,
        ITicketService tickets,
        IProjectMemoryService memory,
        IContextAgentRouteJudge routeJudge,
        IContextAgentService contextAgent,
        IChatModeClassifier modeClassifier,
        IChatClarificationMapper clarificationMapper,
        IChatPromptTemplateProvider promptTemplates,
        ILlmTraceService traceService,
        ILLMService llm)
    {
        _projects = projects;
        _tickets = tickets;
        _memory = memory;
        _routeJudge = routeJudge;
        _contextAgent = contextAgent;
        _modeClassifier = modeClassifier;
        _clarificationMapper = clarificationMapper;
        _promptTemplates = promptTemplates;
        _traceService = traceService;
        _llm = llm;
    }

    public async Task<ProjectChatResponseResult?> RespondAsync(
        int projectId,
        string prompt,
        ChatGovernanceMode? explicitMode = null,
        string? dogfoodTraceId = null,
        string? recentConversationSummary = null,
        long? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPrompt = prompt.ReplaceLineEndings(" ").Trim();
        var recentSummary = recentConversationSummary ?? string.Empty;

        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var tickets = await _tickets.GetRecentTicketsAsync(projectId, 5, cancellationToken).ConfigureAwait(false);
        var decisions = await _memory.GetRecentDecisionsAsync(projectId, 5, cancellationToken).ConfigureAwait(false);
        var rules = await _memory.GetProjectRulesAsync(projectId, cancellationToken).ConfigureAwait(false);
        var documents = await _memory.GetContextDocumentsAsync(projectId, status: "Active", take: 5, cancellationToken: cancellationToken).ConfigureAwait(false);

        var correlationId = string.IsNullOrWhiteSpace(dogfoodTraceId) ? Guid.NewGuid().ToString("N") : dogfoodTraceId;
        var routeDecision = await ResolveContextRouteAsync(
            projectId,
            sessionId,
            normalizedPrompt,
            recentSummary,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        var routeSignals = BuildRouteSignals(routeDecision);

        var contextAgentResult = await RunContextAgentAsync(
            projectId,
            sessionId,
            normalizedPrompt,
            recentSummary,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        var modeDecision = await _modeClassifier.ClassifyAsync(
            new ChatModeClassificationRequest(
                normalizedPrompt,
                recentSummary,
                routeDecision,
                project.Name,
                contextAgentResult.IsClarificationRequired || routeDecision.NeedsClarification,
                explicitMode),
            cancellationToken).ConfigureAwait(false);

        var clarification = _clarificationMapper.Map(
            new ChatContextState(
                contextAgentResult.IsClarificationRequired || routeDecision.NeedsClarification,
                contextAgentResult.ClarificationQuestions.Concat(routeDecision.ClarificationQuestions).ToList(),
                contextAgentResult.ContextSummary),
            normalizedPrompt);

        var finalPrompt = contextAgentResult.FinalPrompt ?? string.Empty;
        var gate = ChatGovernanceGate.FromDecision(modeDecision);
        var responseMode = modeDecision.Mode;
        var assistantResponse = await BuildAssistantMessageAsync(
            contextAgentResult,
            modeDecision,
            clarification,
            finalPrompt,
            normalizedPrompt,
            project.Name,
            cancellationToken).ConfigureAwait(false);

        var contextSummary = BuildContextSummary(project, responseMode, tickets, decisions, documents, rules, routeSignals, contextAgentResult.ContextSummary);
        var linkedFilePaths = responseMode == ChatGovernanceMode.Formalization || responseMode == ChatGovernanceMode.Confirmation
            ? string.Join(Environment.NewLine, DistinctDelimited(
                tickets.Select(t => t.LinkedFilePaths)
                    .Concat(decisions.Select(d => d.LinkedFilePaths))
                    .Concat(contextAgentResult.Evidence.Select(e => e.FilePath)
                )))
            : null;
        var linkedSymbols = responseMode == ChatGovernanceMode.Formalization || responseMode == ChatGovernanceMode.Confirmation
            ? string.Join(Environment.NewLine, DistinctDelimited(
                tickets.Select(t => t.LinkedSymbols)
                    .Concat(decisions.Select(d => d.LinkedSymbols))
                    .Concat(contextAgentResult.Evidence.Select(e => e.SymbolName)
                )))
            : null;
        var reasoningTrace = BuildReasoningTrace(
                contextAgentResult,
                routeSignals,
                modeDecision,
                correlationId);
        var tracingSummary = BuildReasoningSummary(contextAgentResult, responseMode, reasoningTrace);

        var disambiguationQuestion = clarification.Required
            ? BuildDisambiguationQuestion(clarification.Questions)
            : null;

        return new ProjectChatResponseResult(
            assistantResponse,
            responseMode.ToString(),
            modeDecision.Confidence,
            modeDecision.Reason,
            clarification,
            gate,
            reasoningTrace,
            disambiguationQuestion,
            tracingSummary,
            contextSummary,
            linkedFilePaths,
            linkedSymbols,
            correlationId);
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

    private async Task<ContextAgentResult> RunContextAgentAsync(
        int projectId,
        long? sessionId,
        string prompt,
        string recentConversationSummary,
        string traceGroupId,
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
                RecentTickets = await _tickets.GetRecentTicketsAsync(projectId, 20, cancellationToken).ConfigureAwait(false),
                RecentDecisions = await _memory.GetRecentDecisionsAsync(projectId, 20, cancellationToken).ConfigureAwait(false),
                ProjectRules = await _memory.GetProjectRulesAsync(projectId, cancellationToken).ConfigureAwait(false),
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

    private async Task<string> BuildAssistantMessageAsync(
        ContextAgentResult contextAgentResult,
        ChatModeDecision modeDecision,
        ChatClarificationState clarification,
        string finalPrompt,
        string prompt,
        string projectName,
        CancellationToken cancellationToken)
    {
        if (clarification.Required && modeDecision.Mode == ChatGovernanceMode.Exploration)
            return BuildExplorationClarificationResponse(prompt, projectName, clarification);

        if (!contextAgentResult.AllowsProseResponse || string.IsNullOrWhiteSpace(finalPrompt))
        {
            return BuildNonProseResponse(contextAgentResult, modeDecision.Mode, prompt, projectName);
        }

        try
        {
            var compositionPrompt = BuildCompositionPrompt(finalPrompt, modeDecision);
            return (await _llm.GetResponseAsync(compositionPrompt, cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (Exception ex)
        {
            return BuildFallbackResponse(contextAgentResult, modeDecision.Mode, prompt, projectName, ex);
        }
    }

    private string BuildCompositionPrompt(string finalPrompt, ChatModeDecision modeDecision)
    {
        var modeInstruction = modeDecision.Mode switch
        {
            ChatGovernanceMode.Formalization => _promptTemplates.GetTemplate(ChatPromptTemplate.Formalization),
            ChatGovernanceMode.Confirmation => _promptTemplates.GetTemplate(ChatPromptTemplate.Confirmation),
            _ => _promptTemplates.GetTemplate(ChatPromptTemplate.Exploration)
        };

        return $"""
            Governance mode selected by classifier: {modeDecision.Mode}
            Classifier reason: {modeDecision.Reason}
            Classifier confidence: {modeDecision.Confidence:0.00}

            Mode instructions:
            {modeInstruction}

            You are the response composer.
            Use the selected mode. Do not reclassify the mode. Do not output JSON.

            Context-built answer prompt:
            {finalPrompt}
            """.Trim();
    }

    private static string BuildNonProseResponse(
        ContextAgentResult contextAgentResult,
        ChatGovernanceMode mode,
        string prompt,
        string projectName)
    {
        var lane = ModeLabel(mode);
        var sb = new StringBuilder();
        sb.AppendLine($"[{lane}] Non-prose path triggered.");
        sb.AppendLine();
        sb.AppendLine($"Project: {projectName}");
        sb.AppendLine($"Raw request: {prompt}");

        if (!string.IsNullOrWhiteSpace(contextAgentResult.ActionMessage))
        {
            sb.AppendLine();
            sb.AppendLine("Action guidance from safety lane:");
            sb.AppendLine(contextAgentResult.ActionMessage);
        }

        if (!string.IsNullOrWhiteSpace(contextAgentResult.ContextSummary))
        {
            sb.AppendLine();
            sb.AppendLine("Context trace:");
            sb.AppendLine(contextAgentResult.ContextSummary);
        }

        sb.AppendLine();
        sb.AppendLine("Current lane state:");
        sb.AppendLine($"- WasSuccessful: {contextAgentResult.WasSuccessful}");
        sb.AppendLine($"- ResultType: {contextAgentResult.ResultType}");
        sb.AppendLine($"- RequiresAction: {contextAgentResult.RequiresAction}");
        sb.AppendLine($"- AllowsProseResponse: {contextAgentResult.AllowsProseResponse}");

        if (!string.IsNullOrWhiteSpace(contextAgentResult.Warnings))
        {
            sb.AppendLine($"- Warnings: {contextAgentResult.Warnings}");
        }

        if (contextAgentResult.ResultType == ContextAgentResultType.ActionBlocked)
        {
            sb.AppendLine();
            sb.AppendLine("Trade-off exposed:");
            sb.AppendLine("- I can reason safely and continue in exploration, but I am blocked from producing a write/commit-ready lane response.");
        }

        if (contextAgentResult.Evidence.Count > 0 || contextAgentResult.TicketCandidates.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Evidence summary:");
            sb.AppendLine(
                $"- Evidence items: {contextAgentResult.Evidence.Count}; " +
                $"ticket candidates: {contextAgentResult.TicketCandidates.Count}.");
        }

        sb.AppendLine();
        sb.AppendLine(BuildModeNextStepHint(mode));

        return sb.ToString().Trim();
    }

    private static string BuildFallbackResponse(
        ContextAgentResult contextAgentResult,
        ChatGovernanceMode mode,
        string prompt,
        string projectName,
        Exception ex)
    {
        var modeLabel = ModeLabel(mode);
        var sb = new StringBuilder();
        sb.AppendLine($"[{modeLabel}] LLM completion failed during generated answer step.");
        sb.AppendLine();
        sb.AppendLine($"Project: {projectName}");
        sb.AppendLine($"Prompt: {prompt}");
        sb.AppendLine($"Failure: {ex.Message}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(contextAgentResult.ContextSummary))
        {
            sb.AppendLine("Known context:");
            sb.AppendLine(contextAgentResult.ContextSummary);
            sb.AppendLine();
        }

        sb.AppendLine("Available next moves:");
        sb.AppendLine("- Resend this request with the same lane to capture model retry state.");
        sb.AppendLine("- Switch to explicit formalization only after scope, outcomes, and acceptance checks are locked.");
        sb.AppendLine("- Ask for one assumption to be tested before I continue.");
        sb.AppendLine();
        sb.AppendLine("This is still a real, inspectable failure path; it preserves the trace and does not switch to hidden process mode.");
        return sb.ToString().Trim();
    }

    private static string BuildClarificationQuestionBullets(IReadOnlyList<string> questions)
    {
        if (questions.Count == 0)
            return "- Tell me what you want to confirm next.";

        var sb = new StringBuilder();
        foreach (var q in questions)
            sb.AppendLine($"- {q}");
        return sb.ToString();
    }

    private static string BuildExplorationClarificationResponse(
        string prompt,
        string projectName,
        ChatClarificationState clarification)
    {
        var sb = new StringBuilder();
        if (clarification.Kind == ChatClarificationKind.ProductScope)
        {
            sb.AppendLine("Nice. This is a bigger build, so the first useful step is choosing a small playable slice.");
            sb.AppendLine();
            sb.AppendLine("What do you want to shape first?");
            sb.Append(BuildClarificationQuestionBullets(clarification.Questions));
            return sb.ToString().Trim();
        }

        sb.AppendLine("I can keep going, but one missing detail would make the answer more useful.");
        sb.AppendLine();
        sb.Append(BuildClarificationQuestionBullets(clarification.Questions));
        return sb.ToString().Trim();
    }

    private static string BuildDisambiguationQuestion(IReadOnlyList<string> questions)
    {
        if (questions.Count == 0)
            return "Tell me which lane to lock: exploration reasoning or formalization handoff.";

        if (questions.Count == 1)
            return questions[0];

        var sb = new StringBuilder();
        sb.AppendLine("I need clarification before I can proceed:");
        foreach (var question in questions)
        {
            sb.Append("- ").Append(question).AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string ModeLabel(ChatGovernanceMode mode) =>
        mode == ChatGovernanceMode.Formalization
            ? "Formalization"
            : mode == ChatGovernanceMode.Confirmation
                ? "Confirmation"
                : "Exploration";

    private static string BuildModeNextStepHint(ChatGovernanceMode mode) =>
        mode == ChatGovernanceMode.Formalization
            ? "Next step options: 1) confirm lock-down phrasing, 2) stay in exploration and inspect alternatives, 3) reject command-level lane request."
            : "Next step options: 1) ask follow-up probes, 2) request explicit formalization, 3) provide constraints for a narrower scope.";

    private static List<string> BuildReasoningTrace(
        ContextAgentResult contextAgentResult,
        IReadOnlyList<string> routeSignals,
        ChatModeDecision modeDecision,
        string traceGroupId)
    {
        var grouped = contextAgentResult.TraceGroupId.Length > 0
            ? contextAgentResult.TraceGroupId
            : traceGroupId;
        var traceLines = new List<string>();

        if (!string.IsNullOrWhiteSpace(grouped))
            traceLines.Add($"Dogfood trace group: {grouped}");

        traceLines.Add($"Mode classifier: {modeDecision.Mode} ({modeDecision.Confidence:0.00}) - {modeDecision.Reason}");

        if (!string.IsNullOrWhiteSpace(contextAgentResult.ContextSummary))
            traceLines.Add(contextAgentResult.ContextSummary);

        if (contextAgentResult.ResultType != ContextAgentResultType.Prompt)
            traceLines.Add($"ResultType={contextAgentResult.ResultType}; WasSuccessful={contextAgentResult.WasSuccessful}");
        if (contextAgentResult.IsClarificationRequired && contextAgentResult.ClarificationQuestions.Count > 0)
            traceLines.Add($"Clarification questions: {string.Join(" | ", contextAgentResult.ClarificationQuestions)}");

        if (routeSignals.Count > 0)
            traceLines.AddRange(routeSignals.Select(signal => $"Route signal: {signal}"));

        if (routeSignals.Count == 0)
            traceLines.Add("No explicit route signals were attached for this request.");

        if (traceLines.Count == 0)
            traceLines.Add($"[{modeDecision.Mode}] No trace payload captured yet; reasoning path is being assembled.");

        return traceLines;
    }

    private static string BuildReasoningSummary(
        ContextAgentResult contextAgentResult,
        ChatGovernanceMode mode,
        IReadOnlyList<string> reasoningTrace)
    {
        var baseReason = mode switch
        {
            ChatGovernanceMode.Formalization => "Formalization lane selected; governance actions are available after lane is clear.",
            ChatGovernanceMode.Confirmation => "Lane confirmation required before exposing formalization actions.",
            _ => "Exploration lane selected; governance actions stay suppressed."
        };

        var warnings = string.IsNullOrWhiteSpace(contextAgentResult.Warnings)
            ? string.Empty
            : $" Warnings: {contextAgentResult.Warnings}.";

        var detail = contextAgentResult.WasSuccessful ? "" : " Context pipeline marked incomplete.";
        return $"{baseReason} Trace entries: {reasoningTrace.Count}. {detail}{warnings}";
    }

    private static string BuildContextSummary(
        Project project,
        ChatGovernanceMode mode,
        IReadOnlyList<ProjectTicket> tickets,
        IReadOnlyList<ProjectDecision> decisions,
        IReadOnlyList<ProjectContextDocument> documents,
        IReadOnlyList<ProjectRule> rules,
        IReadOnlyList<string> routeSignals,
        string contextResultSummary)
    {
        var laneLabel = mode == ChatGovernanceMode.Formalization
            ? "formalization"
            : mode == ChatGovernanceMode.Confirmation
                ? "confirmation"
                : "exploration";

        var signalSummary = routeSignals.Count == 0
            ? "No route signals were attached."
            : $"Route signal count: {routeSignals.Count}.";

        var contextSummary =
            $"{project.Name}: {laneLabel} lane using project context " +
            $"(tickets={tickets.Count}, decisions={decisions.Count}, documents={documents.Count}, rules={rules.Count}). " +
            signalSummary;

        if (!string.IsNullOrWhiteSpace(contextResultSummary))
            return $"{contextSummary} {contextResultSummary}".Trim();
        return contextSummary;
    }

    private static IReadOnlyList<string> DistinctDelimited(IEnumerable<string?> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(['\r', '\n', ';', '|', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

}
