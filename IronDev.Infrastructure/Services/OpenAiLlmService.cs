using IronDev.Core;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Threading.Tasks;

namespace IronDev.Infrastructure.Services;

public class OpenAiLlmService : ILLMService
{
    private readonly ChatClient? _chatClient;
    private readonly string _model;
    private readonly bool _hasKey;

    public OpenAiLlmService(string? apiKey, string model)
    {
        _model = model;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _hasKey = false;
        }
        else
        {
            _hasKey = true;
            _chatClient = new ChatClient(model, apiKey);
        }
    }

    public async Task<string> GetResponseAsync(string prompt)
    {
        if (!_hasKey || _chatClient == null)
            throw new InvalidOperationException("OPENAI_API_KEY is missing. Please configure it to use chat.");

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