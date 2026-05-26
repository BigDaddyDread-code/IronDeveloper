using IronDev.Core.RunReports;

namespace IronDev.Core.Interfaces;

public interface ITicketRunReviewService
{
    Task<TicketRunReviewDto?> GetRunReviewAsync(
        int projectId,
        long ticketId,
        string runId,
        CancellationToken cancellationToken = default);
}
