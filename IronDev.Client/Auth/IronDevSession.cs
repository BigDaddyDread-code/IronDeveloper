using IronDev.Core.Auth;

namespace IronDev.Client.Auth;

public sealed class IronDevSession : IIronDevSession
{
    public string? AccessToken { get; private set; }
    public UserProfileDto? CurrentUser { get; private set; }
    public TenantDto? CurrentTenant { get; private set; }
    public int? ActiveProjectId { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

    public void SetToken(string token) => AccessToken = token;

    public void SetCurrentUser(UserProfileDto user) => CurrentUser = user;

    public void SetTenant(TenantDto tenant, string token)
    {
        CurrentTenant = tenant;
        AccessToken = token;
        if (CurrentUser is not null)
            CurrentUser = CurrentUser with { SelectedTenantId = tenant.Id };
    }

    public void SetActiveProject(int projectId) => ActiveProjectId = projectId;

    public void Clear()
    {
        AccessToken = null;
        CurrentUser = null;
        CurrentTenant = null;
        ActiveProjectId = null;
    }
}
