using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.Models;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// AG-2 — resolves a role to the LLM its profile configures, reusing the four
/// existing provider implementations. Any agent can run any provider. The API
/// key is never taken from the profile (a profile holds no secret): it is read
/// from the same environment/config the global service uses, per provider.
/// </summary>
public sealed class AgentLlmResolver : IAgentLlmResolver
{
    private readonly ISkeletonAgentProfileService _profiles;
    private readonly IConfiguration _configuration;

    public AgentLlmResolver(ISkeletonAgentProfileService profiles, IConfiguration configuration)
    {
        _profiles = profiles;
        _configuration = configuration;
    }

    public async Task<SkeletonAgentLlm> ResolveAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetAsync(role, cancellationToken).ConfigureAwait(false);
        var global = _configuration.GetSection("Ai").Get<LlmOptions>() ?? new LlmOptions();

        var provider = string.IsNullOrWhiteSpace(profile.Provider) ? global.Provider : profile.Provider;
        var options = new LlmOptions
        {
            Provider = provider,
            Model = string.IsNullOrWhiteSpace(profile.Model) ? global.Model : profile.Model,
            BaseUrl = string.IsNullOrWhiteSpace(profile.BaseUrl) ? global.BaseUrl : profile.BaseUrl,
            TimeoutSeconds = profile.TimeoutSeconds > 0 ? profile.TimeoutSeconds : global.TimeoutSeconds,
            // The key stays out of the profile — it comes from config/env, as before.
            ApiKey = global.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        };

        ILLMService llm = (provider?.ToLowerInvariant() ?? "openai") switch
        {
            "openai" => new OpenAiLlmService(options),
            "localopenai" => new LocalOpenAiCompatibleLlmService(options),
            "ollama" => new OllamaLlmService(options),
            "custom" => new LocalOpenAiCompatibleLlmService(options),
            _ => new FakeLlmService()
        };

        return new SkeletonAgentLlm
        {
            Role = role,
            Llm = llm,
            Provider = options.Provider,
            Model = options.Model
        };
    }
}
