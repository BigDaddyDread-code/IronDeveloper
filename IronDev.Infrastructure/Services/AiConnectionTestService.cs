using System.Net.Http.Headers;
using IronDev.Core.AiConnections;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

public sealed class AiConnectionTestService : IAiConnectionTestService
{
    public const string Boundary =
        "Connection testing may use a protected credential internally. Credential values are never returned, logged, or stored in test health.";

    private readonly IAiConnectionCatalogService _catalog;
    private readonly IAiConnectionCredentialStore _credentials;
    private readonly IAiConnectionTestHealthStore _health;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly Func<string, string?> _environmentVariableReader;

    public AiConnectionTestService(
        IAiConnectionCatalogService catalog,
        IAiConnectionCredentialStore credentials,
        IAiConnectionTestHealthStore health,
        IConfiguration configuration,
        HttpClient httpClient)
        : this(catalog, credentials, health, configuration, httpClient, Environment.GetEnvironmentVariable)
    {
    }

    public AiConnectionTestService(
        IAiConnectionCatalogService catalog,
        IAiConnectionCredentialStore credentials,
        IAiConnectionTestHealthStore health,
        IConfiguration configuration,
        HttpClient httpClient,
        Func<string, string?> environmentVariableReader)
    {
        _catalog = catalog;
        _credentials = credentials;
        _health = health;
        _configuration = configuration;
        _httpClient = httpClient;
        _environmentVariableReader = environmentVariableReader;
    }

    public async Task<AiConnectionTestOutcome> TestAsync(
        int tenantId,
        int userId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var connection = (await _catalog.ListAsync(tenantId, userId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Id, connectionId, StringComparison.OrdinalIgnoreCase));
        if (connection is null)
            return new AiConnectionTestOutcome
            {
                Succeeded = false,
                Status = "UnknownConnection",
                FailureReason = "The AI connection was not found.",
                TestedAtUtc = DateTimeOffset.UtcNow,
                Boundary = Boundary
            };
        if (!connection.Enabled || !connection.TenantAvailable || !connection.ProjectAvailable)
            return await FailureAsync(tenantId, userId, connectionId, "Unavailable", "The AI connection is not enabled and available.", null, cancellationToken).ConfigureAwait(false);

        var provider = connection.ProviderKind.Trim().ToLowerInvariant();
        if (provider == "fake")
            return await SuccessAsync(tenantId, userId, connectionId, null, cancellationToken).ConfigureAwait(false);

        var credential = await _credentials.GetCredentialForUseAsync(tenantId, connectionId, cancellationToken).ConfigureAwait(false)
            ?? DeploymentCredential();
        if (RequiresCredential(provider) && string.IsNullOrWhiteSpace(credential))
            return await FailureAsync(tenantId, userId, connectionId, "MissingCredential", "A credential is required before this connection can be tested.", null, cancellationToken).ConfigureAwait(false);

        var probeUri = ProbeUri(connection);
        if (probeUri is null)
            return await FailureAsync(tenantId, userId, connectionId, "EndpointUnavailable", "The controlled endpoint is not configured as a testable HTTP endpoint.", null, cancellationToken).ConfigureAwait(false);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, probeUri);
            if (!string.IsNullOrWhiteSpace(credential))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await SuccessAsync(tenantId, userId, connectionId, (int)response.StatusCode, cancellationToken).ConfigureAwait(false)
                : await FailureAsync(tenantId, userId, connectionId, "ProviderRejected", $"The controlled provider returned HTTP {(int)response.StatusCode}.", (int)response.StatusCode, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await FailureAsync(tenantId, userId, connectionId, "TimedOut", "The controlled provider did not answer before the test timeout.", null, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return await FailureAsync(tenantId, userId, connectionId, "Unreachable", "The controlled provider could not be reached.", null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<AiConnectionTestOutcome> SuccessAsync(
        int tenantId,
        int userId,
        string connectionId,
        int? statusCode,
        CancellationToken cancellationToken)
    {
        var testedAt = DateTimeOffset.UtcNow;
        await _health.RecordAsync(tenantId, connectionId, true, testedAt, cancellationToken).ConfigureAwait(false);
        var connection = (await _catalog.ListAsync(tenantId, userId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Id, connectionId, StringComparison.OrdinalIgnoreCase));
        return new AiConnectionTestOutcome
        {
            Succeeded = true,
            Status = "Passed",
            HttpStatusCode = statusCode,
            TestedAtUtc = testedAt,
            Connection = connection,
            Boundary = Boundary
        };
    }

    private async Task<AiConnectionTestOutcome> FailureAsync(
        int tenantId,
        int userId,
        string connectionId,
        string status,
        string reason,
        int? statusCode,
        CancellationToken cancellationToken)
    {
        var testedAt = DateTimeOffset.UtcNow;
        await _health.RecordAsync(tenantId, connectionId, false, testedAt, cancellationToken).ConfigureAwait(false);
        var connection = (await _catalog.ListAsync(tenantId, userId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => string.Equals(item.Id, connectionId, StringComparison.OrdinalIgnoreCase));
        return new AiConnectionTestOutcome
        {
            Succeeded = false,
            Status = status,
            FailureReason = reason,
            HttpStatusCode = statusCode,
            TestedAtUtc = testedAt,
            Connection = connection,
            Boundary = Boundary
        };
    }

    private string? DeploymentCredential() =>
        _configuration["Ai:ApiKey"] ??
        _environmentVariableReader("OPENAI_API_KEY") ??
        _environmentVariableReader("LOCAL_OPENAI_API_KEY");

    private static bool RequiresCredential(string provider) => provider is "openai" or "custom";

    private static Uri? ProbeUri(AiConnectionMetadata connection)
    {
        var provider = connection.ProviderKind.Trim().ToLowerInvariant();
        if (connection.ControlledEndpoint == "provider-default:openai")
            return new Uri("https://api.openai.com/v1/models");
        if (!Uri.TryCreate(connection.ControlledEndpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
            return null;
        var path = provider == "ollama" ? "/api/tags" : "/v1/models";
        return new UriBuilder(endpoint) { Path = path, Query = string.Empty }.Uri;
    }
}
