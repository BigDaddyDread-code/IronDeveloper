using IronDev.Core;
using IronDev.Core.Models;
using OpenAI.Chat;
using System;
using System.Threading.Tasks;

namespace IronDev.Infrastructure.Services;

public sealed class OpenAiLlmService : ILLMService
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

    public async Task<string> GetResponseAsync(string prompt)
    {
        try
        {
            var completion = await _chatClient.CompleteChatAsync(prompt);
            return completion.Value.Content[0].Text;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"OpenAI call failed: {ex.Message}", ex);
        }
    }
}