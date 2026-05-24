using IronDev.Client.Http;
using IronDev.Core.Auth;

namespace IronDev.Client.Auth;

public sealed class AuthApiClient : IronDevApiClientBase, IAuthApiClient
{
    private readonly IIronDevSession _session;

    public AuthApiClient(HttpClient http, IIronDevSession session)
        : base(http)
    {
        _session = session;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var response = await PostAsync<LoginResponse>("auth/login", request, ct);
        _session.SetToken(response.Token);
        _session.SetCurrentUser(new UserProfileDto(response.UserId, request.Email, response.DisplayName, null));
        return response;
    }

    public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<TenantDto>>("tenants", ct);

    public async Task<LoginResponse> SelectTenantAsync(SelectTenantRequest request, CancellationToken ct = default)
    {
        var response = await PostAsync<LoginResponse>("tenants/select", request, ct);
        var tenant = (await GetTenantsAsync(ct)).First(t => t.Id == request.TenantId);
        _session.SetTenant(tenant, response.Token);
        _session.SetCurrentUser(new UserProfileDto(response.UserId, _session.CurrentUser?.Email ?? string.Empty, response.DisplayName, tenant.Id));
        return response;
    }

    public async Task<UserProfileDto> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var profile = await GetAsync<UserProfileDto>("auth/me", ct);
        _session.SetCurrentUser(profile);
        return profile;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await PostAsync<object>("auth/logout", new { }, ct);
        _session.Clear();
    }
}
