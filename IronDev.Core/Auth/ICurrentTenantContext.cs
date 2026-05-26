namespace IronDev.Core.Auth;

/// <summary>
/// Provides the current tenant identity for the executing context.
/// Registered as scoped so it maps naturally to per-request scope in the API
/// and per-operation scope in product clients and internal tools.
/// </summary>
public interface ICurrentTenantContext
{
    int TenantId { get; }
}
