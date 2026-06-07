namespace IronDev.Core.RunReports;

public interface IRunReportContractReader
{
    Task<RunReportContractReadResult> ReadAsync(string runId, CancellationToken cancellationToken = default);
}

