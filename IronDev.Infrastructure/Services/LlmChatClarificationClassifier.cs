using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services;

public sealed class LlmChatClarificationClassifier : IChatClarificationClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILLMService _llm;

    public LlmChatClarificationClassifier(ILLMService llm)
    {
        _llm = llm;
    }

    public async Task<ChatClarificationState> ClassifyAsync(
        ChatClarificationClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var raw = await _llm.GetResponseAsync(BuildPrompt(request), cancellationToken).ConfigureAwait(false);
            return ToClarification(Parse(raw), request);
        }
        catch
        {
            return FallbackFromEvidence(request, "Clarification classifier failed before returning valid JSON.");
        }
    }

    private static string BuildPrompt(ChatClarificationClassificationRequest request)
    {
        var contextQuestions = request.ContextState.ClarificationQuestions.Count == 0
            ? "none"
            : string.Join(" | ", request.ContextState.ClarificationQuestions);

        return $$"""
            Classify whether this chat turn needs a clarification question.

            This classifier owns clarification only. It must not change governance mode.

            Clarification kinds:
            - None: no clarification needed.
            - ProductScope: the user is exploring a broad product/build idea and needs a first slice or scope choice.
            - MissingProjectContext: the assistant needs missing project, repository, file, index, or environment context.
            - GovernanceIntent: the user is ambiguous specifically about committing work into a ticket, saved discussion, decision, or build.
            - SafetyOrRisk: the user request has a safety, blast-radius, permission, destructive-action, or irreversible-risk ambiguity.
            - GeneralScope: clarification is useful but does not fit the other kinds.

            Rules:
            - Product vagueness is ProductScope, not GovernanceIntent.
            - Missing project context is MissingProjectContext, not GovernanceIntent.
            - Only ambiguous commitment language should become GovernanceIntent.
            - Do not ask governance/process questions for ordinary product scoping.
            - Prefer one or two concise questions.
            - Return JSON only.

            JSON shape:
            {
              "required": true,
              "kind": "None | ProductScope | MissingProjectContext | GovernanceIntent | SafetyOrRisk | GeneralScope",
              "questions": ["question"],
              "reason": "short explanation"
            }

            User message:
            {{request.UserMessage}}

            Recent conversation:
            {{(string.IsNullOrWhiteSpace(request.RecentConversationSummary) ? "none" : request.RecentConversationSummary)}}

            Selected governance mode:
            {{request.ModeDecision.Mode}} ({{request.ModeDecision.Confidence:0.00}}) - {{request.ModeDecision.Reason}}

            Project summary:
            {{(string.IsNullOrWhiteSpace(request.ProjectSummary) ? "none" : request.ProjectSummary)}}

            Context state:
            RequiresClarification={{request.ContextState.RequiresClarification}}
            ContextSummary={{(string.IsNullOrWhiteSpace(request.ContextState.ContextSummary) ? "none" : request.ContextState.ContextSummary)}}
            ContextQuestions={{contextQuestions}}

            Route hint:
            RequestKind={{request.RouteHint.RequestKind}}
            NeedsClarification={{request.RouteHint.NeedsClarification}}
            ContextModeHint={{request.RouteHint.ContextModeHint}}
            """;
    }

    private static RawClarificationDecision? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var candidate = ExtractJsonPayload(raw);
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        return JsonSerializer.Deserialize<RawClarificationDecision>(candidate, JsonOptions);
    }

    private static ChatClarificationState ToClarification(
        RawClarificationDecision? raw,
        ChatClarificationClassificationRequest request)
    {
        if (raw is null)
            return FallbackFromEvidence(request, "Clarification classifier did not return parseable JSON.");

        if (!TryParseKind(raw.Kind, out var kind))
            return FallbackFromEvidence(request, "Clarification classifier returned unknown kind.");

        if (kind == ChatClarificationKind.None || raw.Required == false)
            return ChatClarificationState.None;

        var questions = NormalizeQuestions(raw.Questions);
        if (questions.Count == 0)
            questions = DefaultQuestions(kind);

        var reason = string.IsNullOrWhiteSpace(raw.Reason)
            ? $"Clarification classifier selected {kind}."
            : raw.Reason.Trim();

        return new ChatClarificationState(true, kind, questions, reason);
    }

    private static ChatClarificationState FallbackFromEvidence(
        ChatClarificationClassificationRequest request,
        string reason)
    {
        if (request.ModeDecision.Mode == ChatGovernanceMode.Confirmation)
        {
            return new ChatClarificationState(
                true,
                ChatClarificationKind.GovernanceIntent,
                DefaultQuestions(ChatClarificationKind.GovernanceIntent),
                $"Fallback clarification evidence: {reason} Confirmation mode requires an explicit lane question, but this does not mutate mode or gate.");
        }

        var questions = NormalizeQuestions(request.ContextState.ClarificationQuestions);
        if (!request.ContextState.RequiresClarification || questions.Count == 0)
            return ChatClarificationState.None;

        return new ChatClarificationState(
            true,
            ChatClarificationKind.GeneralScope,
            questions,
            $"Fallback clarification evidence: {reason}");
    }

    private static IReadOnlyList<string> NormalizeQuestions(IEnumerable<string>? questions)
    {
        return (questions ?? [])
            .Where(question => !string.IsNullOrWhiteSpace(question))
            .Select(question => question.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> DefaultQuestions(ChatClarificationKind kind) =>
        kind switch
        {
            ChatClarificationKind.ProductScope =>
            [
                "What first useful slice do you want to shape?"
            ],
            ChatClarificationKind.MissingProjectContext =>
            [
                "Which project, repository, or file context should I use?"
            ],
            ChatClarificationKind.GovernanceIntent =>
            [
                "Do you want to keep exploring, or turn this into committed project work?"
            ],
            ChatClarificationKind.SafetyOrRisk =>
            [
                "What blast radius or safety boundary should I preserve?"
            ],
            _ =>
            [
                "What detail should I use before I continue?"
            ]
        };

    private static string ExtractJsonPayload(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            return trimmed;

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        return firstBrace >= 0 && lastBrace > firstBrace
            ? trimmed[firstBrace..(lastBrace + 1)]
            : string.Empty;
    }

    private static bool TryParseKind(string? value, out ChatClarificationKind kind)
    {
        return Enum.TryParse(value, ignoreCase: true, out kind) &&
               Enum.IsDefined(kind);
    }

    private sealed record RawClarificationDecision(
        bool Required,
        string? Kind,
        IReadOnlyList<string>? Questions,
        string? Reason);
}
