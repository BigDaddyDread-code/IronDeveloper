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
    private readonly IChatBaDraftService _baDraftService;
    private readonly ProjectChatResponseComposer _composer;
    private readonly ProjectChatResponseMetadataBuilder _metadataBuilder;
    private readonly IProjectChatDocumentSourceService _documentSources;

    public ProjectChatResponseService(
        ProjectChatContextPipeline contextPipeline,
        ProjectChatContextStateCompiler contextStateCompiler,
        IChatModeClassifier modeClassifier,
        IChatClarificationClassifier clarificationClassifier,
        IChatBaDraftService baDraftService,
        ProjectChatResponseComposer composer,
        ProjectChatResponseMetadataBuilder metadataBuilder,
        IProjectChatDocumentSourceService documentSources)
    {
        _contextPipeline = contextPipeline;
        _contextStateCompiler = contextStateCompiler;
        _modeClassifier = modeClassifier;
        _clarificationClassifier = clarificationClassifier;
        _baDraftService = baDraftService;
        _composer = composer;
        _metadataBuilder = metadataBuilder;
        _documentSources = documentSources;
    }

    public async Task<ProjectChatResponseResult?> RespondAsync(
        int projectId,
        string prompt,
        MemoryRetrievalRequestContext memoryRetrievalContext,
        ChatGovernanceMode? explicitMode = null,
        string? dogfoodTraceId = null,
        string? recentConversationSummary = null,
        long? sessionId = null,
        long? sourceMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPrompt = prompt.ReplaceLineEndings(" ").Trim();
        var recentSummary = recentConversationSummary ?? string.Empty;
        var correlationId = string.IsNullOrWhiteSpace(dogfoodTraceId) ? Guid.NewGuid().ToString("N") : dogfoodTraceId;
        var attachedDocumentContexts = sourceMessageId.HasValue && sessionId.HasValue
            ? await _documentSources.GetAttachedContextsAsync(
                projectId,
                sessionId.Value,
                sourceMessageId.Value,
                cancellationToken).ConfigureAwait(false)
            : [];

        var context = await _contextPipeline.RunAsync(
            projectId,
            sessionId,
            normalizedPrompt,
            recentSummary,
            correlationId,
            memoryRetrievalContext,
            cancellationToken,
            explicitMode,
            attachedDocumentContexts).ConfigureAwait(false);

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

        var effectiveRoute = context.EffectiveRoute ?? EffectiveChatRoute.FromRouteDecision(
            routeDecision,
            modeDecision.Mode,
            source: "ProjectChatResponseService.ModeClassifierFallback",
            inputsUsed: ["RouteDecision", "ModeClassifier"]);
        context = context with { EffectiveRoute = effectiveRoute };
        modeDecision = effectiveRoute.ToModeDecision();

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

        var baDraft = await _baDraftService.BuildAsync(
            new ChatBaDraftRequest(
                projectId,
                sessionId,
                normalizedPrompt,
                recentSummary,
                effectiveRoute),
            cancellationToken).ConfigureAwait(false);

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
            DogfoodTraceId: correlationId,
            RouteSource: effectiveRoute.Source,
            RouteChallenge: contextAgentResult.RouteChallenge,
            BaDraft: baDraft,
            DocumentSources: context.AttachedDocumentSources ?? []);
    }
}
