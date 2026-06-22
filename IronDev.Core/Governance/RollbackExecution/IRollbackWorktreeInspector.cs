namespace IronDev.Core.Governance.RollbackExecution;

public interface IRollbackWorktreeInspector
{
    Task<RollbackPreStateObservation?> ObservePreRollbackAsync(
        ControlledRollbackExecutionRequest request,
        CancellationToken cancellationToken);

    Task<RollbackPostStateObservation?> ObservePostRollbackAsync(
        ControlledRollbackExecutionRequest request,
        ControlledRollbackReceipt receipt,
        CancellationToken cancellationToken);
}
