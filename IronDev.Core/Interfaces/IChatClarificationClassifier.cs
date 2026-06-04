using IronDev.Core.Chat;

namespace IronDev.Core.Interfaces;

public interface IChatClarificationClassifier
{
    Task<ChatClarificationState> ClassifyAsync(
        ChatClarificationClassificationRequest request,
        CancellationToken cancellationToken = default);
}
