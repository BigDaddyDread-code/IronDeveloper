namespace IronDev.Core.Governance;

public interface IControlledDryRunExecutor
{
    Task<ControlledDryRunExecutionReport> ExecuteAsync(
        ControlledDryRunRequest request,
        DisposableWorkspaceBoundary workspaceBoundary,
        ControlledDryRunExecutionPlan executionPlan,
        CancellationToken cancellationToken = default);
}
