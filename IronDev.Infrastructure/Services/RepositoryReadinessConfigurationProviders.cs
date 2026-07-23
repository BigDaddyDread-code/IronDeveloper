using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Agents;
using IronDev.Core.AiConnections;
using IronDev.Core.Workbench;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Resolves the effective Builder profile without contacting its model provider.  The effective
/// profile hash is already the server-owned, secret-free stable configuration identity.
/// </summary>
public sealed class BuilderStableConfigurationProvider : IBuilderStableConfigurationProvider
{
    private readonly ISkeletonAgentProfileService _profiles;

    public BuilderStableConfigurationProvider(ISkeletonAgentProfileService profiles)
    {
        _profiles = profiles;
    }

    public async Task<BuilderStableConfigurationBinding?> GetCurrentAsync(
        int tenantId,
        int projectId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0 || projectId <= 0)
            return null;

        var profile = (await _profiles.ListEffectiveAsync(tenantId, projectId, cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault(value => value.Role == SkeletonAgentRole.Builder);
        if (profile is null || string.IsNullOrWhiteSpace(profile.Provider) ||
            string.IsNullOrWhiteSpace(profile.Model) || string.IsNullOrWhiteSpace(profile.EffectiveHash))
            return null;

        try
        {
            var configurationSha256 = RepositoryReadinessCanonicalJson.NormalizeSha256(
                profile.EffectiveHash,
                nameof(profile.EffectiveHash));
            var revision = profile.PublishedVersion is > 0 ? profile.PublishedVersion.Value : 1L;
            return BuilderStableConfigurationEvidenceCodec.NormalizeAndValidate(
                new BuilderStableConfigurationBinding
                {
                    ConfigurationId = DeterministicGuid(
                        $"workbench-builder-configuration-v1\n{tenantId}\n{projectId}\n{configurationSha256}"),
                    Revision = revision,
                    ProviderId = profile.Provider.Trim().ToLowerInvariant(),
                    ModelId = profile.Model.Trim(),
                    ConfigurationSha256 = configurationSha256
                });
        }
        catch (RepositoryReadinessValidationException)
        {
            return null;
        }
    }

    private static Guid DeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16];
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }
}

/// <summary>
/// Reports transient execution availability for display only.  It reads the connection catalog
/// and its last test metadata; it never calls a provider and never writes readiness state.
/// </summary>
public sealed class ExecutionAvailabilityChecker : IExecutionAvailabilityChecker
{
    private readonly IBuilderStableConfigurationProvider _builderConfiguration;
    private readonly ISkeletonAgentProfileService _profiles;
    private readonly IAiConnectionCatalogService _connections;

    public ExecutionAvailabilityChecker(
        IBuilderStableConfigurationProvider builderConfiguration,
        ISkeletonAgentProfileService profiles,
        IAiConnectionCatalogService connections)
    {
        _builderConfiguration = builderConfiguration;
        _profiles = profiles;
        _connections = connections;
    }

    public async Task<ExecutionAvailabilityCheck> CheckAsync(
        ExecutionAvailabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var current = await _builderConfiguration.GetCurrentAsync(
            request.TenantId,
            request.ProjectId,
            cancellationToken).ConfigureAwait(false);
        if (current is null || current.ConfigurationId != request.BuilderConfigurationId ||
            current.Revision != request.BuilderConfigurationRevision)
            return Unavailable("BuilderConfigurationChanged", "The effective Builder configuration changed.", now);

        var profile = (await _profiles.ListEffectiveAsync(
                request.TenantId,
                request.ProjectId,
                cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(value => value.Role == SkeletonAgentRole.Builder);
        if (profile is null)
            return Unavailable("BuilderConfigurationMissing", "No effective Builder configuration is available.", now);

        var catalog = await _connections.ListAsync(
            request.TenantId,
            request.ActorUserId,
            cancellationToken).ConfigureAwait(false);
        var connection = catalog.SingleOrDefault(value => string.Equals(
            value.Id,
            profile.AiConnectionId,
            StringComparison.Ordinal));
        if (connection is null || !connection.Enabled || !connection.TenantAvailable ||
            !connection.ProjectAvailable || !connection.CredentialConfigured)
            return Unavailable(
                "BuilderProviderUnavailable",
                "The configured Builder provider is not currently available.",
                now);

        if (connection.LastFailedTestUtc.HasValue &&
            (!connection.LastSuccessfulTestUtc.HasValue ||
             connection.LastFailedTestUtc.Value > connection.LastSuccessfulTestUtc.Value))
            return Unavailable(
                "BuilderProviderHealthCheckFailed",
                "The configured Builder provider most recently failed its availability check.",
                now);

        if (connection.AvailableModels.Count > 0 && !connection.AvailableModels.Contains(
                profile.Model,
                StringComparer.Ordinal))
            return Unavailable(
                "BuilderModelUnavailable",
                "The configured Builder model is not currently advertised by its provider.",
                now);

        return new ExecutionAvailabilityCheck
        {
            State = ExecutionAvailabilityStates.Available,
            ReasonCode = "BuilderExecutionAvailable",
            SafeMessage = "The configured Builder provider is currently available.",
            CheckedAtUtc = now
        };
    }

    private static ExecutionAvailabilityCheck Unavailable(
        string reasonCode,
        string safeMessage,
        DateTimeOffset checkedAtUtc) => new()
    {
        State = ExecutionAvailabilityStates.Unavailable,
        ReasonCode = reasonCode,
        SafeMessage = safeMessage,
        CheckedAtUtc = checkedAtUtc
    };
}
