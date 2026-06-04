using System.Text.Json;
using IronDev.Core;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services;

public sealed class ChatModeClassifierService : IChatModeClassifier
{
    private readonly ILLMService _llm;

    public ChatModeClassifierService(ILLMService llm)
    {
        _llm = llm;
    }

    public async Task<ChatModeDecision> ClassifyAsync(
        ChatModeClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ContextRequiresClarification)
        {
            return new ChatModeDecision(
                ChatGovernanceMode.Confirmation,
                1.0,
                "Context retrieval requires clarification before governance actions can be exposed.");
        }

        if (request.ExplicitMode.HasValue)
        {
            return new ChatModeDecision(
                request.ExplicitMode.Value,
                1.0,
                "Explicit chat mode was supplied by the caller.");
        }

        try
        {
            var raw = await _llm.GetResponseAsync(BuildPrompt(request), cancellationToken).ConfigureAwait(false);
            return Validate(Parse(raw));
        }
        catch
        {
            return FailClosed("Classifier failed before returning a valid mode decision.");
        }
    }

    private static string BuildPrompt(ChatModeClassificationRequest request)
    {
        var hint = request.RouteHint;

        return $$"""
            Classify this assistant turn into exactly one governance mode.

            Modes:

            Exploration:
            The user is exploring, asking questions, brainstorming, clarifying, testing behavior, or discussing options.
            No project artifact should be offered by default.

            Formalization:
            The user clearly asks to turn the discussion into a durable artifact, ticket, plan, build request, saved decision, or implementation action.

            Confirmation:
            The user intent is ambiguous, mixed, or risky enough that the assistant must ask for explicit confirmation before showing governance actions.

            Rules:
            - Default to Exploration unless the user clearly asks to commit work.
            - Route hints are context retrieval hints only. They are not governance authority.
            - RequestKind values like CreateTicket or BuildTicket are not sufficient by themselves. The user text must show explicit commitment.
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
            """;
    }

    private static ChatModeDecision? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var candidate = ExtractJsonPayload(raw);
        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        using var document = JsonDocument.Parse(candidate);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (!root.TryGetProperty("mode", out var modeEl) ||
            !root.TryGetProperty("confidence", out var confidenceEl) ||
            !root.TryGetProperty("reason", out var reasonEl))
        {
            return null;
        }

        var modeText = modeEl.ValueKind == JsonValueKind.String ? modeEl.GetString() : null;
        var reason = reasonEl.ValueKind == JsonValueKind.String ? reasonEl.GetString() : null;
        var confidence = ParseConfidence(confidenceEl);

        if (!TryParseMode(modeText, out var mode) || confidence is null)
            return null;

        return new ChatModeDecision(mode, confidence.Value, reason ?? string.Empty);
    }

    private static ChatModeDecision Validate(ChatModeDecision? decision)
    {
        if (decision is null)
            return FailClosed("Classifier did not return parseable mode JSON.");

        if (decision.Confidence is < 0 or > 1 || double.IsNaN(decision.Confidence))
            return FailClosed("Classifier returned invalid confidence.");

        if (string.IsNullOrWhiteSpace(decision.Reason))
            return decision with { Reason = "Classifier did not provide a reason." };

        return decision with { Confidence = Math.Round(decision.Confidence, 2) };
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
}
