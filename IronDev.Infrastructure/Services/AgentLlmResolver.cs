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

        var provider = (string.IsNullOrWhiteSpace(profile.Provider) ? global.Provider : profile.Provider)?.Trim() ?? string.Empty;
        var options = new LlmOptions
        {
            Provider = provider,
            Model = string.IsNullOrWhiteSpace(profile.Model) ? global.Model : profile.Model,
            // BaseUrl is deployment config only — never sourced from a profile.
            BaseUrl = global.BaseUrl,
            TimeoutSeconds = profile.TimeoutSeconds > 0 ? profile.TimeoutSeconds : global.TimeoutSeconds,
            // The key stays out of the profile — it comes from config/env, as before.
            ApiKey = global.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        };

        // Fail closed: a typo or hostile profile edit must NOT silently become a
        // fake or unknown model. Only explicit, known providers resolve.
        ILLMService llm = provider.ToLowerInvariant() switch
        {
            "openai" => new OpenAiLlmService(options),
            "localopenai" => new LocalOpenAiCompatibleLlmService(options),
            "ollama" => new OllamaLlmService(options),
            "custom" => new LocalOpenAiCompatibleLlmService(options),
            "fake" => new FakeLlmService(),
            _ => throw new InvalidOperationException(
                $"Agent '{role}' is configured with unknown provider '{provider}'. " +
                $"Known providers: {string.Join(", ", SkeletonAgentProviders.UserSelectable)}, fake (test/local). " +
                "Refusing to run — an unknown provider must never silently become a fake model.")
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
