namespace IronDev.Core.Governance.PullRequestExecution;

public interface IDraftPullRequestInspector
{
    Task<DraftPullRequestRemoteStateObservation> ObservePreMutationAsync(
        ControlledDraftPullRequestExecutionRequest request,
        CancellationToken cancellationToken);

    Task<DraftPullRequestPostStateObservation> ObservePostMutationAsync(
        ControlledDraftPullRequestExecutionRequest request,
        ControlledDraftPullRequestReceipt receipt,
        CancellationToken cancellationToken);
}
