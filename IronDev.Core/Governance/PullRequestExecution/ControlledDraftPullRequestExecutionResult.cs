namespace IronDev.Core.Governance.PullRequestExecution;

public enum ControlledDraftPullRequestExecutionVerdict
{
    Completed = 0,
    Blocked,
    Failed
}

public enum ControlledDraftPullRequestFailureKind
{
    None = 0,
    MissingRequest,
    RequestInvalid,
    PushReceiptMismatch,
    DraftPullRequestAuthorityMismatch,
    TextPackageInvalid,
    RemoteObservationFailed,
    RemoteStateMismatch,
    GatewayFailed,
    ReceiptInvalid,
    PostStateInvalid,
    BoundaryViolation
}

public sealed record ControlledDraftPullRequestExecutionResult
{
    public required bool IsDraftPullRequestMutated { get; init; }

    public required ControlledDraftPullRequestExecutionVerdict Verdict { get; init; }
    public required ControlledDraftPullRequestFailureKind FailureKind { get; init; }

    public required ControlledDraftPullRequestReceipt? Receipt { get; init; }
    public required DraftPullRequestRemoteStateObservation? PreMutationObservation { get; init; }
    public required DraftPullRequestPostStateObservation? PostMutationObservation { get; init; }

    public required GovernedOperationStatus OperationStatus { get; init; }
    public required GovernedOperationStatusValidationResult StatusValidation { get; init; }

    public required IReadOnlyCollection<string> Issues { get; init; }
}
