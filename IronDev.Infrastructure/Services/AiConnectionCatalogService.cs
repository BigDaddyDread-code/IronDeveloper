using IronDev.Core.AiConnections;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

public sealed class AiConnectionCatalogService : IAiConnectionCatalogService
{
    public const string Boundary =
        "AI connection metadata is non-secret. Credential values are write-only and never returned by this endpoint.";

    public const string ContractVersion = "IronDev AI Connection Contract 2.5.0";

    private readonly IConfiguration _configuration;
    private readonly Func<string, string?> _environmentVariableReader;

    public AiConnectionCatalogService(IConfiguration configuration)
        : this(configuration, Environment.GetEnvironmentVariable)
    {
    }

    public AiConnectionCatalogService(IConfiguration configuration, Func<string, string?> environmentVariableReader)
    {
        _configuration = configuration;
        _environmentVariableReader = environmentVariableReader;
    }

    public Task<IReadOnlyList<AiConnectionMetadata>> ListAsync(
        int tenantId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (tenantId <= 0)
        {
            return Task.FromResult<IReadOnlyList<AiConnectionMetadata>>([]);
        }

        var provider = Clean(_configuration["Ai:Provider"], "openai").ToLowerInvariant();
        var model = Clean(_configuration["Ai:Model"], string.Empty);
        var endpoint = SafeEndpoint(_configuration["Ai:BaseUrl"], provider);
        var credentialConfigured = HasCredential();
        var credentialStatus = CredentialStatus(provider, credentialConfigured);
        var enabled = !string.IsNullOrWhiteSpace(provider);

        IReadOnlyList<AiConnectionMetadata> connections =
        [
            new AiConnectionMetadata
            {
                Id = "deployment-default",
                TenantId = tenantId,
                DisplayName = "Deployment default",
                ProviderKind = provider,
                ControlledEndpointId = $"deployment-default-{SafeIdentifier(provider)}",
                ControlledEndpoint = endpoint,
                CredentialConfigured = credentialConfigured,
                CredentialStatus = credentialStatus,
                LastSuccessfulTestUtc = null,
                LastFailedTestUtc = null,
                AvailableModels = string.IsNullOrWhiteSpace(model) ? [] : [model],
                Enabled = enabled,
                TenantAvailable = enabled,
                ProjectAvailable = enabled,
                CredentialRotatedUtc = null,
                CreatedByUserId = 0,
                CreatedUtc = null,
                UpdatedByUserId = userId,
                UpdatedUtc = null,
                Version = ContractVersion,
                Boundary = Boundary
            }
        ];

        return Task.FromResult(connections);
    }

    private bool HasCredential() =>
        !string.IsNullOrWhiteSpace(_configuration["Ai:ApiKey"]) ||
        !string.IsNullOrWhiteSpace(_environmentVariableReader("OPENAI_API_KEY")) ||
        !string.IsNullOrWhiteSpace(_environmentVariableReader("LOCAL_OPENAI_API_KEY"));

    private static string CredentialStatus(string provider, bool credentialConfigured)
    {
        if (ProviderDoesNotRequireCredential(provider))
        {
            return "Not required";
        }

        return credentialConfigured ? "Configured" : "Missing";
    }

    private static bool ProviderDoesNotRequireCredential(string provider) =>
        provider.Equals("fake", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("ollama", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("localopenai", StringComparison.OrdinalIgnoreCase);

    private static string SafeEndpoint(string? rawBaseUrl, string provider)
    {
        var trimmed = rawBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return provider.Equals("openai", StringComparison.OrdinalIgnoreCase)
                ? "provider-default:openai"
                : "deployment-configured";
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
            return $"{uri.Scheme}://{uri.Host}{port}";
        }

        return "deployment-configured";
    }

    private static string Clean(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static string SafeIdentifier(string value)
    {
        var chars = value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Select(char.ToLowerInvariant)
            .ToArray();
        return chars.Length == 0 ? "provider" : new string(chars);
    }
}
