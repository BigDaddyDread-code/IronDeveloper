namespace IronDev.Core.Workflow;

public interface IFailedWorkflowDiagnosisReportService
{
    Task<FailedWorkflowDiagnosisReportResponse> GetReportAsync(
        FailedWorkflowDiagnosisReportRequest request,
        CancellationToken cancellationToken = default);
}
