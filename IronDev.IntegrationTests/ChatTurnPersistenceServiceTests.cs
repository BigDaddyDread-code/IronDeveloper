using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ChatTurnPersistenceServiceTests : IntegrationTestBase
{
    [TestMethod]
    public async Task SaveMessageAsync_AssistantEnvelopePersistsGovernanceClarificationAndTraceRows()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Governance persistence test"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Ticket handoff is ready.",
            Tags = BuildEnvelopeJson(),
            ContextSummary = "Context summary for persisted trace.",
            LinkedFilePaths = "src/App.cs",
            LinkedSymbols = "App"
        });

        var snapshot = await turnPersistence.GetByMessageIdAsync(messageId);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(ChatGovernanceMode.Formalization, snapshot.Mode);
        Assert.AreEqual(0.97, snapshot.ModeConfidence);
        Assert.AreEqual(ChatClarificationKind.GovernanceIntent, snapshot.Clarification.Kind);
        Assert.AreEqual("Do you want to turn this into a ticket?", snapshot.Clarification.Questions.Single());
        Assert.IsTrue(snapshot.Gate.CanCreateTicket);
        Assert.AreEqual("route-123", snapshot.RouteTraceId);
        Assert.AreEqual("dogfood-456", snapshot.DogfoodTraceId);
        Assert.AreEqual("Context summary for persisted trace.", snapshot.ContextSummary);
        Assert.AreEqual("src/App.cs", snapshot.LinkedFilePaths);
        Assert.AreEqual("App", snapshot.LinkedSymbols);
    }

    [TestMethod]
    public async Task SaveMessageAsync_LegacyTagsDoNotPersistNormalizedTurnRows()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Legacy tags test"
        });

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Legacy response.",
            Tags = "projectQuestion"
        });

        var snapshot = await turnPersistence.GetByMessageIdAsync(messageId);

        Assert.IsNull(snapshot);
    }

    [TestMethod]
    public async Task DeleteSessionAsync_DeletesPersistedTurnRowsBeforeMessages()
    {
        var projectId = await SeedProjectAsync();
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Delete turn state test"
        });
        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = "Ticket handoff is ready.",
            Tags = BuildEnvelopeJson()
        });

        Assert.IsNotNull(await turnPersistence.GetByMessageIdAsync(messageId));

        await chat.DeleteSessionAsync(sessionId);

        Assert.IsNull(await turnPersistence.GetByMessageIdAsync(messageId));
    }

    private static string BuildEnvelopeJson() =>
        """
        {
          "v": 1,
          "mode": "Formalization",
          "modeConfidence": 0.97,
          "modeReason": "The user explicitly asked to turn the discussion into project work.",
          "clarification": {
            "required": true,
            "kind": "GovernanceIntent",
            "questions": ["Do you want to turn this into a ticket?"],
            "reason": "The user is committing the discussion into durable work."
          },
          "gate": {
            "mode": "Formalization",
            "canSaveDiscussion": true,
            "canCreateTicket": true,
            "canViewSources": true,
            "canCopyMarkdown": true,
            "reason": "The user explicitly asked to turn the discussion into project work.",
            "confidence": 0.97,
            "governanceActions": ["Save this response as a Discussion.", "Create a Ticket from the saved Discussion."]
          },
          "routeTraceId": "route-123",
          "dogfoodTraceId": "dogfood-456"
        }
        """;
}
