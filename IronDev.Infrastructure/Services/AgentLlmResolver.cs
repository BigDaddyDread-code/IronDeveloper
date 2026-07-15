using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.AiConnections;
using IronDev.Core.Models;
using IronDev.Core.RunReadiness;
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
    private readonly IAiConnectionCatalogService? _connections;
    private readonly IAiConnectionCredentialStore? _credentials;

    public AgentLlmResolver(ISkeletonAgentProfileService profiles, IConfiguration configuration)
    {
        _profiles = profiles;
        _configuration = configuration;
    }

    public AgentLlmResolver(
        ISkeletonAgentProfileService profiles,
        IConfiguration configuration,
        IAiConnectionCatalogService connections,
        IAiConnectionCredentialStore credentials)
    {
        _profiles = profiles;
        _configuration = configuration;
        _connections = connections;
        _credentials = credentials;
    }

    public async Task<SkeletonAgentLlm> ResolveAsync(SkeletonAgentRole role, CancellationToken cancellationToken = default)
    {
        // D-2a: the deterministic alpha-smoke provider. Gated by explicit config
        // (AlphaSmoke:Enabled AND AlphaSmoke:ModelMode=Deterministic), default off,
        // so it can never be reached in normal operation. It fakes only model
        // words — the real proposal/tester/critic services still parse and run.
        if (IsDeterministicAlphaSmoke())
        {
            return new SkeletonAgentLlm
            {
                Role = role,
                Llm = new DeterministicAlphaSmokeLlmService(role, _configuration),
                Provider = DeterministicAlphaSmokeLlmService.ProviderName,
                Model = $"deterministic-{role.ToString().ToLowerInvariant()}"
            };
        }

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

        return Create(role, options);
    }

    public async Task<SkeletonAgentLlm> ResolveAsync(
        SkeletonAgentRole role,
        int tenantId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (IsDeterministicAlphaSmoke())
        {
            return new SkeletonAgentLlm
            {
                Role = role,
                Llm = new DeterministicAlphaSmokeLlmService(role, _configuration),
                Provider = DeterministicAlphaSmokeLlmService.ProviderName,
                Model = $"deterministic-{role.ToString().ToLowerInvariant()}"
            };
        }
        if (tenantId <= 0 || projectId <= 0)
            throw new InvalidOperationException("A governed project run requires tenant and project identity before resolving an agent model.");

        var profile = (await _profiles.ListEffectiveAsync(tenantId, projectId, cancellationToken).ConfigureAwait(false))
            .Single(item => item.Role == role);
        var global = _configuration.GetSection("Ai").Get<LlmOptions>() ?? new LlmOptions();
        var provider = profile.Provider.Trim();
        var baseUrl = global.BaseUrl;
        var apiKey = global.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (_connections is not null && _credentials is not null)
        {
            var connection = (await _connections.ListAsync(tenantId, userId: 0, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => string.Equals(item.Id, profile.AiConnectionId, StringComparison.OrdinalIgnoreCase));
            if (connection is null || !connection.Enabled || !connection.TenantAvailable || !connection.ProjectAvailable)
                throw new InvalidOperationException($"The effective {role} AI connection '{profile.AiConnectionId}' is not enabled and available for this project.");
            provider = connection.ProviderKind.Trim();
            if (Uri.TryCreate(connection.ControlledEndpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme is "http" or "https")
                baseUrl = endpoint.ToString().TrimEnd('/');
            apiKey = await _credentials.GetCredentialForUseAsync(tenantId, connection.Id, cancellationToken).ConfigureAwait(false) ?? apiKey;
        }

        if (provider.Equals(ProjectRunProviders.LocalTestDeterministic, StringComparison.OrdinalIgnoreCase))
        {
            return new SkeletonAgentLlm
            {
                Role = role,
                Llm = new DeterministicAlphaSmokeLlmService(role, _configuration),
                Provider = ProjectRunProviders.LocalTestDeterministic,
                Model = profile.Model
            };
        }

        return Create(role, new LlmOptions
        {
            Provider = provider,
            Model = profile.Model,
            BaseUrl = baseUrl,
            TimeoutSeconds = profile.TimeoutSeconds > 0 ? profile.TimeoutSeconds : global.TimeoutSeconds,
            ApiKey = apiKey
        });
    }

    private static SkeletonAgentLlm Create(SkeletonAgentRole role, LlmOptions options)
    {
        var provider = options.Provider?.Trim() ?? string.Empty;
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
            Provider = provider,
            Model = options.Model ?? string.Empty
        };
    }

    private bool IsDeterministicAlphaSmoke() =>
        string.Equals(_configuration["AlphaSmoke:Enabled"], "true", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(_configuration["AlphaSmoke:ModelMode"], "Deterministic", StringComparison.OrdinalIgnoreCase);
}
