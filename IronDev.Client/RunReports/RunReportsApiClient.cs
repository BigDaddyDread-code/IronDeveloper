using IronDev.Client.Http;
using IronDev.Core.RunReports;

namespace IronDev.Client.RunReports;

public sealed class RunReportsApiClient : IronDevApiClientBase, IRunReportsApiClient
{
    public RunReportsApiClient(HttpClient http)
        : base(http)
    {
    }

    public Task<IReadOnlyList<RunReportSummary>> GetRecentRunsAsync(string? project = null, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<RunReportSummary>>($"run-reports?project={Uri.EscapeDataString(project ?? string.Empty)}", cancellationToken);

    public Task<RunReportDetail?> GetRunAsync(string runId, CancellationToken cancellationToken = default) =>
        GetAsync<RunReportDetail?>($"run-reports/{Uri.EscapeDataString(runId)}", cancellationToken);

    public Task<RunStatusDto> GetRunStatusAsync(string runId, CancellationToken cancellationToken = default) =>
        GetAsync<RunStatusDto>($"runs/{Uri.EscapeDataString(runId)}", cancellationToken);

    public Task<RunReportDto> GetRunReportAsync(string runId, CancellationToken cancellationToken = default) =>
        GetAsync<RunReportDto>($"runs/{Uri.EscapeDataString(runId)}/report", cancellationToken);

    public IAsyncEnumerable<RunEventDto> StreamRunEventsAsync(string runId, CancellationToken cancellationToken = default) =>
        StreamSseRunEventsAsync($"runs/{Uri.EscapeDataString(runId)}/events", cancellationToken);

    public Task<IReadOnlyList<RunEvidenceItem>> GetEvidenceAsync(string runId, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<RunEvidenceItem>>($"run-reports/{Uri.EscapeDataString(runId)}/evidence", cancellationToken);

    public Task<string?> ReadEvidenceTextAsync(string runId, string evidencePath, CancellationToken cancellationToken = default) =>
        GetAsync<string?>($"run-reports/{Uri.EscapeDataString(runId)}/evidence/text?path={Uri.EscapeDataString(evidencePath)}", cancellationToken);
}
