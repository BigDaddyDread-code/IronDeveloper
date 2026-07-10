using IronDev.Core.Models;
using IronDev.Data.Models;

namespace IronDev.Core.Interfaces;

public interface IProjectChatDocumentSourceService
{
    Task<IReadOnlyList<ChatDocumentSource>> GetAvailableSourcesAsync(
        int projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<long, IReadOnlyList<ChatDocumentSource>>> GetSourcesForMessagesAsync(
        int projectId,
        long sessionId,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttachedChatDocumentContext>> GetAttachedContextsAsync(
        int projectId,
        long sessionId,
        long sourceMessageId,
        CancellationToken cancellationToken = default);
}
