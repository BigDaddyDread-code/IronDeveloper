using IronDev.Core.Chat;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectChatResponseService : IProjectChatResponseService
{
    private readonly ProjectChatContextPipeline _contextPipeline;
    private readonly IChatModeClassifier _modeClassifier;
    private readonly IChatClarificationClassifier _clarificationClassifier;
    private readonly ProjectChatResponseComposer _composer;
    private readonly ProjectChatResponseMetadataBuilder _metadataBuilder;

    public ProjectChatResponseService(
        ProjectChatContextPipeline contextPipeline,
        IChatModeClassifier modeClassifier,
        IChatClarificationClassifier clarificationClassifier,
        ProjectChatResponseComposer composer,
        ProjectChatResponseMetadataBuilder metadataBuilder)
    {
        _contextPipeline = contextPipeline;
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

        var modeDecision = await _modeClassifier.ClassifyAsync(
            new ChatModeClassificationRequest(
                normalizedPrompt,
                recentSummary,
                routeDecision,
                context.Project.Name,
                contextAgentResult.IsClarificationRequired || routeDecision.NeedsClarification,
                explicitMode),
            cancellationToken).ConfigureAwait(false);

        var chatContextState = new ChatContextState(
            contextAgentResult.IsClarificationRequired || routeDecision.NeedsClarification,
            contextAgentResult.ClarificationQuestions.Concat(routeDecision.ClarificationQuestions).ToList(),
            contextAgentResult.ContextSummary);

        var clarification = await _clarificationClassifier.ClassifyAsync(
            new ChatClarificationClassificationRequest(
                normalizedPrompt,
                recentSummary,
                chatContextState,
                modeDecision,
                context.Project.Name,
                routeDecision),
            cancellationToken).ConfigureAwait(false);

        var gate = ChatGovernanceGate.FromDecision(modeDecision);
        var assistantResponse = await _composer.BuildAsync(
            contextAgentResult,
            modeDecision,
            clarification,
            contextAgentResult.FinalPrompt ?? string.Empty,
            normalizedPrompt,
            context.Project.Name,
            cancellationToken).ConfigureAwait(false);

        var metadata = _metadataBuilder.Build(context, modeDecision, clarification, correlationId);

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
