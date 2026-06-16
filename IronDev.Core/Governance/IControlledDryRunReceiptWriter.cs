namespace IronDev.Core.Governance;

public interface IControlledDryRunReceiptWriter
{
    Task<ControlledDryRunReceiptWriteResult> ExecuteAndWriteAsync(
        ControlledDryRunRequest request,
        DisposableWorkspaceBoundary workspaceBoundary,
        ControlledDryRunExecutionPlan executionPlan,
        CancellationToken cancellationToken = default);
}
