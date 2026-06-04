using IronDev.Core.Chat;

namespace IronDev.Core.Interfaces;

public interface IChatTurnPersistenceService
{
    Task PersistAsync(
        ChatTurnPersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<ChatTurnPersistenceSnapshot?> GetByMessageIdAsync(
        long chatMessageId,
        CancellationToken cancellationToken = default);
}
