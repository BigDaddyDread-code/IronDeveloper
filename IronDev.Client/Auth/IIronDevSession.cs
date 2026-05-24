using IronDev.Core.Auth;

namespace IronDev.Client.Auth;

public interface IIronDevSession
{
    string? AccessToken { get; }
    UserProfileDto? CurrentUser { get; }
    TenantDto? CurrentTenant { get; }
    int? ActiveProjectId { get; }
    bool IsAuthenticated { get; }

    void SetToken(string token);
    void SetCurrentUser(UserProfileDto user);
    void SetTenant(TenantDto tenant, string token);
    void SetActiveProject(int projectId);
    void Clear();
}
