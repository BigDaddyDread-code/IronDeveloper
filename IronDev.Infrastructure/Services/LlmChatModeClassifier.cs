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
        var deterministicDecision = TryClassifyDeterministically(request);
        if (deterministicDecision is not null)
            return deterministicDecision;

        try
        {
            var raw = await _llm.GetResponseAsync(
                ChatModeClassificationPromptBuilder.BuildPrompt(request),
                cancellationToken).ConfigureAwait(false);
            var parsed = ParsePromptConstrainedDecision(raw);
            return ToDecision(parsed);
        }
        catch
        {
            return FailClosed("Classifier failed before returning a valid mode decision.");
        }
    }

    private static ChatModeDecision? TryClassifyDeterministically(ChatModeClassificationRequest request)
    {
        if (IsExplicitDiscussionCaptureRequest(request.UserMessage))
        {
            return new ChatModeDecision(
                ChatGovernanceMode.Formalization,
                1,
                "The user explicitly asked to save, capture, or record the current discussion as a durable project artifact.");
        }

        if (IsBoundArchitectureCommitRequest(request.UserMessage) &&
            HasRecentArchitectureTarget(request.RecentConversationSummary))
        {
            return new ChatModeDecision(
                ChatGovernanceMode.Formalization,
                0.94,
                "The user asked to add the architecture that is already bound by the recent conversation.");
        }

        if (IsShortAffirmation(request.UserMessage))
        {
            if (LastAssistantAskedForGovernanceCommitment(request.RecentConversationSummary))
            {
                return new ChatModeDecision(
                    ChatGovernanceMode.Formalization,
                    0.92,
                    "The user confirmed the previous assistant question about committing the discussion into project work.");
            }

            return new ChatModeDecision(
                ChatGovernanceMode.Exploration,
                0.86,
                "The user gave a short affirmation that resolves the previous exploration topic, not a durable governance request.");
        }

        return null;
    }

    private static bool IsExplicitDiscussionCaptureRequest(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var normalized = userMessage.Trim().ToLowerInvariant();
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

    private static bool ContainsAnyAction(string normalized, IReadOnlyList<string> actions) =>
        actions.Any(action => normalized.Contains(action, StringComparison.Ordinal));

    private static bool ContainsAnyObject(string normalized, IReadOnlyList<string> objects) =>
        objects.Any(obj => normalized.Contains(obj, StringComparison.Ordinal));

    private static bool IsShortAffirmation(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var normalized = userMessage.Trim().Trim('.', '!', '?').ToLowerInvariant();
        return normalized is "yes" or "y" or "yeah" or "yep" or "sure" or "ok" or "okay" or "cool";
    }

    private static bool IsBoundArchitectureCommitRequest(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return false;

        var normalized = userMessage.Trim().ToLowerInvariant();
        return normalized.Contains("add that architecture", StringComparison.Ordinal) ||
               normalized.Contains("add that artecture", StringComparison.Ordinal) ||
               normalized.Contains("add this architecture", StringComparison.Ordinal) ||
               normalized.Contains("add this artecture", StringComparison.Ordinal) ||
               normalized.Contains("capture that architecture", StringComparison.Ordinal) ||
               normalized.Contains("capture this architecture", StringComparison.Ordinal) ||
               normalized.Contains("record that architecture", StringComparison.Ordinal) ||
               normalized.Contains("record this architecture", StringComparison.Ordinal);
    }

    private static bool HasRecentArchitectureTarget(string recentConversationSummary)
    {
        if (string.IsNullOrWhiteSpace(recentConversationSummary))
            return false;

        var normalized = recentConversationSummary.ToLowerInvariant();
        return normalized.Contains("sql server", StringComparison.Ordinal) ||
               normalized.Contains("entity framework", StringComparison.Ordinal) ||
               normalized.Contains("json", StringComparison.Ordinal) ||
               normalized.Contains("winforms", StringComparison.Ordinal) ||
               normalized.Contains("web or forms", StringComparison.Ordinal) ||
               normalized.Contains("architecture", StringComparison.Ordinal) ||
               normalized.Contains("storage", StringComparison.Ordinal) ||
               normalized.Contains("platform", StringComparison.Ordinal);
    }

    private static bool LastAssistantAskedForGovernanceCommitment(string recentConversationSummary)
    {
        if (string.IsNullOrWhiteSpace(recentConversationSummary))
            return false;

        var lastAssistant = recentConversationSummary
            .Replace("\r\n", "\n")
            .Split('\n')
            .LastOrDefault(line => line.TrimStart().StartsWith("assistant:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(lastAssistant))
            return false;

        var normalized = lastAssistant.ToLowerInvariant();
        return normalized.Contains("save this", StringComparison.Ordinal) ||
               normalized.Contains("record this", StringComparison.Ordinal) ||
               normalized.Contains("create a ticket", StringComparison.Ordinal) ||
               normalized.Contains("turn this into", StringComparison.Ordinal) ||
               normalized.Contains("commit", StringComparison.Ordinal) ||
               normalized.Contains("architecture decision", StringComparison.Ordinal);
    }

    private static RawModeDecision? ParsePromptConstrainedDecision(string raw)
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
