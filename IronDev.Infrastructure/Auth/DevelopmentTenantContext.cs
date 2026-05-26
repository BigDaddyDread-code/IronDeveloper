using IronDev.Core.Auth;

namespace IronDev.Infrastructure.Auth;

/// <summary>
/// Hardcoded tenant context for local development continuity.
/// Always resolves to the default seed tenant (Id=1).
/// Will be replaced by a real session/request-scoped context in Sprint 2.
/// </summary>
public sealed class DevelopmentTenantContext : ICurrentTenantContext
{
    public int TenantId => 1;
}
