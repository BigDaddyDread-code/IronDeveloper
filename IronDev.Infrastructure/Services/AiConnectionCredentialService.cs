using IronDev.Core.AiConnections;

namespace IronDev.Infrastructure.Services;

public sealed class AiConnectionCredentialService : IAiConnectionCredentialService
{
    public const string Boundary =
        "Credential values are accepted only on write, stored protected, and never returned by API responses.";

    private readonly IAiConnectionCredentialStore _store;
    private readonly IAiConnectionCatalogService _catalog;

    public AiConnectionCredentialService(
        IAiConnectionCredentialStore store,
        IAiConnectionCatalogService catalog)
    {
        _store = store;
        _catalog = catalog;
    }

    public async Task<AiConnectionCredentialMutationOutcome> ConfigureAsync(
        int tenantId,
        int userId,
        string connectionId,
        AiConnectionCredentialWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var current = await FindConnectionAsync(tenantId, userId, connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return Failure("Unknown AI connection.", null);
        }

        if (string.IsNullOrWhiteSpace(request.Credential))
        {
            return Failure("Credential is required.", current);
        }

        await _store.StoreAsync(
            tenantId,
            connectionId,
            request.Credential.Trim(),
            userId,
            request.Reason,
            cancellationToken).ConfigureAwait(false);

        return Success(await FindConnectionAsync(tenantId, userId, connectionId, cancellationToken)
            .ConfigureAwait(false));
    }

    public async Task<AiConnectionCredentialMutationOutcome> RevokeAsync(
        int tenantId,
        int userId,
        string connectionId,
        AiConnectionCredentialRevokeRequest request,
        CancellationToken cancellationToken = default)
    {
        var current = await FindConnectionAsync(tenantId, userId, connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return Failure("Unknown AI connection.", null);
        }

        await _store.RevokeAsync(
            tenantId,
            connectionId,
            userId,
            request.Reason,
            cancellationToken).ConfigureAwait(false);

        return Success(await FindConnectionAsync(tenantId, userId, connectionId, cancellationToken)
            .ConfigureAwait(false));
    }

    private async Task<AiConnectionMetadata?> FindConnectionAsync(
        int tenantId,
        int userId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var connections = await _catalog.ListAsync(tenantId, userId, cancellationToken)
            .ConfigureAwait(false);

        return connections.FirstOrDefault(connection =>
            string.Equals(connection.Id, connectionId, StringComparison.OrdinalIgnoreCase));
    }

    private static AiConnectionCredentialMutationOutcome Success(AiConnectionMetadata? connection) =>
        new()
        {
            Succeeded = true,
            Connection = connection,
            Boundary = Boundary
        };

    private static AiConnectionCredentialMutationOutcome Failure(string failureReason, AiConnectionMetadata? connection) =>
        new()
        {
            Succeeded = false,
            FailureReason = failureReason,
            Connection = connection,
            Boundary = Boundary
        };
}
