using IronDev.Core.Auth;

namespace IronDev.Agent.Services;

/// <summary>
/// Bridge between the UI-driven ShellViewModel and the shared services in IronDev.Infrastructure.
/// This allows the core services to automatically use the currently selected tenant.
/// </summary>
public sealed class AgentTenantContext : ICurrentTenantContext
{
    private int _tenantId;

    public int TenantId => _tenantId;

    public void SetTenant(int tenantId)
    {
        _tenantId = tenantId;
    }
}
