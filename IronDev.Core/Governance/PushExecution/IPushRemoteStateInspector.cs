namespace IronDev.Core.Governance.PushExecution;

public interface IPushRemoteStateInspector
{
    Task<PushRemoteStateObservation> ObservePrePushAsync(
        ControlledPushExecutionRequest request,
        CancellationToken cancellationToken);

    Task<PushPostStateObservation> ObservePostPushAsync(
        ControlledPushExecutionRequest request,
        ControlledPushReceipt receipt,
        CancellationToken cancellationToken);
}
