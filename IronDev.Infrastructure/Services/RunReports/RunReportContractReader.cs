using IronDev.Client;
using IronDev.Core.RunReports;

namespace IronDev.Infrastructure.Services.RunReports;

public sealed class RunReportContractReader : IRunReportContractReader
{
    private readonly IIronDevApiClient _client;

    public RunReportContractReader(IIronDevApiClient client)
    {
        _client = client;
    }

    public async Task<RunReportContractReadResult> ReadAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = await _client.GetRunReportAsync(runId, cancellationToken);
            return RunReportContractMapper.MapToReadResult(RunReportContractMapper.MapFromApiReport(report));
        }
        catch (IronDevApiException ex)
        {
            return RunReportContractMapper.MapFromApiFailure(runId, ex.StatusCode, ex.ResponseBody);
        }
    }
}
