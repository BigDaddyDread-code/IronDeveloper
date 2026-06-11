using IronDev.Core.Auth;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Workflow;
using IronDev.Data.Models;

namespace IronDev.Client;

public interface IIronDevApiClient
{
    Task<IronDevApiResponse<System.Text.Json.JsonElement?>> PingAsync(CancellationToken cancellationToken = default);

    Task<IronDevApiResponse<System.Text.Json.JsonElement?>> ListAgentRunsAsync(
        AgentRunListQuery query,
        CancellationToken cancellationToken = default);

    Task<IronDevApiResponse<System.Text.Json.JsonElement?>> GetAgentRunAsync(
        int projectId,
        string agentRunId,
        CancellationToken cancellationToken = default);

    Task<IronDevApiResponse<System.Text.Json.JsonElement?>> GetAgentRunAuditAsync(
        int projectId,
        string agentRunId,
        CancellationToken cancellationToken = default);

    Task<IronDevApiResponse<System.Text.Json.JsonElement?>> CreateManualCriticReviewAsync(
        ManualCriticReviewCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<IronDevApiResponse<System.Text.Json.JsonElement?>> GetManualCriticReviewAsync(
        int projectId,
        string agentRunId,
        CancellationToken cancellationToken = default);

    Task<IronDevApiResponse<System.Text.Json.JsonElement?>> CreateManualMemoryImprovementAsync(
        ManualMemoryImprovementCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<IronDevApiResponse<System.Text.Json.JsonElement?>> GetManualMemoryImprovementAsync(
        int projectId,
        string agentRunId,
        CancellationToken cancellationToken = default);

    Task<IronDevApiResponse<System.Text.Json.JsonElement?>> CreateToolRequestAsync(
        ToolRequestCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<IronDevApiResponse<System.Text.Json.JsonElement?>> GetToolRequestAsync(
        int projectId,
        string toolRequestId,
        CancellationToken cancellationToken = default);

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

    Task<TicketBuildRunDto> StartTicketBuildRunAsync(int projectId, long ticketId, StartTicketBuildRunRequest request, CancellationToken cancellationToken = default);

    Task<SaveDiscussionResponse> SaveDiscussionAsync(int projectId, SaveDiscussionRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BuildScenario>> GetBuildScenariosAsync(int projectId, CancellationToken cancellationToken = default);

    Task<CreateTicketFromDocumentResponse> CreateTicketFromDocumentAsync(int projectId, long documentVersionId, CreateTicketFromDocumentRequest request, CancellationToken cancellationToken = default);

    Task<RunTicketReviewResponse> ReviewTicketAsync(int projectId, long ticketId, RunTicketReviewRequest request, CancellationToken cancellationToken = default);

    Task<StartDisposableCodeRunResponse> StartDisposableCodeRunAsync(int projectId, long ticketId, StartDisposableCodeRunRequest request, CancellationToken cancellationToken = default);

    Task<RunReviewPackage> GetRunReviewPackageAsync(int projectId, long ticketId, string runId, CancellationToken cancellationToken = default);

    Task<RunStatusDto> GetRunAsync(string runId, CancellationToken cancellationToken = default);

    Task<RunReportDto> GetRunReportAsync(string runId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<RunEventDto> StreamRunEventsAsync(string runId, CancellationToken cancellationToken = default);
}
