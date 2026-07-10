using IronDev.Core.Chat;

namespace IronDev.Core.Interfaces;

public interface IChatBaDraftService
{
    Task<BaWorkingDraft?> BuildAsync(
        ChatBaDraftRequest request,
        CancellationToken cancellationToken = default);
}
