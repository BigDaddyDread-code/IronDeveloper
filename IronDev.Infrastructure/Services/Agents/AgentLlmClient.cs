using System.Diagnostics;
using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class AgentLlmClient : IAgentLlmClient
{
    public async Task<AgentLlmCallResult> CompleteAsync(ModelProfile profile, string prompt, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var service = CreateService(profile);
            var response = await service.GetResponseAsync(prompt, ct);
            return new AgentLlmCallResult
            {
                WasAttempted = true,
                WasSuccessful = true,
                InvocationMode = "live_model",
                ResponseText = response,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            return new AgentLlmCallResult
            {
                WasAttempted = true,
                WasSuccessful = false,
                InvocationMode = "live_model_unavailable_fallback",
                ErrorMessage = ex.Message,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    private static ILLMService CreateService(ModelProfile profile)
    {
        var options = new LlmOptions
        {
            Provider = profile.Provider,
            Model = profile.Model,
            ApiKey = ResolveApiKey(profile),
            BaseUrl = profile.BaseUrl,
            TimeoutSeconds = profile.TimeoutSeconds
        };

        return profile.Provider.ToUpperInvariant() switch
        {
            "OPENAI" => new OpenAiLlmService(options),
            "LOCALOPENAI" => new LocalOpenAiCompatibleLlmService(options),
            "OLLAMA" => new OllamaLlmService(options),
            _ => throw new InvalidOperationException($"Unsupported live agent provider '{profile.Provider}'.")
        };
    }

    private static string? ResolveApiKey(ModelProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ApiKeyEnvironmentVariable))
            return Environment.GetEnvironmentVariable(profile.ApiKeyEnvironmentVariable);

        return profile.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            ? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            : Environment.GetEnvironmentVariable("LOCAL_OPENAI_API_KEY");
    }
}
