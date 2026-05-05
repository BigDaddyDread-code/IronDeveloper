using IronDev.Core;
using IronDev.Core.Models;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace IronDev.Infrastructure.Services;

public sealed class OllamaLlmService : ILLMService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaLlmService(LlmOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new ArgumentException("BaseUrl is required for Ollama provider.");
        }

        _httpClient = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
        if (options.TimeoutSeconds > 0)
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        }
        _model = options.Model;
    }

    public async Task<string> GetResponseAsync(string prompt, System.Threading.CancellationToken ct = default)
    {
        var request = new OllamaRequest
        {
            Model = _model,
            Prompt = prompt,
            Stream = false
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/generate", request, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: ct);
            return body?.Response ?? string.Empty;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Ollama call failed: {ex.Message}", ex);
        }
    }

    private sealed class OllamaRequest
    {
        [JsonPropertyName("model")]  public string? Model  { get; set; }
        [JsonPropertyName("prompt")] public string? Prompt { get; set; }
        [JsonPropertyName("stream")] public bool    Stream { get; set; }
    }

    private sealed class OllamaResponse
    {
        [JsonPropertyName("response")] public string? Response { get; set; }
    }
}
