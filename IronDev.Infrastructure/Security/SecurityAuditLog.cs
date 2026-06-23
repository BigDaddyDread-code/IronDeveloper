using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Security;

public sealed class SecurityAuditLog : ISecurityAuditLog
{
    private static readonly string[] SensitiveValueMarkers =
    [
        "password",
        "jwt",
        "bearer ",
        "authorization:",
        "authorization header",
        "api_key",
        "apikey",
        "connection string",
        "request body",
        "raw request",
        "raw prompt",
        "raw completion",
        "chain-of-thought",
        "private reasoning"
    ];

    private readonly object _sync = new();
    private readonly List<SecurityAuditEvent> _events = [];

    public Task AppendAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(auditEvent);

        var normalized = auditEvent with
        {
            EventId = string.IsNullOrWhiteSpace(auditEvent.EventId)
                ? Guid.NewGuid().ToString("N")
                : auditEvent.EventId,
            OccurredUtc = auditEvent.OccurredUtc == default
                ? DateTimeOffset.UtcNow
                : auditEvent.OccurredUtc,
            Metadata = new Dictionary<string, string>(
                auditEvent.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal),
                StringComparer.Ordinal)
        };

        Validate(normalized);

        lock (_sync)
        {
            _events.Add(normalized);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<SecurityAuditEvent> Snapshot()
    {
        lock (_sync)
        {
            return _events.ToArray();
        }
    }

    private static void Validate(SecurityAuditEvent auditEvent)
    {
        if (auditEvent.EventType is SecurityAuditEventType.Unknown)
            throw new InvalidOperationException("Security audit event type is required.");

        if (auditEvent.Outcome is SecurityAuditOutcome.Unknown)
            throw new InvalidOperationException("Security audit outcome is required.");

        foreach (var value in StringValues(auditEvent))
        {
            if (ContainsSensitiveMaterial(value))
                throw new InvalidOperationException("Security audit event contains unsafe material.");
        }
    }

    private static IEnumerable<string?> StringValues(SecurityAuditEvent auditEvent)
    {
        yield return auditEvent.EventId;
        yield return auditEvent.ActorUserId;
        yield return auditEvent.ActorEmailHash;
        yield return auditEvent.TenantId;
        yield return auditEvent.TargetUserId;
        yield return auditEvent.TargetTenantId;
        yield return auditEvent.ReasonCode;
        yield return auditEvent.CorrelationId;
        yield return auditEvent.RemoteIpHash;
        yield return auditEvent.UserAgentHash;
        yield return auditEvent.RequestPath;

        foreach (var item in auditEvent.Metadata)
        {
            yield return item.Key;
            yield return item.Value;
        }
    }

    private static bool ContainsSensitiveMaterial(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        SensitiveValueMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
