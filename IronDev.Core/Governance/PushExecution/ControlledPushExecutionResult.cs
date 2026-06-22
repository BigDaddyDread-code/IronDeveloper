namespace IronDev.Core.Governance.PushExecution;

public enum ControlledPushExecutionVerdict
{
    Completed = 0,
    Blocked,
    Failed
}

public enum ControlledPushFailureKind
{
    None = 0,
    MissingRequest,
    RequestInvalid,
    CommitReceiptMismatch,
    PushAuthorityMismatch,
    RemoteObservationFailed,
    RemoteStateMismatch,
    GatewayFailed,
    ReceiptInvalid,
    PostStateInvalid,
    BoundaryViolation
}

public sealed record ControlledPushExecutionResult
{
    public required bool IsPushExecuted { get; init; }

    public required ControlledPushExecutionVerdict Verdict { get; init; }
    public required ControlledPushFailureKind FailureKind { get; init; }

    public required ControlledPushReceipt? Receipt { get; init; }
    public required PushRemoteStateObservation? PrePushObservation { get; init; }
    public required PushPostStateObservation? PostPushObservation { get; init; }

    public required GovernedOperationStatus OperationStatus { get; init; }
    public required GovernedOperationStatusValidationResult StatusValidation { get; init; }

    public required IReadOnlyCollection<string> Issues { get; init; }
}
