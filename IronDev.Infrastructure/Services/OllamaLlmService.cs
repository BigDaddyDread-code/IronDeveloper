using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.Models;
using IronDev.Core.Workbench;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace IronDev.Infrastructure.Services;

public sealed class OllamaLlmService
    : ILLMService, IWorkbenchBusinessAnalystRoleAwareLlmService, IWorkbenchBuilderRoleAwareLlmService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaLlmService(LlmOptions options)
        : this(options, new HttpClient())
    {
    }

    public OllamaLlmService(LlmOptions options, HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new ArgumentException("BaseUrl is required for Ollama provider.");
        }

        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(options.BaseUrl);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Ollama call failed: {ex.Message}", ex);
        }
    }

    public async Task<WorkbenchBusinessAnalystProviderResponse> GetResponseAsync(
        WorkbenchBusinessAnalystProviderEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var mappedMessages = WorkbenchBusinessAnalystProviderMessageMapper
            .ForSystemUserProvider(envelope);
        var request = new OllamaChatRequest
        {
            Model = _model,
            Messages = mappedMessages.Select(message => new OllamaChatMessage
            {
                Role = message.Role switch
                {
                    AgentModelRole.System => "system",
                    AgentModelRole.User => "user",
                    _ => throw new WorkbenchBusinessAnalystProviderEnvelopeException(
                        $"Ollama cannot safely map Business Analyst role '{message.Role}'.")
                },
                Content = message.Content
            }).ToArray(),
            Format = "json",
            Options = new OllamaChatOptions
            {
                NumberOfPredictedTokens = envelope.ReservedOutputTokens
            },
            Stream = false
        };

        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/chat",
                request,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                cancellationToken: cancellationToken);
            return new WorkbenchBusinessAnalystProviderResponse
            {
                Output = body?.Message?.Content ?? string.Empty,
                SafeRequestId = envelope.SafeRequestId,
                Usage = new AgentModelUsage
                {
                    InputTokens = Math.Max(0, body?.PromptEvaluationCount ?? 0),
                    OutputTokens = Math.Max(0, body?.EvaluationCount ?? 0)
                },
                UsageReported = body?.PromptEvaluationCount is not null &&
                    body.EvaluationCount is not null,
                DurationMilliseconds = Stopwatch.GetElapsedTime(started).Ticks / TimeSpan.TicksPerMillisecond
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Ollama call failed: {ex.Message}", ex);
        }
    }

    public async Task<BuilderProviderResponse> GetBuilderResponseAsync(
        BuilderProviderEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var request = new OllamaChatRequest
        {
            Model = _model,
            Messages = BuilderProviderMessageMapper.Map(envelope).Select(message => new OllamaChatMessage
            {
                Role = message.Role == AgentModelRole.System ? "system" : "user",
                Content = message.Content
            }).ToArray(),
            Format = "json",
            Options = new OllamaChatOptions { NumberOfPredictedTokens = 16_000 },
            Stream = false
        };
        var started = Stopwatch.GetTimestamp();
        var response = await _httpClient.PostAsJsonAsync("/api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            cancellationToken: cancellationToken);
        return new BuilderProviderResponse(
            body?.Message?.Content ?? string.Empty,
            envelope.SafeRequestId, null,
            new AgentModelUsage
            {
                InputTokens = Math.Max(0, body?.PromptEvaluationCount ?? 0),
                OutputTokens = Math.Max(0, body?.EvaluationCount ?? 0)
            },
            body?.PromptEvaluationCount is not null && body.EvaluationCount is not null,
            Stopwatch.GetElapsedTime(started).Ticks / TimeSpan.TicksPerMillisecond);
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

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("messages")] public IReadOnlyList<OllamaChatMessage> Messages { get; set; } = [];
        [JsonPropertyName("format")] public string? Format { get; set; }
        [JsonPropertyName("options")] public OllamaChatOptions Options { get; set; } = new();
        [JsonPropertyName("stream")] public bool Stream { get; set; }
    }

    private sealed class OllamaChatOptions
    {
        [JsonPropertyName("num_predict")] public int NumberOfPredictedTokens { get; set; }
    }

    private sealed class OllamaChatMessage
    {
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
    }

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")] public OllamaChatMessage? Message { get; set; }
        [JsonPropertyName("prompt_eval_count")] public int? PromptEvaluationCount { get; set; }
        [JsonPropertyName("eval_count")] public int? EvaluationCount { get; set; }
    }
}
