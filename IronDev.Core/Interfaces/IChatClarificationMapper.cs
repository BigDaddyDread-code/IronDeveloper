using IronDev.Core.Chat;

namespace IronDev.Core.Interfaces;

public interface IChatClarificationMapper
{
    ChatClarificationState Map(ChatContextState contextState, string userMessage);
}
