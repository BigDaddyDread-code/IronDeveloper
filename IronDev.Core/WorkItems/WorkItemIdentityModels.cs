using IronDev.Data.Models;

namespace IronDev.Core.WorkItems;

public sealed record WorkItemIdentitySnapshot
{
    public long WorkItemId { get; init; }
    public long LegacyTicketId { get; init; }
    public long? CurrentContractId { get; init; }
    public string CurrentStage { get; init; } = ProjectWorkItemStages.Ticket;
    public string CurrentState { get; init; } = "Draft";
}

public interface IWorkItemIdentityService
{
    Task EnsureForTicketAsync(ProjectTicket ticket, long ticketId, CancellationToken cancellationToken = default);
    Task<WorkItemIdentitySnapshot?> GetByLegacyTicketIdAsync(long ticketId, CancellationToken cancellationToken = default);
}
