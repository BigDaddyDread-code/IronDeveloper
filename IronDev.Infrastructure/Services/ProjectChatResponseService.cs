using System.Text;
using System.IO;
using IronDev.Core;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public interface IProjectChatResponseService
{
    Task<ProjectChatResponseResult?> RespondAsync(
        int projectId,
        string prompt,
        ProjectConversationMode mode = ProjectConversationMode.Exploration,
        IReadOnlyList<string>? routeSignals = null,
        string? dogfoodTraceId = null,
        bool includeDetailedMetadata = false,
        string? recentConversationSummary = null,
        long? sessionId = null,
        CancellationToken cancellationToken = default);
}

public sealed record ProjectChatResponseResult(
    string Response,
    string ContextSummary,
    string LinkedFilePaths,
    string LinkedSymbols,
    string Mode,
    bool ShowGovernanceActions,
    IReadOnlyList<string> GovernanceActions,
    IReadOnlyList<string> ReasoningTrace,
    string? DisambiguationQuestion,
    string? ReasoningSummary,
    string? DogfoodTraceId = null,
    string? DogfoodTracePath = null,
    long? TraceId = null);

public enum ProjectConversationMode
{
    Exploration,
    Formalization,
    Confirmation
}

public sealed class ProjectChatResponseService : IProjectChatResponseService
{
    private readonly IProjectService _projects;
    private readonly ITicketService _tickets;
    private readonly IProjectMemoryService _memory;
    private readonly IContextAgentService _contextAgent;
    private readonly ILLMService _llm;
    private readonly ILlmTraceService _traceService;
    private readonly string _explorationInstructionText;
    private readonly string _formalizationInstructionText;
    private const string FormalizationModeInstructionFile = "FormalizationModeInstructions.md";
    private const string ExplorationModeInstructionFile = "ExplorationModeInstructions.md";
    private const string AgentInstructionDirectory = "agent-instructions";
    private const string DocsDirectory = "Docs";
    private const string FormalizationInstructionFallback =
        "You are in Formalization Mode.\n\nThe user is asking to lock work into artifacts.\n\n- Produce concise, implementation-ready output only.\n- Surface risks, trade-offs, dependencies, assumptions, and test impact.\n- Prefer explicit next actions for ticket/discussion handoff.\n- Do not drift into exploratory chatter when user intent is already committed.";
    private const string ExplorationInstructionFallback =
        "You are in Exploration Mode.\n\nThis is a normal conversation or information-gathering turn.\n\n- Answer the user directly and naturally.\n- Do not try to architect, structure, formalize, or turn the discussion into project artefacts.\n- Do not mention tickets, discussions, saving work, or governance steps unless the user explicitly asks.\n- Stay conversational. Think out loud if it helps. Ask clarifying questions.\n- Keep it lightweight and focused on the current question.\n\nOnly move toward formalization when the user clearly wants to commit something.";

    public ProjectChatResponseService(
        IProjectService projects,
        ITicketService tickets,
        IProjectMemoryService memory,
        IContextAgentService contextAgent,
        ILlmTraceService traceService,
        ILLMService llm)
    {
        _projects = projects;
        _tickets = tickets;
        _memory = memory;
        _contextAgent = contextAgent;
        _traceService = traceService;
        _llm = llm;
        _explorationInstructionText = LoadInstruction(ExplorationModeInstructionFile, ExplorationInstructionFallback);
        _formalizationInstructionText = LoadInstruction(FormalizationModeInstructionFile, FormalizationInstructionFallback);
    }

    public async Task<ProjectChatResponseResult?> RespondAsync(
        int projectId,
        string prompt,
        ProjectConversationMode mode = ProjectConversationMode.Exploration,
        IReadOnlyList<string>? routeSignals = null,
        string? dogfoodTraceId = null,
        bool includeDetailedMetadata = false,
        string? recentConversationSummary = null,
        long? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPrompt = prompt.ReplaceLineEndings(" ").Trim();
        var normalizedSignals = routeSignals?.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];

        var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
            return null;

        var tickets = await _tickets.GetRecentTicketsAsync(projectId, 5, cancellationToken).ConfigureAwait(false);
        var decisions = await _memory.GetRecentDecisionsAsync(projectId, 5, cancellationToken).ConfigureAwait(false);
        var rules = await _memory.GetProjectRulesAsync(projectId, cancellationToken).ConfigureAwait(false);
        var documents = await _memory.GetContextDocumentsAsync(projectId, status: "Active", take: 5, cancellationToken: cancellationToken).ConfigureAwait(false);

