using IronDev.Core.Auth;

namespace IronDev.Client.Auth;

public interface IAuthApiClient
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TenantDto>> GetTenantsAsync(CancellationToken ct = default);
    Task<LoginResponse> SelectTenantAsync(SelectTenantRequest request, CancellationToken ct = default);
    Task<UserProfileDto> GetCurrentUserAsync(CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
}
