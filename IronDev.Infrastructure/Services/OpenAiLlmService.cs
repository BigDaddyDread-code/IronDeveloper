using IronDev.Core;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Threading.Tasks;

namespace IronDev.Agent.Services;

public class OpenAiLlmService : ILLMService
{
    private readonly ChatClient _chatClient;
    private readonly string _model;

    public OpenAiLlmService(string apiKey, string model = "gpt-5.4")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));

        _model = model;
        _chatClient = new ChatClient(model, apiKey);
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