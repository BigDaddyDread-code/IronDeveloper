using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Models;

public enum SecurityAuditEventType
{
    Unknown = 0,
    AuthLoginSucceeded = 1,
    AuthLoginFailed = 2,
    AuthLogoutRequested = 3,
    TenantSelectionSucceeded = 4,
    TenantSelectionDenied = 5,
    AdminSecurityChangeRequested = 6,
    AdminSecurityChangeSucceeded = 7,
    AdminSecurityChangeDenied = 8
}

public enum SecurityAuditOutcome
{
    Unknown = 0,
    Succeeded = 1,
    Denied = 2
}

public sealed record SecurityAuditEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public SecurityAuditEventType EventType { get; init; }
    public DateTimeOffset OccurredUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ActorUserId { get; init; } = string.Empty;
    public string ActorEmailHash { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string TargetUserId { get; init; } = string.Empty;
    public string TargetTenantId { get; init; } = string.Empty;
    public SecurityAuditOutcome Outcome { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string RemoteIpHash { get; init; } = string.Empty;
    public string UserAgentHash { get; init; } = string.Empty;
    public string RequestPath { get; init; } = string.Empty;
    public bool Authenticated { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static string HashRedacted(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "redacted:unknown";

        var normalized = value.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
