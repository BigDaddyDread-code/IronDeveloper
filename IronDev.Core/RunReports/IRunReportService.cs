namespace IronDev.Core.RunReports;

public interface IRunReportService
{
    Task<IReadOnlyList<RunReportSummary>> GetRecentRunsAsync(
        string? project = null,
        CancellationToken cancellationToken = default);

    Task<RunReportDetail?> GetRunAsync(
        string runId,
        CancellationToken cancellationToken = default);
}
