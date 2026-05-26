using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

public interface ITicketEvidenceSummaryService
{
    Task<TicketEvidenceSummaryDto?> GetEvidenceSummaryAsync(
        int projectId,
        long ticketId,
        CancellationToken cancellationToken = default);
}
