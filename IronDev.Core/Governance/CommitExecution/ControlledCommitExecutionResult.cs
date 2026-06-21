namespace IronDev.Core.Governance.CommitExecution;

public enum ControlledCommitExecutionVerdict
{
    Completed = 0,
    Blocked,
    Failed
}

public enum ControlledCommitFailureKind
{
    None = 0,
    MissingRequest,
    RequestInvalid,
    CommitPackageInvalid,
    ManifestMismatch,
    SourceApplyReceiptMismatch,
    CommitAuthorityMismatch,
    ExpectedDiffMismatch,
    WorktreeObservationFailed,
    WorktreeStateMismatch,
    ForbiddenFileObserved,
    GatewayFailed,
    ReceiptInvalid,
    PostStateInvalid,
    BoundaryViolation
}

public sealed record ControlledCommitExecutionResult
{
    public required bool IsCommitExecuted { get; init; }

    public required ControlledCommitExecutionVerdict Verdict { get; init; }
    public required ControlledCommitFailureKind FailureKind { get; init; }

    public required ControlledCommitReceipt? Receipt { get; init; }
    public required CommitWorktreeObservation? PreCommitObservation { get; init; }
    public required CommitPostStateObservation? PostCommitObservation { get; init; }

    public required GovernedOperationStatus OperationStatus { get; init; }
    public required GovernedOperationStatusValidationResult StatusValidation { get; init; }

    public required IReadOnlyCollection<string> Issues { get; init; }
}
