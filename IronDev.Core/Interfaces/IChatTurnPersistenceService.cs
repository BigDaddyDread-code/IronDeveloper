using IronDev.Core.Chat;
using System.Data;

namespace IronDev.Core.Interfaces;

public interface IChatTurnPersistenceService
{
    Task PersistAsync(
        ChatTurnPersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task PersistAsync(
        ChatTurnPersistenceRequest request,
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task<ChatTurnPersistenceSnapshot?> GetByMessageIdAsync(
        long chatMessageId,
        CancellationToken cancellationToken = default);
}
