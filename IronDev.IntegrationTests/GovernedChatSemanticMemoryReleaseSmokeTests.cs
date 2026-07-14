using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using IronDev.Api.Controllers;
using IronDev.Core;
using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class GovernedChatSemanticMemoryReleaseSmokeTests : IntegrationTestBase
{
    [TestMethod]
    public async Task GovernedChatSemanticMemoryReleaseSmoke_PersistsAuditAndUsesContextOnlyEvidence()
    {
        var projectId = await SeedProjectAsync(name: "Governed Chat Semantic Memory Smoke");
        var chat = ServiceProvider.GetRequiredService<IChatHistoryService>();
        var sessionId = await chat.SaveSessionAsync(new ProjectChatSession
        {
            ProjectId = projectId,
            Title = "Governed chat semantic memory smoke"
        });

        var semanticEvidenceProvider = new StubSemanticMemoryEvidenceProvider();
        var modeClassifier = new CountingChatModeClassifier();
        var clarificationClassifier = new CountingChatClarificationClassifier();
        var responseService = BuildResponseService(semanticEvidenceProvider, modeClassifier, clarificationClassifier);

        var result = await responseService.RespondAsync(
            projectId,
            "What should we remember about governed audit persistence?",
            await CreateMemoryRetrievalContextAsync(projectId, consumer: "GovernedReleaseSmoke"),
            dogfoodTraceId: "release-smoke",
            recentConversationSummary: "user: we merged atomic audit persistence",
            sessionId: sessionId);

        Assert.IsNotNull(result);
        Assert.AreEqual(ChatGovernanceMode.Exploration.ToString(), result.Mode);
        Assert.IsFalse(result.Gate.ShowGovernanceActions);
        Assert.IsFalse(result.Gate.CanCreateTicket);
        Assert.IsFalse(result.Gate.CanSaveDiscussion);
        Assert.AreEqual("ProjectChatContextPipeline", result.RouteSource);
        Assert.AreEqual(1, semanticEvidenceProvider.CallCount);
        Assert.AreEqual(1, modeClassifier.CallCount);
        Assert.AreEqual(1, clarificationClassifier.CallCount);

        var classifiedState = modeClassifier.LastRequest?.ContextState
            ?? throw new AssertFailedException("Mode classifier did not receive ChatContextState.");
        Assert.AreEqual(ChatContextStateOrigin.ProjectChatResponseCompiler, classifiedState.Origin);
        Assert.IsFalse(classifiedState.EpisodicMemoryEnabled);
        var semanticEvidence = classifiedState.SemanticEvidence ?? Array.Empty<MemoryEvidence>();
        Assert.IsTrue(semanticEvidence.Count > 0);
        Assert.IsTrue(semanticEvidence.All(evidence => evidence.UsedFor == "ContextOnly"));
        Assert.IsTrue(semanticEvidence.Any(evidence => evidence.SourceId == "semantic-decision-release-smoke"));
        Assert.IsTrue(semanticEvidence.All(evidence =>
            !ContainsGovernanceDirective(evidence.AuthorityLevel) &&
            !ContainsGovernanceDirective(evidence.Excerpt) &&
            !ContainsGovernanceDirective(evidence.UsedFor)));

        var reasoningTrace = result.ReasoningTrace?.ToList()
            ?? throw new AssertFailedException("Response did not include a reasoning trace.");
        CollectionAssert.Contains(reasoningTrace, "Memory evidence consumed by classifier:");
        Assert.IsTrue(reasoningTrace.Any(line => line.Contains("semantic-decision-release-smoke", StringComparison.Ordinal)));
        Assert.IsTrue(reasoningTrace.Any(line => line.Contains("UsedFor=ContextOnly", StringComparison.Ordinal)));

        var envelope = new ChatTurnEnvelope(
            V: 1,
            Mode: ChatGovernanceMode.Exploration,
            ModeConfidence: result.ModeConfidence ?? 0,
            ModeReason: result.ModeReason ?? "Release smoke mode decision.",
            Clarification: result.Clarification,
            Gate: result.Gate,
            RouteTraceId: result.DogfoodTraceId,
            DogfoodTraceId: result.DogfoodTraceId,
            RouteSource: result.RouteSource,
            RouteChallenge: result.RouteChallenge);

        var messageId = await chat.SaveMessageAsync(new ChatMessage
        {
            ProjectId = projectId,
            ChatSessionId = sessionId,
            Role = "assistant",
            Message = result.Response,
            Tags = SerializeEnvelope(envelope),
            ContextSummary = result.ContextSummary,
            LinkedFilePaths = result.LinkedFilePaths,
            LinkedSymbols = result.LinkedSymbols
        });

        await AssertPersistedAuditRowsAsync(messageId);

        var turnPersistence = ServiceProvider.GetRequiredService<IChatTurnPersistenceService>();
        var snapshot = await turnPersistence.GetByMessageAsync(projectId, sessionId, messageId);

        Assert.IsNotNull(snapshot);
        Assert.IsFalse(snapshot.IsFallbackEvidence);
        Assert.AreEqual(ChatGovernanceMode.Exploration, snapshot.Mode);
        Assert.AreEqual("ProjectChatContextPipeline", snapshot.RouteSource);
        Assert.IsFalse(snapshot.Gate.ShowGovernanceActions);
        Assert.IsFalse(snapshot.Gate.CanCreateTicket);

        var controller = new ChatController(
            chat,
            ServiceProvider.GetRequiredService<IChatFeedbackService>(),
            turnPersistence,
            responseService,
            new StubProjectStateReviewService(),
            new StubProjectChatDocumentSourceService());
        var auditResult = await controller.GetMessageAudit(projectId, sessionId, messageId);
        var ok = auditResult.Result as OkObjectResult
            ?? throw new AssertFailedException("Audit controller did not return OK.");
        var audit = ok.Value as ChatTurnAuditResponse
            ?? throw new AssertFailedException("Audit controller did not return ChatTurnAuditResponse.");
        Assert.AreEqual(ChatAuditSource.NormalizedRows, audit.Source);
        Assert.AreEqual("ProjectChatContextPipeline", audit.RouteSource);
        Assert.IsFalse(audit.IsFallbackEvidence);

        Assert.AreEqual(1, modeClassifier.CallCount, "Audit replay must not recompute mode.");
        Assert.AreEqual(1, clarificationClassifier.CallCount, "Audit replay must not recompute clarification.");
    }

    private ProjectChatResponseService BuildResponseService(
        ISemanticMemoryEvidenceProvider semanticEvidenceProvider,
        IChatModeClassifier modeClassifier,
        IChatClarificationClassifier clarificationClassifier)
    {
        var contextPipeline = new ProjectChatContextPipeline(
            ServiceProvider.GetRequiredService<IProjectService>(),
            ServiceProvider.GetRequiredService<ITicketService>(),
            ServiceProvider.GetRequiredService<IProjectMemoryService>(),
            semanticEvidenceProvider,
            new StubContextAgentRouteJudge(),
            new StubContextAgentService(),
            ServiceProvider.GetRequiredService<IProjectMembershipService>());

        return new ProjectChatResponseService(
            contextPipeline,
            new ProjectChatContextStateCompiler(),
            modeClassifier,
            clarificationClassifier,
            ServiceProvider.GetRequiredService<IChatBaDraftService>(),
            new ProjectChatResponseComposer(new StubPromptTemplateProvider(), new StubLlmService()),
            new ProjectChatResponseMetadataBuilder(),
            new StubProjectChatDocumentSourceService());
    }

    private async Task AssertPersistedAuditRowsAsync(long messageId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        Assert.AreEqual(1, await CountRowsAsync(connection, "dbo.ChatMessages", "Id = @MessageId", new { MessageId = messageId }));
        Assert.AreEqual(1, await CountRowsAsync(connection, "dbo.ChatTurnGovernance", "ChatMessageId = @MessageId", new { MessageId = messageId }));
        Assert.AreEqual(1, await CountRowsAsync(connection, "dbo.ChatTurnClarifications", "ChatMessageId = @MessageId", new { MessageId = messageId }));
        Assert.AreEqual(1, await CountRowsAsync(connection, "dbo.ChatTurnTraces", "ChatMessageId = @MessageId", new { MessageId = messageId }));
    }

    private static Task<int> CountRowsAsync(SqlConnection connection, string table, string where, object parameters) =>
        connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {table} WHERE {where};", parameters);

    private static string SerializeEnvelope(ChatTurnEnvelope envelope)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(envelope, options);
    }

    private static bool ContainsGovernanceDirective(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("SuggestedMode", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("AutoCreateTicket", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("RecommendedGateState", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubSemanticMemoryEvidenceProvider : ISemanticMemoryEvidenceProvider
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<MemoryEvidence>> GetEvidenceAsync(
            int projectId,
            string userMessage,
            string recentConversationSummary,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<MemoryEvidence>>(
            [
                new MemoryEvidence(
                    SourceId: "semantic-decision-release-smoke",
                    SourceType: "Decision",
                    Title: "Governed audit persistence",
                    Excerpt: "Assistant v1 governance turns are saved with normalized audit rows as one logical write.",
                    IsCurrent: true,
                    RelevanceScore: 0.97,
                    AuthorityLevel: "Accepted",
                    UsedFor: "AutoCreateTicket")
            ]);
        }
    }

    private sealed class CountingChatModeClassifier : IChatModeClassifier
    {
        public int CallCount { get; private set; }
        public ChatModeClassificationRequest? LastRequest { get; private set; }

        public Task<ChatModeDecision> ClassifyAsync(
            ChatModeClassificationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new ChatModeDecision(
                ChatGovernanceMode.Exploration,
                0.91,
                "Release smoke prompt is exploratory; semantic evidence is context only."));
        }
    }

    private sealed class CountingChatClarificationClassifier : IChatClarificationClassifier
    {
        public int CallCount { get; private set; }

        public Task<ChatClarificationState> ClassifyAsync(
            ChatClarificationClassificationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(ChatClarificationState.None);
        }
    }

    private sealed class StubContextAgentRouteJudge : IContextAgentRouteJudge
    {
        public Task<ContextAgentRouteDecision> DecideRouteAsync(
            ContextAgentRouteRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContextAgentRouteDecision
            {
                OriginalUserRequest = request.UserRequest,
                EffectiveWorkText = request.UserRequest,
                RequestKind = ContextRequestKind.GeneralChat,
                Confidence = 0.89,
                Reason = "Release smoke keeps semantic evidence advisory.",
                AllowTicketCreation = true,
                EvidenceUsed = ["Semantic evidence is available but context-only."],
                ContextModeHint = "Exploration"
            });
    }

    private sealed class StubContextAgentService : IContextAgentService
    {
        public Task<ContextAgentResult> RunAsync(
            ContextAgentRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(new ContextAgentResult
            {
                FinalPrompt = "Answer with semantic evidence as context, without exposing governance actions.",
                AllowsProseResponse = true,
                WasSuccessful = true,
                ResultType = ContextAgentResultType.Prompt,
                TraceGroupId = request.TraceGroupId,
                ContextSummary = "Release smoke semantic evidence reached ChatContextState as context-only evidence."
            });
    }

    private sealed class StubPromptTemplateProvider : IChatPromptTemplateProvider
    {
        public string GetTemplate(ChatPromptTemplate template) =>
            "Project: {{PROJECT_NAME}}\nPrompt: {{PROMPT}}\nContext: {{FINAL_PROMPT}}\nMode: {{MODE}}\nRecent: {{RECENT_CONVERSATION}}\nClarification: {{CLARIFICATION_CONTEXT}}";
    }

    private sealed class StubLlmService : ILLMService
    {
        public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default) =>
            Task.FromResult("The reliable next move is to keep semantic memory as context, persist the governed turn, and replay audit from normalized rows.");
    }

    private sealed class StubProjectStateReviewService : IProjectStateReviewService
    {
        public Task<ProjectStateReviewResult?> ReviewAsync(
            int projectId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProjectChatDocumentSourceService : IProjectChatDocumentSourceService
    {
        public Task<IReadOnlyList<ChatDocumentSource>> GetAvailableSourcesAsync(
            int projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatDocumentSource>>([]);

        public Task<IReadOnlyDictionary<long, IReadOnlyList<ChatDocumentSource>>> GetSourcesForMessagesAsync(
            int projectId,
            long sessionId,
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<long, IReadOnlyList<ChatDocumentSource>>>(
                new Dictionary<long, IReadOnlyList<ChatDocumentSource>>());

        public Task<IReadOnlyList<AttachedChatDocumentContext>> GetAttachedContextsAsync(
            int projectId,
            long sessionId,
            long sourceMessageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AttachedChatDocumentContext>>([]);
    }
}
