using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.Models;
using IronDev.Core.Workbench;
using OpenAI.Chat;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace IronDev.Infrastructure.Services;

public sealed class OpenAiLlmService
    : ILLMService, IWorkbenchBusinessAnalystRoleAwareLlmService, IWorkbenchBuilderRoleAwareLlmService
{
    private readonly ChatClient _chatClient;

    public OpenAiLlmService(LlmOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new ArgumentException("API key is required for OpenAI provider. Please configure Ai:ApiKey in appsettings.");
        }

        _chatClient = new ChatClient(options.Model, options.ApiKey);
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
            throw new InvalidOperationException($"OpenAI call failed: {ex.Message}", ex);
        }
    }

    public async Task<WorkbenchBusinessAnalystProviderResponse> GetResponseAsync(
        WorkbenchBusinessAnalystProviderEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var messages = WorkbenchBusinessAnalystProviderMessageMapper.ForOpenAi(envelope)
            .Select(ToChatMessage)
            .ToArray();
        var started = Stopwatch.GetTimestamp();
        try
        {
            var completion = await _chatClient.CompleteChatAsync(
                messages,
                new ChatCompletionOptions
                {
                    ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
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
            throw new InvalidOperationException($"OpenAI call failed: {ex.Message}", ex);
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
        var completion = await _chatClient.CompleteChatAsync(messages,
            new ChatCompletionOptions { ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() },
            cancellationToken);
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
            // OpenAI 2.10 exposes the provider's developer role behind an evaluation
            // diagnostic. This focused boundary intentionally uses it: demoting this
            // message would destroy the instruction hierarchy this adapter guarantees.
#pragma warning disable OPENAI001
            AgentModelRole.Developer => new DeveloperChatMessage(message.Content),
#pragma warning restore OPENAI001
            AgentModelRole.User => new UserChatMessage(message.Content),
            _ => throw new WorkbenchBusinessAnalystProviderEnvelopeException(
                $"OpenAI does not support Business Analyst envelope role '{message.Role}'.")
        };
}
