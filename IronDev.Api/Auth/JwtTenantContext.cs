using System;
using System.Security.Claims;
using IronDev.Core.Auth;
using Microsoft.AspNetCore.Http;

namespace IronDev.Api.Auth;

/// <summary>
/// Request-scoped tenant context resolved from JWT claims.
/// After a user calls POST /api/tenants/select and receives a tenant-bearing token,
/// every subsequent request carries tenant_id in the token and this resolves it.
/// Returns 0 if no tenant claim is present (base login token).
/// </summary>
public sealed class JwtTenantContext : ICurrentTenantContext
{
    public int TenantId { get; }

    public JwtTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        var claim = httpContextAccessor.HttpContext?.User.FindFirst("tenant_id");
        TenantId = claim is not null && int.TryParse(claim.Value, out var id) ? id : 0;
    }
}

/// <summary>
/// Typed accessor for the authenticated user's identity claims, resolved per-request.
/// </summary>
public sealed class CurrentUserContext
{
    public int UserId { get; }
    public string Email { get; }
    public string DisplayName { get; }
    public int? TenantId { get; }

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP context available.");

        UserId = int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value, out var uid) ? uid : 0;
        Email = user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("email")?.Value
            ?? string.Empty;
        DisplayName = user.FindFirst("display_name")?.Value ?? string.Empty;

        var tenantClaim = user.FindFirst("tenant_id");
        TenantId = tenantClaim is not null && int.TryParse(tenantClaim.Value, out var tid) ? tid : null;
    }
}
