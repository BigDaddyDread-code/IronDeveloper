namespace IronDev.Core.AgentMemory;

public interface IAgentMemoryRunReportService
{
    Task<RunMemoryReport> BuildAsync(
        RunMemoryReportRequest request,
        CancellationToken cancellationToken = default);
}
