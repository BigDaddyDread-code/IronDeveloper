using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.Models;
using IronDev.Core.Workbench;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace IronDev.Infrastructure.Services;

public sealed class LocalOpenAiCompatibleLlmService
    : ILLMService, IWorkbenchBusinessAnalystRoleAwareLlmService, IWorkbenchBuilderRoleAwareLlmService
{
    private readonly ChatClient _chatClient;

    public LocalOpenAiCompatibleLlmService(LlmOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new ArgumentException("BaseUrl is required for LocalOpenAI provider.");
        }

        var apiKey = options.ApiKey ?? "local-key";
        var endpoint = new Uri(options.BaseUrl);

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = endpoint
        };

        var client = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
        _chatClient = client.GetChatClient(options.Model);
    }

    public async Task<string> GetResponseAsync(string prompt, System.Threading.CancellationToken ct = default)
    {
        try
        {
            var completion = await _chatClient.CompleteChatAsync(new UserChatMessage[] { new(prompt) }, cancellationToken: ct);
            return completion.Value.Content[0].Text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Local OpenAI compatible call failed: {ex.Message}", ex);
        }
    }

    public async Task<WorkbenchBusinessAnalystProviderResponse> GetResponseAsync(
        WorkbenchBusinessAnalystProviderEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        // OpenAI-compatible servers do not uniformly implement the developer role.
        // Demote the advisory profile to user authority; the immutable policy remains system.
        var messages = WorkbenchBusinessAnalystProviderMessageMapper
            .ForSystemUserProvider(envelope)
            .Select(ToChatMessage)
            .ToArray();
        var started = Stopwatch.GetTimestamp();
        try
        {
            var completion = await _chatClient.CompleteChatAsync(
                messages,
                new ChatCompletionOptions
                {
                    MaxOutputTokenCount = envelope.ReservedOutputTokens
                },
                cancellationToken);
            completion.GetRawResponse().Headers.TryGetValue(
                "x-request-id",
                out var providerRequestId);
            return new WorkbenchBusinessAnalystProviderResponse
            {
                Output = string.Concat(completion.Value.Content.Select(part => part.Text)),
                SafeRequestId = envelope.SafeRequestId,
                ProviderRequestId = string.IsNullOrWhiteSpace(providerRequestId)
                    ? null
                    : providerRequestId,
                Usage = new AgentModelUsage
                {
                    InputTokens = completion.Value.Usage?.InputTokenCount ?? 0,
                    OutputTokens = completion.Value.Usage?.OutputTokenCount ?? 0
                },
                UsageReported = completion.Value.Usage is not null,
                DurationMilliseconds = Stopwatch.GetElapsedTime(started).Ticks / TimeSpan.TicksPerMillisecond
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Local OpenAI compatible call failed: {ex.Message}", ex);
        }
    }

    public async Task<BuilderProviderResponse> GetBuilderResponseAsync(
        BuilderProviderEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var messages = BuilderProviderMessageMapper.Map(envelope)
            .Select(message => message.Role == AgentModelRole.System
                ? (ChatMessage)new SystemChatMessage(message.Content)
                : new UserChatMessage(message.Content)).ToArray();
        var started = Stopwatch.GetTimestamp();
        var completion = await _chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        completion.GetRawResponse().Headers.TryGetValue("x-request-id", out var requestId);
        return new BuilderProviderResponse(
            string.Concat(completion.Value.Content.Select(part => part.Text)),
            envelope.SafeRequestId, requestId,
            new AgentModelUsage
            {
                InputTokens = completion.Value.Usage?.InputTokenCount ?? 0,
                OutputTokens = completion.Value.Usage?.OutputTokenCount ?? 0
            },
            completion.Value.Usage is not null,
            Stopwatch.GetElapsedTime(started).Ticks / TimeSpan.TicksPerMillisecond);
    }

    private static ChatMessage ToChatMessage(WorkbenchBusinessAnalystProviderMessage message) =>
        message.Role switch
        {
            AgentModelRole.System => new SystemChatMessage(message.Content),
            AgentModelRole.User => new UserChatMessage(message.Content),
            _ => throw new WorkbenchBusinessAnalystProviderEnvelopeException(
                $"Local OpenAI compatibility mode cannot safely map Business Analyst role '{message.Role}'.")
        };
}
