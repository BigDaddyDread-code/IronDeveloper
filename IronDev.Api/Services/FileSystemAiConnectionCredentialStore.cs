using System.Text.Json;
using IronDev.Core.AiConnections;
using Microsoft.AspNetCore.DataProtection;

namespace IronDev.Api.Services;

public sealed class FileSystemAiConnectionCredentialStore : IAiConnectionCredentialStore
{
    private const string StoreVersion = "IronDev AI Credential Store 2.5.0";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _rootDirectory;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;

    public FileSystemAiConnectionCredentialStore(
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider)
        : this(configuration, dataProtectionProvider, TimeProvider.System)
    {
    }

    public FileSystemAiConnectionCredentialStore(
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider clock)
    {
        _rootDirectory = ResolveRootDirectory(configuration);
        _protector = dataProtectionProvider.CreateProtector("IronDev.AiConnections.Credentials.v1");
        _clock = clock;
    }

    public async Task<AiConnectionCredentialStoredMetadata?> GetMetadataAsync(
        int tenantId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = PathFor(tenantId, connectionId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        var envelope = await JsonSerializer.DeserializeAsync<CredentialEnvelope>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);

        if (envelope is null)
        {
            return null;
        }

        return new AiConnectionCredentialStoredMetadata
        {
            CredentialConfigured = envelope.CredentialConfigured,
            CredentialStatus = envelope.CredentialStatus,
            CredentialRotatedUtc = envelope.CredentialRotatedUtc,
            CredentialRevokedUtc = envelope.CredentialRevokedUtc,
            UpdatedByUserId = envelope.UpdatedByUserId,
            UpdatedUtc = envelope.UpdatedUtc
        };
    }

    public Task StoreAsync(
        int tenantId,
        string connectionId,
        string credential,
        int userId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var envelope = new CredentialEnvelope
        {
            Version = StoreVersion,
            TenantId = tenantId,
            ConnectionId = connectionId,
            ProtectedCredential = _protector.Protect(credential),
            CredentialConfigured = true,
            CredentialStatus = "Configured",
            CredentialRotatedUtc = now,
            CredentialRevokedUtc = null,
            Reason = Clean(reason),
            UpdatedByUserId = userId,
            UpdatedUtc = now
        };

        return WriteEnvelopeAsync(tenantId, connectionId, envelope, cancellationToken);
    }

    public Task RevokeAsync(
        int tenantId,
        string connectionId,
        int userId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var envelope = new CredentialEnvelope
        {
            Version = StoreVersion,
            TenantId = tenantId,
            ConnectionId = connectionId,
            ProtectedCredential = null,
            CredentialConfigured = false,
            CredentialStatus = "Revoked",
            CredentialRotatedUtc = null,
            CredentialRevokedUtc = now,
            Reason = Clean(reason),
            UpdatedByUserId = userId,
            UpdatedUtc = now
        };

        return WriteEnvelopeAsync(tenantId, connectionId, envelope, cancellationToken);
    }

    private async Task WriteEnvelopeAsync(
        int tenantId,
        string connectionId,
        CredentialEnvelope envelope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = PathFor(tenantId, connectionId);
        var directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private string PathFor(int tenantId, string connectionId) =>
        Path.Combine(
            _rootDirectory,
            $"tenant-{tenantId}",
            $"{SafeFileName(connectionId)}.credential.json");

    private static string ResolveRootDirectory(IConfiguration configuration)
    {
        var configured = configuration["AiConnections:CredentialStorePath"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IronDev",
            "ai-connection-credentials");
    }

    private static string SafeFileName(string value)
    {
        var cleaned = value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Select(char.ToLowerInvariant)
            .ToArray();

        return cleaned.Length == 0 ? "connection" : new string(cleaned);
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private sealed record CredentialEnvelope
    {
        public required string Version { get; init; }
        public required int TenantId { get; init; }
        public required string ConnectionId { get; init; }
        public string? ProtectedCredential { get; init; }
        public required bool CredentialConfigured { get; init; }
        public required string CredentialStatus { get; init; }
        public DateTimeOffset? CredentialRotatedUtc { get; init; }
        public DateTimeOffset? CredentialRevokedUtc { get; init; }
        public string? Reason { get; init; }
        public required int UpdatedByUserId { get; init; }
        public required DateTimeOffset UpdatedUtc { get; init; }
    }
}
