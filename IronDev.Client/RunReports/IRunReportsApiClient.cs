using IronDev.Core.RunReports;

namespace IronDev.Client.RunReports;

public interface IRunReportsApiClient : IRunReportService, IRunEvidenceService
{
    Task<RunStatusDto> GetRunStatusAsync(string runId, CancellationToken cancellationToken = default);

    Task<RunReportDto> GetRunReportAsync(string runId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<RunEventDto> StreamRunEventsAsync(string runId, CancellationToken cancellationToken = default);
}
