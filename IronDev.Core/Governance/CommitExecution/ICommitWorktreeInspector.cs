namespace IronDev.Core.Governance.CommitExecution;

public interface ICommitWorktreeInspector
{
    Task<CommitWorktreeObservation> ObservePreCommitAsync(
        ControlledCommitExecutionRequest request,
        CancellationToken cancellationToken);

    Task<CommitPostStateObservation> ObservePostCommitAsync(
        ControlledCommitExecutionRequest request,
        ControlledCommitReceipt receipt,
        CancellationToken cancellationToken);
}