        var correlationId = string.IsNullOrWhiteSpace(dogfoodTraceId) ? Guid.NewGuid().ToString("N") : dogfoodTraceId;
        var contextAgentResult = await RunContextAgentAsync(
            projectId,
            sessionId,
            normalizedPrompt,
            recentConversationSummary ?? string.Empty,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        if (contextAgentResult is null)
        {
            return BuildFailureResult(project, mode, tickets, decisions, documents, rules, normalizedSignals, correlationId);
        }

        var responseMode = mode;
        if (contextAgentResult.IsClarificationRequired)
            responseMode = ProjectConversationMode.Confirmation;

        var finalPrompt = contextAgentResult.FinalPrompt ?? string.Empty;
        var response = await BuildAssistantMessageAsync(contextAgentResult, responseMode, finalPrompt, normalizedPrompt, project.Name, cancellationToken).ConfigureAwait(false);

        var reasoningTrace = BuildReasoningTrace(
            contextAgentResult,
            normalizedSignals,
            includeDetailedMetadata,
            responseMode,
            correlationId);
        var tracingSummary = BuildReasoningSummary(contextAgentResult, responseMode, reasoningTrace);

        var linkedFilePaths = DistinctDelimited(
            tickets.Select(t => t.LinkedFilePaths)
                .Concat(decisions.Select(d => d.LinkedFilePaths))
                .Concat(contextAgentResult.Evidence.Select(e => e.FilePath))
        );

        var linkedSymbols = DistinctDelimited(
            tickets.Select(t => t.LinkedSymbols)
                .Concat(decisions.Select(d => d.LinkedSymbols))
                .Concat(contextAgentResult.Evidence.Select(e => e.SymbolName))
        );

        var disambiguationQuestion = contextAgentResult.IsClarificationRequired
            ? BuildDisambiguationQuestion(contextAgentResult.ClarificationQuestions)
            : null;
        var showGovernanceActions = responseMode == ProjectConversationMode.Formalization &&
            !contextAgentResult.IsClarificationRequired &&
            contextAgentResult.AllowsProseResponse;
        var governanceActions = showGovernanceActions && contextAgentResult.SuggestedActions.Length > 0
            ? contextAgentResult.SuggestedActions
            : responseMode == ProjectConversationMode.Formalization
                ? DefaultFormalizationActions
                : Array.Empty<string>();

        return new ProjectChatResponseResult(
            response,
            BuildContextSummary(project, responseMode, tickets, decisions, documents, rules, normalizedSignals, contextAgentResult.ContextSummary),
            string.Join(Environment.NewLine, linkedFilePaths),
            string.Join(Environment.NewLine, linkedSymbols),
            responseMode.ToString(),
            showGovernanceActions,
            governanceActions,
            reasoningTrace,
            disambiguationQuestion,
            tracingSummary,
            correlationId);
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
        ProjectConversationMode mode,
        string finalPrompt,
        string prompt,
        string projectName,
        CancellationToken cancellationToken)
    {
        if (contextAgentResult.IsClarificationRequired)
        {
            var questions = BuildClarificationQuestionBullets(contextAgentResult.ClarificationQuestions);
            return
                $"I can't safely answer yet. I need clarification for this request." +
                $"{Environment.NewLine}{Environment.NewLine}{questions}";
        }

        if (!contextAgentResult.AllowsProseResponse || string.IsNullOrWhiteSpace(finalPrompt))
        {
            return BuildNonProseResponse(contextAgentResult, mode, prompt, projectName);
        }

        try
        {
            var modePrompt = InjectModeInstruction(finalPrompt, mode);
            return (await _llm.GetResponseAsync(modePrompt, cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (Exception ex)
        {
            return BuildFallbackResponse(contextAgentResult, mode, prompt, projectName, ex);
        }
    }

    private string InjectModeInstruction(string finalPrompt, ProjectConversationMode mode)
    {
        var instruction = mode switch
        {
            ProjectConversationMode.Formalization => _formalizationInstructionText,
            ProjectConversationMode.Exploration => _explorationInstructionText,
            ProjectConversationMode.Confirmation => _explorationInstructionText,
            _ => _explorationInstructionText
        };

        return $"{instruction}\n\n{finalPrompt}".Trim();
    }

    private static string LoadInstruction(string fileName, string fallback)
    {
        var filePath = ResolveInstructionPath(fileName);
        if (string.IsNullOrWhiteSpace(filePath))
            return fallback;

        try
        {
            return File.ReadAllText(filePath);
        }
        catch
        {
            return fallback;
        }
    }

    private static string? ResolveInstructionPath(string fileName)
    {
        var roots = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var current = Path.GetFullPath(root);
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(current, DocsDirectory, AgentInstructionDirectory, fileName);
                if (File.Exists(candidate))
                    return candidate;

                var parent = Directory.GetParent(current);
                if (parent is null)
                    break;
                current = parent.FullName;
            }
        }

        return null;
    }

    private static string BuildNonProseResponse(
        ContextAgentResult contextAgentResult,
        ProjectConversationMode mode,
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
        ProjectConversationMode mode,
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

    private ProjectChatResponseResult BuildFailureResult(
        Project project,
        ProjectConversationMode mode,
        IReadOnlyList<ProjectTicket> tickets,
        IReadOnlyList<ProjectDecision> decisions,
        IReadOnlyList<ProjectContextDocument> documents,
        IReadOnlyList<ProjectRule> rules,
        IReadOnlyList<string> normalizedSignals,
        string correlationId)
    {
        var contextSummary = BuildContextSummary(
            project,
            mode,
            tickets,
            decisions,
            documents,
            rules,
            normalizedSignals,
            string.Empty);

        var modeLabel = ModeLabel(mode);
        var failureResponse =
            $"[{modeLabel}] Pipeline assembly failed before response model could be produced." +
            $"{Environment.NewLine}{Environment.NewLine}" +
            $"Correlation: {correlationId}" +
            $"{Environment.NewLine}Route signals: {(normalizedSignals.Count == 0 ? \"none\" : string.Join(\" | \", normalizedSignals))}" +
            $"{Environment.NewLine}Recovery action: retry request after transient dependency recovers or refine scope.";

        return new ProjectChatResponseResult(
            failureResponse,
            contextSummary,
            string.Join(Environment.NewLine, Array.Empty<string>()),
            string.Join(Environment.NewLine, Array.Empty<string>()),
            mode.ToString(),
            false,
            Array.Empty<string>(),
            BuildReasoningTrace(new ContextAgentResult { WasSuccessful = false }, normalizedSignals, includeDetailedMetadata: false, mode, correlationId),
            "Retry with fewer constraints or provide explicit intent.",
            "Response pipeline was not able to run.",
            correlationId);
    }

    private static string ModeLabel(ProjectConversationMode mode) =>
        mode == ProjectConversationMode.Formalization
            ? "Formalization"
            : mode == ProjectConversationMode.Confirmation
                ? "Confirmation"
                : "Exploration";

    private static string BuildModeNextStepHint(ProjectConversationMode mode) =>
        mode == ProjectConversationMode.Formalization
            ? "Next step options: 1) confirm lock-down phrasing, 2) stay in exploration and inspect alternatives, 3) reject command-level lane request."
            : "Next step options: 1) ask follow-up probes, 2) request explicit formalization, 3) provide constraints for a narrower scope.";

    private static List<string> BuildReasoningTrace(
        ContextAgentResult contextAgentResult,
        IReadOnlyList<string> routeSignals,
        bool includeDetailedMetadata,
        ProjectConversationMode mode,
        string traceGroupId)
    {
        var grouped = contextAgentResult.TraceGroupId.Length > 0
            ? contextAgentResult.TraceGroupId
            : traceGroupId;
        var traceLines = new List<string>();

        if (!string.IsNullOrWhiteSpace(grouped))
            traceLines.Add($"Dogfood trace group: {grouped}");

        if (!string.IsNullOrWhiteSpace(contextAgentResult.ContextSummary))
            traceLines.Add(contextAgentResult.ContextSummary);

        if (contextAgentResult.ResultType != ContextAgentResultType.Prompt)
            traceLines.Add($"ResultType={contextAgentResult.ResultType}; WasSuccessful={contextAgentResult.WasSuccessful}");
        if (contextAgentResult.IsClarificationRequired && contextAgentResult.ClarificationQuestions.Count > 0)
            traceLines.Add($"Clarification questions: {string.Join(" | ", contextAgentResult.ClarificationQuestions)}");

        if (routeSignals.Count > 0)
            traceLines.AddRange(routeSignals.Select(signal => $"Route signal: {signal}"));

        if (includeDetailedMetadata && routeSignals.Count == 0)
            traceLines.Add("No explicit route signals were attached for this request.");

        if (traceLines.Count == 0)
            traceLines.Add($"[{mode}] No trace payload captured yet; reasoning path is being assembled.");

        return traceLines;
    }

    private static string BuildReasoningSummary(
        ContextAgentResult contextAgentResult,
        ProjectConversationMode mode,
        IReadOnlyList<string> reasoningTrace)
    {
        var baseReason = mode switch
        {
            ProjectConversationMode.Formalization => "Formalization lane selected; governance actions are available after lane is clear.",
            ProjectConversationMode.Confirmation => "Lane confirmation required before exposing formalization actions.",
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
        ProjectConversationMode mode,
        IReadOnlyList<ProjectTicket> tickets,
        IReadOnlyList<ProjectDecision> decisions,
        IReadOnlyList<ProjectContextDocument> documents,
        IReadOnlyList<ProjectRule> rules,
        IReadOnlyList<string> routeSignals,
        string contextResultSummary)
    {
        var laneLabel = mode == ProjectConversationMode.Formalization
            ? "formalization"
            : mode == ProjectConversationMode.Confirmation
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

    private static IReadOnlyList<string> DefaultFormalizationActions => new[]
    {
        "Save this response as a Discussion.",
        "Create a Ticket from the saved Discussion."
    };
}
