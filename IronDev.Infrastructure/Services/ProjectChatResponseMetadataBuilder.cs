using System.Text;
using IronDev.Core.Chat;
using IronDev.Core.Models;
using IronDev.Data.Models;

namespace IronDev.Infrastructure.Services;

public sealed record ProjectChatResponseMetadata(
    string ContextSummary,
    string? LinkedFilePaths,
    string? LinkedSymbols,
    IReadOnlyList<string> ReasoningTrace,
    string? DisambiguationQuestion,
    string ReasoningSummary);

public sealed class ProjectChatResponseMetadataBuilder
{
    public ProjectChatResponseMetadata Build(
        ProjectChatContextPipelineResult context,
        ChatModeDecision modeDecision,
        string traceGroupId,
        ChatContextState? contextState = null)
    {
        var clarification = contextState?.ClassifiedClarification ?? ChatClarificationState.None;
        var responseMode = modeDecision.Mode;
        var contextSummary = BuildContextSummary(
            context.Project,
            responseMode,
            context.Tickets,
            context.Decisions,
            context.Documents,
            context.Rules,
            context.RouteSignals,
            context.ContextAgentResult.ContextSummary);

        var linkedFilePaths = responseMode == ChatGovernanceMode.Formalization || responseMode == ChatGovernanceMode.Confirmation
            ? string.Join(Environment.NewLine, DistinctDelimited(
                context.Tickets.Select(t => t.LinkedFilePaths)
                    .Concat(context.Decisions.Select(d => d.LinkedFilePaths))
                    .Concat(context.ContextAgentResult.Evidence.Select(e => e.FilePath)
                )))
            : null;

        var linkedSymbols = responseMode == ChatGovernanceMode.Formalization || responseMode == ChatGovernanceMode.Confirmation
            ? string.Join(Environment.NewLine, DistinctDelimited(
                context.Tickets.Select(t => t.LinkedSymbols)
                    .Concat(context.Decisions.Select(d => d.LinkedSymbols))
                    .Concat(context.ContextAgentResult.Evidence.Select(e => e.SymbolName)
                )))
            : null;

        var reasoningTrace = BuildReasoningTrace(
            context.ContextAgentResult,
            context.RouteSignals,
            modeDecision,
            traceGroupId,
            contextState);

        var reasoningSummary = BuildReasoningSummary(context.ContextAgentResult, responseMode, reasoningTrace);
        var disambiguationQuestion = clarification.Required
            ? BuildDisambiguationQuestion(clarification.Questions)
            : null;

        return new ProjectChatResponseMetadata(
            contextSummary,
            linkedFilePaths,
            linkedSymbols,
            reasoningTrace,
            disambiguationQuestion,
            reasoningSummary);
    }

    private static List<string> BuildReasoningTrace(
        ContextAgentResult contextAgentResult,
        IReadOnlyList<string> routeSignals,
        ChatModeDecision modeDecision,
        string traceGroupId,
        ChatContextState? contextState)
    {
        var grouped = contextAgentResult.TraceGroupId.Length > 0
            ? contextAgentResult.TraceGroupId
            : traceGroupId;
        var traceLines = new List<string>();

        if (!string.IsNullOrWhiteSpace(grouped))
            traceLines.Add($"Dogfood trace group: {grouped}");

        traceLines.Add($"Mode classifier: {modeDecision.Mode} ({modeDecision.Confidence:0.00}) - {modeDecision.Reason}");
        var clarification = contextState?.ClassifiedClarification ?? ChatClarificationState.None;
        traceLines.Add(
            $"Clarification classifier: Required={clarification.Required}; Kind={clarification.Kind}; Reason={clarification.Reason ?? "none"}");

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

        if (contextState is not null)
        {
            traceLines.Add($"Context state provenance: origin={contextState.Origin}; fromChatContextState={contextState.Origin == ChatContextStateOrigin.ProjectChatResponseCompiler}");
            if (contextState.SemanticEvidence?.Count > 0)
            {
                traceLines.Add("Memory evidence consumed by classifier:");
                traceLines.AddRange(contextState.SemanticEvidence.Select(
                    e => $"Evidence: SourceId={e.SourceId}; SourceType={e.SourceType}; AuthorityLevel={e.AuthorityLevel}; IsCurrent={e.IsCurrent}; StalenessReason={FormatEvidenceNullable(e.StalenessReason)}; SupersededBy={FormatEvidenceNullable(e.SupersededBySourceId)}; RetrievalTraceId={FormatEvidenceNullable(e.RetrievalTraceId)}; RetrievalRank={FormatEvidenceNullable(e.RetrievalRank?.ToString())}; MatchReason={FormatEvidenceNullable(e.MatchReason)}; FromChatContextState={contextState.Origin == ChatContextStateOrigin.ProjectChatResponseCompiler}; UsedFor={e.UsedFor}; {TruncateText(e.Excerpt, 140)}"));
            }
            else
            {
                traceLines.Add("No semantic memory evidence was supplied to the classifier.");
            }

            if (contextState.AvailableSkillHints?.Count > 0)
            {
                var skills = string.Join(" | ", contextState.AvailableSkillHints.Select(
                    h => $"[{contextState.Origin == ChatContextStateOrigin.ProjectChatResponseCompiler}] {h.SkillId}={h.DisplayName}"));
                traceLines.Add($"Skill availability context: {skills}");
            }

            traceLines.Add(contextState.EpisodicMemoryEnabled
                ? "Episodic memory enabled for this classification."
                : "Episodic memory disabled for this classification.");

            if (contextState.ActiveArtifact is not null)
            {
                traceLines.Add($"Active artifact: {contextState.ActiveArtifact.ArtifactType} {contextState.ActiveArtifact.ArtifactId}");
            }

            if (!string.IsNullOrWhiteSpace(contextState.CurrentUserMessage))
            {
                traceLines.Add($"Current user message in context state: {TruncateText(contextState.CurrentUserMessage, 200)}");
            }
        }

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

    private static string FormatEvidenceNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "none" : value;

    private static IReadOnlyList<string> DistinctDelimited(IEnumerable<string?> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(['\r', '\n', ';', '|', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (text.Length <= maxLength)
            return text;

        return text[..maxLength].TrimEnd() + "...";
    }
}
