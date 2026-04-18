using System.Collections.Generic;
using IronDev.Agent.Models;
using IronDev.Agent.Services.Interfaces;

namespace IronDev.Agent.Services.Mock;

public class MockChatShellService : IChatShellService
{
    public IReadOnlyList<ChatSummary> GetMessages()
    {
        return new List<ChatSummary>
        {
            new ChatSummary { Role = "assistant", Content = "Welcome to the chat!" }
        };
    }
}
