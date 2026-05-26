using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.Workflow;
using IronDev.Data.Models;

namespace IronDev.Client.Tickets;

public interface ITicketsApiClient :
    IDraftTicketService,
    ITicketBuildOrchestrator,
    IBuilderReadinessService,
    IBuilderProposalService
{
    Task<ProjectTicket> CreateTicketAsync(int projectId, CreateProjectTicketRequest request, CancellationToken cancellationToken = default);
    Task<ProjectTicket> ImportExternalTicketAsync(int projectId, ImportExternalTicketRequest request, CancellationToken cancellationToken = default);
    Task<ProjectTicket> GenerateTicketFromDiscussionAsync(int projectId, GenerateTicketFromDiscussionRequest request, CancellationToken cancellationToken = default);
    Task<long> SaveTicketAsync(ProjectTicket ticket, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default);
    Task<ProjectTicket?> GetTicketByIdAsync(long ticketId, CancellationToken cancellationToken = default);
    Task<bool> ArchiveTicketAsync(long ticketId, CancellationToken cancellationToken = default);
    Task<CodebaseTicketGenerationResult> GenerateTicketsAsync(int projectId, CancellationToken cancellationToken = default);
    Task<TicketBuildRunDto> StartTicketBuildRunAsync(int projectId, long ticketId, StartTicketBuildRunRequest request, CancellationToken cancellationToken = default);
}
