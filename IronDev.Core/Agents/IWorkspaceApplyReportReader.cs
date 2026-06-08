namespace IronDev.Core.Agents;

public interface IWorkspaceApplyReportReader
{
    Task<WorkspaceApplyReportSummary> ReadAsync(
        WorkspaceApplyReportRequest request,
        CancellationToken cancellationToken = default);
}
