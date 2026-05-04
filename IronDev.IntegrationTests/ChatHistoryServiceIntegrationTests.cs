using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public class ChatHistoryServiceIntegrationTests : IntegrationTestBase
{
    [TestMethod]
    public async Task SaveMessageAsync_And_GetRecentMessagesAsync_ShouldRoundTripMessages()
    {
        using var scope = ServiceProvider.CreateScope();
        var chatHistoryService = scope.ServiceProvider.GetRequiredService<IChatHistoryService>();

        var projectId = await SeedProjectAsync();
        
        // 1. Create a session
        var session = new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Test Session",
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        var sessionId = await chatHistoryService.SaveSessionAsync(session);

        // 2. Save messages
        await chatHistoryService.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "user",
            Message = "Hello IronDev",
            Tags = "tag1"
        });

        await chatHistoryService.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Hello back",
            Tags = "tag2"
        });

        // 3. Verify retrieval
        var messages = await chatHistoryService.GetRecentMessagesAsync(projectId, sessionId, 10);

        Assert.HasCount(2, messages);
        Assert.AreEqual("user", messages[0].Role);
        Assert.AreEqual("Hello IronDev", messages[0].Message);
        Assert.AreEqual("assistant", messages[1].Role);
        Assert.AreEqual("Hello back", messages[1].Message);
    }
}
