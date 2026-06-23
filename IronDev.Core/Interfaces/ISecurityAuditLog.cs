using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface ISecurityAuditLog
{
    Task AppendAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default);

    IReadOnlyList<SecurityAuditEvent> Snapshot();
}
