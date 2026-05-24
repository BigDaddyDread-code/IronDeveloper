using IronDev.Core.Auth;

namespace IronDev.Client;

public interface IIronDevApiClient
{
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<UserProfileDto> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantDto>> GetTenantsAsync(CancellationToken cancellationToken = default);

    Task<LoginResponse> SelectTenantAsync(SelectTenantRequest request, CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);
}
