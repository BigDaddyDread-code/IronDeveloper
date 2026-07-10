using IronDev.Core.Chat;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ChatBaDraftTitleDerivationTests
{
    [TestMethod]
    public async Task ExplicitLatestTitleOverridesEarlierSessionTopic()
    {
        const int projectId = 7;
        const long sessionId = 9007;
        const string prompt = "Create a ticket titled Verify LocalTest Chat Draft Review. The review must show provenance.";
        var history = new Mock<IChatHistoryService>();
        history
            .Setup(service => service.GetRecentMessagesAsync(projectId, sessionId, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ChatMessage
                {
                    Id = 5001,
                    ProjectId = projectId,
                    ChatSessionId = sessionId,
                    Role = "user",
                    Message = "Name the two most important next UX areas in one sentence."
                },
                new ChatMessage
                {
                    Id = 5002,
                    ProjectId = projectId,
                    ChatSessionId = sessionId,
                    Role = "user",
                    Message = prompt
                }
            ]);
        var routeDecision = new ContextAgentRouteDecision
        {
            OriginalUserRequest = prompt,
            EffectiveWorkText = prompt,
            RequestKind = ContextRequestKind.CreateTicket,
            Confidence = 0.95,
            Reason = "Explicit ticket request.",
            AllowTicketCreation = true
        };
        var service = new ChatBaDraftService(history.Object);

        var draft = await service.BuildAsync(new ChatBaDraftRequest(
            projectId,
            sessionId,
            prompt,
            string.Empty,
            EffectiveChatRoute.FromRouteDecision(
                routeDecision,
                ChatGovernanceMode.Formalization,
                "ChatBaDraftTitleDerivationTests")));

        Assert.IsNotNull(draft);
        Assert.AreEqual("Verify LocalTest Chat Draft Review", draft.CandidateTitle);
        Assert.AreEqual(2, draft.SourceMessageIds.Count);
    }
}
