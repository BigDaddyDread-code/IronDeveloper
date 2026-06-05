using System.Text;
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
        ChatClarificationState clarification,
        string finalPrompt,
        string prompt,
        string projectName,
        CancellationToken cancellationToken)
    {
        if (clarification.Required && modeDecision.Mode == ChatGovernanceMode.Exploration)
            return BuildExplorationClarificationResponse(prompt, projectName, clarification);

        if (!contextAgentResult.AllowsProseResponse || string.IsNullOrWhiteSpace(finalPrompt))
            return BuildNonProseResponse(contextAgentResult, modeDecision.Mode, prompt, projectName);

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
            Do not mention governance modes, classifier names, classifier confidence, route hints, gates, or internal policy machinery to the user unless the user explicitly asks how the cockpit made the decision.
            Translate the selected mode into natural user-facing language.

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
            sb.AppendLine($"- Warnings: {contextAgentResult.Warnings}");

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

    public static string BuildExplorationClarificationResponse(
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

    private static string BuildClarificationQuestionBullets(IReadOnlyList<string> questions)
    {
        if (questions.Count == 0)
            return "- Tell me what you want to confirm next.";

        var sb = new StringBuilder();
        foreach (var q in questions)
            sb.AppendLine($"- {q}");
        return sb.ToString();
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
}
