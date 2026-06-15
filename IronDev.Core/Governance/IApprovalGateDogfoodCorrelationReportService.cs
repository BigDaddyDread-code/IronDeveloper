namespace IronDev.Core.Governance;

public interface IApprovalGateDogfoodCorrelationReportService
{
    Task<ApprovalGateDogfoodCorrelationReportResponse> GetReportAsync(
        ApprovalGateDogfoodCorrelationReportRequest request,
        CancellationToken cancellationToken = default);
}
