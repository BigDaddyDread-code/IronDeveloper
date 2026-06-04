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
        var asksToCreate = lower.Contains("build", StringComparison.Ordinal) ||
            lower.Contains("make", StringComparison.Ordinal) ||
            lower.Contains("create", StringComparison.Ordinal) ||
            lower.Contains("write", StringComparison.Ordinal);
        var productNoun = lower.Contains("game", StringComparison.Ordinal) ||
            lower.Contains("app", StringComparison.Ordinal) ||
            lower.Contains("site", StringComparison.Ordinal) ||
            lower.Contains("system", StringComparison.Ordinal) ||
            lower.Contains("tool", StringComparison.Ordinal) ||
            lower.Contains("monopoly", StringComparison.Ordinal) ||
            lower.Contains("minesweeper", StringComparison.Ordinal) ||
            lower.Contains("naughts", StringComparison.Ordinal) ||
            lower.Contains("crosses", StringComparison.Ordinal);

        return asksToCreate && productNoun;
    }
}
