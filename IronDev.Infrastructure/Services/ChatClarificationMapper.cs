using IronDev.Core.Chat;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services;

public sealed class ChatClarificationMapper : IChatClarificationMapper
{
    public ChatClarificationState Map(ChatContextState contextState, string userMessage)
    {
        var questions = contextState.ClarificationQuestions
            .Where(question => !string.IsNullOrWhiteSpace(question))
            .Select(question => question.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!contextState.RequiresClarification || questions.Count == 0)
            return ChatClarificationState.None;

        var kind = LooksLikeProductScope(userMessage)
            ? ChatClarificationKind.ProductScope
            : ChatClarificationKind.GeneralScope;

        var reason = kind == ChatClarificationKind.ProductScope
            ? "The user is exploring a broad product idea that needs scope before implementation detail."
            : "The context agent surfaced missing detail that should be asked conversationally.";

        return new ChatClarificationState(true, kind, questions, reason);
    }

    private static bool LooksLikeProductScope(string userMessage)
    {
        var lower = userMessage.ToLowerInvariant();
        return lower.Contains("build", StringComparison.Ordinal) ||
            lower.Contains("make", StringComparison.Ordinal) ||
            lower.Contains("create", StringComparison.Ordinal) ||
            lower.Contains("write", StringComparison.Ordinal) ||
            lower.Contains("design", StringComparison.Ordinal) ||
            lower.StartsWith("i want ", StringComparison.Ordinal) ||
            lower.Contains(" i want ", StringComparison.Ordinal);
    }
}
