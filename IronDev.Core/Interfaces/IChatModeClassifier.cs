using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface IChatModeClassifier
{
    Task<ChatModeDecision> ClassifyAsync(
        ChatModeClassificationRequest request,
        CancellationToken cancellationToken = default);
}
