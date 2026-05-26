using IronDev.Core.Auth;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Data.Models;

namespace IronDev.Client;

public interface IIronDevApiClient
{
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<UserProfileDto> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantDto>> GetTenantsAsync(CancellationToken cancellationToken = default);

    Task<LoginResponse> SelectTenantAsync(SelectTenantRequest request, CancellationToken cancellationToken = default);

    Task LogoutAsync(CancellationToken cancellationToken = default);

    Task<ProjectTicket> CreateTicketAsync(int projectId, CreateProjectTicketRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectTicket>> GetTicketsAsync(int projectId, int take = 50, CancellationToken cancellationToken = default);

    Task<ProjectTicket?> GetProjectTicketAsync(int projectId, long ticketId, CancellationToken cancellationToken = default);

    Task<ProjectTicket> ImportExternalTicketAsync(int projectId, ImportExternalTicketRequest request, CancellationToken cancellationToken = default);

    Task<RunStatusDto> GetRunAsync(string runId, CancellationToken cancellationToken = default);

    Task<RunReportDto> GetRunReportAsync(string runId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<RunEventDto> StreamRunEventsAsync(string runId, CancellationToken cancellationToken = default);
}
