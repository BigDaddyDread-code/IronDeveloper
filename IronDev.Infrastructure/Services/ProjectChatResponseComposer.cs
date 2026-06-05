using IronDev.Core;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectChatResponseComposer
{
    private readonly IChatPromptTemplateProvider _promptTemplates;
    private readonly ILLMService _llm;

    public ProjectChatResponseComposer(IChatPromptTemplateProvider promptTemplates, ILLMService llm)
    {
        _promptTemplates = promptTemplates;
        _llm = llm;
    }

    public async Task<string> BuildAsync(
        ContextAgentResult contextAgentResult,
        ChatModeDecision modeDecision,
        ChatContextState contextState,
        string finalPrompt,
        string prompt,
        string recentConversationSummary,
        string projectName,
        CancellationToken cancellationToken)
    {
        var clarification = contextState.ClassifiedClarification ?? ChatClarificationState.None;

        if (modeDecision.Mode == ChatGovernanceMode.Exploration)
        {
            return await BuildExplorationAnswerAsync(
                contextAgentResult,
                clarification,
                finalPrompt,
                prompt,
                recentConversationSummary,
                projectName,
                cancellationToken).ConfigureAwait(false);
        }

        if (!contextAgentResult.AllowsProseResponse)
            return BuildPlainFallbackResponse(modeDecision.Mode, prompt, projectName);

        if (modeDecision.Mode == ChatGovernanceMode.Formalization && IsExplicitDiscussionCaptureRequest(prompt))
            return BuildFormalizationSaveDiscussionResponse(prompt, recentConversationSummary);

        if (modeDecision.Mode == ChatGovernanceMode.Formalization && IsBoundArchitectureCommitRequest(prompt))
            return BuildFormalizationBoundArchitectureResponse(recentConversationSummary);

        if (modeDecision.Mode == ChatGovernanceMode.Formalization && IsArtifactDraftRequest(prompt))
        {
            return await BuildFormalizationArtifactDraftAsync(
                contextAgentResult,
                clarification,
                finalPrompt,
                prompt,
                recentConversationSummary,
                projectName,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(finalPrompt))
            return BuildPlainFallbackResponse(modeDecision.Mode, prompt, projectName);

        try
        {
            var compositionPrompt = BuildCompositionPrompt(finalPrompt, modeDecision);
            var response = (await _llm.GetResponseAsync(compositionPrompt, cancellationToken).ConfigureAwait(false)).Trim();
            return modeDecision.Mode == ChatGovernanceMode.Exploration
                ? StripObviousInternalLeaks(response)
                : response;
        }
        catch (Exception ex)
        {
            return BuildPlainFallbackResponse(modeDecision.Mode, prompt, projectName, ex.Message);
        }
    }

    private async Task<string> BuildExplorationAnswerAsync(
        ContextAgentResult contextAgentResult,
        ChatClarificationState clarification,
        string finalPrompt,
        string prompt,
        string recentConversationSummary,
        string projectName,
        CancellationToken cancellationToken)
    {
        try
        {
            var compositionPrompt = BuildExplorationCompositionPrompt(
                contextAgentResult,
                clarification,
                finalPrompt,
                prompt,
                recentConversationSummary,
                projectName);
            var response = (await _llm.GetResponseAsync(compositionPrompt, cancellationToken).ConfigureAwait(false)).Trim();
            return StripObviousInternalLeaks(response);
        }
        catch (Exception ex)
        {
            return BuildPlainFallbackResponse(ChatGovernanceMode.Exploration, prompt, projectName, ex.Message);
        }
    }

    private async Task<string> BuildFormalizationArtifactDraftAsync(
        ContextAgentResult contextAgentResult,
        ChatClarificationState clarification,
        string finalPrompt,
        string prompt,
        string recentConversationSummary,
        string projectName,
        CancellationToken cancellationToken)
    {
        try
        {
            var compositionPrompt = BuildFormalizationArtifactDraftPrompt(
                contextAgentResult,
                clarification,
                finalPrompt,
                prompt,
                recentConversationSummary,
                projectName);
            var response = (await _llm.GetResponseAsync(compositionPrompt, cancellationToken).ConfigureAwait(false)).Trim();
            return response;
        }
        catch
        {
            return BuildDeterministicArtifactDraft(prompt, recentConversationSummary);
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
            Do not mention governance modes, classifier names, classifier confidence, route hints, gates, or internal policy machinery to the user unless the user explicitly asks how the cockpit made the decision.
            Translate the selected mode into natural user-facing language.
            If the user asks for a recommendation, next slice, suggested first slice, or "what should we do next", answer with a concrete recommendation first. Ask at most one follow-up after the recommendation.
            Use clarification questions to improve the answer, not replace the answer.

            Context-built answer prompt:
            {finalPrompt}
            """.Trim();
    }

    private string BuildExplorationCompositionPrompt(
        ContextAgentResult contextAgentResult,
        ChatClarificationState clarification,
        string finalPrompt,
        string prompt,
        string recentConversationSummary,
        string projectName)
    {
        var modeInstruction = _promptTemplates.GetTemplate(ChatPromptTemplate.Exploration);
        var contextPrompt = string.IsNullOrWhiteSpace(finalPrompt)
            ? BuildThinExplorationPrompt(prompt, recentConversationSummary)
            : finalPrompt.Trim();
        var clarificationCue = BuildClarificationCue(clarification);

        return $"""
            You are writing the assistant's visible reply for a chat turn.

            Exploration instructions:
            {modeInstruction}

            Hard constraints:
            - Answer the user naturally and directly.
            - Use the recent conversation to preserve the current topic.
            - If the user asks for a design, recommendation, or next step, provide one.
            - Clarification state is only a cue. It must not replace the answer.
            - Use clarification questions to improve the answer, not replace the answer.
            - Ask at most one follow-up question, and only after a substantive answer.
            - Do not mention governance modes, classifiers, gates, route hints, audit, traces, tickets, saved discussions, or internal process.
            - Do not ask the user to save, formalize, create a ticket, or record anything unless they explicitly ask.
            - Do not use generic templates.
            - Do not default to "smallest playable loop" unless the current topic is actually a game/slice discussion.

            Project:
            {projectName}

            Current user message:
            {prompt}

            Recent conversation:
            {(string.IsNullOrWhiteSpace(recentConversationSummary) ? "none" : recentConversationSummary)}

            Clarification cue:
            {clarificationCue}

            Context cue:
            {(string.IsNullOrWhiteSpace(contextAgentResult.ContextSummary) ? "none" : contextAgentResult.ContextSummary)}

            Answer cue:
            {contextPrompt}
            """.Trim();
    }

    private string BuildFormalizationArtifactDraftPrompt(
        ContextAgentResult contextAgentResult,
        ChatClarificationState clarification,
        string finalPrompt,
        string prompt,
        string recentConversationSummary,
        string projectName)
    {
        var modeInstruction = _promptTemplates.GetTemplate(ChatPromptTemplate.Formalization);
        var artifactType = InferArtifactType(prompt);

        return $"""
            You are creating a durable project artifact from recent conversation.

            Formalization instructions:
            {modeInstruction}

            Hard constraints:
            - Extract known decisions before asking questions.
            - Do not ask the user to repeat decisions that are already present in the recent conversation.
            - If information is missing, list it under Open Questions.
            - Use the artifact type requested by the user.
            - Ask at most one follow-up after drafting the artifact.
            - Do not say "give me the core outcome" when recent conversation contains usable decisions.

            Artifact type:
            {artifactType}

            Required structure:
            - Decided
            - Open Questions
            - Recommended First Slice
            - Risks / Assumptions

            Project:
            {projectName}

            Current user message:
            {prompt}

            Recent conversation:
            {(string.IsNullOrWhiteSpace(recentConversationSummary) ? "none" : recentConversationSummary)}

            Clarification metadata:
            Required={clarification.Required}
            Kind={clarification.Kind}
            Questions={(clarification.Questions.Count == 0 ? "none" : string.Join(" | ", clarification.Questions))}
            Reason={clarification.Reason}

            Context cue:
            {(string.IsNullOrWhiteSpace(contextAgentResult.ContextSummary) ? "none" : contextAgentResult.ContextSummary)}

            Context-built prompt:
            {(string.IsNullOrWhiteSpace(finalPrompt) ? "none" : finalPrompt)}
            """.Trim();
    }

    private static string BuildThinExplorationPrompt(string prompt, string recentConversationSummary)
    {
        return $"""
            The available context is thin, but still answer usefully.
            Infer the current topic from the user message and recent conversation.
            Make reasonable assumptions and state them briefly.

            User message:
            {prompt}

            Recent conversation:
            {(string.IsNullOrWhiteSpace(recentConversationSummary) ? "none" : recentConversationSummary)}
            """.Trim();
    }

    private static string BuildClarificationCue(ChatClarificationState clarification)
    {
        if (!clarification.Required)
            return "No required clarification. Answer directly.";

        var questions = clarification.Questions.Count == 0
            ? "none"
            : string.Join(" | ", clarification.Questions);

        return $"""
            Clarification may be useful, but it is not a replacement for the answer.
            Kind: {clarification.Kind}
            Suggested questions: {questions}
            If needed, ask at most one targeted follow-up after answering.
            """.Trim();
    }

    private static string BuildPlainFallbackResponse(
        ChatGovernanceMode mode,
        string prompt,
        string projectName,
        string? failure = null)
    {
        if (mode == ChatGovernanceMode.Exploration)
        {
            return $"""
                I can still help from what we have.

                For "{prompt}", I would start by sketching the goal, the safest first behavior, and one small way to test it manually. If this involves generating commands or changing data, keep the first version suggestion-only and require confirmation before anything runs.
                """.Trim();
        }

        if (mode == ChatGovernanceMode.Confirmation)
            return "I can keep exploring this, or we can turn it into committed project work. Which lane do you want?";

        return "I can formalize this once the target outcome and acceptance checks are clear. Give me the core outcome you want locked in first.";
    }

    private static string StripObviousInternalLeaks(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        var lines = response
            .Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => !IsObviousInternalLeak(line))
            .Select(line => line.TrimEnd())
            .ToList();

        return string.Join('\n', lines).Trim();
    }

    private static bool IsObviousInternalLeak(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var normalized = line.Trim().ToLowerInvariant();
        return normalized.StartsWith("governance mode selected", StringComparison.Ordinal) ||
               normalized.StartsWith("classifier reason", StringComparison.Ordinal) ||
               normalized.StartsWith("classifier confidence", StringComparison.Ordinal) ||
               normalized.StartsWith("route hint", StringComparison.Ordinal) ||
               normalized.StartsWith("contextmodehint", StringComparison.Ordinal) ||
               normalized.StartsWith("gate:", StringComparison.Ordinal) ||
               normalized.StartsWith("audit source", StringComparison.Ordinal) ||
               normalized.StartsWith("trace id", StringComparison.Ordinal);
    }

    public static string BuildFormalizationSaveDiscussionResponse(
        string prompt,
        string recentConversationSummary)
    {
        var title = InferDiscussionTitle(prompt, recentConversationSummary);

        return $"""
            Yes. Save this as a Discussion titled "{title}".

            Suggested discussion shape:
            - purpose: capture the current game rules and design direction
            - scope: keep this as discussion/design memory, not a build ticket yet
            - include: core rules, progression, player loop, and open questions
            - next step: once the rules settle, split the discussion into small build tickets

            This is ready for the save-discussion path.
            """.Trim();
    }

    public static string BuildFormalizationBoundArchitectureResponse(string recentConversationSummary)
    {
        var architecture = InferBoundArchitecture(recentConversationSummary);
        return $"""
            Yes. Add {architecture} as the architecture decision for the current project discussion.

            Suggested architecture decision shape:
            - decision: use {architecture}
            - scope: storage/data architecture for the current game design
            - rationale: JSON is fine for static prototype/config data, but {architecture} is the durable path for saves, progression, and queryable game state
            - boundary: this records the architecture choice; implementation tickets can come after the decision is captured

            This is ready for the architecture-decision path.
            """.Trim();
    }

    private static bool IsExplicitDiscussionCaptureRequest(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return false;

        var normalized = prompt.Trim().ToLowerInvariant();
        return normalized.Contains("save this discussion", StringComparison.Ordinal) ||
               normalized.Contains("save the discussion", StringComparison.Ordinal) ||
               normalized.Contains("save discussion", StringComparison.Ordinal) ||
               normalized.Contains("save this chat", StringComparison.Ordinal) ||
               normalized.Contains("save the chat", StringComparison.Ordinal) ||
               normalized.Contains("save this conversation", StringComparison.Ordinal) ||
               normalized.Contains("save the conversation", StringComparison.Ordinal) ||
               normalized.Contains("capture this discussion", StringComparison.Ordinal) ||
               normalized.Contains("capture the discussion", StringComparison.Ordinal) ||
               normalized.Contains("capture discussion", StringComparison.Ordinal) ||
               normalized.Contains("capture this chat", StringComparison.Ordinal) ||
               normalized.Contains("capture the chat", StringComparison.Ordinal) ||
               normalized.Contains("capture this conversation", StringComparison.Ordinal) ||
               normalized.Contains("capture the conversation", StringComparison.Ordinal) ||
               normalized.Contains("record this discussion", StringComparison.Ordinal) ||
               normalized.Contains("record the discussion", StringComparison.Ordinal) ||
               normalized.Contains("record discussion", StringComparison.Ordinal) ||
               normalized.Contains("record this chat", StringComparison.Ordinal) ||
               normalized.Contains("record the chat", StringComparison.Ordinal) ||
               normalized.Contains("record this conversation", StringComparison.Ordinal) ||
               normalized.Contains("record the conversation", StringComparison.Ordinal) ||
               normalized.Contains("record this as", StringComparison.Ordinal) ||
               normalized.Contains("capture this as", StringComparison.Ordinal) ||
               (ContainsAnyAction(normalized, ["save", "capture", "record"]) &&
                ContainsAnyObject(normalized, ["discussion", "conversation", "chat", "decision", "rules", "design"]));
    }

    private static bool IsArtifactDraftRequest(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return false;

        var normalized = prompt.Trim().ToLowerInvariant();
        var artifactLanguage =
            normalized.Contains("document", StringComparison.Ordinal) ||
            normalized.Contains("doc", StringComparison.Ordinal) ||
            normalized.Contains("architecture", StringComparison.Ordinal) ||
            normalized.Contains("artecture", StringComparison.Ordinal) ||
            normalized.Contains("rules", StringComparison.Ordinal) ||
            normalized.Contains("game play", StringComparison.Ordinal) ||
            normalized.Contains("gameplay", StringComparison.Ordinal) ||
            normalized.Contains("discussion", StringComparison.Ordinal);
        var creationLanguage =
            normalized.Contains("create", StringComparison.Ordinal) ||
            normalized.Contains("add", StringComparison.Ordinal) ||
            normalized.Contains("draft", StringComparison.Ordinal) ||
            normalized.Contains("write", StringComparison.Ordinal) ||
            normalized.Contains("make", StringComparison.Ordinal);

        return artifactLanguage && creationLanguage;
    }

    private static string InferArtifactType(string prompt)
    {
        var normalized = (prompt ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Contains("architecture", StringComparison.Ordinal) ||
            normalized.Contains("artecture", StringComparison.Ordinal))
        {
            return "Architecture document";
        }

        if (normalized.Contains("game play", StringComparison.Ordinal) ||
            normalized.Contains("gameplay", StringComparison.Ordinal))
        {
            return "Gameplay document";
        }

        if (normalized.Contains("rules", StringComparison.Ordinal))
            return "Rules document";

        if (normalized.Contains("discussion", StringComparison.Ordinal))
            return "Discussion";

        return "Project artifact";
    }

    private static string BuildDeterministicArtifactDraft(string prompt, string recentConversationSummary)
    {
        var combined = $"{prompt}\n{recentConversationSummary}".ToLowerInvariant();
        if (combined.Contains("fish", StringComparison.Ordinal) ||
            combined.Contains("fishing", StringComparison.Ordinal))
        {
            return BuildFishingArchitectureDraft(combined);
        }

        return """
            Yes. I can draft the artifact from what has already been discussed.

            Decided:
            - The current conversation contains enough direction to start a draft.

            Open Questions:
            - Which details should be treated as fixed decisions versus still-open options?

            Recommended First Slice:
            - Capture the known decisions first, then split implementation work after the artifact is reviewed.

            Risks / Assumptions:
            - I am treating the recent conversation as the source of truth for this draft.
            """.Trim();
    }

    private static string BuildFishingArchitectureDraft(string combined)
    {
        var decided = new List<string>
        {
            "Game: fishing game where fish get smarter each day",
            "AI concept: fish behavior improves over days"
        };

        if (combined.Contains("unity", StringComparison.Ordinal))
            decided.Add("Engine/client: Unity");
        if (combined.Contains("sql sever", StringComparison.Ordinal) ||
            combined.Contains("sql server", StringComparison.Ordinal))
        {
            decided.Add("Backend: SQL Server");
        }
        if (combined.Contains("dapper", StringComparison.Ordinal))
            decided.Add("Data access: Dapper");
        if (combined.Contains("game play", StringComparison.Ordinal) ||
            combined.Contains("gameplay", StringComparison.Ordinal) ||
            combined.Contains("interface", StringComparison.Ordinal))
        {
            decided.Add("Structure: gameplay mechanics separated from interface/UI");
        }
        if (combined.Contains("credit", StringComparison.Ordinal))
            decided.Add("Progression: Fishman/player earns credits");
        if (combined.Contains("gear", StringComparison.Ordinal))
            decided.Add("Economy: credits buy better fishing gear");

        return $"""
            Yes. I would create an architecture document from what is already decided and the questions still needing answers.

            Decided:
            {FormatBullets(decided)}

            Open Questions:
            - How exactly do fish get smarter: bite timing, avoidance, route changes, bait memory, or difficulty scaling?
            - How are credits earned: catching fish, selling fish, surviving days, quests, or combat?
            - What does combat with smart fish mean mechanically?
            - Which gameplay data needs backend persistence?
            - Is SQL Server needed in slice 1, or should the first Unity prototype stay local?
            - What UI screens are needed first?

            Recommended First Slice:
            - Unity scene with one fishing loop
            - one simple fish behavior model
            - credit counter
            - one gear upgrade
            - local save first unless backend persistence is required immediately

            Risks / Assumptions:
            - Start fish intelligence as rules-based, not machine learning.
            - Treat credit as in-game currency/progression.
            - SQL Server + Dapper may be too heavy for slice 1 unless backend persistence is part of the goal.
            """.Trim();
    }

    private static string FormatBullets(IEnumerable<string> items) =>
        string.Join(Environment.NewLine, items.Select(item => $"- {item}"));

    private static bool IsBoundArchitectureCommitRequest(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return false;

        var normalized = prompt.Trim().ToLowerInvariant();
        return normalized.Contains("add that architecture", StringComparison.Ordinal) ||
               normalized.Contains("add that artecture", StringComparison.Ordinal) ||
               normalized.Contains("add this architecture", StringComparison.Ordinal) ||
               normalized.Contains("add this artecture", StringComparison.Ordinal) ||
               normalized.Contains("capture that architecture", StringComparison.Ordinal) ||
               normalized.Contains("capture this architecture", StringComparison.Ordinal) ||
               normalized.Contains("record that architecture", StringComparison.Ordinal) ||
               normalized.Contains("record this architecture", StringComparison.Ordinal);
    }

    private static bool ContainsAnyAction(string normalized, IReadOnlyList<string> actions) =>
        actions.Any(action => normalized.Contains(action, StringComparison.Ordinal));

    private static bool ContainsAnyObject(string normalized, IReadOnlyList<string> objects) =>
        objects.Any(obj => normalized.Contains(obj, StringComparison.Ordinal));

    private static string InferDiscussionTitle(string prompt, string recentConversationSummary)
    {
        var combined = $"{prompt}\n{recentConversationSummary}".ToLowerInvariant();
        if (combined.Contains("rules", StringComparison.Ordinal) && combined.Contains("dragon", StringComparison.Ordinal))
            return "Pet Dragon Game Rules";

        if (combined.Contains("rules", StringComparison.Ordinal) && combined.Contains("game", StringComparison.Ordinal))
            return "Game Rules";

        return "Project Discussion";
    }

    private static string InferBoundArchitecture(string recentConversationSummary)
    {
        var normalized = (recentConversationSummary ?? string.Empty).ToLowerInvariant();
        if (normalized.Contains("sql server", StringComparison.Ordinal) &&
            (normalized.Contains("entity framework", StringComparison.Ordinal) ||
             normalized.Contains("ef core", StringComparison.Ordinal)))
        {
            return "SQL Server + Entity Framework";
        }

        if (normalized.Contains("sql server", StringComparison.Ordinal))
            return "SQL Server";

        if (normalized.Contains("winforms", StringComparison.Ordinal) ||
            normalized.Contains("windows forms", StringComparison.Ordinal))
        {
            return "WinForms";
        }

        return "the selected architecture";
    }

}
