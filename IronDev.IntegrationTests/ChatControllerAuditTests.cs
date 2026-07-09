using System.Data;
using System.Text.Json;
using IronDev.Api.Controllers;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ChatControllerAuditTests
{
    [TestMethod]
    public async Task GetMessageAudit_ReturnsDurableAudit_WhenScopedSnapshotExists()
    {
        var snapshot = BuildSnapshot(messageId: 9001);
        var controller = new ChatController(
            new UnusedChatHistoryService(),
            new UnusedChatFeedbackService(),
            new ScopedTurnPersistenceService(7, 9701, 9001, snapshot),
            new UnusedProjectChatResponseService(),
            new UnusedProjectStateReviewService());

        var result = await controller.GetMessageAudit(7, 9701, 9001);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var audit = ok.Value as ChatTurnAuditResponse;
        Assert.IsNotNull(audit);
        Assert.AreEqual(ChatAuditSource.NormalizedRows, audit.Source);
        Assert.AreEqual(9001, audit.ChatMessageId);
        Assert.AreEqual(ChatGovernanceMode.Formalization, audit.Mode);
        Assert.AreEqual("ProjectChatContextPipeline", audit.RouteSource);
        Assert.IsNotNull(audit.RouteChallenge);
        Assert.AreEqual(ChatGovernanceMode.Confirmation, audit.RouteChallenge.SuggestedMode);
        Assert.AreEqual(ContextRequestKind.CreateTicket, audit.RouteChallenge.SuggestedRequestKind);
        Assert.IsNotNull(audit.BaDraft);
        Assert.AreEqual("Parcels can be marked Lost", audit.BaDraft.CandidateTitle);
        Assert.IsFalse(audit.IsFallbackEvidence);
    }

    [TestMethod]
    public async Task GetMessageAudit_LabelsTagsFallback_WhenSnapshotIsFallbackEvidence()
    {
        var snapshot = BuildSnapshot(messageId: 9001, isFallbackEvidence: true);
        var controller = new ChatController(
            new UnusedChatHistoryService(),
            new UnusedChatFeedbackService(),
            new ScopedTurnPersistenceService(7, 9701, 9001, snapshot),
            new UnusedProjectChatResponseService(),
            new UnusedProjectStateReviewService());

        var result = await controller.GetMessageAudit(7, 9701, 9001);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var audit = ok.Value as ChatTurnAuditResponse;
        Assert.IsNotNull(audit);
        Assert.AreEqual(ChatAuditSource.TagsFallback, audit.Source);
        Assert.IsTrue(audit.IsFallbackEvidence);
    }

    [TestMethod]
    public async Task GetMessageAudit_SerializesAuditEnumsAsStrings()
    {
        var snapshot = BuildSnapshot(messageId: 9001);
        var controller = new ChatController(
            new UnusedChatHistoryService(),
            new UnusedChatFeedbackService(),
            new ScopedTurnPersistenceService(7, 9701, 9001, snapshot),
            new UnusedProjectChatResponseService(),
            new UnusedProjectStateReviewService());

        var result = await controller.GetMessageAudit(7, 9701, 9001);
        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);

        var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        StringAssert.Contains(json, "\"source\":\"NormalizedRows\"");
        StringAssert.Contains(json, "\"mode\":\"Formalization\"");
        StringAssert.Contains(json, "\"kind\":\"None\"");
        StringAssert.Contains(json, "\"routeSource\":\"ProjectChatContextPipeline\"");
        StringAssert.Contains(json, "\"suggestedMode\":\"Confirmation\"");
        StringAssert.Contains(json, "\"suggestedRequestKind\":\"CreateTicket\"");
        StringAssert.Contains(json, "\"baDraft\"");
        StringAssert.Contains(json, "\"candidateTitle\":\"Parcels can be marked Lost\"");
        Assert.IsFalse(json.Contains("\"mode\":0", StringComparison.Ordinal), json);
        Assert.IsFalse(json.Contains("\"kind\":0", StringComparison.Ordinal), json);
    }

    [TestMethod]
    public async Task GetMessageAudit_ReturnsNotFound_WhenScopedAuditIsMissing()
    {
        var controller = new ChatController(
            new UnusedChatHistoryService(),
            new UnusedChatFeedbackService(),
            new ScopedTurnPersistenceService(7, 9701, 9001, BuildSnapshot(messageId: 9001)),
            new UnusedProjectChatResponseService(),
            new UnusedProjectStateReviewService());

        var wrongProject = await controller.GetMessageAudit(8, 9701, 9001);
        var wrongSession = await controller.GetMessageAudit(7, 9702, 9001);
        var wrongMessage = await controller.GetMessageAudit(7, 9701, 9002);

        Assert.IsInstanceOfType(wrongProject.Result, typeof(NotFoundResult));
        Assert.IsInstanceOfType(wrongSession.Result, typeof(NotFoundResult));
        Assert.IsInstanceOfType(wrongMessage.Result, typeof(NotFoundResult));
    }

    private static ChatTurnPersistenceSnapshot BuildSnapshot(long messageId, bool isFallbackEvidence = false) =>
        new(
            messageId,
            ChatGovernanceMode.Formalization,
            0.91,
            "The user explicitly asked to save project work.",
            ChatClarificationState.None,
            ChatGovernanceGate.FromDecision(new ChatModeDecision(
                ChatGovernanceMode.Formalization,
                0.91,
                "The user explicitly asked to save project work.")),
            "route-audit-test",
            "dogfood-audit-test",
            "Durable audit context.",
            "src/App.cs",
            "App",
            isFallbackEvidence,
            "ProjectChatContextPipeline",
            new ChatRouteChallenge(
                ChatGovernanceMode.Confirmation,
                ContextRequestKind.CreateTicket,
                0.51,
                "Classifier advisory differed, but the pipeline route remained final."),
            new BaWorkingDraft
            {
                CandidateTitle = "Parcels can be marked Lost",
                SourceMessageIds = ["101"],
                Confidence = 0.82,
                ReadyForConfirmation = true
            });

    private sealed class ScopedTurnPersistenceService : IChatTurnPersistenceService
    {
        private readonly int _projectId;
        private readonly long _sessionId;
        private readonly long _messageId;
        private readonly ChatTurnPersistenceSnapshot _snapshot;

        public ScopedTurnPersistenceService(
            int projectId,
            long sessionId,
            long messageId,
            ChatTurnPersistenceSnapshot snapshot)
        {
            _projectId = projectId;
            _sessionId = sessionId;
            _messageId = messageId;
            _snapshot = snapshot;
        }

        public Task PersistAsync(ChatTurnPersistenceRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PersistAsync(
            ChatTurnPersistenceRequest request,
            IDbConnection connection,
            IDbTransaction transaction,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ChatTurnPersistenceSnapshot?> GetByMessageIdAsync(
            long chatMessageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(chatMessageId == _messageId ? _snapshot : null);

        public Task<ChatTurnPersistenceSnapshot?> GetByMessageAsync(
            int projectId,
            long chatSessionId,
            long chatMessageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                projectId == _projectId &&
                chatSessionId == _sessionId &&
                chatMessageId == _messageId
                    ? _snapshot
                    : null);
    }

    private sealed class UnusedChatHistoryService : IChatHistoryService
    {
        public Task<IReadOnlyList<ProjectChatSession>> GetRecentSessionsAsync(int projectId, int take = 50, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProjectChatSession?> GetSessionByIdAsync(long sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> SaveSessionAsync(ProjectChatSession session, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> SaveMessageAsync(ChatMessage message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ChatMessage?> GetMessageByIdAsync(
            long messageId,
            int projectId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(
            int projectId,
            long sessionId,
            int take,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedChatFeedbackService : IChatFeedbackService
    {
        public Task<long> SaveFeedbackAsync(ChatMessageFeedback feedback, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<string> GetProjectFeedbackSummaryAsync(int projectId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedProjectChatResponseService : IProjectChatResponseService
    {
        public Task<ProjectChatResponseResult?> RespondAsync(
            int projectId,
            string prompt,
            ChatGovernanceMode? explicitMode = null,
            string? dogfoodTraceId = null,
            string? recentConversationSummary = null,
            long? sessionId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedProjectStateReviewService : IProjectStateReviewService
    {
        public Task<ProjectStateReviewResult?> ReviewAsync(int projectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
