using IronDev.Core.Auth;

namespace IronDev.Agent.Services;

/// <summary>
/// Bridge between the UI-driven ShellViewModel and the shared services in IronDev.Infrastructure.
/// This allows the core services to automatically use the currently selected tenant and project.
/// </summary>
public sealed class AgentTenantContext : ICurrentTenantContext
{
    private int _tenantId;
    private int _activeProjectId;

    public int TenantId        => _tenantId;
    /// <summary>The ProjectId of the project the user most recently opened. 0 if none.</summary>
    public int ActiveProjectId => _activeProjectId;

    public void SetTenant(int tenantId)
    {
        _tenantId = tenantId;
    }

    /// <summary>Called by ShellViewModel when a project is activated.</summary>
    public void SetProject(int projectId)
    {
        _activeProjectId = projectId;
    }
}
