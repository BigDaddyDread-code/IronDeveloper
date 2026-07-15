using IronDev.Api.Controllers;
using IronDev.Core.Agents;
using IronDev.Core.Auth;
using IronDev.Core.Builder;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReadiness;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ChatBaWorkingDraftTests : IntegrationTestBase
{
    [TestMethod]
    public async Task ChatBa_CreatesCandidateDraft_FromInformalConversation()
    {
        var projectId = await SeedProjectAsync();
        var sessionId = await SeedChatAsync(projectId, "Need parcels to be marked lost.");

        var draft = await BuildDraftAsync(projectId, sessionId, "Need parcels to be marked lost.");

        Assert.IsNotNull(draft);
        Assert.AreEqual("Parcels can be marked Lost", draft.CandidateTitle);
        StringAssert.Contains(draft.Problem, "controlled way");
        CollectionAssert.Contains(draft.BusinessRules.ToList(), "Parcels can be marked Lost.");
        Assert.AreEqual("Ticket", draft.SuggestedArtifact);
    }

    [TestMethod]
    public async Task ChatBa_UpdatesExistingDraft_FromShortFollowUp()
    {
        var projectId = await SeedProjectAsync();
        var sessionId = await SeedChatAsync(
            projectId,
            "Need parcels to be marked lost.",
            "only after dispatch");

        var draft = await BuildDraftAsync(projectId, sessionId, "only after dispatch");

        Assert.IsNotNull(draft);
        Assert.AreEqual("Parcels can be marked Lost", draft.CandidateTitle);
        CollectionAssert.Contains(draft.BusinessRules.ToList(), "Only dispatched parcels can be marked Lost.");
        Assert.AreEqual(2, draft.SourceMessageIds.Count);
    }

    [TestMethod]
    public async Task ChatBa_DoesNotCreateTicketWithoutConfirmation()
    {
        var projectId = await SeedProjectAsync();
        var sessionId = await SeedChatAsync(projectId, "Need parcels to be marked lost.");

        _ = await BuildDraftAsync(projectId, sessionId, "Need parcels to be marked lost.");

        var tickets = await ServiceProvider.GetRequiredService<ITicketService>().GetRecentTicketsAsync(projectId);
        Assert.AreEqual(0, tickets.Count);
    }

    [TestMethod]
    public async Task ChatBa_PreservesSourceMessageIds()
    {
        var projectId = await SeedProjectAsync();
        var messageIds = new List<long>();
        var sessionId = await SeedChatAsync(
            projectId,
            messageIds,
            "Need parcels to be marked lost.",
            "only after dispatch",
            "not delivered");

        var draft = await BuildDraftAsync(projectId, sessionId, "not delivered");

        Assert.IsNotNull(draft);
        CollectionAssert.AreEqual(
            messageIds.Select(id => id.ToString()).ToList(),
            draft.SourceMessageIds.ToList());
    }

    [TestMethod]
    public async Task ChatBa_FlagsContradictoryRules()
    {
        var projectId = await SeedProjectAsync();
        var sessionId = await SeedChatAsync(
            projectId,
            "Need parcels to be marked lost.",
            "Delivered parcels cannot be Lost.",
            "include delivered too");

        var draft = await BuildDraftAsync(projectId, sessionId, "include delivered too");

        Assert.IsNotNull(draft);
        Assert.AreEqual(1, draft.PotentialConflicts.Count);
        StringAssert.Contains(draft.PotentialConflicts.Single(), "delivered parcels");
        Assert.AreEqual("Which delivered-parcel rule is correct?", draft.OpenQuestions.Single());
        Assert.IsFalse(draft.ReadyForConfirmation);
    }

    [TestMethod]
    public async Task ChatBa_AsksOnlyOneNextQuestion()
    {
        var projectId = await SeedProjectAsync();
        var sessionId = await SeedChatAsync(
            projectId,
            "Need parcels to be marked lost.",
            "only after dispatch",
            "needs audit too");

        var draft = await BuildDraftAsync(projectId, sessionId, "needs audit too");

        Assert.IsNotNull(draft);
        Assert.AreEqual(1, draft.OpenQuestions.Count);
        Assert.AreEqual("Should marking a parcel Lost require a reason/comment?", draft.OpenQuestions.Single());
    }

    [TestMethod]
    public async Task ChatBa_ConfirmedDraftCreatesTicketWithProvenance()
    {
        var projectId = await SeedProjectAsync();
        var sessionId = await SeedChatAsync(
            projectId,
            "Need parcels to be marked lost.",
            "only after dispatch",
            "not delivered",
            "lost should be terminal");
        var draft = await BuildDraftAsync(projectId, sessionId, "lost should be terminal");
        Assert.IsNotNull(draft);

        var controller = BuildTicketsController();
        var result = await controller.ConfirmBaDraft(
            projectId,
            new ConfirmBaWorkingDraftRequest(sessionId, draft),
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var ticket = ok.Value as ProjectTicket;
        Assert.IsNotNull(ticket);
        Assert.AreEqual("Parcels can be marked Lost", ticket.Title);
        Assert.AreEqual(sessionId, ticket.SourceChatSessionId);
        Assert.AreEqual(long.Parse(draft.SourceMessageIds.Last()), ticket.SourceChatMessageId);
        StringAssert.Contains(ticket.GenerationNote, "did not approve");
        StringAssert.Contains(ticket.Content, "BA working draft");

        var references = await ServiceProvider.GetRequiredService<IArtifactSourceReferenceService>()
            .GetForArtifactAsync(TenantContext.TenantId, projectId, "Ticket", ticket.Id);
        foreach (var sourceMessageId in draft.SourceMessageIds.Select(long.Parse))
        {
            Assert.IsTrue(
                references.Any(reference =>
                    reference.SourceType == "ChatMessage" &&
                    reference.SourceId == sourceMessageId &&
                    reference.ReferenceType == "CreatedFrom"),
                $"Missing source chat message reference {sourceMessageId}.");
        }
    }

    [TestMethod]
    public async Task ChatBa_DraftIsNotTreatedAsApprovalOrAuthority()
    {
        var projectId = await SeedProjectAsync();
        var sessionId = await SeedChatAsync(projectId, "Need parcels to be marked lost.");

        var draft = await BuildDraftAsync(projectId, sessionId, "Need parcels to be marked lost.");

        Assert.IsNotNull(draft);
        StringAssert.Contains(draft.Boundary, "not a ticket");
        StringAssert.Contains(draft.Boundary, "approval");
        StringAssert.Contains(draft.Boundary, "commit");
        Assert.IsTrue(draft.ReadyForConfirmation || draft.Confidence > 0);
    }

    private async Task<BaWorkingDraft?> BuildDraftAsync(int projectId, long sessionId, string prompt)
    {
        var service = ServiceProvider.GetRequiredService<IChatBaDraftService>();
        return await service.BuildAsync(
            new ChatBaDraftRequest(
                projectId,
                sessionId,
                prompt,
                string.Empty,
                BuildEffectiveRoute(prompt)));
    }

    private static EffectiveChatRoute BuildEffectiveRoute(string prompt)
    {
        var routeDecision = new ContextAgentRouteDecision
        {
            OriginalUserRequest = prompt,
            EffectiveWorkText = prompt,
            RequestKind = ContextRequestKind.GeneralChat,
            Confidence = 0.77,
            Reason = "Informal BA-shaping candidate.",
            AllowTicketCreation = false
        };

        return EffectiveChatRoute.FromRouteDecision(
            routeDecision,
            ChatGovernanceMode.Exploration,
            "ChatBaWorkingDraftTests",
            ["CurrentPrompt", "RecentConversation"]);
    }

    private async Task<long> SeedChatAsync(int projectId, params string[] userMessages) =>
        await SeedChatAsync(projectId, new List<long>(), userMessages);

    private async Task<long> SeedChatAsync(int projectId, List<long> messageIds, params string[] userMessages)
    {
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "CHAT-BA-0 test"
        });

        foreach (var userMessage in userMessages)
        {
            var messageId = await chat.SaveMessageAsync(new ChatMessage
            {
                ProjectId = projectId,
                ChatSessionId = sessionId,
                Role = "user",
                Message = userMessage
            });
            messageIds.Add(messageId);
        }

        return sessionId;
    }

    private TicketsController BuildTicketsController() =>
        new(
            ServiceProvider.GetRequiredService<ITicketService>(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            ServiceProvider.GetRequiredService<IChatHistoryService>(),
            ServiceProvider.GetRequiredService<IArtifactSourceReferenceService>(),
            ServiceProvider.GetRequiredService<ICurrentTenantContext>(),
            new FailClosedRunReadinessService());

    private sealed class FailClosedRunReadinessService : IProjectRunReadinessService
    {
        public Task<ProjectRunReadiness> EvaluateAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectRunReadiness
            {
                ProjectId = projectId,
                ProjectSetupReady = false,
                ExecutionReady = false,
                ReadyToRun = false,
                State = ProjectRunReadinessStates.RunConfigurationRequired,
                BlockedCount = 1,
                Blockers =
                [
                    new ProjectRunReadinessBlocker
                    {
                        Role = SkeletonAgentRole.Builder,
                        ReasonCode = ProjectRunReadinessReasonCodes.RunAgentProfileMissing,
                        Reason = "The direct controller test uses an explicit fail-closed readiness fixture.",
                        NextSafeAction = "Inject the real project run-readiness service before testing readiness."
                    }
                ]
            });
    }
}
