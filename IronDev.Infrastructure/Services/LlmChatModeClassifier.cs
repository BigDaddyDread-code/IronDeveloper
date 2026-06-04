using System.Text.Json;
using IronDev.Core;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services;

public sealed class LlmChatModeClassifier : IChatModeClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILLMService _llm;

    public LlmChatModeClassifier(ILLMService llm)
    {
        _llm = llm;
    }

    public async Task<ChatModeDecision> ClassifyAsync(
        ChatModeClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var raw = await _llm.GetResponseAsync(BuildPrompt(request), cancellationToken).ConfigureAwait(false);
            var parsed = ParseStructuredDecision(raw);
            return ToDecision(parsed);
        }
        catch
        {
            return FailClosed("Classifier failed before returning a valid mode decision.");
        }
    }

    private static string BuildPrompt(ChatModeClassificationRequest request)
    {
        var hint = request.RouteHint;
        var explicitConstraint = request.ExplicitMode.HasValue
            ? request.ExplicitMode.Value.ToString()
            : "none";

        return $$"""
            Classify this assistant turn into exactly one governance mode.

            Modes:

            Exploration:
            The user is exploring, asking questions, brainstorming, clarifying, testing behavior, or discussing options.
            Product vagueness and missing scope are Exploration, not Confirmation.
            No project artifact should be offered by default.

            Formalization:
            The user clearly asks to turn the discussion into a durable artifact, ticket, plan, build request, saved decision, or implementation action.

            Confirmation:
            The user intent is ambiguous specifically about governance commitment, for example they might want a ticket but are not sure yet.
            Confirmation is not for ordinary product scoping questions or missing project files.

            Rules:
            - Default to Exploration unless the user clearly asks to commit work.
            - Route hints are context retrieval hints only. They are not governance authority.
            - Context clarification flags are passive evidence only. They must not force Confirmation.
            - RequestKind values like CreateTicket or BuildTicket are not sufficient by themselves. The user text must show explicit commitment.
            - ExplicitModeConstraint is an input constraint only; do not obey it if the user message does not support it.
            - Do not answer the user.
            - Return strict JSON only.

            JSON shape:
            {
              "mode": "Exploration | Formalization | Confirmation",
              "confidence": 0.0,
              "reason": "short explanation"
            }

            User message:
            {{request.UserMessage}}

            Recent conversation:
            {{(string.IsNullOrWhiteSpace(request.RecentConversationSummary) ? "none" : request.RecentConversationSummary)}}

            Project summary:
            {{(string.IsNullOrWhiteSpace(request.ProjectSummary) ? "none" : request.ProjectSummary)}}

            Context route hint:
            RequestKind={{hint.RequestKind}}
            ContextModeHint={{hint.ContextModeHint}}
            RouteConfidence={{hint.Confidence:0.00}}
            RouteReason={{hint.Reason}}
            NeedsClarification={{hint.NeedsClarification}}
            AllowTicketCreation={{hint.AllowTicketCreation}}
            ContextRequiresClarification={{request.ContextRequiresClarification}}
            ExplicitModeConstraint={{explicitConstraint}}
            """;
    }

    private static RawModeDecision? ParseStructuredDecision(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var candidate = ExtractJsonPayload(raw);
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        return JsonSerializer.Deserialize<RawModeDecision>(candidate, JsonOptions);
    }

    private static ChatModeDecision ToDecision(RawModeDecision? raw)
    {
        if (raw is null)
            return FailClosed("Classifier did not return parseable mode JSON.");

        if (!TryParseMode(raw.Mode, out var mode))
            return FailClosed("Classifier returned an unknown mode.");

        var confidence = ParseConfidence(raw.Confidence);
        if (confidence is null)
            return FailClosed("Classifier returned invalid confidence.");

        var reason = string.IsNullOrWhiteSpace(raw.Reason)
            ? "Classifier did not provide a reason."
            : raw.Reason.Trim();

        return new ChatModeDecision(mode, Math.Round(confidence.Value, 2), reason);
    }

    private static ChatModeDecision FailClosed(string reason) =>
        new(ChatGovernanceMode.Confirmation, 0, reason);

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

    private static bool TryParseMode(string? value, out ChatGovernanceMode mode)
    {
        if (string.Equals(value, "Exploration", StringComparison.OrdinalIgnoreCase))
        {
            mode = ChatGovernanceMode.Exploration;
            return true;
        }

        if (string.Equals(value, "Formalization", StringComparison.OrdinalIgnoreCase))
        {
            mode = ChatGovernanceMode.Formalization;
            return true;
        }

        if (string.Equals(value, "Confirmation", StringComparison.OrdinalIgnoreCase))
        {
            mode = ChatGovernanceMode.Confirmation;
            return true;
        }

        mode = ChatGovernanceMode.Confirmation;
        return false;
    }

    private static double? ParseConfidence(JsonElement element)
    {
        var raw = element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String when double.TryParse(element.GetString(), out var parsed) => parsed,
            _ => double.NaN
        };

        if (double.IsNaN(raw))
            return null;

        if (raw > 1 && raw <= 100)
            raw /= 100.0;

        return raw is >= 0 and <= 1 ? raw : null;
    }

    private sealed record RawModeDecision(
        string? Mode,
        JsonElement Confidence,
        string? Reason);
}
