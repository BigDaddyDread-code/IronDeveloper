using IronDev.Core.Chat;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectChatResponseService : IProjectChatResponseService
{
    private readonly ProjectChatContextPipeline _contextPipeline;
    private readonly ProjectChatContextStateCompiler _contextStateCompiler;
    private readonly IChatModeClassifier _modeClassifier;
    private readonly IChatClarificationClassifier _clarificationClassifier;
    private readonly ProjectChatResponseComposer _composer;
    private readonly ProjectChatResponseMetadataBuilder _metadataBuilder;

    public ProjectChatResponseService(
        ProjectChatContextPipeline contextPipeline,
        ProjectChatContextStateCompiler contextStateCompiler,
        IChatModeClassifier modeClassifier,
        IChatClarificationClassifier clarificationClassifier,
        ProjectChatResponseComposer composer,
        ProjectChatResponseMetadataBuilder metadataBuilder)
    {
        _contextPipeline = contextPipeline;
        _contextStateCompiler = contextStateCompiler;
        _modeClassifier = modeClassifier;
        _clarificationClassifier = clarificationClassifier;
        _composer = composer;
        _metadataBuilder = metadataBuilder;
    }

    public async Task<ProjectChatResponseResult?> RespondAsync(
        int projectId,
        string prompt,
        ChatGovernanceMode? explicitMode = null,
        string? dogfoodTraceId = null,
        string? recentConversationSummary = null,
        long? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPrompt = prompt.ReplaceLineEndings(" ").Trim();
        var recentSummary = recentConversationSummary ?? string.Empty;
        var correlationId = string.IsNullOrWhiteSpace(dogfoodTraceId) ? Guid.NewGuid().ToString("N") : dogfoodTraceId;

        var context = await _contextPipeline.RunAsync(
            projectId,
            sessionId,
            normalizedPrompt,
            recentSummary,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        if (context is null)
            return null;

        var contextAgentResult = context.ContextAgentResult;
        var routeDecision = context.RouteDecision;

        var chatContextState = _contextStateCompiler.Compile(context, normalizedPrompt, recentSummary);

        var modeDecision = await _modeClassifier.ClassifyAsync(
            new ChatModeClassificationRequest(
                UserMessage: normalizedPrompt,
                RecentConversationSummary: recentSummary,
                RouteHint: routeDecision,
                ProjectSummary: context.Project.Name,
                ContextRequiresClarification: contextAgentResult.IsClarificationRequired || routeDecision.NeedsClarification,
                ExplicitMode: explicitMode,
                ContextState: chatContextState),
            cancellationToken).ConfigureAwait(false);

        var clarification = await _clarificationClassifier.ClassifyAsync(
            new ChatClarificationClassificationRequest(
                normalizedPrompt,
                recentSummary,
                chatContextState,
                modeDecision,
                context.Project.Name,
                routeDecision),
            cancellationToken).ConfigureAwait(false);
        chatContextState = chatContextState with { ClassifiedClarification = clarification };

        var gate = ChatGovernanceGate.FromDecision(modeDecision);
        var assistantResponse = await _composer.BuildAsync(
            contextAgentResult,
            modeDecision,
            chatContextState,
            contextAgentResult.FinalPrompt ?? string.Empty,
            normalizedPrompt,
            recentSummary,
            context.Project.Name,
            cancellationToken).ConfigureAwait(false);

        var metadata = _metadataBuilder.Build(context, modeDecision, correlationId, chatContextState);

        return new ProjectChatResponseResult(
            assistantResponse,
            modeDecision.Mode.ToString(),
            modeDecision.Confidence,
            modeDecision.Reason,
            clarification,
            gate,
            metadata.ReasoningTrace,
            metadata.DisambiguationQuestion,
            metadata.ReasoningSummary,
            metadata.ContextSummary,
            metadata.LinkedFilePaths,
            metadata.LinkedSymbols,
            correlationId);
    }
}
