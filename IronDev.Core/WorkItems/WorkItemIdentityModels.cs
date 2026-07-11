using IronDev.Data.Models;

namespace IronDev.Core.WorkItems;

public sealed record WorkItemIdentitySnapshot
{
    public long WorkItemId { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public string Title { get; init; } = string.Empty;
    public long? LegacyTicketId { get; init; }
    public long? CurrentContractId { get; init; }
    public string CurrentStage { get; init; } = ProjectWorkItemStages.Ticket;
    public string CurrentState { get; init; } = "Draft";
    public long Version { get; init; }
    public WorkItemContractSnapshot? CurrentContract { get; init; }
}

public sealed record WorkItemContractSnapshot
{
    public long ContractId { get; init; }
    public int ContractVersion { get; init; }
    public long WorkItemId { get; init; }
    public long? SourceTicketId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? AcceptanceCriteria { get; init; }
    public string? LinkedFilePaths { get; init; }
    public long? SourceWorkshopSessionId { get; init; }
    public long? SourceWorkshopMessageId { get; init; }
    public long? SourceDocumentVersionId { get; init; }
}

public interface IWorkItemIdentityService
{
    Task EnsureForTicketAsync(ProjectTicket ticket, long ticketId, CancellationToken cancellationToken = default);
    Task<WorkItemIdentitySnapshot?> GetByWorkItemIdAsync(int projectId, long workItemId, CancellationToken cancellationToken = default);
    Task<WorkItemIdentitySnapshot?> GetByLegacyTicketIdAsync(long ticketId, CancellationToken cancellationToken = default);
}
