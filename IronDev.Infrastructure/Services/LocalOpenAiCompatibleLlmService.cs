using IronDev.Core;
using IronDev.Core.Models;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Threading.Tasks;

namespace IronDev.Infrastructure.Services;

public sealed class LocalOpenAiCompatibleLlmService : ILLMService
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
}
