using IronDev.Core.Workflow;

namespace IronDev.Core.Interfaces;

public interface ITicketBuildRunService
{
    Task<TicketBuildRunDto?> StartDisposableAsync(
        int projectId,
        long ticketId,
        StartTicketBuildRunRequest? request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TicketBuildRunSummaryDto>?> GetRunsAsync(
        int projectId,
        long ticketId,
        int take = 50,
        CancellationToken cancellationToken = default);

    Task<TicketBuildRunDetailDto?> GetRunAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default);
}
