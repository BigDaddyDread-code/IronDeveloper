namespace IronDev.Core.Auth;

// ── Request/Response DTOs ────────────────────────────────────────────────────

/// <summary>Credentials submitted by the user at login.</summary>
public record LoginRequest(string Email, string Password);

/// <summary>Returned on successful login (base token) or tenant selection (tenant-bearing token).</summary>
public record LoginResponse(string Token, int UserId, string DisplayName);

/// <summary>Returned by GET /api/auth/me.</summary>
public record UserProfileDto(int UserId, string Email, string DisplayName, int? SelectedTenantId);

/// <summary>A tenant the current user is a member of.</summary>
public record TenantDto(int Id, string Name, string Slug);

/// <summary>Body for POST /api/tenants/select.</summary>
public record SelectTenantRequest(int TenantId);
