namespace IronDev.Core.AiConnections;

/// <summary>
/// V25-06 - tenant-scoped AI connection metadata.
/// The payload is intentionally non-secret: it exposes provider, controlled
/// endpoint, status, and availability, never credential values or raw secret
/// references.
/// </summary>
public sealed record AiConnectionMetadata
{
    public required string Id { get; init; }
    public required int TenantId { get; init; }
    public required string DisplayName { get; init; }
    public required string ProviderKind { get; init; }
    public required string ControlledEndpointId { get; init; }
    public required string ControlledEndpoint { get; init; }
    public required bool CredentialConfigured { get; init; }
    public required string CredentialStatus { get; init; }
    public required IReadOnlyList<string> SupportedPurposes { get; init; }
    public required string PurposeDescription { get; init; }
    public DateTimeOffset? LastSuccessfulTestUtc { get; init; }
    public DateTimeOffset? LastFailedTestUtc { get; init; }
    public IReadOnlyList<string> AvailableModels { get; init; } = [];
    public required bool Enabled { get; init; }
    public required bool TenantAvailable { get; init; }
    public required bool ProjectAvailable { get; init; }
    public DateTimeOffset? CredentialRotatedUtc { get; init; }
    public DateTimeOffset? CredentialRevokedUtc { get; init; }
    public required int CreatedByUserId { get; init; }
    public DateTimeOffset? CreatedUtc { get; init; }
    public required int UpdatedByUserId { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }
    public required string Version { get; init; }
    public required string Boundary { get; init; }
}

public interface IAiConnectionCatalogService
{
    Task<IReadOnlyList<AiConnectionMetadata>> ListAsync(int tenantId, int userId, CancellationToken cancellationToken = default);
}

public sealed record AiConnectionTestHealth
{
    public DateTimeOffset? LastSuccessfulTestUtc { get; init; }
    public DateTimeOffset? LastFailedTestUtc { get; init; }
    public string LastStatus { get; init; } = "NotTested";
}

public interface IAiConnectionTestHealthStore
{
    Task<AiConnectionTestHealth?> GetAsync(int tenantId, string connectionId, CancellationToken cancellationToken = default);
    Task RecordAsync(int tenantId, string connectionId, bool succeeded, DateTimeOffset testedAtUtc, CancellationToken cancellationToken = default);
}

public sealed record AiConnectionTestOutcome
{
    public required bool Succeeded { get; init; }
    public required string Status { get; init; }
    public string? FailureReason { get; init; }
    public int? HttpStatusCode { get; init; }
    public required DateTimeOffset TestedAtUtc { get; init; }
    public AiConnectionMetadata? Connection { get; init; }
    public required string Boundary { get; init; }
}

public interface IAiConnectionTestService
{
    Task<AiConnectionTestOutcome> TestAsync(int tenantId, int userId, string connectionId, CancellationToken cancellationToken = default);
}

public sealed record AiConnectionCredentialWriteRequest
{
    public required string Credential { get; init; }
    public string? Reason { get; init; }
}

public sealed record AiConnectionCredentialRevokeRequest
{
    public string? Reason { get; init; }
}

public sealed record AiConnectionCredentialMutationOutcome
{
    public required bool Succeeded { get; init; }
    public string? FailureReason { get; init; }
    public AiConnectionMetadata? Connection { get; init; }
    public required string Boundary { get; init; }
}

public sealed record AiConnectionCredentialStoredMetadata
{
    public required bool CredentialConfigured { get; init; }
    public required string CredentialStatus { get; init; }
    public DateTimeOffset? CredentialRotatedUtc { get; init; }
    public DateTimeOffset? CredentialRevokedUtc { get; init; }
    public required int UpdatedByUserId { get; init; }
    public required DateTimeOffset UpdatedUtc { get; init; }
}

public interface IAiConnectionCredentialStore
{
    Task<AiConnectionCredentialStoredMetadata?> GetMetadataAsync(
        int tenantId,
        string connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>Internal provider-use path only. Credential values must never enter an API response, log, or audit payload.</summary>
    Task<string?> GetCredentialForUseAsync(
        int tenantId,
        string connectionId,
        CancellationToken cancellationToken = default);

    Task StoreAsync(
        int tenantId,
        string connectionId,
        string credential,
        int userId,
        string? reason,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(
        int tenantId,
        string connectionId,
        int userId,
        string? reason,
        CancellationToken cancellationToken = default);
}

public interface IAiConnectionCredentialService
{
    Task<AiConnectionCredentialMutationOutcome> ConfigureAsync(
        int tenantId,
        int userId,
        string connectionId,
        AiConnectionCredentialWriteRequest request,
        CancellationToken cancellationToken = default);

    Task<AiConnectionCredentialMutationOutcome> RevokeAsync(
        int tenantId,
        int userId,
        string connectionId,
        AiConnectionCredentialRevokeRequest request,
        CancellationToken cancellationToken = default);
}
