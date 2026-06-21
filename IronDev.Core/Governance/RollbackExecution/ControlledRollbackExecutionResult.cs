using IronDev.Core.Governance;

namespace IronDev.Core.Governance.RollbackExecution;

public enum ControlledRollbackExecutionVerdict
{
    Completed = 1,
    Blocked = 2,
    Failed = 3
}

public enum ControlledRollbackFailureKind
{
    None = 0,
    MissingRequest = 1,
    RequestInvalid = 2,
    RollbackTargetInvalid = 3,
    RollbackAuthorityInvalid = 4,
    PolicyApprovedPathInvalid = 5,
    ApplyReceiptMismatch = 6,
    PreStateInvalid = 7,
    DirtyWorktree = 8,
    PartialRollbackRisk = 9,
    GatewayFailed = 10,
    ReceiptInvalid = 11,
    PostStateInvalid = 12,
    BoundaryViolation = 13
}

public sealed record ControlledRollbackExecutionResult
{
    public required bool IsRollbackExecuted { get; init; }

    public required ControlledRollbackExecutionVerdict Verdict { get; init; }
    public required ControlledRollbackFailureKind FailureKind { get; init; }

    public required ControlledRollbackReceipt? Receipt { get; init; }
    public required RollbackPreStateObservation? PreState { get; init; }
    public required RollbackPostStateObservation? PostState { get; init; }

    public required GovernedOperationStatus OperationStatus { get; init; }
    public required GovernedOperationStatusValidationResult StatusValidation { get; init; }

    public required IReadOnlyCollection<string> Issues { get; init; }
}
